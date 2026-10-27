using System.Collections.Concurrent;
using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;
using NewCosmos.Services.System;

namespace NewCosmos.Helpers;

/// <summary>
/// 统一地址拼接服务 — 所有模板输出的地址都走这里
/// 规则：镇名 + 村名(去除"村委会"/"居民委员会"后缀) + 门牌号
/// </summary>
public class AddressResolver
{
    // 本类注册为 Singleton 且会被并发访问——缓存必须用 ConcurrentDictionary。
    // 普通 Dictionary 并发写会损坏内部桶链，典型表现为读操作 CPU 打满死循环。
    private const int FullAddressCacheLimit = 5000;

    private readonly IRegionService _regionService;
    private readonly ILoggerService _logger;
    private readonly ConcurrentDictionary<string, string> _villageNameCache = new();
    private readonly ConcurrentDictionary<string, string> _fullAddressCache = new();

    // 完整地址（县+镇+村）解析缓存：社区代码 → 三级行政区名称
    private readonly ConcurrentDictionary<string, (string County, string Town, string Village)> _communityNameCache = new();
    private readonly ConcurrentDictionary<int, (string TownName, int CountyId)> _townInfoById = new();
    private readonly ConcurrentDictionary<int, string> _countyNameById = new();

    public AddressResolver(IRegionService regionService, ILoggerService logger)
    {
        _regionService = regionService;
        _logger = logger;
    }

    /// <summary>
    /// 预加载一批 village_code 对应的村名。
    /// 走**批量**接口一次取回——原实现逐条 <c>GetVillageByCodeAsync</c>：
    /// 本地快照缺失时是 N 次打库（N+1），已加载时则是每条都全量扫一遍村表（N × 万级）。
    /// </summary>
    public async Task WarmupAsync(IEnumerable<string> communityCodes, CancellationToken ct = default)
    {
        var codes = communityCodes
            .Where(c => !string.IsNullOrWhiteSpace(c) && !_villageNameCache.ContainsKey(c))
            .Distinct()
            .ToList();

        if (codes.Count == 0) return;

        try
        {
            var result = await _regionService.GetVillagesByCodesAsync(codes, ct);
            if (result.IsFailure)
            {
                _logger.Warn($"地址预热：批量查村失败（{codes.Count} 个编码，本次跳过）: {result.ErrorCode} {result.Message}");
                return;
            }

            foreach (var v in result.Value ?? new List<RegionVillage>())
            {
                if (string.IsNullOrEmpty(v.VillageCode)) continue;
                _villageNameCache[v.VillageCode] = StripVillageSuffix(v.VillageName);
            }
        }
        catch (OperationCanceledException)
        {
            // 页面离开/令牌取消属正常路径，静默（原实现 catch {} 也吞，保持不变）
        }
        catch (Exception ex)
        {
            // 原实现是裸 catch {}：预热失败零痕迹，地址退化时无从定位
            _logger.Warn($"地址预热：批量查村异常（{codes.Count} 个编码）: {ex.Message}");
        }
    }

    /// <summary>去掉村级名称后缀（与历史口径完全一致，勿改顺序）</summary>
    private static string StripVillageSuffix(string? villageName) =>
        (villageName ?? string.Empty)
            .Replace("村委会", "")
            .Replace("居民委员会", "")
            .Replace("社区", "");

    /// <summary>
    /// 获取村名（去后缀）
    /// </summary>
    public string GetVillageName(string? communityCode)
    {
        if (string.IsNullOrWhiteSpace(communityCode)) return "";
        return _villageNameCache.TryGetValue(communityCode, out var name) ? name : "";
    }

    /// <summary>
    /// 拼接完整地址：村名 + 门牌号
    /// </summary>
    public string BuildAddress(string? communityCode, string? detailAddress)
    {
        if (string.IsNullOrWhiteSpace(detailAddress))
        {
            var village = GetVillageName(communityCode);
            return !string.IsNullOrWhiteSpace(village) ? village : "";
        }

        // 详细地址已包含镇/村名（完整地址），直接使用，不再拼接
        if (detailAddress.Contains("镇") || detailAddress.Contains("村") || detailAddress.Contains("街道"))
            return detailAddress;

        var villageName = GetVillageName(communityCode);
        if (string.IsNullOrWhiteSpace(villageName))
            return detailAddress;
        return $"{villageName}{detailAddress}";
    }

