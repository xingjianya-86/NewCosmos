using System.Globalization;
using System.Text;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace NewCosmos.Services.Domain.ElderlyBenefits;

/// <summary>高龄认证报表 PDF 排版选项。</summary>
public sealed class ElderlyCertificationPdfOptions
{
    /// <summary>制表时间（页眉显示）。</summary>
    public DateTime GeneratedAt { get; init; } = DateTime.Now;

    /// <summary>页脚左侧文字；为空时使用系统名。</summary>
    public string? FooterLeft { get; init; }
}

/// <summary>
/// 高龄年度认证报表 PDF 渲染器：PDFsharp 直接排版，不依赖 Office COM，桌面与 Web 共用。
/// 列定义/行数据来自 <see cref="ElderlyCertificationReport"/>，本类只负责分页、换行、格式化与画线。
/// 中文字体走 <see cref="ChinesePdfFontResolver"/>；找不到字体时显式失败，禁止输出乱码。
/// </summary>
public static class ElderlyCertificationPdfRenderer
{
    private const double PageMargin = 26;
    private const double TitleTop = 20;
    private const double TitleHeight = 22;
    private const double MetaTop = 43;
    private const double HeaderTop = 64;
    private const double FooterHeight = 22;
    private const double CellPadding = 2;
    private const double LineHeight = 9.4;
    private const double MinRowHeight = 14;
    private const double TitleFontSize = 14;
    private const double MetaFontSize = 7.5;
    private const double HeaderFontSize = 7;
    private const double BodyFontSize = 7;
    private const double FooterFontSize = 7;

    private static readonly XColor HeaderFillColor = XColor.FromArgb(0xEF, 0xF2, 0xF5);
    private static readonly XColor GridColor = XColor.FromArgb(0x9A, 0xA4, 0xAE);

    /// <summary>单份报表生成 PDF。</summary>
    public static Result<byte[]> Render(
        ElderlyCertificationReport report,
        ElderlyCertificationPdfOptions? options = null)
        => RenderMany(new[] { report }, options);

    /// <summary>
    /// 多份报表合并为一份 PDF（每份报表从新页开始，页码连续）。
    /// 用于桌面“勾选多张后一次打印”。
    /// </summary>
    public static Result<byte[]> RenderMany(
        IReadOnlyList<ElderlyCertificationReport> reports,
        ElderlyCertificationPdfOptions? options = null)
    {
        if (reports == null || reports.Count == 0)
            return Result.Failure<byte[]>(ErrorCodes.VALIDATION_FAILED, "没有可生成的认证报表");
        if (reports.Any(r => r == null || r.Columns.Count == 0))
            return Result.Failure<byte[]>(ErrorCodes.VALIDATION_FAILED, "报表缺少列定义");

        if (!ChinesePdfFontResolver.EnsureRegistered(out var fontError))
            return Result.Failure<byte[]>(ErrorCodes.DOCUMENT_GENERATION_FAILED, fontError ?? "中文字体不可用");

        try
        {
            options ??= new ElderlyCertificationPdfOptions();
            using var document = new PdfDocument();
            document.Info.Title = reports.Count == 1
                ? reports[0].Title
                : "高龄年度认证报表";
            document.Info.Creator = "民政社会救助管理系统";

            var pageNo = 0;
            foreach (var report in reports)
                pageNo = DrawReport(document, report, options, pageNo);

            using var stream = new MemoryStream();
            document.Save(stream, false);
            return Result.Success(stream.ToArray());
        }
        catch (Exception ex)
        {
            return Result.Failure<byte[]>(
                ErrorCodes.DOCUMENT_GENERATION_FAILED, $"生成认证 PDF 失败：{ex.Message}");
        }
    }

