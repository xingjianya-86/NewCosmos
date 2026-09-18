namespace NewCosmos.Services.Import;

public interface IExcelSheetReader : IDisposable
{
    int RowCount { get; }
    int ColumnCount { get; }
    bool IsEmpty { get; }
    string GetCellText(int row, int col);
    string GetMergedCellText(int row, int col);
}