using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;

namespace NewCosmos.Helpers;

/// <summary>
/// 图片压缩助手：把手机拍摄的大图压到长边 ≤2000px、JPEG 质量 q85，控制入库体积。
/// 用 MAUI 跨平台 <see cref="PlatformImage"/>（Windows/Android 均实现），零原生依赖。
/// 解码/编码失败时原样返回源字节（宁可存大图也不丢图）。
/// </summary>
public static class ImageCompressor
{
    /// <summary>长边上限（像素）。</summary>
    private const int MaxDimension = 2000;

    /// <summary>JPEG 质量（0~1，q85 兼顾清晰与体积）。</summary>
    private const float JpegQuality = 0.85f;

    /// <summary>压缩图片：长边超限时等比缩小并转 JPEG。已经是小图时也统一转 JPEG 以减小体积。</summary>
    /// <param name="source">源图片字节（JPEG/PNG/WebP 等 PlatformImage 能解码的格式）</param>
    /// <returns>压缩后的 JPEG 字节；解码失败时原样返回源字节</returns>
    public static byte[] Compress(byte[] source)
    {
        if (source == null || source.Length == 0)
            return source ?? Array.Empty<byte>();

        try
        {
            using var input = new MemoryStream(source);
            using var image = PlatformImage.FromStream(input);
            if (image == null)
                return source;

            var width = image.Width;
            var height = image.Height;

            // 无需缩小：仍转 JPEG（若源已是小 JPEG，体积变化不大，但格式统一利于后续打印）
            if (width <= MaxDimension && height <= MaxDimension)
            {
                using var ms = new MemoryStream();
                image.Save(ms, ImageFormat.Jpeg, JpegQuality);
                return ms.ToArray();
            }

            // 等比缩小：长边压到 MaxDimension
            var scale = MaxDimension / (double)(width > height ? width : height);
            var targetW = Math.Max(1, (int)Math.Round(width * scale));
            var targetH = Math.Max(1, (int)Math.Round(height * scale));

            using var resized = image.Downsize(Math.Max(targetW, targetH), disposeOriginal: false);
            using var output = new MemoryStream();
            resized.Save(output, ImageFormat.Jpeg, JpegQuality);
            return output.ToArray();
        }
        catch
        {
            // 解码/编码失败不丢图：原样返回源字节
            return source;
        }
    }
}
