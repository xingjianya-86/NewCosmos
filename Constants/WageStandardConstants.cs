namespace NewCosmos.Constants;

/// <summary>
/// 黑龙江省最低工资标准档位（黑政规〔2025〕1号，2025-12-01 起执行，有效期三年）。
///
/// 职责边界（重要）：
///   · 档位【数值】存 nc_config_standards（standard_type = MinWageLevel1/2/3），由标准配置管理维护，
///     本类只存「区县 → 档位」映射与政策文号——数值不在代码中硬编码（AGENTS §5.6、§11）。
///     政策调整时只改配置表，不改代码。
///   · 本类只服务于文书中的【事实性参照】（说明劳动能力对应的收入能力 + 就业引导）。
///     最低工资标准在《牡丹江市低收入人口认定实施细则》中仅用于「就业必要成本扣减」（第十六条）
///     与「特困照料护理标准」（第四十五条），**不得**用于给有劳动能力未就业成员核定家庭收入——
///     那属于无法定依据的收入核定，文书一旦被申诉即会被推翻，并连带动摇拒保决定本身。
/// </summary>
public static class WageStandardConstants
{
    /// <summary>第一档 standard_type：哈尔滨市区（呼兰/阿城/双城除外）、大庆市区</summary>
    public const string Level1Type = "MinWageLevel1";

    /// <summary>第二档 standard_type：牡丹江市区及绥芬河市、齐齐哈尔/佳木斯/鸡西/双鸭山/七台河/鹤岗/绥化市区等</summary>
    public const string Level2Type = "MinWageLevel2";

    /// <summary>第三档 standard_type：伊春市区、黑河市区，全省其他各县（市）——林口县属本档</summary>
    public const string Level3Type = "MinWageLevel3";

    /// <summary>政策文号（写入文书时随数值一同输出，便于群众核对依据）</summary>
    public const string PolicyDocument = "黑政规〔2025〕1号";

    /// <summary>现行标准生效日（同上，写入文书）</summary>
    public const string EffectiveDate = "2025-12-01";

    /// <summary>
    /// 区县（nc_biz_applications.district）→ standard_type 映射。
    /// 键必须与库中 district 实际存储值完全一致（含"县/区/市"后缀，区分大小写不适用）。
    /// 新增区县时在此补充一行即可，无需改动任何调用点。
    /// </summary>
    private static readonly Dictionary<string, string> DistrictLevelMap = new(StringComparer.Ordinal)
    {
        // —— 牡丹江市（本市业务范围）——
        // 林口县：牡丹江市下辖县，既非市区亦非绥芬河市 → 黑政规〔2025〕1号第三档
        ["林口县"] = Level3Type,
        ["绥芬河市"] = Level2Type,
        ["海林市"] = Level3Type,
        ["宁安市"] = Level3Type,
        ["穆棱市"] = Level3Type,
        ["东宁市"] = Level3Type,
        ["林口"] = Level3Type,          // 录入端可能省略后缀
        ["爱民区"] = Level2Type,
        ["东安区"] = Level2Type,
        ["阳明区"] = Level2Type,
        ["西安区"] = Level2Type,

        // —— 其他地市（档案跨区导入时的兜底覆盖）——
        ["哈尔滨市"] = Level1Type,
        ["大庆市"] = Level1Type,
        ["齐齐哈尔市"] = Level2Type,
        ["佳木斯市"] = Level2Type,
        ["鸡西市"] = Level2Type,
        ["双鸭山市"] = Level2Type,
        ["七台河市"] = Level2Type,
        ["鹤岗市"] = Level2Type,
        ["绥化市"] = Level2Type,
        ["伊春市"] = Level3Type,
        ["黑河市"] = Level3Type,
        ["大兴安岭地区"] = Level2Type,
    };

    /// <summary>
    /// 按区县解析应适用的最低工资档位 standard_type。
    /// 未收录区县时按方案 A 回退第三档（最保守口径，不阻断文书生成），由调用方记 Warn 日志。
    /// </summary>
    /// <param name="district">nc_biz_applications.district 的值，可为 null</param>
    /// <returns>standard_type；district 为空时返回第三档</returns>
    public static string ResolveLevelType(string? district)
    {
        if (string.IsNullOrWhiteSpace(district))
            return Level3Type;

        var trimmed = district.Trim();

        // 1) 精确匹配（首选）
        if (DistrictLevelMap.TryGetValue(trimmed, out var exact))
            return exact;

        // 2) 包含匹配：district 可能带前缀（如"牡丹江市林口县"）或与市级名混合，
        //    按映射键长度降序探测，避免短键（如"林口"）抢先命中长名中的子串
        foreach (var kv in DistrictLevelMap.OrderByDescending(p => p.Key.Length))
        {
            if (trimmed.Contains(kv.Key, StringComparison.Ordinal))
                return kv.Value;
        }

        // 3) 兜底：未收录 → 第三档（调用方须记 Warn，便于后续补全映射）
        return Level3Type;
    }

    /// <summary>该区县是否已收录进映射（供调用方判断是否需要记 Warn）</summary>
    public static bool IsDistrictKnown(string? district)
    {
        if (string.IsNullOrWhiteSpace(district))
            return false;

        var trimmed = district.Trim();
        if (DistrictLevelMap.ContainsKey(trimmed))
            return true;

        return DistrictLevelMap.Keys.Any(k => trimmed.Contains(k, StringComparison.Ordinal));
    }
}