    private static int DrawReport(
        PdfDocument document,
        ElderlyCertificationReport report,
        ElderlyCertificationPdfOptions options,
        int pageNo)
    {
        var titleFont = new XFont(ChinesePdfFontResolver.FaceName, TitleFontSize, XFontStyleEx.Bold);
        var metaFont = new XFont(ChinesePdfFontResolver.FaceName, MetaFontSize, XFontStyleEx.Regular);
        var headerFont = new XFont(ChinesePdfFontResolver.FaceName, HeaderFontSize, XFontStyleEx.Bold);
        var bodyFont = new XFont(ChinesePdfFontResolver.FaceName, BodyFontSize, XFontStyleEx.Regular);
        var footerFont = new XFont(ChinesePdfFontResolver.FaceName, FooterFontSize, XFontStyleEx.Regular);

        var headerCache = new Dictionary<char, double>();
        var bodyCache = new Dictionary<char, double>();
        var rowIndex = 0;

        do
        {
            pageNo++;
            var page = document.AddPage();
            page.Size = PageSize.A4;
            page.Orientation = PageOrientation.Landscape;
            using var gfx = XGraphics.FromPdfPage(page);

            var contentWidth = page.Width.Point - PageMargin * 2;
            var widths = ComputeColumnWidths(report.Columns, contentWidth);

            // 每页重复标题与表头，翻页后仍能直接阅读。
            gfx.DrawString(report.Title, titleFont, XBrushes.Black,
                new XRect(PageMargin, TitleTop, contentWidth, TitleHeight), XStringFormats.TopCenter);

            var meta = $"制表时间：{options.GeneratedAt:yyyy-MM-dd HH:mm}    共 {report.Rows.Count:N0} 行";
            gfx.DrawString(meta, metaFont, XBrushes.Gray,
                new XRect(PageMargin, MetaTop, contentWidth, 12), XStringFormats.TopRight);

            var headerLines = BuildHeaderLines(gfx, report.Columns, headerFont, widths, headerCache);
            var headerHeight = Math.Max(
                MinRowHeight,
                headerLines.Max(lines => lines.Count) * LineHeight + CellPadding * 2);
            DrawHeader(gfx, report.Columns, widths, headerLines, headerFont, headerHeight);

            var y = HeaderTop + headerHeight;
            var bodyStart = y;
            var bottom = page.Height.Point - FooterHeight - 8;

            if (report.Rows.Count == 0)
            {
                gfx.DrawString("本报表无数据", bodyFont, XBrushes.Gray,
                    new XRect(PageMargin, y + 24, contentWidth, 20), XStringFormats.TopCenter);
            }
            else
            {
                while (rowIndex < report.Rows.Count)
                {
                    var row = report.Rows[rowIndex];
                    var lines = BuildRowLines(gfx, report.Columns, row, bodyFont, widths, bodyCache);
                    var rowHeight = Math.Max(
                        MinRowHeight,
                        lines.Max(cellLines => cellLines.Count) * LineHeight + CellPadding * 2);

                    // 至少画一行再换页，避免超高的单行把分页卡死。
                    if (y + rowHeight > bottom && y > bodyStart)
                        break;

                    DrawRow(gfx, widths, lines, bodyFont, rowHeight, y);
                    y += rowHeight;
                    rowIndex++;
                }
            }

            DrawFooter(gfx, page, footerFont, options, pageNo);
        } while (rowIndex < report.Rows.Count);

        return pageNo;
    }

    private static void DrawHeader(
        XGraphics gfx,
        IReadOnlyList<ElderlyCertificationReportColumn> columns,
        double[] widths,
        List<List<string>> headerLines,
        XFont font,
        double height)
    {
        var fill = new XSolidBrush(HeaderFillColor);
        var pen = new XPen(GridColor, 0.4);
        var x = PageMargin;

        for (var i = 0; i < columns.Count; i++)
        {
            gfx.DrawRectangle(fill, x, HeaderTop, widths[i], height);
            gfx.DrawRectangle(pen, x, HeaderTop, widths[i], height);

            var lines = headerLines[i];
            for (var line = 0; line < lines.Count; line++)
            {
                gfx.DrawString(lines[line], font, XBrushes.Black,
                    new XRect(x + CellPadding, HeaderTop + CellPadding + line * LineHeight,
                        widths[i] - CellPadding * 2, LineHeight),
                    XStringFormats.TopCenter);
            }
            x += widths[i];
        }
    }

    private static void DrawRow(
        XGraphics gfx,
        double[] widths,
        List<List<string>> lines,
        XFont font,
        double height,
        double top)
    {
        var pen = new XPen(GridColor, 0.4);
        var x = PageMargin;

        for (var i = 0; i < widths.Length; i++)
        {
            gfx.DrawRectangle(pen, x, top, widths[i], height);

            var cellLines = lines[i];
            for (var line = 0; line < cellLines.Count; line++)
            {
                gfx.DrawString(cellLines[line], font, XBrushes.Black,
                    new XRect(x + CellPadding, top + CellPadding + line * LineHeight,
                        widths[i] - CellPadding * 2, LineHeight),
                    XStringFormats.TopLeft);
            }
            x += widths[i];
        }
    }

    private static void DrawFooter(
        XGraphics gfx,
        PdfPage page,
        XFont font,
        ElderlyCertificationPdfOptions options,
        int pageNo)
    {
        var contentWidth = page.Width.Point - PageMargin * 2;
        var y = page.Height.Point - FooterHeight;

        gfx.DrawString(options.FooterLeft ?? "民政社会救助管理系统 · 高龄年度认证", font, XBrushes.Gray,
            new XRect(PageMargin, y, contentWidth / 2, 12), XStringFormats.TopLeft);
        gfx.DrawString($"第 {pageNo} 页", font, XBrushes.Gray,
            new XRect(PageMargin + contentWidth / 2, y, contentWidth / 2, 12), XStringFormats.TopRight);
    }

