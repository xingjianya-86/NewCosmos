namespace NewCosmos.Services.Import;

public static class ImportDataReader
{
    public static string ReadString(Dictionary<string, int> columnMap, string[] row, string columnName)
    {
        if (!columnMap.TryGetValue(columnName, out var index) || index < 0 || index >= row.Length)
            return string.Empty;
        return (row[index] ?? "").Trim();
    }

    public static int ReadInt(Dictionary<string, int> columnMap, string[] row, string columnName)
    {
        var val = ReadString(columnMap, row, columnName);
        if (string.IsNullOrEmpty(val)) return 0;
        if (int.TryParse(val, out var result)) return result;
        if (decimal.TryParse(val, out var dec)) return (int)dec;
        Serilog.Log.Warning("[ImportDataReader] 整数列解析失败，按 0 导入: Column={Column}, Value={Value}", columnName, val);
        return 0;
    }

    public static decimal ReadDecimal(Dictionary<string, int> columnMap, string[] row, string columnName)
    {
        var val = ReadString(columnMap, row, columnName);
        if (string.IsNullOrEmpty(val)) return 0;
        if (decimal.TryParse(val, out var result)) return result;
        Serilog.Log.Warning("[ImportDataReader] 金额列解析失败，按 0 导入: Column={Column}, Value={Value}", columnName, val);
        return 0;
    }

    public static DateTime? ReadDate(Dictionary<string, int> columnMap, string[] row, string columnName)
    {
        var val = ReadString(columnMap, row, columnName);
        if (string.IsNullOrEmpty(val)) return null;
        if (DateTime.TryParse(val, out var result)) return result;
        return null;
    }

    public static bool ReadBool(Dictionary<string, int> columnMap, string[] row, string columnName)
    {
        var val = ReadString(columnMap, row, columnName);
        if (string.IsNullOrEmpty(val)) return false;
        if (bool.TryParse(val, out var result)) return result;
        return val is "1" or "是" or "true" or "yes" or "Yes";
    }
}
