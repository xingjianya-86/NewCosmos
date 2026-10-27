using NewCosmos.Models.Results;

namespace NewCosmos.Services.Platform;

/// <summary>
/// 照片导出到系统相册：Android 走 MediaStore（API 29+ 免存储权限），Windows 落「图片\NewCosmos」。
/// 用于手机端档案照片留档/外发打印。
/// </summary>
public interface IAlbumExportService
{
    /// <summary>保存一张图片到系统相册，返回落地位置（URI 或路径）。</summary>
    /// <param name="fileName">文件名（含扩展名，如 xxx.webp/xxx.jpg）</param>
    /// <param name="imageBytes">图片字节（WebP/JPEG/PNG 原样写入）</param>
    /// <param name="mimeType">MIME 类型（Android MediaStore 用；如 image/webp）</param>
    Task<Result<string>> SaveToAlbumAsync(
        string fileName, byte[] imageBytes, string mimeType, CancellationToken ct = default);
}
