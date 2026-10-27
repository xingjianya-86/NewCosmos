using System.Text;

namespace NewCosmos.Services.Import;

/// <summary>整表读取结果：第 0 行为表头，其余为数据行（已剔除空行）</summary>
public sealed class TabularData
{
    public string[] Headers { get; init; } = Array.Empty<string>();
    public List<string[]> Rows { get; init; } = new();
    public bool IsEmpty => Headers.Length == 0 || Rows.Count == 0;
}

/// <summary>
/// 统一的表格文件读取器：按扩展名分流 —— Excel 走 <see cref="SheetReaderFactory"/>，
/// 文本走 CSV/TSV 解析。
/// 背景：组织导入原先无论什么扩展名都 <c>File.ReadAllLines</c> + <c>Split('\t', ',')</c>，
/// 而界面写着"支持 .xlsx / .xls / .csv"——选 Excel 读到的是二进制乱码。
/// </summary>
public static class TabularFileReader
{
    static TabularFileReader()
    {
        // .NET 默认不带 GBK/CP936 等代码页；中文 Windows 下 Excel「另存为 CSV」是 ANSI，
        // 不注册这里会在 GetEncoding(936) 时抛 NotSupportedException。
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>读取整表。Excel 用 <c>IExcelSheetReader</c>；文本自动识别分隔符并支持引号包裹。</summary>
    public static TabularData Read(string filePath)
    {
        if (SheetReaderFactory.IsExcelFile(filePath))
            return ReadExcel(filePath);

        return ReadText(filePath);
    }

    private static TabularData ReadExcel(string filePath)
    {
        using var reader = SheetReaderFactory.Create(filePath);
        if (reader.RowCount == 0 || reader.ColumnCount == 0)
            return new TabularData();

        var header = new string[reader.ColumnCount];
        for (var c = 0; c < header.Length; c++)
            header[c] = reader.GetCellText(0, c)?.Trim() ?? string.Empty;

        var rows = new List<string[]>(Math.Max(0, reader.RowCount - 1));
        for (var r = 1; r < reader.RowCount; r++)
        {
            var cells = new string[reader.ColumnCount];
            var allEmpty = true;
            for (var c = 0; c < cells.Length; c++)
            {
                cells[c] = reader.GetCellText(r, c)?.Trim() ?? string.Empty;
                if (cells[c].Length > 0) allEmpty = false;
            }
            if (!allEmpty) rows.Add(cells);
        }

        return new TabularData { Headers = header, Rows = rows };
    }

    private static TabularData ReadText(string filePath)
    {
        var text = DecodeText(File.ReadAllBytes(filePath));
        if (text.Length == 0)
            return new TabularData();

        var lines = text.Split('\n');
        if (lines.Length == 0)
            return new TabularData();

        var delimiter = DetectDelimiter(lines[0]);
        var headers = ParseLine(lines[0].TrimEnd('\r'), delimiter);

        var rows = new List<string[]>(lines.Length - 1);
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;
            rows.Add(ParseLine(line, delimiter));
        }

        return new TabularData { Headers = headers, Rows = rows };
    }

    /// <summary>
    /// 编码识别：BOM → 指定编码；否则先按严格 UTF-8 解，失败回退 GBK。
    /// 原实现 <c>File.ReadAllText</c> 恒用 UTF-8，GBK 的中文 CSV 会整表乱码
    /// （表头变成 "����"，直接被判为"缺少名称列"）。
    /// </summary>
    private static string DecodeText(byte[] bytes)
    {
        if (bytes.Length == 0) return string.Empty;

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)
                .GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            try
            {
                return Encoding.GetEncoding(936).GetString(bytes);   // GBK
            }
            catch (Exception)
            {
                return Encoding.UTF8.GetString(bytes);                // 兜底：至少不抛
            }
        }
    }

    /// <summary>按首行出现次数最多的 , / ; / \t 决定分隔符（制表符优先级相同，取数量最多者）</summary>
    public static char DetectDelimiter(string headerLine)
    {
        char best = ',';
        var bestCount = -1;
        foreach (var candidate in new[] { ',', '\t', ';' })
        {
            var count = headerLine.Count(ch => ch == candidate);
            if (count > bestCount)
            {
                bestCount = count;
                best = candidate;
            }
        }
        return best;
    }

    /// <summary>
    /// 解析一行：支持 "..." 包裹（内含分隔符/换行）、"" 转义双引号。
    /// 原实现是 <c>line.Split('\t', ',')</c>——地址里带逗号就直接错列。
    /// </summary>
    public static string[] ParseLine(string line, char delimiter)
    {
        var result = new List<string>(8);
        var sb = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (inQuotes)
            {
                if (ch == '"')
                {
                    // "" → 字面双引号；单个 " → 结束引号段
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    sb.Append(ch);
                }
            }
            else if (ch == '"')
            {
                inQuotes = true;
            }
            else if (ch == delimiter)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(ch);
            }
        }

        result.Add(sb.ToString());
        return result.ToArray();
    }
}
