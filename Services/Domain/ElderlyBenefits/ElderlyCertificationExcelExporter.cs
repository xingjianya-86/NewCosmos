using NewCosmos.Constants;
using NewCosmos.Models.Results;
using OfficeOpenXml;
using OfficeOpenXml.Style;

namespace NewCosmos.Services.Domain.ElderlyBenefits;

/// <summary>
/// 高龄年度认证 Excel 导出器：EPPlus 8 生成带标题行/表头/冻结窗格/数字日期格式的 .xlsx。
/// 空数据也生成“标题 + 表头”的空表（合同要求无数据仍返回可打开的模板）。
/// 不写文件、不做 PDF/打印，只返回 byte[]，由调用方决定落盘位置。
/// </summary>
public static class ElderlyCertificationExcelExporter
{
    private const string LicenseOrganization = "民政社会救助管理系统";
    private const int HeaderRow = 2;
    private const int FirstDataRow = 3;
    private const int AutoFitSampleRows = 500;

    /// <summary>生成 xlsx 字节。失败返回 Result.Failure，不吞异常。</summary>
    public static Result<byte[]> Export(ElderlyCertificationReport report)
    {
        if (report == null)
            return Result.Failure<byte[]>(ErrorCodes.VALIDATION_FAILED, "报表数据为空");
        if (report.Columns.Count == 0)
            return Result.Failure<byte[]>(ErrorCodes.VALIDATION_FAILED, "报表没有列定义");

        try
        {
            ExcelPackage.License.SetNonCommercialOrganization(LicenseOrganization);
            using var package = new ExcelPackage();
            var sheet = package.Workbook.Worksheets.Add(ResolveSheetName(report.FormKey));
            var columnCount = report.Columns.Count;

            BuildTitle(sheet, report.Title, columnCount);
            BuildHeader(sheet, report.Columns);
            var lastDataRow = BuildDataRows(sheet, report, columnCount);

            ApplyColumnFormats(sheet, report.Columns);
            ApplyBorders(sheet, columnCount, lastDataRow);
            ApplyViewSettings(sheet, columnCount);
            ApplyColumnWidths(sheet, report.Columns, lastDataRow);

            return Result.Success(package.GetAsByteArray());
        }
        catch (Exception ex)
        {
            return Result.Failure<byte[]>(ErrorCodes.UNKNOWN_ERROR, $"生成认证 Excel 失败：{ex.Message}");
        }
    }

    private static void BuildTitle(ExcelWorksheet sheet, string title, int columnCount)
    {
        if (columnCount > 1)
            sheet.Cells[1, 1, 1, columnCount].Merge = true;

        var cell = sheet.Cells[1, 1];
        cell.Value = title;
        cell.Style.Font.Bold = true;
        cell.Style.Font.Size = 16;
        cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
        sheet.Row(1).Height = 28;
    }

    private static void BuildHeader(
        ExcelWorksheet sheet,
        IReadOnlyList<ElderlyCertificationReportColumn> columns)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            var cell = sheet.Cells[HeaderRow, i + 1];
            cell.Value = columns[i].Name;
            cell.Style.Font.Bold = true;
            cell.Style.Font.Size = 11;
            cell.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            cell.Style.VerticalAlignment = ExcelVerticalAlignment.Center;
            cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
            cell.Style.Fill.BackgroundColor.SetColor(global::System.Drawing.Color.FromArgb(0xEF, 0xF2, 0xF5));
        }
        sheet.Row(HeaderRow).Height = 22;
    }

    /// <summary>写数据，返回最后一行行号（空数据时等于表头行）。</summary>
    private static int BuildDataRows(ExcelWorksheet sheet, ElderlyCertificationReport report, int columnCount)
    {
        var rowIndex = FirstDataRow;
        foreach (var row in report.Rows)
        {
            for (var c = 0; c < columnCount; c++)
            {
                var value = row.Get(report.Columns[c].Key);
                if (value is null || value is string text && text.Length == 0)
                    continue;

                sheet.Cells[rowIndex, c + 1].Value = value;
            }
            rowIndex++;
        }
        return rowIndex - 1;
    }

    private static void ApplyColumnFormats(
        ExcelWorksheet sheet,
        IReadOnlyList<ElderlyCertificationReportColumn> columns)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            var column = sheet.Column(i + 1);
            column.Style.Numberformat.Format = GetNumberFormat(columns[i].Format);
            if (columns[i].Format is ElderlyCertificationReportColumnFormat.Date)
                column.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
        }
    }

    private static void ApplyBorders(ExcelWorksheet sheet, int columnCount, int lastDataRow)
    {
        var lastRow = Math.Max(lastDataRow, HeaderRow);
        var range = sheet.Cells[HeaderRow, 1, lastRow, columnCount];
        range.Style.Border.Top.Style = ExcelBorderStyle.Thin;
        range.Style.Border.Bottom.Style = ExcelBorderStyle.Thin;
        range.Style.Border.Left.Style = ExcelBorderStyle.Thin;
        range.Style.Border.Right.Style = ExcelBorderStyle.Thin;
    }

    private static void ApplyViewSettings(ExcelWorksheet sheet, int columnCount)
    {
        // 冻结标题行 + 表头（第 1、2 行），滚动时表头始终可见。
        sheet.View.FreezePanes(HeaderRow + 1, 1);
        sheet.Cells[HeaderRow, 1, HeaderRow, columnCount].AutoFilter = true;
    }

    private static void ApplyColumnWidths(
        ExcelWorksheet sheet,
        IReadOnlyList<ElderlyCertificationReportColumn> columns,
        int lastDataRow)
    {
        // 只取表头 + 前 500 行估宽，避免十万行报表 AutoFit 过慢。
        var sampleLastRow = Math.Min(Math.Max(lastDataRow, HeaderRow), HeaderRow + AutoFitSampleRows);
        sheet.Cells[HeaderRow, 1, sampleLastRow, columns.Count].AutoFitColumns();

        for (var i = 0; i < columns.Count; i++)
        {
            var minimum = columns[i].Width;
            var actual = sheet.Column(i + 1).Width;
            sheet.Column(i + 1).Width = Math.Clamp(Math.Max(actual, minimum), minimum, 60);
        }
    }

    private static string GetNumberFormat(ElderlyCertificationReportColumnFormat format) => format switch
    {
        ElderlyCertificationReportColumnFormat.Integer => "0",
        ElderlyCertificationReportColumnFormat.Money => "#,##0.00",
        ElderlyCertificationReportColumnFormat.Date => "yyyy-mm-dd",
        ElderlyCertificationReportColumnFormat.Percent1 => "0.0\"%\"",
        _ => "@"
    };

    private static string ResolveSheetName(string formKey)
    {
        var name = ElderlyCertificationReportFormKeys.GetName(formKey);
        if (string.IsNullOrWhiteSpace(name))
            name = "报表";

        foreach (var invalid in new[] { ':', '\\', '/', '?', '*', '[', ']' })
            name = name.Replace(invalid, ' ');

        name = name.Trim();
        return name.Length > 31 ? name[..31] : name;
    }
}
