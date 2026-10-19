namespace NewCosmos.Constants;

/// <summary>
/// 后补追缴相关常量
/// </summary>
public static class RecoveryConstants
{
    /// <summary>
    /// 录入模式
    /// </summary>
    public const string INPUT_MODE_SEARCH = "Search";
    public const string INPUT_MODE_MANUAL = "Manual";

    /// <summary>
    /// 来源类型
    /// </summary>
    public const string SOURCE_TYPE_RURAL_SUBSISTENCE = "RuralSubsistence";
    public const string SOURCE_TYPE_URBAN_SUBSISTENCE = "UrbanSubsistence";
    public const string SOURCE_TYPE_RIGID_EXPENDITURE = "RigidExpenditure";
    public const string SOURCE_TYPE_TEMP_RELIEF = "TempRelief";
    public const string SOURCE_TYPE_ELDERLY = "Elderly";
    public const string SOURCE_TYPE_OTHER = "Other";

    /// <summary>
    /// 来源类型显示名称映射
    /// </summary>
    public static readonly Dictionary<string, string> SourceTypeDisplayNames = new()
    {
        { SOURCE_TYPE_RURAL_SUBSISTENCE, "农村低保" },
        { SOURCE_TYPE_URBAN_SUBSISTENCE, "城市低保" },
        { SOURCE_TYPE_RIGID_EXPENDITURE, "刚性支出" },
        { SOURCE_TYPE_TEMP_RELIEF, "临时救助" },
        { SOURCE_TYPE_ELDERLY, "普惠高龄" },
        { SOURCE_TYPE_OTHER, "其他" }
    };

    /// <summary>
    /// 状态
    /// </summary>
    public const string STATUS_DRAFT = "Draft";
    public const string STATUS_CONFIRMED = "Confirmed";
    public const string STATUS_PRINTED = "Printed";

    /// <summary>
    /// 获取来源类型显示名称
    /// </summary>
    public static string GetSourceTypeDisplayName(string sourceType)
    {
        return SourceTypeDisplayNames.TryGetValue(sourceType, out var displayName)
            ? displayName
            : sourceType;
    }

    /// <summary>
    /// 获取所有来源类型列表
    /// </summary>
    public static List<KeyValuePair<string, string>> GetAllSourceTypes()
    {
        return SourceTypeDisplayNames.ToList();
    }

    // ── 应追缴总额：金额来源与自动计算口径（2026-10-19） ──
    // 口径铁律：recovery_amount 是唯一事实来源（表单/列表/打印/落库同源），
    // 「月保障额 × 追缴月数」只是自动计算的一种方式；算不出时必须人工填写并留原因。

    /// <summary>
    /// 金额来源：变更记录分段累加（依据历史变更记录逐月取标准、按段求和）
    /// </summary>
    public const string AMOUNT_SOURCE_SEGMENT = "Segment";

    /// <summary>
    /// 金额来源：月标准 × 追缴月数（无历史变更记录时按单一月标准定额计算）
    /// </summary>
    public const string AMOUNT_SOURCE_FLAT = "Flat";

    /// <summary>
    /// 金额来源：人工核实填写（自动算不出或自动值与实际不符）
    /// </summary>
    public const string AMOUNT_SOURCE_MANUAL = "Manual";

    /// <summary>
    /// 金额来源显示名称映射
    /// </summary>
    public static readonly Dictionary<string, string> AmountSourceDisplayNames = new()
    {
        { AMOUNT_SOURCE_SEGMENT, "自动分段计算" },
        { AMOUNT_SOURCE_FLAT, "月标准×月数" },
        { AMOUNT_SOURCE_MANUAL, "手工填写" }
    };

    /// <summary>
    /// 获取金额来源显示名称
    /// </summary>
    public static string GetAmountSourceDisplayName(string? amountSource)
    {
        return amountSource != null && AmountSourceDisplayNames.TryGetValue(amountSource, out var displayName)
            ? displayName
            : (amountSource ?? string.Empty);
    }

    /// <summary>
    /// 自动计算上限：追缴跨度超过该月数不再自动计算，转人工填写
    /// </summary>
    public const int MaxAutoMonths = 36;

    /// <summary>
    /// 分段依据：保障金变更记录（nc_biz_change_records）
    /// </summary>
    public const string SEGMENT_SOURCE_CHANGE = "ChangeRecord";

    /// <summary>
    /// 分段依据：台账/申请主表现值
    /// </summary>
    public const string SEGMENT_SOURCE_LEDGER = "Ledger";

    /// <summary>
    /// 分段依据：渐退期原保障金（nc_biz_grace_periods.original_guarantee_amount）
    /// </summary>
    public const string SEGMENT_SOURCE_GRACE = "GracePeriod";

    // ── 无法自动计算的原因文案（RecoveryConstants.RESULT_REASON_*） ──

    /// <summary>原因：追缴起止月无效</summary>
    public const string REASON_INVALID_RANGE = "追缴起止月无效，请重新选择追缴周期";

    /// <summary>原因：追缴月数非正</summary>
    public const string REASON_NO_MONTHS = "追缴月数必须大于 0";

    /// <summary>原因：无任何历史月保障标准可依据（历史数据不全）</summary>
    public const string REASON_NO_HISTORY = "无历史月保障标准可依据（历史数据不全），请手工填写应追缴总额";

    /// <summary>原因模板：某月之前无历史月保障标准（{0} = yyyy-MM）</summary>
    public const string REASON_NO_HISTORY_MONTH = "{0} 之前无历史月保障标准（历史数据不全），请手工填写应追缴总额";

    /// <summary>原因模板：追缴跨度超上限（{0} = 上限月数）</summary>
    public const string REASON_TOO_LONG = "追缴跨度超过 {0} 个月，不做自动计算，请手工填写应追缴总额";

    /// <summary>原因：自动算得金额为 0（无有效保障金月份）</summary>
    public const string REASON_ZERO_AMOUNT = "按历史标准算得金额为 0（无有效保障金月份），请手工填写应追缴总额";

    /// <summary>原因：历史数据读取失败</summary>
    public const string REASON_HISTORY_LOAD_FAILED = "历史数据读取失败（详见日志），请手工填写应追缴总额";

    /// <summary>手工填写的默认原因提示（占位符与缺省留痕）</summary>
    public const string MANUAL_NOTE_PLACEHOLDER = "请说明手工填写原因（如：历史数据不全、按业务核实金额等）";

    /// <summary>
    /// 分段依据显示名称映射
    /// </summary>
    public static readonly Dictionary<string, string> SegmentSourceDisplayNames = new()
    {
        { SEGMENT_SOURCE_CHANGE, "变更记录" },
        { SEGMENT_SOURCE_LEDGER, "台账现值" },
        { SEGMENT_SOURCE_GRACE, "渐退期原标准" }
    };

    /// <summary>
    /// 获取分段依据显示名称
    /// </summary>
    public static string GetSegmentSourceDisplayName(string? source)
    {
        return source != null && SegmentSourceDisplayNames.TryGetValue(source, out var displayName)
            ? displayName
            : (source ?? string.Empty);
    }
}
