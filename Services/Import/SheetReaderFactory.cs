namespace NewCosmos.Services.Import;

public static class SheetReaderFactory
{
    static SheetReaderFactory()
    {
        // .xls 走 ExcelDataReader（BIFF），.NET 默认不带 GBK/CP1252 等代码页；
        // 不注册会直接抛 "No data is available for encoding 1252"（真实样例已复现）。
        global::System.Text.Encoding.RegisterProvider(global::System.Text.CodePagesEncodingProvider.Instance);
    }

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