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
}
