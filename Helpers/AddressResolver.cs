using System.Collections.Concurrent;
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
    private readonly ConcurrentDictionary<string, string> _villageNameCache = new();
    private readonly ConcurrentDictionary<string, string> _fullAddressCache = new();

    // 完整地址（县+镇+村）解析缓存：社区代码 → 三级行政区名称
    private readonly ConcurrentDictionary<string, (string County, string Town, string Village)> _communityNameCache = new();
    private readonly ConcurrentDictionary<int, (string TownName, int CountyId)> _townInfoById = new();
    private readonly ConcurrentDictionary<int, string> _countyNameById = new();

    public AddressResolver(IRegionService regionService)
    {
        _regionService = regionService;
    }

    /// <summary>
    /// 预加载一批 village_code 对应的村名，避免逐条查询
    /// </summary>
    public async Task WarmupAsync(IEnumerable<string> communityCodes, CancellationToken ct = default)
    {
        var codes = communityCodes
            .Where(c => !string.IsNullOrWhiteSpace(c) && !_villageNameCache.ContainsKey(c))
            .Distinct()
            .ToList();

        foreach (var code in codes)
        {
            try
            {
                var vr = await _regionService.GetVillageByCodeAsync(code, ct);
                if (vr.IsSuccess && vr.Value != null)
                {
                    var name = vr.Value.VillageName
                        .Replace("村委会", "")
                        .Replace("居民委员会", "")
                        .Replace("社区", "");
                    _villageNameCache[code] = name;
                }
            }
            catch { }
        }
    }

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
    /// 预加载一批 village_code 对应的"县/镇/村"名称，避免逐条查询。
    /// </summary>
    public async Task WarmupFullAddressAsync(IEnumerable<string> communityCodes, CancellationToken ct = default)
    {
        var codes = communityCodes
            .Where(c => !string.IsNullOrWhiteSpace(c) && !_communityNameCache.ContainsKey(c!))
            .Distinct()
            .ToList();

        foreach (var code in codes)
        {
            try
            {
                _communityNameCache[code!] = await ResolveCommunityNamesAsync(code!, ct);
            }
            catch { }
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

    private async Task<(string County, string Town, string Village)> ResolveCommunityNamesAsync(string communityCode, CancellationToken ct)
    {
        var vr = await _regionService.GetVillageByCodeAsync(communityCode, ct);
        if (vr.IsFailure || vr.Value == null)
            return (string.Empty, string.Empty, string.Empty);

        var village = vr.Value.VillageName
            .Replace("村委会", "")
            .Replace("居民委员会", "")
            .Replace("社区", "");

        var townName = string.Empty;
        var countyName = string.Empty;
        var countyId = 0;

        if (!_townInfoById.TryGetValue(vr.Value.TownId, out var townInfo))
        {
            var townResult = await _regionService.GetTownByIdAsync(vr.Value.TownId, ct);
            townInfo = townResult.IsSuccess && townResult.Value != null
                ? (townResult.Value.TownName, townResult.Value.CountyId)
                : (string.Empty, 0);
            _townInfoById[vr.Value.TownId] = townInfo;
        }
        townName = townInfo.TownName;
        countyId = townInfo.CountyId;

        if (countyId > 0)
        {
            if (!_countyNameById.TryGetValue(countyId, out countyName))
            {
                var countyResult = await _regionService.GetCountyByIdAsync(countyId, ct);
                countyName = countyResult.IsSuccess && countyResult.Value != null ? countyResult.Value.CountyName : string.Empty;
                _countyNameById[countyId] = countyName;
            }
        }

        return (countyName ?? string.Empty, townName ?? string.Empty, village);
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
