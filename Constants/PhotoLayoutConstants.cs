namespace NewCosmos.Constants;

/// <summary>
/// 照片打印版式常量（手机照片远程打印到 PC 时，控制照片在纸上的占位/缩放）。
/// 尺寸单位为 Word COM 的「点」（point, 1/72 英寸）；A4 ≈ 595×842 点。
/// 版式相对固定，集中在此维护；后续扩展直接加常量并补 <see cref="GetBox"/> 即可。
/// </summary>
public static class PhotoLayoutConstants
{
    /// <summary>整页：照片等比缩放到接近一页（留标准页边距）。</summary>
    public const string FULL_PAGE = "FULL_PAGE";

    /// <summary>半页：照片等比缩放到约半页高（一张纸可放两张）。</summary>
    public const string HALF_PAGE = "HALF_PAGE";

    /// <summary>证件原寸：缩放到身份证实际尺寸（85.6×54mm ≈ 242×153 点），用于证件核验留痕。</summary>
    public const string ID_CARD = "ID_CARD";

    /// <summary>全部有效版式（供 Picker 绑定）。</summary>
    public static readonly (string Code, string Name)[] All =
    {
        (FULL_PAGE, "整页"),
        (HALF_PAGE, "半页"),
        (ID_CARD, "证件原寸"),
    };

    /// <summary>版式代码 → 中文显示名。</summary>
    public static string GetDisplayName(string? layout) => layout switch
    {
        FULL_PAGE => "整页",
        HALF_PAGE => "半页",
        ID_CARD => "证件原寸",
        _ => "整页"
    };

    /// <summary>
    /// 取版式的「等比缩放包围盒」（宽×高，单位点）。照片在不放大的前提下缩放到装得下该盒。
    /// 未知版式回落到整页，保证任何取值都能打印出图（不因版式写错而空白）。
    /// </summary>
    public static (float MaxWidth, float MaxHeight) GetBox(string? layout) => layout switch
    {
        HALF_PAGE => (480f, 350f),
        ID_CARD => (242f, 153f),
        _ => (480f, 700f),
    };
}
