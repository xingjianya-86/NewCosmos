using System.Text.RegularExpressions;
using NewCosmos.Models.Options;
using NewCosmos.Services.Core;
using OfficeOpenXml;

namespace NewCosmos.Services.Templates;

/// <summary>
/// Excel 模板引擎（基于 EPPlus + COM）
/// 填充/保存全部走 EPPlus（无 COM）；仅打印与 PDF 导出经 OfficeComWorker 串行化的 Office COM。
/// </summary>
public class ExcelEngine : ITemplateEngine
{
    public string PrinterName { get; set; } = string.Empty;
    public bool IsDuplex { get; set; }
    private readonly ILoggerService _logger;
    private readonly PerformanceOptions _perfOptions;
    private readonly StorageOptions _storageOptions;
    private ExcelPackage _package = null!;
    private ExcelWorksheet _worksheet = null!;
    private string _tempFilePath = string.Empty;
    private bool _disposed;

    public ExcelEngine(ILoggerService logger, PerformanceOptions perfOptions, StorageOptions storageOptions)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _perfOptions = perfOptions;
        _storageOptions = storageOptions ?? throw new ArgumentNullException(nameof(storageOptions));
        ExcelPackage.License.SetNonCommercialOrganization("民政社会救助管理系统");
        OfficeComWorker.AttachLogger(logger);
    }

    public void Load(byte[] templateData)
    {
        if (templateData == null || templateData.Length == 0)
            throw new ArgumentException("模板数据不能为空", nameof(templateData));

        _logger.Debug("调试");
        _package = new ExcelPackage(new MemoryStream(templateData));
        if (_package.Workbook.Worksheets.Count == 0)
            throw new InvalidOperationException("ExcelEngine: 未加载模板");

        _worksheet = _package.Workbook.Worksheets[0];
        _logger.Info("执行操作");
    }

    public void LoadFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("文件路径不能为空", nameof(filePath));

        if (!File.Exists(filePath))
            throw new FileNotFoundException("模板文件不存在", filePath);

        _logger.Debug("执行");
        _package = new ExcelPackage(new FileInfo(filePath));
        if (_package.Workbook.Worksheets.Count == 0)
            throw new InvalidOperationException("ExcelEngine: 未加载模板");

        _worksheet = _package.Workbook.Worksheets[0];
        _tempFilePath = filePath;
        _logger.Info("执行操作");
    }

    public void ReplaceField(string placeholder, string value)
    {
        if (_worksheet == null)
            throw new InvalidOperationException("ExcelEngine: 未加载工作表");

        if (string.IsNullOrEmpty(placeholder))
            return;

        _logger.Debug("执行");

        var dimension = _worksheet.Dimension;
        if (dimension == null) return;

        for (int row = dimension.Start.Row; row <= dimension.End.Row; row++)
        {
            for (int col = dimension.Start.Column; col <= dimension.End.Column; col++)
            {
                var cell = _worksheet.Cells[row, col];
                var cellText = cell.Text;

                if (!string.IsNullOrEmpty(cellText) && cellText.Contains(placeholder))
                {
                    if (string.IsNullOrEmpty(value) && (cellText.Trim() == placeholder + "元/月" || cellText.Trim() == placeholder + "元整" || cellText.Trim() == placeholder + "元"))
                        cell.Value = "-";
                    else
                    {
                        cell.Value = cellText.Replace(placeholder, value);
                        // 值含换行时启用单元格自动换行（与 ReplaceFields 一致）
                        if ((value ?? string.Empty).Contains('\n'))
                            cell.Style.WrapText = true;
                    }
                }
            }
        }
    }

    public void ReplaceFields(Dictionary<string, string> fields)
    {
        if (fields == null || fields.Count == 0)
            return;

        if (_worksheet == null)
            throw new InvalidOperationException("ExcelEngine: 未加载工作表");

        var dimension = _worksheet.Dimension;
        if (dimension == null) return;

        _logger.Debug("执行");

        // 单遍扫描：每个单元格读一次 .Text，对含占位符的单元格做多占位符一次性替换。
        // 原实现对每个字段各扫一遍全表（O(字段数×行×列)），100 字段 × 200×30 表 = 60 万次 .Text 求值。
        for (int row = dimension.Start.Row; row <= dimension.End.Row; row++)
        {
            for (int col = dimension.Start.Column; col <= dimension.End.Column; col++)
            {
                var cell = _worksheet.Cells[row, col];
                var cellText = cell.Text;
                if (string.IsNullOrEmpty(cellText))
                    continue;

                string? replaced = null;
                foreach (var kvp in fields)
                {
                    if (string.IsNullOrEmpty(kvp.Key))
                        continue;

                    var current = replaced ?? cellText;
                    if (current.Contains(kvp.Key))
                    {
                        if (string.IsNullOrEmpty(kvp.Value))
                        {
                            // 金额占位符无值：整格"占位符+元/月/元整/元"替换为 "-"，否则删除占位符
                            var trimmed = current.Trim();
                            if (trimmed == kvp.Key + "元/月" || trimmed == kvp.Key + "元整" || trimmed == kvp.Key + "元")
                                replaced = "-";
                            else
                                replaced = current.Replace(kvp.Key, "");
                        }
                        else
                        {
                            replaced = current.Replace(kvp.Key, kvp.Value);
                        }
                    }
                }

                if (replaced != null)
                {
                    cell.Value = replaced;
                    // 值含换行时启用单元格自动换行，否则 Excel 不显示多行（勾选清单 5+4 两行排版）
                    if (replaced.Contains('\n'))
                        cell.Style.WrapText = true;
                }
            }
        }
    }

    public void ReplaceCompositeField(CompositeFieldConfig composite, Dictionary<string, string> subFieldValues)
    {
        if (_worksheet == null)
            throw new InvalidOperationException("ExcelEngine: 未加载工作表");

        if (string.IsNullOrEmpty(composite.Placeholder))
            return;

        var expandedValue = composite.Template;
        foreach (var subField in composite.SubFields)
        {
            if (subFieldValues.TryGetValue(subField, out var value))
            {
                expandedValue = expandedValue.Replace(subField, value ?? string.Empty);
            }
            else
            {
                expandedValue = expandedValue.Replace(subField, string.Empty);
            }
        }

        _logger.Debug("执行");
        ReplaceField(composite.Placeholder, expandedValue);
    }

    public void ReplaceCompositeFields(List<CompositeFieldConfig> composites, Dictionary<string, string> allFieldValues)
    {
        if (composites == null || composites.Count == 0)
            return;

        _logger.Debug("执行");
        foreach (var composite in composites)
        {
            ReplaceCompositeField(composite, allFieldValues);
        }
    }

    public void ReplaceTableByPlaceholder(string tableStartMarker, string tableEndMarker, List<Dictionary<string, string>> rows)
    {
        if (_worksheet == null)
            throw new InvalidOperationException("ExcelEngine: 未加载工作表");

        if (rows == null || rows.Count == 0)
            return;

        _logger.Debug($"[ExcelEngine] Replacing table: {tableStartMarker}..{tableEndMarker}");

        var dimension = _worksheet.Dimension;
        if (dimension == null) return;

        int templateRowIndex = -1;
        for (int row = dimension.Start.Row; row <= dimension.End.Row; row++)
        {
            var rowText = GetRowText(row);
            if (rowText.Contains(tableStartMarker) && rowText.Contains(tableEndMarker))
            {
                templateRowIndex = row;
                break;
            }
        }

        if (templateRowIndex < 0)
        {
            _logger.Warn("警告");
            return;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            int targetRow = templateRowIndex + i;

            if (i > 0)
            {
                _worksheet.InsertRow(targetRow, 1);
                CopyRowFormat(templateRowIndex, targetRow);
            }

            FillRowData(targetRow, rows[i]);
        }

        _logger.Info("执行操作");
    }

    private void FillRowData(int targetRow, Dictionary<string, string> data)
    {
        if (_worksheet == null || data == null) return;

        var dimension = _worksheet.Dimension;
        if (dimension == null) return;

        for (int col = dimension.Start.Column; col <= dimension.End.Column; col++)
        {
            var cellValue = _worksheet.Cells[targetRow, col]?.Text;
            if (string.IsNullOrEmpty(cellValue)) continue;

            foreach (var kvp in data)
            {
                if (cellValue.Contains(kvp.Key))
                {
                    _worksheet.Cells[targetRow, col].Value = cellValue.Replace(kvp.Key, kvp.Value ?? string.Empty);
                    break;
                }
            }
        }
    }

    public async Task<byte[]> RenderWithPagingAsync(
        byte[] templateData,
        Dictionary<string, string> fields,
        List<string> tablePlaceholders,
        List<Dictionary<string, string>> rows,
        int pageSize,
        CancellationToken ct = default)
    {
        if (templateData == null || templateData.Length == 0)
            throw new ArgumentException("模板数据不能为空", nameof(templateData));

        if (rows == null || rows.Count == 0)
        {
            using var singleEngine = new ExcelEngine(_logger, _perfOptions, _storageOptions);
            singleEngine.Load(templateData);
            singleEngine.ReplaceFields(fields);
            return await singleEngine.SaveAsync(ct);
        }

        if (pageSize <= 0) pageSize = rows.Count;

        var chunks = TemplateEngineHelpers.ChunkList(rows, pageSize);
        _logger.Info($"[ExcelEngine] 印章模式渲染: {rows.Count} 行数, 每页 {pageSize} 行, 共 {chunks.Count} 页");

        if (chunks.Count == 1)
        {
            using var singleEngine = new ExcelEngine(_logger, _perfOptions, _storageOptions);
            singleEngine.Load(templateData);
            singleEngine.ReplaceFields(fields);
            ReplaceTableByPlaceholders(singleEngine._worksheet!, tablePlaceholders, chunks[0]);
            return await singleEngine.SaveAsync(ct);
        }

        var pageBytes = new List<byte[]>();
        foreach (var chunk in chunks)
        {
            ct.ThrowIfCancellationRequested();

            using var pageEngine = new ExcelEngine(_logger, _perfOptions, _storageOptions);
            pageEngine.Load(templateData);
            pageEngine.ReplaceFields(fields);
            ReplaceTableByPlaceholders(pageEngine._worksheet!, tablePlaceholders, chunk);
            pageBytes.Add(await pageEngine.SaveAsync(ct));
        }

        _logger.Info($"[ExcelEngine] 印章模式渲染完成: {pageBytes.Count} 页");
        return MergeExcelDocuments(pageBytes);
    }

    private void ReplaceTableByPlaceholders(ExcelWorksheet worksheet, List<string> tablePlaceholders, List<Dictionary<string, string>> rows)
    {
        var dimension = worksheet.Dimension;
        if (dimension == null) return;

        int templateRowIndex = -1;
        for (int row = dimension.Start.Row; row <= dimension.End.Row; row++)
        {
            var rowText = GetRowText(row);
            if (tablePlaceholders.Any(p => rowText.Contains(p)))
            {
                templateRowIndex = row;
                break;
            }
        }

        if (templateRowIndex < 0)
        {
            _logger.Warn("警告");
            return;
        }

        for (int i = 0; i < rows.Count; i++)
        {
            int targetRow = templateRowIndex + i;

            if (i > 0)
            {
                worksheet.InsertRow(targetRow, 1);
                CopyRowFormat(templateRowIndex, targetRow);
            }

            FillRowData(targetRow, rows[i]);
        }

        _logger.Debug($"[ExcelEngine] 表格填充: {rows.Count} 条");
    }

    private byte[] MergeExcelDocuments(List<byte[]> documents)
    {
        if (documents.Count == 0) return Array.Empty<byte>();
        if (documents.Count == 1) return documents[0];

        using var mergedPackage = new ExcelPackage();
        mergedPackage.Workbook.Worksheets.Add("Sheet1");

        using (var firstStream = new MemoryStream(documents[0]))
        using (var firstPackage = new ExcelPackage(firstStream))
        {
            if (firstPackage.Workbook.Worksheets.Count > 0)
            {
                var firstSheet = firstPackage.Workbook.Worksheets[0];
                var mergedSheet = mergedPackage.Workbook.Worksheets[0];
                mergedSheet.Cells["A1"].LoadFromDataTable(firstSheet.Cells.ToDataTable(), true);
            }
        }

        for (int i = 1; i < documents.Count; i++)
        {
            var sheetName = $"Page{i + 1}";
            var newSheet = mergedPackage.Workbook.Worksheets.Add(sheetName);

            using var chunkStream = new MemoryStream(documents[i]);
            using var chunkPackage = new ExcelPackage(chunkStream);
            if (chunkPackage.Workbook.Worksheets.Count > 0)
            {
                var chunkSheet = chunkPackage.Workbook.Worksheets[0];
                newSheet.Cells["A1"].LoadFromDataTable(chunkSheet.Cells.ToDataTable(), true);
            }
        }

        _logger.Info($"[ExcelEngine] 文档合并完成: {documents.Count} 页");
        return mergedPackage.GetAsByteArray();
    }

    public async Task<byte[]> SaveAsync(CancellationToken ct = default)
    {
        if (_package == null)
            throw new InvalidOperationException("ExcelEngine: 未加载文档");

        _logger.Debug("调试");
        var bytes = await _package.GetAsByteArrayAsync();
        _logger.Info("执行操作");
        return bytes;
    }

    public async Task SaveToFileAsync(string outputPath, CancellationToken ct = default)
    {
        if (_package == null)
            throw new InvalidOperationException("ExcelEngine: 未加载文档");

        if (string.IsNullOrWhiteSpace(outputPath))
            throw new ArgumentException("输出路径不能为空", nameof(outputPath));

        _logger.Debug("执行");
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var fileInfo = new FileInfo(outputPath);
        await _package.SaveAsAsync(fileInfo);
        _logger.Info("执行操作");
    }

    public async Task<byte[]> ExportPdfAsync(CancellationToken ct = default)
    {
        if (_package == null)
            throw new InvalidOperationException("ExcelEngine: 未加载文档");

        _logger.Info("[ExcelEngine] ExportPdfAsync 开始");

        var xlsxPath = TemplateEngineHelpers.NewTempFilePath(_storageOptions, ".xlsx");
        var pdfPath = TemplateEngineHelpers.NewTempFilePath(_storageOptions, ".pdf");

        try
        {
            // 直接用 EPPlus 保存的文件导出 PDF（填充过程不经过 Office COM）
            await SaveToFileAsync(xlsxPath, ct);
            return await OfficeComDocuments.ExportToPdfAsync(OfficeAppKind.Excel, xlsxPath, pdfPath, _logger, ct);
        }
        finally
        {
            TemplateEngineHelpers.TryDeleteFile(xlsxPath, _logger, "ExcelEngine");
            TemplateEngineHelpers.TryDeleteFile(pdfPath, _logger, "ExcelEngine");
        }
    }

    public async Task<string> ExportPdfToFileAsync(string outputDir, CancellationToken ct = default)
    {
        if (_package == null)
            throw new InvalidOperationException("ExcelEngine: 未加载文档");

        if (string.IsNullOrWhiteSpace(outputDir))
            throw new ArgumentException("输出目录不能为空", nameof(outputDir));

        Directory.CreateDirectory(outputDir);

        // 修复：Load(byte[]) 路径下 _tempFilePath 为空串（非 null），原判断永真会生成 ".pdf" 无名文件
        var baseName = !string.IsNullOrEmpty(_tempFilePath)
            ? Path.GetFileNameWithoutExtension(_tempFilePath)
            : $"document_{DateTime.Now:yyyyMMdd_HHmmss}";
        var pdfPath = Path.Combine(outputDir, $"{baseName}.pdf");

        var counter = 1;
        while (File.Exists(pdfPath))
        {
            pdfPath = Path.Combine(outputDir, $"{baseName}_{counter}.pdf");
            counter++;
        }

        _logger.Debug("执行");

        var xlsxPath = TemplateEngineHelpers.NewTempFilePath(_storageOptions, ".xlsx", subDirectory: null);

        try
        {
            await SaveToFileAsync(xlsxPath, ct);
            await OfficeComDocuments.ExportToPdfAsync(OfficeAppKind.Excel, xlsxPath, pdfPath, _logger, ct);
            _logger.Info("执行操作");
            return pdfPath;
        }
        finally
        {
            TemplateEngineHelpers.TryDeleteFile(xlsxPath, _logger, "ExcelEngine");
        }
    }

    public async Task PrintFromFileAsync(string filePath, int copies = 1, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new FileNotFoundException("文件不存在", filePath);

        _logger.Debug($"[ExcelEngine] Printing from file: {filePath}");

        await OfficeComDocuments.PrintFileAsync(OfficeAppKind.Excel, filePath, PrinterName, IsDuplex, copies, _logger, ct);

        _logger.Info("执行操作");
    }

    public async Task<byte[]> ExportPdfFromFileAsync(string sourceFilePath, string pdfOutputPath = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
            throw new FileNotFoundException("文件不存在", sourceFilePath);

        _logger.Debug("执行");

        var deleteAfterRead = string.IsNullOrEmpty(pdfOutputPath);

        if (string.IsNullOrEmpty(pdfOutputPath))
        {
            var pdfDir = Path.GetDirectoryName(sourceFilePath) ?? Path.GetTempPath();
            pdfOutputPath = Path.Combine(pdfDir, Path.GetFileNameWithoutExtension(sourceFilePath) + ".pdf");
        }

        try
        {
            var pdfBytes = await OfficeComDocuments.ExportToPdfAsync(OfficeAppKind.Excel, sourceFilePath, pdfOutputPath, _logger, ct);
            _logger.Info("执行操作");

            if (deleteAfterRead)
            {
                TemplateEngineHelpers.TryDeleteFile(pdfOutputPath, _logger, "ExcelEngine");
            }

            return pdfBytes;
        }
        catch (Exception ex)
        {
            _logger.Error($"操作失败: {ex.Message}");
            throw;
        }
    }

    public List<string> GetUnresolvedPlaceholders()
    {
        if (_worksheet == null)
            return new List<string>();

        var placeholders = new HashSet<string>();
        var pattern = new Regex(@"\{\w+\}");
        var dimension = _worksheet.Dimension;
        if (dimension == null) return placeholders.ToList();

        for (int row = dimension.Start.Row; row <= dimension.End.Row; row++)
        {
            for (int col = dimension.Start.Column; col <= dimension.End.Column; col++)
            {
                var cellText = _worksheet.Cells[row, col].Text;
                if (string.IsNullOrEmpty(cellText)) continue;

                foreach (Match match in pattern.Matches(cellText))
                {
                    placeholders.Add(match.Value);
                }
            }
        }

        return placeholders.ToList();
    }

    private string GetRowText(int row)
    {
        if (_worksheet == null) return string.Empty;

        var dimension = _worksheet.Dimension;
        if (dimension == null) return string.Empty;

        var texts = new List<string>();
        for (int col = dimension.Start.Column; col <= dimension.End.Column; col++)
        {
            texts.Add(_worksheet.Cells[row, col].Text ?? string.Empty);
        }
        return string.Join(" ", texts);
    }

    private void CopyRowFormat(int sourceRow, int targetRow)
    {
        if (_worksheet == null) return;

        var dimension = _worksheet.Dimension;
        if (dimension == null) return;

        _worksheet.Row(targetRow).Height = _worksheet.Row(sourceRow).Height;

        for (int col = dimension.Start.Column; col <= dimension.End.Column; col++)
        {
            var sourceCell = _worksheet.Cells[sourceRow, col];
            var targetCell = _worksheet.Cells[targetRow, col];

            targetCell.StyleID = sourceCell.StyleID;
        }
    }

    /// <summary>
    /// 删除指定行区间（含端行）。用于"固定行数模板"分页时删除尾部空行（如公示名单每页 22 行）。
    /// </summary>
    public void DeleteRows(int startRow, int endRow)
    {
        if (_worksheet == null)
            throw new InvalidOperationException("ExcelEngine: 未加载工作表");

        if (startRow <= 0 || endRow < startRow)
            return;

        _logger.Debug($"执行删除行: {startRow}~{endRow}");
        // 一次删除连续区间（EPPlus DeleteRow 按序号删除，先删行号大的区间更稳妥——此处区间连续，直接从 startRow 删除 count 行即可）
        _worksheet.DeleteRow(startRow, endRow - startRow + 1);
    }

    public void Dispose()
    {
        if (_disposed) return;

        // 修复：Load 从未被调用时 _package 为 null，原实现直接解引用会 NRE
        _package?.Dispose();
        _package = null;
        _worksheet = null;
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}
