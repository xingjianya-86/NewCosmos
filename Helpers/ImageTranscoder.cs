using NewCosmos.Constants;
using SkiaSharp;

namespace NewCosmos.Helpers;

/// <summary>
/// 图片格式识别与转码：数据库统一存 WebP，但预览/打印/旧数据仍可能是 JPEG/PNG。
/// 通过魔数识别格式，按需转成 WinUI/Word/WPS 稳定支持的 JPEG/PNG。
/// </summary>
public static class ImageTranscoder
{
    /// <summary>识别图片格式：webp/jpeg/png，无法识别返回空串。</summary>
    public static string DetectFormat(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 12
            && data[0] == (byte)'R' && data[1] == (byte)'I'
            && data[2] == (byte)'F' && data[3] == (byte)'F'
            && data[8] == (byte)'W' && data[9] == (byte)'E'
            && data[10] == (byte)'B' && data[11] == (byte)'P')
        {
            return PhotoFormatConstants.WebP;
        }

        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
            return PhotoFormatConstants.Jpeg;

        if (data.Length >= 8
            && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47
            && data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
        {
            return PhotoFormatConstants.Png;
        }

        return string.Empty;
    }

    public static bool IsWebP(byte[]? data) => data != null && DetectFormat(data) == PhotoFormatConstants.WebP;

    /// <summary>
    /// 转为 JPEG（预览/照片打印/相册导出用）：已经是 JPEG 直接透传，其余解码后重编码。
    /// </summary>
    public static bool TryToJpeg(byte[]? source, out byte[] result, int quality = 90)
    {
        result = Array.Empty<byte>();
        if (source == null || source.Length == 0) return false;

        if (DetectFormat(source) == PhotoFormatConstants.Jpeg)
        {
            result = source;
            return true;
        }

        return TryEncode(source, SKEncodedImageFormat.Jpeg, quality, out result);
    }

    /// <summary>
    /// 转为 PNG（Word 模板照片占位符用）：已经是 PNG 直接透传，其余解码后重编码。
    /// </summary>
    public static bool TryToPng(byte[]? source, out byte[] result)
    {
        result = Array.Empty<byte>();
        if (source == null || source.Length == 0) return false;

        if (DetectFormat(source) == PhotoFormatConstants.Png)
        {
            result = source;
            return true;
        }

        return TryEncode(source, SKEncodedImageFormat.Png, 100, out result);
    }

    /// <summary>
    /// Word 插入兼容格式：JPEG/PNG 透传并返回正确扩展名；WebP 转 JPEG（照片体积更小），
    /// 未知格式再尝试转 PNG。
    /// </summary>
    public static bool TryGetWordCompatible(byte[]? source, out byte[] result, out string extension)
    {
        result = Array.Empty<byte>();
        extension = ".png";
        if (source == null || source.Length == 0) return false;

        var format = DetectFormat(source);
        switch (format)
        {
            case PhotoFormatConstants.Jpeg:
                result = source;
                extension = ".jpg";
                return true;
            case PhotoFormatConstants.Png:
                result = source;
                extension = ".png";
                return true;
            case PhotoFormatConstants.WebP:
                if (!TryToJpeg(source, out result)) return false;
                extension = ".jpg";
                return true;
            default:
                if (!TryToPng(source, out result)) return false;
                extension = ".png";
                return true;
        }
    }

    private static bool TryEncode(byte[] source, SKEncodedImageFormat format, int quality, out byte[] result)
    {
        result = Array.Empty<byte>();
        try
        {
            using var image = SKImage.FromEncodedData(source);
            if (image == null) return false;

            using var bitmap = SKBitmap.FromImage(image);
            if (bitmap == null) return false;

            using var data = bitmap.Encode(format, quality);
            if (data == null || data.Size == 0) return false;

            result = data.ToArray();
            return result.Length > 0;
        }
        catch
        {
            return false;
        }
    }
}
