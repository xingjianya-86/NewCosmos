namespace NewCosmos.Constants;

/// <summary>
/// 照料类型常量
/// </summary>
public static class CaregiverTypeConstants
{
    /// <summary>
    /// 无照料
    /// </summary>
    public const string NONE = "None";
    public const string FAMILY = "Family";
    public const string INSTITUTION = "Institution";

    /// <summary>
    /// 所有照料类型
    /// </summary>
    public static readonly string[] All = { NONE, FAMILY, INSTITUTION };

    /// <summary>
    /// 是否为有效照料类型
    /// </summary>
    public static bool IsValid(string type) => All.Contains(type);

    /// <summary>
    /// 是否需要照料人
    /// </summary>
    public static bool RequiresCaregiver(string type) => type == FAMILY || type == INSTITUTION;
}
