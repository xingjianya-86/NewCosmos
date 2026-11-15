namespace NewCosmos.Constants;

/// <summary>
/// 照片水印常量（2026-11-15，11-15 二改）：仅「入户排查 / 公示核查」类别在保存时把水印烧进图片，
/// 版式 = 左下角信息条（分类·户主·身份证 / 地址： / 时间： / 经纬度：）
/// + 图片中央两行水印（现场拍照 / cosmos，半透明白字无描边）。
/// 坐标口径（2026-11-15 二改）：不取设备 GPS，按**所在村屯查询信息**取区划坐标列
/// （缺失时在线地理编码回写）——坐标即固定地理编码值，同村屯恒同坐标。
/// 时间口径：取档案时间轴时间（入户调查日 / 公示起日）+ 对应记录创建时分，取不到回退采集时刻。
/// 字号与边距全部按图片尺寸比例（各分辨率观感一致），阈值集中在本文件单点维护。
/// </summary>
public static class PhotoWatermarkConstants
{
    /// <summary>中央水印第一行。</summary>
    public const string CenterLineTop = "现场拍照";

    /// <summary>中央水印第二行。</summary>
    public const string CenterLineBottom = "cosmos";

    // ── 村屯在线地理编码（区划表坐标缺失时回写；有坐标后不再联网） ──

    /// <summary>地理编码服务端点（Nominatim / OpenStreetMap，免费无需密钥；超时见 <see cref="GeocodeTimeoutSeconds"/>）。</summary>
    public const string GeocodeApiUrl = "https://nominatim.openstreetmap.org/search";

    /// <summary>地理编码请求 User-Agent（Nominatim 使用政策要求可识别的 UA）。</summary>
    public const string GeocodeUserAgent = "NewCosmos-PhotoWatermark/1.0";

    /// <summary>单次地理编码超时（秒）：超时/断网仅省略水印经纬度行，不阻断保存。</summary>
    public const double GeocodeTimeoutSeconds = 8;

    /// <summary>信息条字号 = min(宽,高) × 该比例。</summary>
    public const double InfoTextRatio = 0.030;

    /// <summary>信息条字号下限（小图保护）。</summary>
    public const float InfoMinFontSize = 12f;

    /// <summary>信息条距图片左/下边缘比例（相对 min(宽,高)）。</summary>
    public const double InfoMarginRatio = 0.025;

    /// <summary>中央水印字号 = 宽 × 该比例。</summary>
    public const double CenterTextRatio = 0.11;

    /// <summary>中央水印不透明度（0~1；无描边，浅色底观感偏淡时可调高）。</summary>
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
