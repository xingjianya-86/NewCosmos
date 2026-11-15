namespace NewCosmos.Constants;

/// <summary>
/// 照片水印常量（2026-11-15）：仅「入户排查 / 公示核查」类别在保存时把水印烧进图片，
/// 版式 = 左下角半透明信息条（分类·户主姓名·身份证 / 地址 / 时间 / 经纬度）
/// + 图片中央两行水印（现场拍照 / cosmos）。
/// 字号与边距全部按图片尺寸比例（各分辨率观感一致），阈值集中在本文件单点维护。
/// </summary>
public static class PhotoWatermarkConstants
{
    /// <summary>中央水印第一行。</summary>
    public const string CenterLineTop = "现场拍照";

    /// <summary>中央水印第二行。</summary>
    public const string CenterLineBottom = "cosmos";

    /// <summary>经纬度随机偏差半径（米，±均匀分布；按需求「小幅度偏差」定为 30 米）。</summary>
    public const double GpsJitterMeters = 30;

    /// <summary>单次定位超时（秒），超时/无权限则省略水印经纬度行，不阻断保存。</summary>
    public const double GpsTimeoutSeconds = 10;

    /// <summary>信息条字号 = min(宽,高) × 该比例。</summary>
    public const double InfoTextRatio = 0.030;

    /// <summary>信息条字号下限（小图保护）。</summary>
    public const float InfoMinFontSize = 12f;

    /// <summary>信息条距图片左/下边缘比例（相对 min(宽,高)）。</summary>
    public const double InfoMarginRatio = 0.025;

    /// <summary>中央水印字号 = 宽 × 该比例。</summary>
    public const double CenterTextRatio = 0.11;

    /// <summary>中央水印不透明度（0~1）。</summary>
    public const float CenterAlpha = 0.55f;

    /// <summary>信息条背景色 ARGB（半透明黑，白字可读）。</summary>
    public const uint InfoBarBackArgb = 0xA6000000;

    /// <summary>中文优先字体族（Windows 微软雅黑 / Android Noto 等；匹配不到自动按字符兜底，防豆腐块）。</summary>
    public static readonly string[] PreferredFontFamilies =
    {
        "Microsoft YaHei",
        "微软雅黑",
        "SimHei",
        "Noto Sans CJK SC",
        "PingFang SC",
        "sans-serif",
    };
}
