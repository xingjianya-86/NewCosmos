using NewCosmos.Models.Results;

namespace NewCosmos.Services.Platform;

/// <summary>
/// 照片导出到系统相册：Android 走 MediaStore（API 29+ 免存储权限），Windows 落「图片\NewCosmos」。
/// 用于手机端档案照片留档/外发打印。
/// </summary>
public interface IAlbumExportService
{
    /// <summary>保存一张 JPEG 到系统相册，返回落地位置（URI 或路径）。</summary>
    Task<Result<string>> SaveToAlbumAsync(string fileName, byte[] jpegBytes, CancellationToken ct = default);
}
