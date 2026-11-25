using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using SkiaSharp;

namespace NewCosmos.Helpers;

/// <summary>
/// 照片水印绘制（SkiaSharp，2026-11-15）：左下角半透明信息条 + 图片中央两行水印。
/// 供「入户排查 / 公示核查」类别在保存链路（<c>ArchivePhotoService.SavePhotoAsync</c>）
/// 压缩前调用——先水印、再统一压 WebP，保证最终仍 ≤500KB 硬上限。
/// 任何解码/绘制失败返回 null，调用方按原图继续保存并记 Warn（保照片优先于水印）。
/// </summary>
public static class PhotoWatermarkHelper
{
    /// <summary>中文字形探测字符（字体匹配用，含于常见 CJK 字体）。</summary>
    private const char CjkProbeChar = '入';

    /// <summary>
    /// 整图水印：解码 → 绘制 → JPEG q95 中间字节（随后仍会走 ImageCompressor 统一压缩为 WebP）。
    /// 失败返回 null（调用方按原图入库并记日志）。
    /// </summary>
    public static byte[]? Apply(byte[]? source, PhotoWatermarkInfo info, string categoryName)
    {
        if (source == null || source.Length == 0 || info == null) return null;

        SKTypeface? typeface = null;
        try
        {
            using var bitmap = ImageCompressor.DecodeBitmap(source);
            if (bitmap == null) return null;

            typeface = ResolveTypeface();
            if (!TryDraw(bitmap, info, categoryName, typeface)) return null;

            using var encoded = bitmap.Encode(SKEncodedImageFormat.Jpeg, 95);
            return encoded != null && encoded.Size > 0 ? encoded.ToArray() : null;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[PhotoWatermark] 水印生成失败，按无水印继续");
            return null; // 水印失败不阻断保存
        }
        finally
        {
            if (typeface != null && !ReferenceEquals(typeface, SKTypeface.Default))
                typeface.Dispose();
        }
    }

    /// <summary>在已解码位图上绘制全部水印元素（供 Apply 与单测共用）。返回是否成功。</summary>
    public static bool TryDraw(SKBitmap bitmap, PhotoWatermarkInfo info, string categoryName, SKTypeface? typeface)
    {
        if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0) return false;

        var lines = info.BuildInfoLines(categoryName);
        using var canvas = new SKCanvas(bitmap);

        DrawCenterMark(canvas, bitmap.Width, bitmap.Height, typeface);
        if (lines.Count > 0)
            DrawInfoBar(canvas, bitmap.Width, bitmap.Height, lines, typeface);

        canvas.Flush();
        return true;
    }

    /// <summary>左下角信息条：半透明圆角黑底 + 白字多行（每行 = PhotoWatermarkInfo.BuildInfoLines）。</summary>
    private static void DrawInfoBar(
        SKCanvas canvas, int width, int height, IReadOnlyList<string> lines, SKTypeface? typeface)
    {
        var minEdge = Math.Min(width, height);
        var fontSize = Math.Max(PhotoWatermarkConstants.InfoMinFontSize,
            (float)(minEdge * PhotoWatermarkConstants.InfoTextRatio));

        using var font = new SKFont(typeface, fontSize);
        using var textPaint = new SKPaint
        {
            IsAntialias = true,
            Color = SKColors.White,
            Style = SKPaintStyle.Fill,
        };

        var padX = fontSize * 0.6f;
        var padY = fontSize * 0.45f;
        var lineHeight = fontSize * 1.35f;

        var textWidth = 0f;
        foreach (var line in lines)
            textWidth = Math.Max(textWidth, font.MeasureText(line, textPaint));

        var margin = minEdge * (float)PhotoWatermarkConstants.InfoMarginRatio;
        var barWidth = textWidth + padX * 2;
        var barHeight = lineHeight * lines.Count + padY * 2;
        var left = margin;
        var top = height - margin - barHeight;

        var argb = PhotoWatermarkConstants.InfoBarBackArgb;
        using var backPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(
                (byte)((argb >> 16) & 0xFF),
                (byte)((argb >> 8) & 0xFF),
                (byte)(argb & 0xFF),
                (byte)((argb >> 24) & 0xFF)),
            Style = SKPaintStyle.Fill,
        };
        var radius = fontSize * 0.3f;
        canvas.DrawRoundRect(SKRect.Create(left, top, barWidth, barHeight), radius, radius, backPaint);

        var baseline = top + padY + fontSize;
        foreach (var line in lines)
        {
            using var blob = SKTextBlob.Create(line, font, new SKPoint(left + padX, baseline));
            canvas.DrawText(blob, 0, 0, textPaint);
            baseline += lineHeight;
        }
    }

    /// <summary>图片中央两行水印（现场拍照 / cosmos）：半透明白字，无描边（2026-11-15 按需求去掉深色描边）。</summary>
    private static void DrawCenterMark(SKCanvas canvas, int width, int height, SKTypeface? typeface)
    {
        var fontSize = (float)(width * PhotoWatermarkConstants.CenterTextRatio);
        if (fontSize <= 0) return;

        using var font = new SKFont(typeface, fontSize);
        var alpha = (byte)Math.Clamp((int)(PhotoWatermarkConstants.CenterAlpha * 255), 0, 255);

        using var fillPaint = new SKPaint { IsAntialias = true, Color = SKColors.White.WithAlpha(alpha), Style = SKPaintStyle.Fill };

        var lineHeight = fontSize * 1.15f;
        var top1 = PhotoWatermarkConstants.CenterLineTop;
        var bottom2 = PhotoWatermarkConstants.CenterLineBottom;

        var maxWidth = Math.Max(font.MeasureText(top1, fillPaint), font.MeasureText(bottom2, fillPaint));
        var x = (width - maxWidth) / 2f;
        var baseline1 = (height - lineHeight * 2) / 2f + fontSize;

        DrawCenterLine(canvas, top1, x, baseline1, font, fillPaint);
        DrawCenterLine(canvas, bottom2, x, baseline1 + lineHeight, font, fillPaint);
    }

    private static void DrawCenterLine(SKCanvas canvas, string text, float x, float baseline, SKFont font, SKPaint fill)
    {
        using var blob = SKTextBlob.Create(text, font, new SKPoint(x, baseline));
        canvas.DrawText(blob, 0, 0, fill);
    }

    /// <summary>
    /// 中文字体解析：按 <see cref="PhotoWatermarkConstants.PreferredFontFamilies"/> 逐族按字符匹配，
    /// 全部落空再按字符全局兜底（Skia 自带 CJK 回退），最后退回默认字体——避免中文画成豆腐块。
    /// </summary>
    private static SKTypeface? ResolveTypeface()
    {
        try
        {
            foreach (var family in PhotoWatermarkConstants.PreferredFontFamilies)
            {
                var typeface = SKFontManager.Default.MatchCharacter(family, CjkProbeChar);
                if (typeface != null) return typeface;
            }

            return SKFontManager.Default.MatchCharacter(CjkProbeChar) ?? SKTypeface.Default;
        }
        catch
        {
            return SKTypeface.Default;
        }
    }
}
