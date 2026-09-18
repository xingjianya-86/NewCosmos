using ExcelDataReader;
using System.Data;

namespace NewCosmos.Services.Import;

public class ExcelDataReaderSheetReader : IExcelSheetReader
{
    private readonly IExcelDataReader _reader;
    private readonly FileStream _stream;
    private readonly DataTable _dataTable;

    public int RowCount => _dataTable.Rows.Count;
    public int ColumnCount => _dataTable.Columns.Count;
    public bool IsEmpty => _dataTable.Rows.Count == 0;

    public string GetCellText(int row, int col)
    {
        var dataRow = row - 1;
        var dataCol = col - 1;
        if (dataRow < 0 || dataRow >= _dataTable.Rows.Count || dataCol < 0 || dataCol >= _dataTable.Columns.Count)
            return "";
        var value = _dataTable.Rows[dataRow][dataCol];
        return value.ToString()?.Trim() ?? "";
    }

    public string GetMergedCellText(int row, int col)
    {
        return GetCellText(row, col);
    }

    public ExcelDataReaderSheetReader(string filePath)
    {
        _stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        _reader = ExcelReaderFactory.CreateReader(_stream);
        var ds = _reader.AsDataSet();
        if (ds.Tables.Count == 0)
        {
            _reader.Dispose();
            _stream.Dispose();
            throw new InvalidOperationException("请确保Excel文件至少包含一个工作表");
        }
        _dataTable = ds.Tables[0];
    }

    public void Dispose()
    {
        _dataTable.Dispose();
        _reader.Dispose();
        _stream.Dispose();
    }
}
