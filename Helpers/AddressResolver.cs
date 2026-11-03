using System.Collections.Concurrent;
using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;
using NewCosmos.Services.System;

namespace NewCosmos.Helpers;

/// <summary>
/// 统一地址拼装服务 — 所有模板输出的地址都走这里。
/// <para>两种入参语义**分家**，禁止混用：</para>
/// <list type="number">
/// <item><see cref="BuildAddressFromText"/>：入参是**名称文本**（district/town/community/address 四列），
/// 纯字符串去重拼装，<c>nc_biz_applications</c> 侧一律走它；</item>
/// <item><see cref="BuildAddress"/> / <see cref="BuildFullAddress"/>：入参 <c>communityCode</c> 是
/// **village_code**（资产核查、月报等 community 列存编码的表），依赖区划表查询。</item>
/// </list>
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

    /// <summary>
    /// 去掉村级名称后缀。
    /// 「村民委员会」必须排在最前：它内部**不含**连续的「村委会」三字（村-民-委-员-会），
    /// 若按旧顺序先替换「村委会」会落空，导致「柞木村村民委员会」清理后原样残留（2026-11-02 实测缺陷）。
    /// </summary>
    private static string StripVillageSuffix(string? villageName) =>
        (villageName ?? string.Empty)
            .Replace("村民委员会", "")
            .Replace("村委会", "")
            .Replace("居民委员会", "")
            .Replace("社区", "");

    /// <summary>
    /// 规范化村级名称（**地址用途专用**，与 <see cref="StripVillageSuffix"/> 分工不同，勿混用）：
    /// <list type="bullet">
    /// <item><c>柞木村村民委员会</c> → <c>柞木村</c>（去掉「村民委员会」，村字已在其前）</item>
    /// <item><c>土甸子村委会</c> → <c>土甸子村</c>（「村委会」→「村」，否则丢掉「村」字）</item>
    /// <item><c>东关居民委员会</c> → <c>东关社区</c></item>
    /// </list>
    /// <see cref="StripVillageSuffix"/> 会把 <c>土甸子村委会</c> 变成 <c>土甸子</c>（丢字），
    /// 故地址拼装不得走它；它只服务「村名缓存」这一既有口径。
    /// 只处理**结尾**后缀，地址中段的「XX村」不动。
    /// </summary>
    private static string NormalizeVillageForAddress(string? name)
    {
        var s = name?.Trim() ?? string.Empty;
        if (s.EndsWith("村民委员会", StringComparison.Ordinal)) return s[..^5];
        if (s.EndsWith("居民委员会", StringComparison.Ordinal)) return s[..^5] + "社区";
        if (s.EndsWith("村委会", StringComparison.Ordinal)) return s[..^3] + "村";
        return s;
    }

    /// <summary>
    /// 清理单个地址级：去首尾空白；<c>-</c> / 空 视为无值（库中确有 3 行存了占位横杠，
    /// 高龄表 detail_address 亦实测存 <c>-</c>，直接拼接会打出「…复兴村委会-」）。
    /// </summary>
    public static string CleanLevel(string? value)
    {
        var s = value?.Trim() ?? string.Empty;
        return s is "-" or "—" ? string.Empty : s;
    }

    /// <summary>
    /// **文本级地址拼装唯一入口**（纯字符串，不查库、可单测）。
    /// 适用：<c>nc_biz_applications</c> 的 <c>district/town/community/address</c> 四列都是**名称文本**的场景。
    /// ⚠️ 与 <c>BuildAddress(communityCode, …)</c> 入参语义不同——那个吃 village_code，
    /// 这个吃村名；村名喂给 code 版会永远查不到缓存（2026-11-02 修复的分步路径缺陷）。
    ///
    /// <para>规则：以 <paramref name="detail"/> 为基底，县/镇/村三级**已含则跳过、缺则插入**；
    /// 插入按**游标定位**（补在已含级之后），不是整串前置——否则 detail 只带县缺镇村时会拼成
    /// 「柳树镇柞木村林口县…」这种乱序。输出 = 县 + 镇 + 村 + 门牌（不含省/市，与户籍地址口径对齐）。</para>
    ///
    /// <para>实测数据六形态对照（district=林口县, town=柳树镇, community=柞木村村民委员会）：</para>
    /// <list type="table">
    /// <item><term>detail=<c>林口县柳树镇柞木村委会</c></term><description>→ <c>林口县柳树镇柞木村</c>（三级全含，只清后缀）</description></item>
    /// <item><term>detail=<c>柳树镇土甸子村</c></term><description>→ <c>林口县柳树镇土甸子村</c>（只补县）</description></item>
    /// <item><term>detail=<c>土甸子村委会</c></term><description>→ <c>林口县柳树镇土甸子村</c>（补县镇）</description></item>
    /// <item><term>detail=<c>工农屯245号</c></term><description>→ <c>林口县柳树镇柞木村工农屯245号</c>（三级全补）</description></item>
    /// <item><term>detail=<c>林口县工农屯245号</c></term><description>→ <c>林口县柳树镇柞木村工农屯245号</c>（镇村插在县之后）</description></item>
    /// <item><term>detail=空 或 <c>-</c></term><description>→ <c>林口县柳树镇柞木村</c>（不再整格变空）</description></item>
    /// </list>
    ///
    /// <para>已知边界：detail 自带**外县**名（数据冲突）时按「已含县字样」跳过、不重复叠加本县名，
    /// 该冲突属数据问题、不在拼装层强行纠正。</para>
    /// </summary>
    public static string BuildAddressFromText(string? district, string? town, string? community, string? detail)
    {
        var county = CleanLevel(district);
        var townName = CleanLevel(town);
        var village = NormalizeVillageForAddress(community);
        // detail 同样做结尾后缀规范化（全地址形态「…柞木村委会」→「…柞木村」）
        var result = NormalizeVillageForAddress(CleanLevel(detail));
        var cursor = 0;

        // 每级的匹配 token：本级名称 + 该级的层级字样（名称缺失/写法不一时靠层级字样兜底）
        cursor = ApplyLevel(result, cursor, county, new[] { county, "县" }, out result);
        cursor = ApplyLevel(result, cursor, townName, new[] { townName, "镇", "街道", "乡" }, out result);
        _ = ApplyLevel(result, cursor, village, new[] { village, "村" }, out result);

        return result;
    }

    /// <summary>
    /// 应用一个地址级：从 <paramref name="cursor"/> 起找得到 → 游标推进到该级之后（不重复拼）；
    /// 找不到 → 在游标处插入该级、游标推进插入长度。返回新游标，结果经 <paramref name="output"/> 返回。
    /// </summary>
    private static int ApplyLevel(string source, int cursor, string level, string[] tokens, out string output)
    {
        output = source;
        if (string.IsNullOrEmpty(level)) return cursor;

        var best = -1;
        var bestLen = 0;
        foreach (var token in tokens)
        {
            if (string.IsNullOrEmpty(token)) continue;
            var i = source.IndexOf(token, cursor, StringComparison.Ordinal);
            if (i >= 0 && (best < 0 || i < best))
            {
                best = i;
                bestLen = token.Length;
            }
        }

        if (best >= 0) return best + bestLen;

        output = source.Insert(cursor, level);
        return cursor + level.Length;
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
