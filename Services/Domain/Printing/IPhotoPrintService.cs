using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.Printing;

/// <summary>
/// 照片打印服务：把手机采集的证件/核查照片直接打印成纸质留痕（PC 端执行，无模板）。
/// 用 Word COM 建空白文档 → 按版式插入图片并等比缩放 → 存临时 docx → 复用既有打印管线输出。
/// </summary>
public interface IPhotoPrintService
{
    /// <summary>打印一张照片（JPEG 字节）。</summary>
    /// <param name="photoJpeg">照片二进制（JPEG）</param>
    /// <param name="layout">版式（PhotoLayoutConstants，空=整页）</param>
    /// <param name="printerName">目标打印机（空=系统默认）</param>
    /// <param name="isDuplex">是否双面</param>
    /// <param name="copies">份数</param>
    Task<Result> PrintPhotoAsync(
        byte[] photoJpeg,
        string? layout,
        string? printerName,
        bool isDuplex,
        int copies,
        CancellationToken ct = default);
}
