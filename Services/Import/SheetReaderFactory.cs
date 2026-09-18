namespace NewCosmos.Services.Import;

public static class SheetReaderFactory
{
    public static IExcelSheetReader Create(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".xls" => new ExcelDataReaderSheetReader(filePath),
            ".xlsx" => new EpplusSheetReader(filePath),
            _ => throw new NotSupportedException($"不支持的文件格式: {ext}")
        };
    }

    public static bool IsExcelFile(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext == ".xls" || ext == ".xlsx";
    }
}