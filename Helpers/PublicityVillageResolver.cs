using System.Text.RegularExpressions;

namespace NewCosmos.Helpers;

/// <summary>
/// 公示单村名解析器：从导入库家庭地址文本中提取并标准化村名（公示单按村张贴）。
/// 规则沿用旧脚本 Scripts/archive/GeneratePublicity.py：
/// 1) 优先取 community（村/社区）字段，清理"村委会/委员会/村公所/居委会/社区"等后缀；
/// 2) 否则从 address 中按"镇/乡"之后的正则提取村名；
/// 3) 应用别名映射（嘎库→戛库等）与自然村→行政村映射；
/// 4) 命中非本地关键词（外市县等）或无村名时归入"未分组"。
/// </summary>
public static class PublicityVillageResolver
{
    /// <summary>非本地地址排除关键词（沿用旧脚本，命中视为无法归村）</summary>
    private static readonly string[] NonLocalKeywords = { "北京市", "东宁市", "爱民区", "林业局" };

    /// <summary>村名别名 → 官方标准名</summary>
    private static readonly Dictionary<string, string> VillageAliases = new(StringComparer.Ordinal)
    {
        ["嘎库村"] = "戛库村",
        ["土甸村"] = "土甸子村",
        ["做木村"] = "柞木村",
        ["土侭子村"] = "土甸子村",   // 错别字（侭应为甸），当前库历史数据
        ["土仍子村"] = "土甸子村",   // 错别字变体（防御）
        ["土旬子村"] = "土甸子村"    // 错别字变体（防御）
    };

    /// <summary>自然村/屯 → 所属行政村</summary>
    private static readonly Dictionary<string, string> SubVillageMap = new(StringComparer.Ordinal)
    {
        ["工农村"] = "柳树村",
        ["宝泉村"] = "柳树村",
        ["长岭屯村"] = "戛库村",
        ["富强村"] = "三道村",
        ["丰谷屯"] = "三道村",
        ["七星村"] = "柳毛村",
        ["光明村"] = "柳宝村",
        ["光明屯村"] = "柳宝村",
        ["万水村"] = "复兴村",
        ["永富屯"] = "双河村",
        ["北海村"] = "双河村",
        ["二北沟村"] = "宝山村",
        ["双宝村"] = "宝山村",
        ["吴家孔屯村"] = "土甸子村",
        ["石青村"] = "土甸子村",
        ["龙山村"] = "柞木村",
        ["农场村"] = "柞木村",
        ["荣华村"] = "柞木村",
        ["晨光村"] = "榆树村",
        ["直村"] = "镇直",
        ["镇直村"] = "镇直",
        ["柳树镇本级"] = "镇直",
        ["柳树镇本级村"] = "镇直",
        ["镇本级"] = "镇直",
        ["西村"] = "柳西村"
    };

    /// <summary>统一入口：从家庭地址/村字段解析并标准化村名，无法解析返回"未分组"</summary>
    public static string Resolve(string? address, string? community)
    {
        var raw = ResolveRaw(address, community);
        if (string.IsNullOrWhiteSpace(raw))
            return "未分组";
        return Normalize(raw);
    }

    private static string ResolveRaw(string? address, string? community)
    {
        // ① 优先 community 字段
        // 后缀逐一剥离（顺序：长的村民委员会 在 村委会 之前，避免只剥掉"委员会"残留"村"字）
        if (!string.IsNullOrWhiteSpace(community))
        {
            var cleaned = Regex.Replace(community,
                @"(村民委员会|居民委员会|村公所|居委会|村委会|委员会|社区|$)", "");
            if (!string.IsNullOrWhiteSpace(cleaned))
                return EnsureSuffix(cleaned);
        }

        if (string.IsNullOrWhiteSpace(address))
            return "";

        // 非本地地址（外市县等）无法归村
        if (NonLocalKeywords.Any(address.Contains))
            return "";

        // ② 从 address 中"镇/乡"之后提取
        var afterTown = Regex.Match(address, @"[镇乡](.+)");
        if (afterTown.Success)
        {
            var after = Regex.Replace(afterTown.Groups[1].Value,
                @"(村民委员会|居民委员会|村公所|居委会|村委会|委员会|社区)", "");
            var village = Regex.Match(after, @"^([\u4e00-\u9fff]+?村)");
            if (village.Success)
                return village.Groups[1].Value;
            var fallback = Regex.Match(after, @"^([\u4e00-\u9fff]+)");
            if (fallback.Success)
                return EnsureSuffix(fallback.Groups[1].Value);
        }

        // ③ 直接匹配 "XX村"
        var direct = Regex.Match(address, @"([\u4e00-\u9fff]+?村)");
        if (direct.Success)
            return direct.Groups[1].Value;

        return "";
    }

    private static string EnsureSuffix(string name)
    {
        return name.EndsWith("村", StringComparison.Ordinal)
            || name.EndsWith("社区", StringComparison.Ordinal)
            ? name
            : name + "村";
    }

    private static string Normalize(string raw)
    {
        if (VillageAliases.TryGetValue(raw, out var alias)) return alias;
        if (SubVillageMap.TryGetValue(raw, out var sub)) return sub;
        return raw;
    }
}
