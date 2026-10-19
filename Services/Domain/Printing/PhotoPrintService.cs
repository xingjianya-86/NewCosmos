#if WINDOWS
using System.Runtime.InteropServices;
using NewCosmos.Constants;
using NewCosmos.Models.Options;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Templates;

namespace NewCosmos.Services.Domain.Printing;

/// <summary>
/// 照片打印服务实现（PC 端，无模板）：Word COM 建空白文档 → 按版式插入 JPEG 并等比缩放 →
/// 存临时 docx → 复用既有 <see cref="OfficeComDocuments.PrintFileAsync"/> 打印管线输出。
/// COM 调用统一走 <see cref="OfficeComWorker"/>（STA 串行 + 超时保护），与 WordEngine 同源。
/// </summary>
public class PhotoPrintService : IPhotoPrintService
{
    private readonly ILoggerService _logger;
    private readonly StorageOptions _storageOptions;

    public PhotoPrintService(ILoggerService logger, StorageOptions storageOptions)
    {
        _logger = logger;
        _storageOptions = storageOptions ?? throw new ArgumentNullException(nameof(storageOptions));
    }

    public async Task<Result> PrintPhotoAsync(
        byte[] photoJpeg,
        string? layout,
        string? printerName,
        bool isDuplex,
        int copies,
        CancellationToken ct = default)
    {
        if (photoJpeg == null || photoJpeg.Length == 0)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "照片数据为空，无法打印");

        var imgPath = string.Empty;
        var docxPath = string.Empty;
        try
        {
            // 1) 照片字节落临时文件（AddPicture 按文件插入，SaveWithDocument 内嵌进 docx）
            imgPath = TemplateEngineHelpers.NewTempFilePath(_storageOptions, ".jpg", prefix: "photo_");
            await File.WriteAllBytesAsync(imgPath, photoJpeg, ct);

            // 2) 建空白文档 → 插图 → 按版式缩放 → 存临时 docx（COM 在 STA worker 上串行执行）
            docxPath = TemplateEngineHelpers.NewTempFilePath(_storageOptions, ".docx", prefix: "photo_print_");
            var fullDocx = Path.GetFullPath(docxPath);
            var (boxW, boxH) = PhotoLayoutConstants.GetBox(layout);

            await OfficeComWorker.Instance.InvokeAsync<object?>("照片打印排版", OfficeAppKind.Word, ctx =>
            {
                var app = ctx.App;
                dynamic? doc = null;
                dynamic? shape = null;
                try
                {
                    doc = app.Documents.Add();
                    ctx.DocumentOpened();

                    // 标准页边距（36 点 = 0.5 英寸），给照片留白
                    try
                    {
                        dynamic ps = doc.PageSetup;
                        ps.TopMargin = 36f; ps.BottomMargin = 36f;
                        ps.LeftMargin = 36f; ps.RightMargin = 36f;
                    }
                    catch
                    {
                        // 页边距设置失败不影响出图（用文档默认边距）
                    }

                    // 在文档起始处插入图片（Range(0,0) = 文首空位）
                    dynamic range = doc.Range(0, 0);
                    shape = doc.InlineShapes.AddPicture(imgPath, false, true, range);

                    // 按版式包围盒等比缩放（不放大失真由调用方保证照片分辨率；此处统一 fit-in-box）
                    try
                    {
                        float w = shape.Width;
                        float h = shape.Height;
                        if (w > 0 && h > 0)
                        {
                            float scale = Math.Min(boxW / w, boxH / h);
                            shape.Width = w * scale;
                            shape.Height = h * scale;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"照片打印：按版式缩放失败（保留原尺寸）: {ex.Message}");
                    }

                    doc.SaveAs2(fullDocx);
                    return null;
                }
                finally
                {
                    try { if (shape != null) Marshal.ReleaseComObject(shape); } catch { /* 忽略 */ }
                    try { if (doc != null) doc.Close(false); } catch { /* 忽略 */ }
                    try { if (doc != null) Marshal.ReleaseComObject(doc); } catch { /* 忽略 */ }
                    try { ctx.DocumentClosed(); } catch { /* 忽略 */ }
                }
            }, ct: ct);

            if (!File.Exists(docxPath))
                return Result.Failure(ErrorCodes.PRINT_FAILED, "照片排版文档未生成，无法打印");

            // 3) 复用既有 Word 打印管线输出
            await OfficeComDocuments.PrintFileAsync(
                OfficeAppKind.Word, docxPath, printerName ?? string.Empty, isDuplex, copies, _logger, ct);

            _logger.Info($"照片打印完成: Layout={PhotoLayoutConstants.GetDisplayName(layout)}, Copies={copies}");
            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            return Result.Failure(ErrorCodes.PRINT_FAILED, "照片打印已取消");
        }
        catch (BusinessException be)
        {
            _logger.Error($"照片打印失败: {be.Message}");
            return Result.Failure(be.ErrorCode, be.Message ?? "照片打印失败");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "照片打印异常");
            return Result.Failure(ErrorCodes.PRINT_FAILED, $"照片打印失败: {ex.Message}");
        }
        finally
        {
            TemplateEngineHelpers.TryDeleteFile(imgPath, _logger, "PhotoPrintService");
            TemplateEngineHelpers.TryDeleteFile(docxPath, _logger, "PhotoPrintService");
        }
    }
}
#endif
