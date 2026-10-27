namespace NewCosmos.Constants;

/// <summary>
/// 档案照片格式常量与扩展名/MIME 映射。
/// 数据库 nc_biz_archive_photos.image_format 存小写格式名（webp/jpeg/png）。
/// </summary>
public static class PhotoFormatConstants
{
    public const string WebP = "webp";
    public const string Jpeg = "jpeg";
    public const string Png = "png";

    /// <summary>格式名 → 文件扩展名（含点）。未知格式按 JPEG 兼容处理。</summary>
    public static string GetExtension(string? format) => (format?.Trim().ToLowerInvariant()) switch
    {
        WebP => ".webp",
        Png => ".png",
        _ => ".jpg"
    };

    /// <summary>格式名 → MIME 类型（相册导出用）。未知格式按 JPEG 兼容处理。</summary>
    public static string GetMimeType(string? format) => (format?.Trim().ToLowerInvariant()) switch
    {
        WebP => "image/webp",
        Png => "image/png",
        _ => "image/jpeg"
    };

    /// <summary>格式名 → 中文显示（列表/日志用）。</summary>
    public static string GetDisplayName(string? format) => (format?.Trim().ToLowerInvariant()) switch
    {
        WebP => "WebP",
        Png => "PNG",
        _ => "JPEG"
    };
}
