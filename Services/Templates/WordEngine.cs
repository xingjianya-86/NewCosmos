#if WINDOWS
using System.Runtime.InteropServices;
using NewCosmos.Constants;
using NewCosmos.Models.Options;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NPOI.XWPF.UserModel;

namespace NewCosmos.Services.Templates;

/// <summary>
/// Word 模板引擎（全链路 Office COM）
/// 加载 → 填充 → 保存 → 导出PDF 全部走 Office COM。
/// 所有 COM 调用经 OfficeComWorker 封送到唯一 STA 线程串行执行；
/// Word 应用实例由 worker 池化复用（原实现每次操作冷启动/退出一个 WINWORD 进程）。
/// </summary>
public class WordEngine : ITemplateEngine
{
    public string PrinterName { get; set; } = string.Empty;
    public bool IsDuplex { get; set; }
    private readonly ILoggerService _logger;
    private readonly PerformanceOptions _perfOptions;
    private readonly StorageOptions _storageOptions;

    // Office COM 文档对象：在 worker 线程上创建，仅允许在 worker 操作体内解引用
    private dynamic? _doc;
    private string _tempFilePath = string.Empty;
    private bool _disposed;

    public WordEngine(ILoggerService logger, PerformanceOptions perfOptions, StorageOptions storageOptions)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _perfOptions = perfOptions;
        _storageOptions = storageOptions ?? throw new ArgumentNullException(nameof(storageOptions));
        OfficeComWorker.AttachLogger(logger);
    }

    public void Load(byte[] templateData)
    {
        if (templateData == null || templateData.Length == 0)
            throw new ArgumentException("模板数据不能为空", nameof(templateData));

        _logger.Debug("WordEngine: 加载模板");

        // 保存到临时文件，然后用 Office COM 打开
        _tempFilePath = TemplateEngineHelpers.NewTempFilePath(_storageOptions, ".docx", prefix: "template_");
        File.WriteAllBytes(_tempFilePath, templateData);

        OpenDocument(_tempFilePath);
        _logger.Info("WordEngine: 模板加载完成（Office COM）");
    }

    public void LoadFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new FileNotFoundException("模板文件不存在", filePath);

        _logger.Debug("WordEngine: 从文件加载");
        _tempFilePath = filePath;
        OpenDocument(filePath);
        _logger.Info("WordEngine: 模板文件加载完成（Office COM）");
    }

    private void OpenDocument(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        OfficeComWorker.Instance.Invoke<object?>($"打开 Word 文档 {Path.GetFileName(filePath)}", OfficeAppKind.Word, ctx =>
        {
            var app = ctx.App;
            _doc = app.Documents.Open(fullPath);
            ctx.DocumentOpened();
            return null;
        });
    }

    /// <summary>关闭并释放当前文档（必须在 worker 操作体内调用），应用实例归还池中复用。</summary>
    private void CloseDocumentOnWorker(OfficeComWorker.OperationContext ctx)
    {
        if (_doc == null) return;

        try { _doc.Close(false); }
        catch (Exception ex) { _logger.Debug($"WordEngine: 关闭文档失败（预期）: {ex.Message}"); }

        try { Marshal.ReleaseComObject(_doc); }
        catch (Exception ex) { _logger.Debug($"WordEngine: 释放文档 COM 对象失败（预期）: {ex.Message}"); }

        _doc = null;
        ctx.DocumentClosed();
    }

    /// <summary>
    /// 释放局部 COM RCW（必须在创建它的 worker 操作体内调用）。
    /// Word 每次属性访问（Content/Range/Find/Paragraphs[i]…）都会产生新 RCW，
    /// 不释放会随打印任务堆积导致 Word 进程/句柄泄漏。非 COM 对象静默跳过。
    /// </summary>
    private static void SafeRelease(params object?[] comObjects)
    {
        foreach (var obj in comObjects)
        {
            if (obj == null) continue;
            try
            {
                if (Marshal.IsComObject(obj))
                    Marshal.ReleaseComObject(obj);
            }
            catch
            {
                // RCW 已释放或对象不可释放——释放路径失败不得影响主流程
            }
        }
    }

    /// <summary>
    /// 在 worker 线程上执行单个占位符替换（COM 失败仅记 Warn，与原行为一致）。
    /// 非空值走 Find 定位 + Range.Text 赋值：Find/Replace 的 ReplaceWith 参数上限 255 字符且不接受控制字符，
    /// 超长文本（如救助原因叙述）会抛 COM 异常导致占位符静默残留；Range.Text 赋值无长度限制。
    /// </summary>
    private void ReplaceFieldOnWorker(string placeholder, string value)
    {
        if (_doc is null || string.IsNullOrEmpty(placeholder)) return;

        // Word 内部换行即段落标记 \r：统一归一化，避免 \n 作为裸控制字符写入文档
        var valueSafe = (value ?? string.Empty)
            .Replace("\r\n", "\r", StringComparison.Ordinal)
            .Replace("\n", "\r", StringComparison.Ordinal);

        try
        {
            var maxHits = 100; // 防御异常模板导致的死循环
            var replaced = 0;

            if (string.IsNullOrEmpty(valueSafe))
            {
                // 金额占位符无值：先尝试"占位符+元/月/元整/元"整段替换为 "-"（后缀从长到短），再退化为删除占位符。
                // 替换目标均为短文本，可安全使用 ReplaceWith。
                var content = _doc.Content;
                var find = content.Find;
                try
                {
                    find.ClearFormatting();
                    find.Forward = true;
                    find.Wrap = 0;
                    find.MatchCase = false;
                    find.MatchWholeWord = false;

                    foreach (var suffix in new[] { "元/月", "元整", "元" })
                    {
                        find.Execute(Replace: 2, FindText: placeholder + suffix, ReplaceWith: "-");
                    }
                    find.Execute(Replace: 2, FindText: placeholder, ReplaceWith: "");
                }
                finally
                {
                    SafeRelease(find, content);
                }
            }
            else
            {
                // Find 命中并赋值后 Range 会收缩为局部文本，无法继续向后搜索；
                // 因此每轮都从整篇文档重新定位、替换当前第一处，直至无匹配。
                // （valueSafe 不含占位符原文，不会自我重复命中；同一占位符多处出现均可覆盖，
                //  如验收报告 {日期止} 在正文与落款各出现一次）
                while (replaced < maxHits)
                {
                    var searchRange = _doc.Content;
                    var find = searchRange.Find;
                    try
                    {
                        find.ClearFormatting();
                        find.Forward = true;
                        find.Wrap = 0; // wdFindStop
                        find.MatchCase = false;
                        find.MatchWholeWord = false;

                        if (!find.Execute(FindText: placeholder)) break;

                        searchRange.Text = valueSafe;
                        replaced++;
                    }
                    finally
                    {
                        // 每轮的 Range/Find 都是新 RCW，循环上限 100，不释放会堆积
                        SafeRelease(find, searchRange);
                    }
                }
            }

            if (replaced > 0)
                _logger.Debug($"WordEngine: 替换 {placeholder} ×{replaced}");
        }
        catch (Exception ex)
        {
            _logger.Warn($"WordEngine: 替换 {placeholder} 失败: {ex.Message}");
        }
    }

    public void ReplaceField(string placeholder, string value)
    {
        if (_doc == null) return;
        if (string.IsNullOrEmpty(placeholder)) return;

        try
        {
            OfficeComWorker.Instance.Invoke<object?>($"替换字段 {placeholder}", OfficeAppKind.Word, ctx =>
            {
                ReplaceFieldOnWorker(placeholder, value);
                return null;
            });
        }
        catch (Exception ex)
        {
            _logger.Warn($"WordEngine: 替换 {placeholder} 失败: {ex.Message}");
        }
    }

    public void ReplaceFields(Dictionary<string, string> fields)
    {
        if (fields == null || fields.Count == 0) return;
        if (_doc == null) return;

        _logger.Info($"WordEngine: 批量替换 {fields.Count} 个字段");

        // 整批合并为一次 worker 操作：避免每个字段一次线程封送与排队
        OfficeComWorker.Instance.Invoke<object?>($"批量替换 {fields.Count} 个字段", OfficeAppKind.Word, ctx =>
        {
            foreach (var kvp in fields)
            {
                ReplaceFieldOnWorker(kvp.Key, kvp.Value);
            }
            return null;
        });
    }

    /// <summary>
    /// 删除内容仅为单个待删占位符的整段（含段落标记）。用于会议记录模板多余的成员行，
    /// 必须在 ReplaceFields 之前调用（此时段内为占位符原文，匹配最稳）。
    /// </summary>
    public void RemovePlaceholderParagraphs(IReadOnlyCollection<string> placeholders)
    {
        var doc = _doc;
        if (doc == null) return;
        if (placeholders == null || placeholders.Count == 0) return;

        _logger.Info($"WordEngine: 删除占位符段落 {placeholders.Count} 个");

        OfficeComWorker.Instance.Invoke<object?>($"删除占位符段落 {placeholders.Count} 个", OfficeAppKind.Word, ctx =>
        {
            var doc = _doc;
            if (doc == null) return null;

            var removeSet = new HashSet<string>(placeholders);
            // 第一遍：收集待删段落的字符区间（Word 字符位置，删除段落不会使其漂移）
            var targetRanges = new List<(long Start, long End)>();
            var paragraphs = doc.Paragraphs;
            var paraCount = paragraphs.Count;
            try
            {
                for (var i = 1; i <= paraCount; i++)
                {
                    dynamic? para = null;
                    dynamic? paraRange = null;
                    try
                    {
                        para = paragraphs[i];
                        paraRange = para.Range;
                        var text = (paraRange.Text ?? string.Empty).Trim();
                        if (removeSet.Contains(text))
                        {
                            targetRanges.Add(((long)paraRange.Start, (long)paraRange.End));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"WordEngine: 扫描段落 {i} 失败: {ex.Message}");
                    }
                    finally
                    {
                        SafeRelease(paraRange, para);
                    }
                }
            }
            finally
            {
                SafeRelease(paragraphs);
            }
            // 第二遍：从高到低删除整段（倒序保证低位字符位置不受影响）。
            // 注意：Paragraph.Range 已含段落标记，Range.End 指向段落末尾之后，范围语义为 [Start, End)，
            // 故 doc.Range(start, end) 已覆盖整段；End+1 会把下一段首字符划入范围，误删后面内容。
            for (var j = targetRanges.Count - 1; j >= 0; j--)
            {
                var (start, end) = targetRanges[j];
                dynamic? rng = null;
                try
                {
                    rng = doc.Range(start, end);
                    rng.Delete();
                }
                catch (Exception ex)
                {
                    _logger.Warn($"WordEngine: 删除段 {start}~{end} 失败: {ex.Message}");
                }
                finally
                {
                    SafeRelease(rng);
                }
            }
            return null;
        });
    }

    public void ReplaceCompositeField(CompositeFieldConfig composite, Dictionary<string, string> subFieldValues)
    {
        if (composite == null || string.IsNullOrEmpty(composite.Placeholder)) return;

        var expandedValue = composite.Template;
        foreach (var subField in composite.SubFields)
        {
            var val = subFieldValues.TryGetValue(subField, out var v) ? v : string.Empty;
            expandedValue = expandedValue.Replace(subField, val ?? string.Empty);
        }
        ReplaceField(composite.Placeholder, expandedValue);
    }

    public void ReplaceCompositeFields(List<CompositeFieldConfig> composites, Dictionary<string, string> allFieldValues)
    {
        if (composites == null) return;
        foreach (var composite in composites)
        {
            ReplaceCompositeField(composite, allFieldValues);
        }
    }

    public void ReplaceTableByPlaceholder(string tableStartMarker, string tableEndMarker, List<Dictionary<string, string>> rows)
    {
        if (_doc is null || rows == null || rows.Count == 0) return;

        try
        {
            OfficeComWorker.Instance.Invoke<object?>($"填充表格 {tableStartMarker}", OfficeAppKind.Word, ctx =>
            {
                // 使用 Word COM 的 Find 定位表格起始标记
                // 局部 RCW 统一在 finally 中释放（原来每个 Range/Find/Table/Row/Cell 都泄漏）
                dynamic? content = null;
                dynamic? find = null;
                dynamic? selection = null;
                dynamic? startRange = null;
                dynamic? endRange = null;
                dynamic? fullRange = null;
                try
                {
                    content = _doc!.Content;
                    find = content.Find;
                    find.ClearFormatting();
                    find.Text = tableStartMarker;
                    find.Forward = true;
                    find.Wrap = 0;

                    if (!find.Execute())
                    {
                        _logger.Warn($"WordEngine: 未找到表格标记 {tableStartMarker}");
                        return null;
                    }

                    // 获取标记所在的段落
                    selection = _doc.Application.Selection;
                    startRange = selection.Range;

                    // 查找结束标记
                    find.Text = tableEndMarker;
                    if (!find.Execute())
                    {
                        _logger.Warn($"WordEngine: 未找到结束标记 {tableEndMarker}");
                        return null;
                    }

                    endRange = selection.Range;
                    fullRange = _doc.Range(startRange.Start, endRange.End);

                    // 获取范围内的表格
                    if (fullRange.Tables.Count > 0)
                    {
                        dynamic? table = fullRange.Tables[1]; // 第一个表格
                        dynamic? templateRow = null;
                        try
                        {
                            templateRow = table.Rows[table.Rows.Count]; // 最后一行作为模板

                            foreach (var rowData in rows)
                            {
                                dynamic? newRow = table.Rows.Add(templateRow);
                                try
                                {
                                    dynamic? cells = newRow.Cells;
                                    try
                                    {
                                        foreach (var cellObj in cells)
                                        {
                                            dynamic? cell = cellObj;
                                            dynamic? cellParas = null;
                                            try
                                            {
                                                cellParas = cell.Paragraphs;
                                                foreach (var paraObj in cellParas)
                                                {
                                                    dynamic? para = paraObj;
                                                    dynamic? pRange = null;
                                                    dynamic? pFind = null;
                                                    try
                                                    {
                                                        pRange = para.Range;
                                                        var text = (string?)pRange.Text ?? string.Empty;
                                                        foreach (var kvp in rowData)
                                                        {
                                                            if (text.Contains(kvp.Key))
                                                            {
                                                                pFind = pRange.Find;
                                                                pFind.Execute(kvp.Key, false, false, false, false, false, false, 1, false, kvp.Value ?? "", 2);
                                                            }
                                                        }
                                                    }
                                                    finally
                                                    {
                                                        SafeRelease(pFind, pRange, para);
                                                    }
                                                }
                                            }
                                            finally
                                            {
                                                SafeRelease(cellParas, cell);
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        SafeRelease(cells);
                                    }
                                }
                                finally
                                {
                                    SafeRelease(newRow);
                                }
                            }

                            _logger.Info($"WordEngine: 表格填充 {rows.Count} 行");
                        }
                        finally
                        {
                            SafeRelease(templateRow, table);
                        }
                    }
                    return null;
                }
                finally
                {
                    SafeRelease(fullRange, endRange, startRange, selection, find, content);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.Warn($"WordEngine: 表格替换异常: {ex.Message}");
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
        // 防御：rows 为空时走单页纯字段填充（原实现直接 rows.Count 会 NRE）
        if (rows == null || rows.Count == 0)
        {
            using var singleEngine = new WordEngine(_logger, _perfOptions, _storageOptions);
            singleEngine.Load(templateData);
            singleEngine.ReplaceFields(fields);
            return await singleEngine.SaveAsync(ct);
        }

        if (pageSize <= 0) pageSize = rows.Count;

        var chunks = TemplateEngineHelpers.ChunkList(rows, pageSize);
        _logger.Info($"WordEngine: 分页渲染 {rows.Count} 行, 每页 {pageSize} 行, 共 {chunks.Count} 页");

        if (chunks.Count == 1)
        {
            using var engine = new WordEngine(_logger, _perfOptions, _storageOptions);
            engine.Load(templateData);
            engine.ReplaceFields(fields);
            engine.ReplaceTableByPlaceholder(tablePlaceholders.FirstOrDefault() ?? "", "", chunks[0]);
            return await engine.SaveAsync(ct);
        }

        var pageBytes = new List<byte[]>();
        foreach (var chunk in chunks)
        {
            ct.ThrowIfCancellationRequested();

            using var pageEngine = new WordEngine(_logger, _perfOptions, _storageOptions);
            pageEngine.Load(templateData);
            pageEngine.ReplaceFields(fields);
            pageEngine.ReplaceTableByPlaceholder(tablePlaceholders.FirstOrDefault() ?? "", "", chunk);
            pageBytes.Add(await pageEngine.SaveAsync(ct));
        }

        _logger.Info($"WordEngine: 分页渲染完成: {pageBytes.Count} 页");
        return MergeDocuments(pageBytes);
    }

    private byte[] MergeDocuments(List<byte[]> documents)
    {
        if (documents.Count == 0) return Array.Empty<byte>();
        if (documents.Count == 1) return documents[0];

        using var firstStream = new MemoryStream(documents[0]);
        using var mergedDoc = new XWPFDocument(firstStream);

        for (int i = 1; i < documents.Count; i++)
        {
            using var chunkStream = new MemoryStream(documents[i]);
            using var chunkDoc = new XWPFDocument(chunkStream);

            var pageBreakPara = mergedDoc.CreateParagraph();
            pageBreakPara.CreateRun().AddBreak(NPOI.XWPF.UserModel.BreakType.PAGE);

            foreach (var para in chunkDoc.Paragraphs)
            {
                var newPara = mergedDoc.CreateParagraph();
                newPara.Alignment = para.Alignment;
                foreach (var run in para.Runs)
                {
                    var newRun = newPara.CreateRun();
                    newRun.SetText(run.Text ?? "");
                    newRun.FontSize = run.FontSize;
                    newRun.IsBold = run.IsBold;
                    newRun.FontFamily = run.FontFamily;
                }
            }

            foreach (var table in chunkDoc.Tables)
            {
                var newTable = mergedDoc.CreateTable(table.NumberOfRows,
                    table.Rows.Count > 0 ? table.GetRow(0).GetTableCells().Count : 1);
                for (int r = 0; r < table.NumberOfRows; r++)
                {
                    var srcRow = table.GetRow(r);
                    var dstRow = newTable.GetRow(r);
                    for (int c = 0; c < srcRow.GetTableCells().Count && c < dstRow.GetTableCells().Count; c++)
                    {
                        var srcCell = srcRow.GetTableCells()[c];
                        var dstCell = dstRow.GetTableCells()[c];
                        dstCell.Paragraphs[0].CreateRun().SetText(srcCell.Paragraphs[0].ParagraphText);
                    }
                }
            }
        }

        using var ms = new MemoryStream();
        mergedDoc.Write(ms);
        return ms.ToArray();
    }

    public async Task<byte[]> SaveAsync(CancellationToken ct = default)
    {
        if (_doc == null) throw new InvalidOperationException("文档未加载");

        var path = TemplateEngineHelpers.NewTempFilePath(_storageOptions, ".docx", prefix: "save_");
        try
        {
            await SaveToFileAsync(path, ct);
            return await File.ReadAllBytesAsync(path, ct);
        }
        finally
        {
            TemplateEngineHelpers.TryDeleteFile(path, _logger, "WordEngine");
        }
    }

    public async Task SaveToFileAsync(string outputPath, CancellationToken ct = default)
    {
        if (_doc == null) throw new InvalidOperationException("文档未加载");

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var fullPath = Path.GetFullPath(outputPath);

        await OfficeComWorker.Instance.InvokeAsync<object?>($"保存 Word 文档 {Path.GetFileName(outputPath)}", OfficeAppKind.Word, ctx =>
        {
            // 使用 Office COM 保存
            _doc!.SaveAs2(fullPath);

            // 关闭文档释放文件锁，让后续 PrintFromFileAsync / ExportPdfFromFileAsync 能打开同一文件；
            // Word 应用实例归还池中复用（原实现此处直接 Quit 整个 Word 进程）
            CloseDocumentOnWorker(ctx);
            return null;
        }, ct: ct);

        _logger.Info("WordEngine: 文件保存完成（Office COM）");
    }

    public async Task<byte[]> ExportPdfAsync(CancellationToken ct = default)
    {
        if (_doc == null) throw new InvalidOperationException("文档未加载");

        _logger.Info("WordEngine: 导出 PDF");

        var pdfPath = TemplateEngineHelpers.NewTempFilePath(_storageOptions, ".pdf");
        var fullPdfPath = Path.GetFullPath(pdfPath);

        try
        {
            await OfficeComWorker.Instance.InvokeAsync<object?>("导出 PDF（当前文档）", OfficeAppKind.Word, ctx =>
            {
                try
                {
                    _doc!.ExportAsFixedFormat(fullPdfPath, 17); // 17 = wdExportFormatPDF
                }
                catch (Exception ex) when (ex is not BusinessException)
                {
                    throw new BusinessException(ErrorCodes.DOCUMENT_PDF_CONVERSION_FAILED,
                        $"PDF 导出失败：Office 转换出错（{ex.Message}）", ex);
                }

                // 成功后立即关闭文档，释放文件锁（与原行为一致：成功路径关闭，失败保留待 Dispose 清理）
                CloseDocumentOnWorker(ctx);
                return null;
            }, ct: ct);

            if (!File.Exists(fullPdfPath))
                throw new BusinessException(ErrorCodes.DOCUMENT_PDF_CONVERSION_FAILED,
                    "PDF 导出失败：Office 已执行但未生成输出文件");

            var pdfBytes = await File.ReadAllBytesAsync(fullPdfPath, ct);
            _logger.Info($"WordEngine: PDF 导出成功: {pdfBytes.Length} bytes");
            return pdfBytes;
        }
        finally
        {
            TemplateEngineHelpers.TryDeleteFile(fullPdfPath, _logger, "WordEngine");
        }
    }

    public async Task<string> ExportPdfToFileAsync(string outputDir, CancellationToken ct = default)
    {
        if (_doc == null) throw new InvalidOperationException("文档未加载");

        Directory.CreateDirectory(outputDir);
        var baseName = Path.GetFileNameWithoutExtension(_tempFilePath);
        var pdfPath = Path.Combine(outputDir, $"{baseName}.pdf");

        var counter = 1;
        while (File.Exists(pdfPath))
        {
            pdfPath = Path.Combine(outputDir, $"{baseName}_{counter}.pdf");
            counter++;
        }

        var fullPdfPath = Path.GetFullPath(pdfPath);

        await OfficeComWorker.Instance.InvokeAsync<object?>($"导出 PDF 到 {Path.GetFileName(pdfPath)}", OfficeAppKind.Word, ctx =>
        {
            try
            {
                _doc!.ExportAsFixedFormat(fullPdfPath, 17);
            }
            catch (Exception ex) when (ex is not BusinessException)
            {
                throw new BusinessException(ErrorCodes.DOCUMENT_PDF_CONVERSION_FAILED,
                    $"PDF 导出失败：Office 转换出错（{ex.Message}）", ex);
            }

            // 释放 COM 对象，避免文件锁
            CloseDocumentOnWorker(ctx);
            return null;
        }, ct: ct);

        _logger.Info($"WordEngine: PDF 导出到文件: {pdfPath}");
        return pdfPath;
    }

    public async Task PrintFromFileAsync(string filePath, int copies = 1, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            throw new FileNotFoundException("文件不存在", filePath);

        await OfficeComDocuments.PrintFileAsync(OfficeAppKind.Word, filePath, PrinterName, IsDuplex, copies, _logger, ct);
        _logger.Info($"WordEngine: 打印完成 (Duplex={IsDuplex})");
    }

    public async Task<byte[]> ExportPdfFromFileAsync(string sourceFilePath, string pdfOutputPath = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
            throw new FileNotFoundException("源文件不存在", sourceFilePath);

        pdfOutputPath ??= Path.Combine(
            _storageOptions.GetTempPath("PrintTemp"),
            $"{Path.GetFileNameWithoutExtension(sourceFilePath)}.pdf");

        var outputDir = Path.GetDirectoryName(Path.GetFullPath(pdfOutputPath));
        if (!string.IsNullOrEmpty(outputDir))
            Directory.CreateDirectory(outputDir);

        var pdfBytes = await OfficeComDocuments.ExportToPdfAsync(OfficeAppKind.Word, sourceFilePath, pdfOutputPath, _logger, ct);
        _logger.Info("WordEngine: 从文件导出 PDF 完成");
        return pdfBytes;
    }

    public List<string> GetUnresolvedPlaceholders()
    {
        // Office COM 方式下，未解析的占位符检查需要遍历文档内容
        // 简化实现：返回空列表
        return new List<string>();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_doc != null)
        {
            try
            {
                // 只关闭本引擎的文档；共享的 Word 应用实例由 OfficeComWorker 统一管理生命周期
                OfficeComWorker.Instance.Invoke<object?>("释放 Word 文档", OfficeAppKind.Word, ctx =>
                {
                    CloseDocumentOnWorker(ctx);
                    return null;
                }, timeout: TimeSpan.FromSeconds(30));
            }
            catch (Exception ex)
            {
                _logger.Warn($"WordEngine: 释放文档失败: {ex.Message}");
            }
        }

        GC.SuppressFinalize(this);
    }
}
#endif
