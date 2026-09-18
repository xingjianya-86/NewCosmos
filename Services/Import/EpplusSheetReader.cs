using OfficeOpenXml;

namespace NewCosmos.Services.Import;

public class EpplusSheetReader : IExcelSheetReader
{
    private readonly ExcelPackage _package;
    private readonly ExcelWorksheet _worksheet;

    public int RowCount => _worksheet.Dimension?.Rows ?? 0;
    public int ColumnCount => _worksheet.Dimension?.Columns ?? 0;
    public bool IsEmpty => _worksheet.Dimension == null;

    public string GetCellText(int row, int col)
    {
        return _worksheet.Cells[row, col].Text.Trim() ?? string.Empty;
    }

    public string GetMergedCellText(int row, int col)
    {
        var value = _worksheet.Cells[row, col].Text.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(value))
            return value;

        if (_worksheet.MergedCells == null)
            return string.Empty;

        foreach (var mergedRange in _worksheet.MergedCells)
        {
            if (mergedRange == null)
                continue;

            var range = _worksheet.Cells[mergedRange];
            if (range.Start.Row <= row && row <= range.End.Row &&
                range.Start.Column <= col && col <= range.End.Column)
            {
                return _worksheet.Cells[range.Start.Row, range.Start.Column].Text.Trim() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    public EpplusSheetReader(string filePath)
    {
        ExcelPackage.License.SetNonCommercialOrganization("民政社会救助管理系统");
        _package = new ExcelPackage(new FileInfo(filePath));
        if (_package.Workbook.Worksheets.Count == 0)
        {
            _package.Dispose();
            throw new InvalidOperationException("Excel 文件没有工作表");
        }
        _worksheet = _package.Workbook.Worksheets[0];
    }

    public void Dispose()
    {
        _package.Dispose();
    }
}
