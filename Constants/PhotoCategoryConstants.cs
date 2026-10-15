namespace NewCosmos.Constants;

/// <summary>
/// 档案照片类别常量（手机端采集身份证/户口本等照片存档的分类标定）。
/// 类别相对固定，集中在此维护；后续扩展直接加常量即可。
/// </summary>
public static class PhotoCategoryConstants
{
    public const string ID_CARD = "ID_CARD";                          // 身份证
    public const string HOUSEHOLD_REGISTER = "HOUSEHOLD_REGISTER";    // 户口本
    public const string PUBLICITY_CHECK = "PUBLICITY_CHECK";          // 公示/核查照片
    public const string OTHER_CERT = "OTHER_CERT";                    // 其他证件照

    /// <summary>全部有效类别（供 Picker 绑定）。</summary>
    public static readonly (string Code, string Name)[] All =
    {
        (ID_CARD, "身份证"),
        (HOUSEHOLD_REGISTER, "户口本"),
        (PUBLICITY_CHECK, "公示/核查照片"),
        (OTHER_CERT, "其他证件照"),
    };

    /// <summary>类别代码 → 中文显示名。</summary>
    public static string GetDisplayName(string? category) => category switch
    {
        ID_CARD => "身份证",
        HOUSEHOLD_REGISTER => "户口本",
        PUBLICITY_CHECK => "公示/核查照片",
        OTHER_CERT => "其他证件照",
        _ => "其他"
    };
}