    /// <summary>
    /// 异步拼接完整地址（首次自动查询，后续走缓存）
    /// </summary>
    public async Task<string> BuildAddressAsync(string? communityCode, string? detailAddress, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(communityCode))
            return detailAddress ?? "";

        var cacheKey = $"{communityCode}|{detailAddress}";
        if (_fullAddressCache.TryGetValue(cacheKey, out var cached))
            return cached;

        // 确保已预加载
        if (!_villageNameCache.ContainsKey(communityCode))
            await WarmupAsync(new[] { communityCode }, ct);

        var result = BuildAddress(communityCode, detailAddress);

        // 每个不同门牌号都会产生一条缓存，长时间运行需防止无界增长
        if (_fullAddressCache.Count >= FullAddressCacheLimit)
            _fullAddressCache.Clear();
        _fullAddressCache[cacheKey] = result;
        return result;
    }

    /// <summary>
    /// 预加载一批 village_code 对应的"县/镇/村"名称。
    /// 村一级走**批量**接口一次取回——原实现逐条 <c>GetVillageByCodeAsync</c>，
    /// 是 N 次打库（快照缺失）或 N × 全量扫村表（快照已加载）。
    /// 镇/县两级按 ID 记忆化，实际查询次数 = 不同乡镇/县区数，与编码条数无关。
    /// </summary>
    public async Task WarmupFullAddressAsync(IEnumerable<string> communityCodes, CancellationToken ct = default)
    {
        var codes = communityCodes
            .Where(c => !string.IsNullOrWhiteSpace(c) && !_communityNameCache.ContainsKey(c!))
            .Distinct()
            .ToList();

        if (codes.Count == 0) return;

        List<RegionVillage> villages;
        try
        {
            var batch = await _regionService.GetVillagesByCodesAsync(codes, ct);
            if (batch.IsFailure)
            {
                _logger.Warn($"地址预热：批量查村失败（{codes.Count} 个编码，本次跳过）: {batch.ErrorCode} {batch.Message}");
                return;
            }
            villages = batch.Value ?? new List<RegionVillage>();
        }
        catch (OperationCanceledException)
        {
            return;   // 页面离开/令牌取消属正常路径
        }
        catch (Exception ex)
        {
            _logger.Warn($"地址预热：批量查村异常（{codes.Count} 个编码）: {ex.Message}");
            return;
        }

        var found = new HashSet<string>(StringComparer.Ordinal);
        foreach (var v in villages)
        {
            if (string.IsNullOrEmpty(v.VillageCode)) continue;
            found.Add(v.VillageCode!);
            try
            {
                var resolved = await ResolveNamesFromVillageAsync(v, ct);
                // null = 查询失败：**不写缓存**，下次重试；写进去会把偶发故障固化成"地址永远缺三级"
                if (resolved.HasValue)
                    _communityNameCache[v.VillageCode!] = resolved.Value;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.Warn($"地址预热：解析县/镇/村失败 Code={v.VillageCode}: {ex.Message}");
            }
        }

        // 批量里没有的编码 = 查无此村：按原口径记空三元组，避免下次重复预热
        foreach (var code in codes)
        {
            if (!found.Contains(code!.Trim()))
                _communityNameCache[code] = (string.Empty, string.Empty, string.Empty);
        }
    }

    /// <summary>
    /// 拼接完整地址：县 + 镇 + 村(去后缀) + 详细门牌。
    /// 逐级补全：详细地址缺哪一级就补哪一级，已含的层级不重复叠加。
    /// 需先调用 <see cref="WarmupFullAddressAsync"/>（未预加载时退化为村名+门牌）。
    /// </summary>
    public string BuildFullAddress(string? communityCode, string? detailAddress)
    {
        var detail = detailAddress?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(communityCode))
            return detail;

        if (!_communityNameCache.TryGetValue(communityCode, out var names))
        {
            // 未预加载：退化为原有拼接（村名 + 门牌）
            return BuildAddress(communityCode, detail);
        }

        var prefix = string.Empty;

        if (!string.IsNullOrEmpty(names.County) && !detail.Contains(names.County))
            prefix += names.County;

        var hasTownLevel = detail.Contains("镇") || detail.Contains("街道");
        if (!hasTownLevel && !string.IsNullOrEmpty(names.Town) && !detail.Contains(names.Town))
            prefix += names.Town;

        if (!detail.Contains("村") && !string.IsNullOrEmpty(names.Village) && !detail.Contains(names.Village))
            prefix += names.Village;

        return prefix + detail;
    }

    /// <summary>
    /// 由已批量取回的村级实体解析「县 / 镇 / 村」（村名去后缀）。
    /// 返回 <c>null</c> = **查询失败**（调用方不得缓存，下次重试）；
    /// 镇/县按 ID 记忆化 —— 同一批里重复的乡镇/县区只查一次。
    /// 原实现把失败与"未找到"混为一谈，且会把失败结果写进缓存——一次偶发 DB 故障
    /// 就让该编码的地址在进程存活期内永久缺县/镇（同 _villageCodeCache 的历史缺陷）。
    /// </summary>
    private async Task<(string County, string Town, string Village)?> ResolveNamesFromVillageAsync(
        RegionVillage village, CancellationToken ct)
    {
        var villageName = StripVillageSuffix(village.VillageName);

        if (!_townInfoById.TryGetValue(village.TownId, out var townInfo))
        {
            var townResult = await _regionService.GetTownByIdAsync(village.TownId, ct);
            if (townResult.IsFailure)
            {
                _logger.Warn($"地址解析：查乡镇失败（不缓存）TownId={village.TownId}: {townResult.ErrorCode} {townResult.Message}");
                return null;
            }
            townInfo = townResult.Value != null
                ? (townResult.Value.TownName, townResult.Value.CountyId)
                : (string.Empty, 0);
            _townInfoById[village.TownId] = townInfo;
        }

        var townName = townInfo.TownName;
        var countyId = townInfo.CountyId;
        var countyName = string.Empty;

        if (countyId > 0)
        {
            if (!_countyNameById.TryGetValue(countyId, out countyName))
            {
                var countyResult = await _regionService.GetCountyByIdAsync(countyId, ct);
                if (countyResult.IsFailure)
                {
                    _logger.Warn($"地址解析：查县/区失败（不缓存）CountyId={countyId}: {countyResult.ErrorCode} {countyResult.Message}");
                    return null;
                }
                countyName = countyResult.Value != null ? countyResult.Value.CountyName : string.Empty;
                _countyNameById[countyId] = countyName;
            }
        }

        return (countyName ?? string.Empty, townName ?? string.Empty, villageName);
    }

    /// <summary>
    /// 从身份证提取性别（第17位：奇数=男，偶数=女）
    /// </summary>
    public static string ExtractGenderFromIdCard(string? idCard)
    {
        if (string.IsNullOrWhiteSpace(idCard) || idCard.Length != 18) return "-";
        if (!int.TryParse(idCard[16].ToString(), out var digit)) return "-";
        return digit % 2 == 1 ? "男" : "女";
    }

    /// <summary>
    /// 从身份证提取年龄（第7-14位=出生日期）
    /// </summary>
    public static string ExtractAgeFromIdCard(string? idCard)
    {
        if (string.IsNullOrWhiteSpace(idCard) || idCard.Length != 18) return "-";
        if (DateTime.TryParseExact(idCard[6..14], "yyyyMMdd", null,
            System.Globalization.DateTimeStyles.None, out var birthDate))
        {
            var age = DateTime.Today.Year - birthDate.Year;
            if (DateTime.Today < birthDate.AddYears(age)) age--;
            return age.ToString();
        }
        return "-";
    }
}
