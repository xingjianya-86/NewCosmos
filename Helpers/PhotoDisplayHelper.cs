using NewCosmos.Constants;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ArchiveManagement;

namespace NewCosmos.Helpers;

/// <summary>可挂缩略图的照片清单行（桌面查询页 <c>ArchivePhotoListItem</c> / 手机采集页 <c>MobileArchivePhotoItem</c> 共同实现）。</summary>
public interface IPhotoThumbnailTarget
{
    long Id { get; }
    ImageSource? Thumbnail { get; set; }
}

/// <summary>
/// 照片展示共用逻辑（桌面查询页 / 手机采集页，2026-11-15 收敛两份逐字重复的实现）：
/// ① 库内字节 → 可显示 ImageSource（WebP 统一先转 JPEG 适配 WinUI，不改库内格式）；
/// ② 列表缩略图 4 路并发加载（切页/切类别/离开页面由调用方令牌取消）。
/// </summary>
public static class PhotoDisplayHelper
{
    /// <summary>缩略图长边像素（两端既有实现一致：160px JPEG）。</summary>
    public const int ThumbnailMaxEdge = 160;

    /// <summary>
    /// 库内照片字节 → 页面可显示的 ImageSource。
    /// WebP 统一先转 JPEG（WinUI 对 WebP 支持不稳定）；失败返回 null 并给出用户可读原因。
    /// </summary>
    public static ImageSource? ToDisplaySource(byte[] data, string? declaredFormat, out string? error)
    {
        error = null;
        if (data == null || data.Length == 0)
        {
            error = "照片读取失败（可能已被删除）";
            return null;
        }

        var format = ImageTranscoder.DetectFormat(data);
        if (string.IsNullOrEmpty(format))
            format = declaredFormat;

        var displayBytes = data;
        if (format == PhotoFormatConstants.WebP && !ImageTranscoder.TryToJpeg(data, out displayBytes))
        {
            error = "照片预览失败（WebP 转码失败）";
            return null;
        }

        return ImageSource.FromStream(() => new MemoryStream(displayBytes));
    }

    /// <summary>
    /// 为一组照片行异步生成缩略图（4 路并发，失败只记 Warn 不中断其余）；
    /// 取到的 ImageSource 经主线程回调写入 <see cref="IPhotoThumbnailTarget.Thumbnail"/>。
    /// </summary>
    public static async Task LoadThumbnailsAsync(
        IArchivePhotoService photoService,
        IReadOnlyList<IPhotoThumbnailTarget> items,
        ILoggerService logger,
        CancellationToken ct)
    {
        if (items.Count == 0) return;

        using var gate = new SemaphoreSlim(4);
        var tasks = items.Select(async item =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var data = await photoService.GetPhotoDataAsync(item.Id, ct);
                if (data.IsFailure)
                {
                    logger.Warn($"照片缩略图读取失败，已跳过 Id={item.Id}: {data.ErrorCode} {data.Message}");
                    return;
                }
                if (data.Value == null || data.Value.Length == 0) return;

                // 输出 JPEG（该页手机/桌面都会打开，WinUI 对 WebP 支持不稳定）
                var thumb = ImageCompressor.CreateThumbnail(data.Value, maxEdge: ThumbnailMaxEdge, jpeg: true);
                if (thumb == null || ct.IsCancellationRequested) return;

                var source = ImageSource.FromStream(() => new MemoryStream(thumb));
                MainThread.BeginInvokeOnMainThread(() => item.Thumbnail = source);
            }
            catch (OperationCanceledException)
            {
                // 切页/切类别/离开页面：忽略
            }
            catch (Exception ex)
            {
                logger.Warn($"照片缩略图加载失败 Id={item.Id}: {ex.Message}");
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks);
    }
}
