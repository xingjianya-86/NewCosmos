using NewCosmos.Constants;
using NewCosmos.Models.Results;
using SkiaSharp;

namespace NewCosmos.Helpers;

/// <summary>
/// 图片压缩助手：统一把档案照片转成 WebP，并保证单张体积不超过
/// <see cref="PhotoCompressionConstants.HardMaxBytes"/>（500KB）。
/// 采用 SkiaSharp 编解码（Windows 原生 libSkiaSharp 已随 NPOI 进入输出；Android 已引用 SkiaSharp）。
/// 失败时返回 Result.Failure，绝不退回“存原图”（否则体积无法保证）。
/// </summary>
public static class ImageCompressor
{
    /// <summary>压缩结果：WebP 字节 + 实际尺寸/质量，供日志与批量转换统计。</summary>
    public readonly record struct CompressedPhoto(
        byte[] Data, int Width, int Height, int Quality, string Format, int OriginalBytes);

    /// <summary>
    /// 压缩为 WebP：长边 ≤2000px，质量 q80→q50，仍超 500KB 再缩尺寸，最终仍超则失败。
    /// 已经是 WebP 且不超过硬上限的字节直接透传（避免重复编码）。
    /// </summary>
    /// <param name="source">原始图片字节（JPEG/PNG/WebP/HEIC 等平台可解码格式）</param>
    /// <param name="category">照片类别（当前阈值统一，保留参数供后续按类别细分）</param>
    public static Result<CompressedPhoto> CompressToWebP(byte[] source, string? category = null)
    {
        if (source == null || source.Length == 0)
            return Result.Failure<CompressedPhoto>(ErrorCodes.VALIDATION_FAILED, "照片内容为空");

        // 已经是 WebP 且不超硬上限：直接透传
        if (ImageTranscoder.DetectFormat(source) == PhotoFormatConstants.WebP
            && source.Length <= PhotoCompressionConstants.HardMaxBytes)
        {
            var (w, h) = TryGetDimensions(source);
            return Result.Success(new CompressedPhoto(source, w, h, 0, PhotoFormatConstants.WebP, source.Length));
        }

        try
        {
            using var original = DecodeBitmap(source);
            if (original == null)
                return Result.Failure<CompressedPhoto>(ErrorCodes.VALIDATION_FAILED,
                    "无法解码照片，请更换图片后重试");

            var maxEdge = Math.Max(1, Math.Min(PhotoCompressionConstants.MaxLongEdge,
                Math.Max(original.Width, original.Height)));

            for (var attempt = 0; attempt <= PhotoCompressionConstants.MaxDownscaleAttempts; attempt++)
            {
                var (targetW, targetH) = FitWithin(original.Width, original.Height, maxEdge);
                var needsResize = targetW != original.Width || targetH != original.Height;

                using var resized = needsResize
                    ? original.Resize(new SKImageInfo(targetW, targetH),
                        new SKSamplingOptions(SKCubicResampler.Mitchell))
                    : null;
                var bitmap = resized ?? original;
                if (bitmap == null)
                    return Result.Failure<CompressedPhoto>(ErrorCodes.VALIDATION_FAILED, "照片缩放失败");

                for (var quality = PhotoCompressionConstants.StartQuality;
                     quality >= PhotoCompressionConstants.MinQuality;
                     quality -= PhotoCompressionConstants.QualityStep)
                {
                    using var data = bitmap.Encode(SKEncodedImageFormat.Webp, quality);
                    if (data == null || data.Size == 0) continue;

                    var bytes = data.ToArray();
                    if (bytes.Length <= PhotoCompressionConstants.HardMaxBytes)
                    {
                        return Result.Success(new CompressedPhoto(
                            bytes, bitmap.Width, bitmap.Height, quality,
                            PhotoFormatConstants.WebP, source.Length));
                    }
                }

                if (maxEdge <= PhotoCompressionConstants.MinLongEdge) break;
                var next = Math.Max(PhotoCompressionConstants.MinLongEdge,
                    (int)Math.Round(maxEdge * PhotoCompressionConstants.DownscalePercent / 100.0));
                if (next >= maxEdge) break;
                maxEdge = next;
            }

            return Result.Failure<CompressedPhoto>(ErrorCodes.VALIDATION_FAILED,
                $"照片压缩后仍超过 {PhotoCompressionConstants.HardMaxBytes / 1024}KB，请重新拍摄或选择更清晰的图片");
        }
        catch (Exception ex)
        {
            return Result.Failure<CompressedPhoto>(ErrorCodes.VALIDATION_FAILED, $"照片压缩失败：{ex.Message}");
        }
    }

    /// <summary>是否为 WebP 字节。</summary>
    public static bool IsWebP(byte[]? data) => data != null && ImageTranscoder.IsWebP(data);

    /// <summary>解码为位图：优先 SkiaSharp；失败时用平台解码器（兼容 HEIC/HEIF）转 PNG 再交给 SkiaSharp。</summary>
    private static SKBitmap? DecodeBitmap(byte[] source)
    {
        try
        {
            using var image = SKImage.FromEncodedData(source);
            if (image != null)
            {
                var bitmap = SKBitmap.FromImage(image);
                if (bitmap != null) return bitmap;
            }
        }
        catch
        {
            // 继续走平台解码兜底
        }

        try
        {
            using var input = new MemoryStream(source);
            using var platformImage = Microsoft.Maui.Graphics.Platform.PlatformImage.FromStream(input);
            if (platformImage == null) return null;

            using var png = new MemoryStream();
            platformImage.Save(png, Microsoft.Maui.Graphics.ImageFormat.Png);
            png.Position = 0;
            return SKBitmap.Decode(png);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>等比缩放到长边不超过 maxEdge。</summary>
    private static (int Width, int Height) FitWithin(int width, int height, int maxEdge)
    {
        if (width <= 0 || height <= 0) return (1, 1);
        var longEdge = Math.Max(width, height);
        if (longEdge <= maxEdge) return (width, height);

        var scale = maxEdge / (double)longEdge;
        return (Math.Max(1, (int)Math.Round(width * scale)),
                Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static (int Width, int Height) TryGetDimensions(byte[] data)
    {
        try
        {
            using var image = SKImage.FromEncodedData(data);
            return image == null ? (0, 0) : (image.Width, image.Height);
        }
        catch
        {
            return (0, 0);
        }
    }
}