    private static List<List<string>> BuildHeaderLines(
        XGraphics gfx,
        IReadOnlyList<ElderlyCertificationReportColumn> columns,
        XFont font,
        double[] widths,
        Dictionary<char, double> cache)
    {
        var result = new List<List<string>>(columns.Count);
        for (var i = 0; i < columns.Count; i++)
        {
            result.Add(WrapText(gfx, columns[i].Name, font, widths[i] - CellPadding * 2, cache));
        }
        return result;
    }

    private static List<List<string>> BuildRowLines(
        XGraphics gfx,
        IReadOnlyList<ElderlyCertificationReportColumn> columns,
        ElderlyCertificationReportRow row,
        XFont font,
        double[] widths,
        Dictionary<char, double> cache)
    {
        var result = new List<List<string>>(columns.Count);
        for (var i = 0; i < columns.Count; i++)
        {
            var text = FormatValue(row.Get(columns[i].Key), columns[i].Format);
            result.Add(WrapText(gfx, text, font, widths[i] - CellPadding * 2, cache));
        }
        return result;
    }

    private static double[] ComputeColumnWidths(
        IReadOnlyList<ElderlyCertificationReportColumn> columns,
        double contentWidth)
    {
        var weights = columns.Select(c => Math.Max(1, c.Width)).ToArray();
        var totalWeight = weights.Sum();
        var widths = new double[weights.Length];
        var used = 0.0;

        for (var i = 0; i < weights.Length; i++)
        {
            if (i == weights.Length - 1)
            {
                widths[i] = Math.Max(14, contentWidth - used);
                break;
            }

            var width = Math.Max(14, contentWidth * weights[i] / totalWeight);
            widths[i] = width;
            used += width;
        }

        // 最小值可能把总宽撑出页面：只压缩非最小列，保证不横向溢出。
        var overflow = widths.Sum() - contentWidth;
        if (overflow > 0)
        {
            var shrinkable = widths.Where(w => w > 14).ToArray();
            var shrinkTotal = shrinkable.Sum();
            if (shrinkTotal > 0)
            {
                for (var i = 0; i < widths.Length; i++)
                {
                    if (widths[i] <= 14) continue;
                    widths[i] -= overflow * (widths[i] / shrinkTotal);
                }
            }
        }

        return widths;
    }

    /// <summary>
    /// 按字符宽度换行：中文没有空格，只能逐字测量；宽度缓存避免大报表反复测量。
    /// </summary>
    private static List<string> WrapText(
        XGraphics gfx,
        string? text,
        XFont font,
        double maxWidth,
        Dictionary<char, double> cache)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            lines.Add(string.Empty);
            return lines;
        }

        var builder = new StringBuilder();
        var currentWidth = 0.0;
        foreach (var ch in text)
        {
            if (ch == '\r') continue;
            if (ch == '\n')
            {
                lines.Add(builder.ToString());
                builder.Clear();
                currentWidth = 0;
                continue;
            }

            if (!cache.TryGetValue(ch, out var charWidth))
            {
                charWidth = MeasureCharWidth(gfx, ch, font);
                cache[ch] = charWidth;
            }

            if (currentWidth + charWidth > maxWidth && builder.Length > 0)
            {
                lines.Add(builder.ToString());
                builder.Clear();
                currentWidth = 0;
            }

            builder.Append(ch);
            currentWidth += charWidth;
        }

        if (builder.Length > 0 || lines.Count == 0)
            lines.Add(builder.ToString());

        return lines;
    }

    private static double MeasureCharWidth(XGraphics gfx, char ch, XFont font)
    {
        try
        {
            return gfx.MeasureString(ch.ToString(), font).Width;
        }
        catch
        {
            // 极端字体缺字时退回估算宽度，避免整份报表生成失败。
            return font.Size * (ch <= 0x7F ? 0.55 : 1.0);
        }
    }

    private static string FormatValue(object? value, ElderlyCertificationReportColumnFormat format)
    {
        if (value == null) return string.Empty;

        var culture = CultureInfo.GetCultureInfo("zh-CN");
        return format switch
        {
            ElderlyCertificationReportColumnFormat.Integer => Convert.ToInt64(value).ToString("N0", culture),
            ElderlyCertificationReportColumnFormat.Money => Convert.ToDecimal(value).ToString("N2", culture),
            ElderlyCertificationReportColumnFormat.Date => value switch
            {
                DateTime dateTime => dateTime.ToString("yyyy-MM-dd", culture),
                DateOnly dateOnly => dateOnly.ToString("yyyy-MM-dd", culture),
                _ => value.ToString() ?? string.Empty
            },
            ElderlyCertificationReportColumnFormat.Percent1 =>
                Convert.ToDecimal(value).ToString("0.0", culture) + "%",
            _ => value.ToString() ?? string.Empty
        };
    }
}
