using NewCosmos.Helpers;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Domain.Reporting;
using NewCosmos.Services.Templates;
using NewCosmos.Services.UserManagement;

namespace NewCosmos.Services.Domain.Printing;

public class PrintExecuteService : BaseService, IPrintExecuteService
{
    protected override string ServiceName => "PrintExecuteService";

    /// <summary>
    /// businessType → 打印权限码中心映射。
    /// 新增业务打印类型时必须在此登记并同步 PermissionSeedCatalog 权限定义，
    /// 禁止动态拼串（历史上 PRINT_{type} 拼串曾产生与常量拼写不符的幽灵权限码）。
    /// </summary>
    private static readonly Dictionary<string, string> PrintPermissionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AssetVerification"] = PermissionCodes.PRINT_ASSETVERIFICATION,
        ["FamilyApplication"] = PermissionCodes.PRINT_FAMILYAPPLICATION,
        ["AssetVerificationMonthlyReport"] = PermissionCodes.PRINT_ASSETVERIFICATIONMONTHLYREPORT,
        ["EconomicReview"] = PermissionCodes.PRINT_ECONOMICREVIEW,
        ["ElderlyBenefits"] = PermissionCodes.PRINT_ELDERLYBENEFITS,
        ["LowIncomeProof"] = PermissionCodes.PRINT_LOWINCOMEPROOF,
        ["TempRelief"] = PermissionCodes.PRINT_TEMPRELIEF,
        ["Recovery"] = PermissionCodes.PRINT_RECOVERY,
        // 动态管理记录打印复用"家庭收入档案"打印权限（不新增权限种子）
        ["DynamicManagementRecord"] = PermissionCodes.PRINT_FAMILYAPPLICATION,
    };

    private readonly IPrintService _printService;
    private readonly ITemplateService _templateService;
    private readonly ITemplateEngineFactory _engineFactory;
    private readonly IPrintRecordService _printRecordService;
    private readonly IDatabaseService _db;
    private readonly INewPermissionService _permissionService;

    public PrintExecuteService(
        IPrintService printService,
        ITemplateService templateService,
        ITemplateEngineFactory engineFactory,
        IPrintRecordService printRecordService,
        IDatabaseService db,
        INewPermissionService permissionService,
        ILoggerService logger) : base(logger)
    {
        _printService = printService;
        _templateService = templateService;
        _engineFactory = engineFactory;
        _printRecordService = printRecordService;
        _db = db;
        _permissionService = permissionService;
    }

    public async Task<Result<byte[]>> GeneratePreviewPdfAsync(
        long templateId,
        Dictionary<string, string> fields,
        List<Dictionary<string, string>> tableRows,
        CancellationToken ct = default)
    {
        LogInfo($"执行操作");

        try
        {
            // 获取模板配置以检查 copiesBySupporter
            var template = await _templateService.GetByIdAsync(templateId, ct);
            var config = template != null && !string.IsNullOrEmpty(template.ConfigJson)
                ? TemplateConfig.FromJson(template.ConfigJson) : null;

            // 赡养人模式：为每个赡养人独立生成PDF并合并
            if (config != null && config.CopiesBySupporter && tableRows != null && tableRows.Count > 0)
            {
                LogInfo($"赡养人预览模式: {tableRows.Count} 人");
                var allPdfBytes = new List<byte[]>();

                for (int i = 0; i < tableRows.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var mergedFields = new Dictionary<string, string>(fields, StringComparer.Ordinal);
                    foreach (var kv in tableRows[i])
                        mergedFields[kv.Key] = kv.Value;

                    using var engine = await _engineFactory.CreateFromTemplateIdAsync(templateId, ct);
                    var placeholderFields = MapFieldsToPlaceholders(mergedFields, config.Fields, config.IndexShifts);
                    engine.ReplaceFields(placeholderFields);
                    var pdfBytes = await engine.ExportPdfAsync(ct);
                    allPdfBytes.Add(pdfBytes);

                    LogInfo($"赡养人预览 {i + 1}/{tableRows.Count}");
                }

                var mergedPdf = allPdfBytes.Count == 1 ? allPdfBytes[0] : MergePdfBytes(allPdfBytes);
                return Result.Success(mergedPdf);
            }

            // 原有逻辑
            Result<byte[]> pdfResult;
            if (tableRows != null && tableRows.Count > 0)
            {
                // 索引槽分页：名册类模板用 FAMILY_MEMBER_NAME_1..N 索引槽承载一页容量，
                // 行数超过槽位容量时按每页 N 人分页并合并，预览与打印路径行为一致，避免第 N+1 行起被静默丢弃
                var indexSlotCapacity = GetIndexSlotCapacity(config);
                var indexOverCapacity = indexSlotCapacity > 0 && tableRows.Count > indexSlotCapacity;
                var excelOverLimit = config != null && tableRows.Count > ExcelConstants.MaxRowsPerFile;
                var chunkPageSize = indexOverCapacity ? indexSlotCapacity : (excelOverLimit ? ExcelConstants.MaxRowsPerFile : 0);
                var chunkedRows = chunkPageSize > 0 ? ChunkRows(tableRows, chunkPageSize) : null;

                if (chunkedRows is { Count: > 1 })
                {
                    var chunkPdfBytes = new List<byte[]>();
                    for (var chunkIndex = 0; chunkIndex < chunkedRows.Count; chunkIndex++)
                    {
                        ct.ThrowIfCancellationRequested();
                        var chunkPdf = await _printService.GeneratePdfWithTableAsync(
                            templateId, fields, chunkedRows[chunkIndex], ct);
                        if (chunkPdf.IsFailure)
                        {
                            LogError($"分块预览生成失败: 块{chunkIndex + 1}/{chunkedRows.Count} - {chunkPdf.Message}");
                            return Result.Failure<byte[]>(ErrorCodes.DOCUMENT_GENERATION_FAILED, $"分块预览生成失败: {chunkPdf.Message}");
                        }
                        chunkPdfBytes.Add(chunkPdf.Value);
                        LogInfo($"分块预览 {chunkIndex + 1}/{chunkedRows.Count}");
                    }
                    pdfResult = Result.Success(chunkPdfBytes.Count == 1 ? chunkPdfBytes[0] : MergePdfBytes(chunkPdfBytes));
                }
                else
                {
                    pdfResult = await _printService.GeneratePdfWithTableAsync(templateId, fields, tableRows, ct);
                }
            }
            else
            {
                pdfResult = await _printService.GeneratePdfAsync(templateId, fields, ct);
            }

            if (pdfResult.IsFailure)
            {
                LogError($"操作失败");
            }

            return pdfResult;
        }
        catch (Exception ex)
        {
            LogError($"操作失败");
            return Result.Failure<byte[]>(ErrorCodes.DOCUMENT_GENERATION_FAILED, $"文档生成失败: {ex.Message}");
        }
    }

    public async Task<Result<PrintRecord>> ExecutePrintAsync(
        long templateId,
        string templateName,
        string businessType,
        long? businessId,
        string batchNo,
        Dictionary<string, string> fields,
        List<Dictionary<string, string>> tableRows,
        string applicantName,
        string applicantIdCard,
        string printerName = null,
        int copies = 1,
        bool isDuplex = false,
        CancellationToken ct = default)
    {
        LogInfo($"执行打印: Template={templateName}");

        var currentUserId = App.CurrentUserId;
        if (!currentUserId.HasValue)
        {
            LogError($"失败");
            return Result.Failure<PrintRecord>(ErrorCodes.AUTHENTICATION_FAILED, "用户未登录");
        }

        if (!PrintPermissionMap.TryGetValue(businessType, out var permissionCode))
        {
            LogError($"未登记的打印业务类型: {businessType}");
            return Result.Failure<PrintRecord>(ErrorCodes.PERMISSION_DENIED, $"未登记的打印业务类型: {businessType}");
        }
        var hasPermission = await _permissionService.HasPermissionAsync(currentUserId.Value, permissionCode, ct);
        if (!hasPermission)
        {
            LogError($"用户无打印权限, UserId={currentUserId}");
            return Result.Failure<PrintRecord>(ErrorCodes.PERMISSION_DENIED, $"无权打印该文档（缺少权限: {permissionCode}）");
        }

        try
        {
            var template = await _templateService.GetByIdAsync(templateId, ct);
            if (template == null)
            {
                return Result.Failure<PrintRecord>(ErrorCodes.NOT_FOUND, $"模板未找到: TemplateId={templateId}");
            }

            // 直接从已取回的模板解析 config（原实现 GetTemplateConfigAsync 会再次 GetByIdAsync，纯冗余往返）
            var config = string.IsNullOrEmpty(template.ConfigJson) ? null : TemplateConfig.FromJson(template.ConfigJson);

            var category = OutputPathHelper.GetCategoryDisplayName(businessType);
            var ext = template.FileType.ToLowerInvariant() switch
            {
                "xlsx" or "excel" => ".xlsx",
                "docx" or "word" => ".docx",
                _ => ".docx"
            };
            var sourceFilePath = OutputPathHelper.GetFilePath(category, applicantName, applicantIdCard, templateName, ext);
            var pdfFilePath = Path.ChangeExtension(sourceFilePath, ".pdf");

            OutputPathHelper.EnsureDirectoryExists(category, applicantName, applicantIdCard);

            // 按赡养人每人一份
            if (config != null && config.CopiesBySupporter && tableRows != null && tableRows.Count > 0)
            {
                LogInfo($"赡养人模式: {tableRows.Count} 人");
                var allPdfBytes = new List<byte[]>();
                var allSourceBytes = new List<byte[]>();

                for (int i = 0; i < tableRows.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var supporterRow = tableRows[i];
                    var suffix = $"_{i + 1}";

                    // 合并基础字段 + 当前赡养人字段
                    var mergedFields = new Dictionary<string, string>(fields, StringComparer.Ordinal);
                    foreach (var kv in supporterRow)
                    {
                        mergedFields[kv.Key] = kv.Value;
                    }

                    var supporterName = supporterRow.TryGetValue("SUPPORTER_NAME", out var sn) ? sn : $"赡养人{i + 1}";
                    var supporterFilePath = OutputPathHelper.GetFilePath(category, applicantName, applicantIdCard, $"{templateName}_{supporterName}", ext);
                    var supporterPdfPath = Path.ChangeExtension(supporterFilePath, ".pdf");

                    using var spEngine = await _engineFactory.CreateFromTemplateIdAsync(templateId, ct);
                    spEngine.PrinterName = printerName;
                    spEngine.IsDuplex = isDuplex;

                    var spPlaceholderFields = MapFieldsToPlaceholders(mergedFields, config.Fields, config.IndexShifts);
                    spEngine.ReplaceFields(spPlaceholderFields);

                    await spEngine.SaveToFileAsync(supporterFilePath, ct);

                    // 打印本份文档（原实现漏掉了这一步：只生成文件不打印，却仍返回 Success）
                    await spEngine.PrintFromFileAsync(supporterFilePath, copies, ct);

                    var supporterPdfBytes = await spEngine.ExportPdfFromFileAsync(supporterFilePath, supporterPdfPath, ct);
                    if (supporterPdfBytes != null) allPdfBytes.Add(supporterPdfBytes);

                    var supporterSourceBytes = await File.ReadAllBytesAsync(supporterFilePath, ct);
                    allSourceBytes.Add(supporterSourceBytes);

                    LogInfo($"赡养人 {i + 1}/{tableRows.Count}: {supporterName}");
                }

                // 合并所有PDF
                var mergedPdf = allPdfBytes.Count == 1 ? allPdfBytes[0] : MergePdfBytes(allPdfBytes);
                var mergedSource = allSourceBytes.Count == 1 ? allSourceBytes[0] : allSourceBytes[0]; // 用第一个作为源

                var spRecord = new PrintRecord
                {
                    BatchNo = batchNo,
                    BusinessType = businessType,
                    BusinessId = businessId,
                    TemplateId = templateId,
                    TemplateName = templateName,
                    PdfData = mergedPdf,
                    PdfSize = mergedPdf.Length,
                    SourceData = mergedSource,
                    SourceType = template.FileType,
                    SourceSize = mergedSource.Length,
                    FilePath = sourceFilePath,
                    PdfPath = pdfFilePath,
                    PrinterName = printerName,
                    Copies = copies,
                    OperatorId = currentUserId,
                    Status = "Completed"
                };

                var spSaveResult = await _printRecordService.SaveAsync(spRecord, ct);
                if (spSaveResult.IsFailure)
                {
                    LogError($"操作失败");
                }

                LogInfo($"赡养人模式完成: {tableRows.Count} 份");
                return Result.Success(spRecord);
            }

            // 正常流程

            // 超限拆分：Excel 模板数据行超过单文件上限时，自动拆为 文件名_1/文件名_2 多个文件
            // 索引槽分页：名册类模板用 FAMILY_MEMBER_NAME_1..N 索引槽承载一页容量（如核查月报名册 N=28），
            // 行数超过槽位容量时按每页 N 人分页——一页满了自动进入第二页，避免第 N+1 行起被静默丢弃
            var excelOverLimit = ext == ".xlsx" && tableRows is { Count: > ExcelConstants.MaxRowsPerFile };
            var indexSlotCapacity = GetIndexSlotCapacity(config);
            var indexOverCapacity = indexSlotCapacity > 0 && tableRows is { Count: > 0 } && tableRows.Count > indexSlotCapacity;
            var chunkPageSize = indexOverCapacity ? indexSlotCapacity : (excelOverLimit ? ExcelConstants.MaxRowsPerFile : 0);
            var chunkedRows = chunkPageSize > 0
                ? ChunkRows(tableRows!, chunkPageSize)
                : null;
            var chunkCount = chunkedRows?.Count ?? 1;
            var normalChunkPdfBytes = new List<byte[]>();
            var firstSourcePath = sourceFilePath;
            var finalPdfPath = pdfFilePath;

            for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                ct.ThrowIfCancellationRequested();

                var chunkRows = chunkedRows?[chunkIndex];
                var chunkSuffix = chunkedRows == null ? string.Empty : $"_{chunkIndex + 1}";
                var chunkSourcePath = chunkedRows == null ? sourceFilePath
                    : OutputPathHelper.GetFilePath(category, applicantName, applicantIdCard, $"{templateName}{chunkSuffix}", ext);
                var chunkPdfPath = chunkedRows == null ? pdfFilePath : Path.ChangeExtension(chunkSourcePath, ".pdf");
                if (chunkIndex == 0)
                {
                    firstSourcePath = chunkSourcePath;
                    finalPdfPath = chunkPdfPath;
                }

                using var engine = await _engineFactory.CreateFromTemplateIdAsync(templateId, ct);
                engine.PrinterName = printerName;
                engine.IsDuplex = isDuplex;

                // config 可为 null（模板未配置 config_json）——MapFieldsToPlaceholders 对 null 映射会原样透传字段
                if (chunkRows is { Count: > 0 })
                {
                    var flattenedFields = FlattenTableRowsToFields(fields, chunkRows, config!);
                    var placeholderFields = MapFieldsToPlaceholders(flattenedFields, config?.Fields!, config?.IndexShifts);
                    engine.ReplaceFields(placeholderFields);

                    if (config != null && config.Tables.Count > 0)
                    {
                        var tableConfig = config.Tables[0];
                        var placeholderRows = MapRowsToPlaceholders(chunkRows, tableConfig.Columns);
                        engine.ReplaceTableByPlaceholder(tableConfig.StartMarker, tableConfig.EndMarker, placeholderRows);
                    }
                }
                else
                {
                    // 无超限拆分时也要展平 tableRows（目录等索引槽模板靠行数据填槽，
                    // 否则打印时丢失行数据、槽位全部落 defaultValue 变空白，而预览路径始终展平）
                    var flattenedFields = FlattenTableRowsToFields(fields, tableRows!, config!);
                    var placeholderFields = MapFieldsToPlaceholders(flattenedFields, config?.Fields!, config?.IndexShifts);
                    engine.ReplaceFields(placeholderFields);
                }

                LogInfo($"执行操作");
                await engine.SaveToFileAsync(chunkSourcePath, ct);
                LogInfo($"执行操作");

                LogInfo($"开始打印源文档: Template={templateName}");
                await engine.PrintFromFileAsync(chunkSourcePath, copies, ct);
                LogInfo($"打印命令已发出");

                LogInfo($"执行操作");
                var pdfBytes = await engine.ExportPdfFromFileAsync(chunkSourcePath, chunkPdfPath, ct);
                if (pdfBytes != null) normalChunkPdfBytes.Add(pdfBytes);
                LogInfo($"执行操作");
            }

            // 多文件拆分：合并所有 PDF 写入统一 PDF 文件
            var mergedPdfBytes = normalChunkPdfBytes.Count == 1 ? normalChunkPdfBytes[0] : MergePdfBytes(normalChunkPdfBytes);
            if (chunkedRows != null && chunkedRows.Count > 1)
            {
                try
                {
                    await File.WriteAllBytesAsync(finalPdfPath, mergedPdfBytes, ct);
                }
                catch (Exception ex)
                {
                    LogError($"合并PDF写入失败: {ex.Message}");
                }
                LogInfo($"超限拆分完成: 共 {chunkedRows.Count} 个文件");
            }

            byte[] sourceBytes = await File.ReadAllBytesAsync(firstSourcePath, ct);

            var record = new PrintRecord
            {
                BatchNo = batchNo,
                BusinessType = businessType,
                BusinessId = businessId,
                TemplateId = templateId,
                TemplateName = templateName,
                PdfData = mergedPdfBytes,
                PdfSize = mergedPdfBytes.Length,
                SourceData = sourceBytes,
                SourceType = template.FileType,
                SourceSize = sourceBytes.Length,
                FilePath = firstSourcePath,
                PdfPath = finalPdfPath,
                PrinterName = printerName,
                Copies = copies,
                OperatorId = currentUserId,
                Status = "Completed"
            };

            var saveResult = await _printRecordService.SaveAsync(record, ct);
            if (saveResult.IsFailure)
            {
                LogError($"操作失败");
            }

            LogInfo($"打印完成: Template={templateName}");
            return Result.Success(record);
        }
        catch (OperationCanceledException)
        {
            LogWarn($"警告");
            return Result.Failure<PrintRecord>(ErrorCodes.PRINT_FAILED, "打印任务已取消");
        }
        catch (Exception ex)
        {
            LogError($"操作失败");

            var failedRecord = new PrintRecord
            {
                BatchNo = batchNo,
                BusinessType = businessType,
                BusinessId = businessId,
                TemplateId = templateId,
                TemplateName = templateName,
                Status = "Failed",
                Remark = ex.Message,
                Copies = copies,
                OperatorId = currentUserId
            };
            await _printRecordService.SaveAsync(failedRecord, ct);

            return Result.Failure<PrintRecord>(ErrorCodes.PRINT_FAILED, $"打印执行失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 合并多份 PDF（PDFsharp 真合并——PDF 有交叉引用表/对象编号，字节拼接会产出无效文件）。
    /// 合并失败时降级返回第一份并记录错误，绝不返回损坏字节。
    /// </summary>
    private byte[] MergePdfBytes(List<byte[]> pdfs)
    {
        if (pdfs.Count == 0) return Array.Empty<byte>();
        if (pdfs.Count == 1) return pdfs[0];

        try
        {
            using var output = new PdfSharp.Pdf.PdfDocument();
            foreach (var pdf in pdfs)
            {
                using var inputStream = new MemoryStream(pdf);
                using var input = PdfSharp.Pdf.IO.PdfReader.Open(inputStream, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
                for (var i = 0; i < input.PageCount; i++)
                {
                    output.AddPage(input.Pages[i]);
                }
            }

            using var ms = new MemoryStream();
            output.Save(ms);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"PDF 合并失败，降级保存第一份（共 {pdfs.Count} 份）");
            return pdfs[0];
        }
    }

    // 注：原 GetTemplateConfigAsync 已删除——它重复拉取模板行；config 现直接从 ExecutePrintAsync 已取回的模板解析

    /// <summary>
    /// 将数据行按指定大小分块（超限导出拆分用）
    /// </summary>
    private static List<List<Dictionary<string, string>>> ChunkRows(
        List<Dictionary<string, string>> rows, int maxRowsPerChunk)
    {
        var chunks = new List<List<Dictionary<string, string>>>();
        for (var i = 0; i < rows.Count; i += maxRowsPerChunk)
        {
            var count = Math.Min(maxRowsPerChunk, rows.Count - i);
            chunks.Add(rows.GetRange(i, count));
        }
        return chunks;
    }

    /// <summary>
    /// 检测模板 config 中 FAMILY_MEMBER_NAME_ 索引槽的最大槽位（如 _1.._28 → 28）。
    /// 名册类模板用索引槽承载一页容量；0 表示无该索引槽模板（不触发分页拆分）。
    /// </summary>
    private static int GetIndexSlotCapacity(TemplateConfig? config)
    {
        if (config == null || config.Fields.Count == 0) return 0;

        const string prefix = "FAMILY_MEMBER_NAME_";
        var maxSlot = 0;
        foreach (var field in config.Fields)
        {
            if (!field.FieldKey.StartsWith(prefix, StringComparison.Ordinal)) continue;
            if (!int.TryParse(field.FieldKey[prefix.Length..], out var num)) continue;
            if (num > maxSlot) maxSlot = num;
        }
        return maxSlot;
    }

    private static Dictionary<string, string> FlattenTableRowsToFields(
        Dictionary<string, string> fields,
        List<Dictionary<string, string>> tableRows,
        TemplateConfig config)
    {
        if (tableRows == null || tableRows.Count == 0 || config == null)
            return new Dictionary<string, string>(fields, StringComparer.Ordinal);

        // 始终检测 config.Fields 中是否有索引字段（不依赖 config.Tables）
        var hasIndexedFields = config.Fields.Any(f =>
        {
            var idx = f.FieldKey.LastIndexOf('_');
            return idx > 0 && idx < f.FieldKey.Length - 1
                && int.TryParse(f.FieldKey[(idx + 1)..], out _);
        });
        if (!hasIndexedFields)
            return new Dictionary<string, string>(fields, StringComparer.Ordinal);

        var result = new Dictionary<string, string>(fields, StringComparer.Ordinal);

        var maxIndexByBase = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var field in config.Fields)
        {
            var idx = field.FieldKey.LastIndexOf('_');
            if (idx <= 0 || idx >= field.FieldKey.Length - 1) continue;
            if (!int.TryParse(field.FieldKey[(idx + 1)..], out var num)) continue;
            var baseKey = field.FieldKey[..idx];
            if (!maxIndexByBase.TryGetValue(baseKey, out var existing) || num > existing)
                maxIndexByBase[baseKey] = num;
        }

        if (maxIndexByBase.Count == 0)
            return result;

        for (var i = 0; i < tableRows.Count; i++)
        {
            var suffix = i + 1;
            foreach (var kv in tableRows[i])
            {
                if (maxIndexByBase.ContainsKey(kv.Key))
                    result[$"{kv.Key}_{suffix}"] = kv.Value;
            }
        }

        foreach (var (baseKey, maxIdx) in maxIndexByBase)
        {
            // 按各索引组实际已填充值决定置空范围（从 _1 起，而非 tableRows.Count+1）：
            // 1) 由 ViewModel 直接填充、与 tableRows 无关的索引（如 SUPPORTER_*/SHARED_*）真实值被保留；
            // 2) 无数据的槽位全部清空（含 _1），避免残留未替换占位符。
            for (var i = 1; i <= maxIdx; i++)
            {
                var key = $"{baseKey}_{i}";
                if (!result.ContainsKey(key))
                    result[key] = string.Empty;
            }
        }

        return result;
    }

    private static Dictionary<string, string> MapFieldsToPlaceholders(
        Dictionary<string, string> fieldValues,
        List<TemplateFieldMapping>? mappings,
        IReadOnlyList<IndexShiftRule>? indexShifts = null)
    {
        if (mappings == null || mappings.Count == 0 || fieldValues == null || fieldValues.Count == 0)
            return fieldValues != null
                ? new Dictionary<string, string>(fieldValues, StringComparer.Ordinal)
                : new Dictionary<string, string>(StringComparer.Ordinal);

        fieldValues = TemplateFieldResolver.Project(fieldValues, indexShifts);
        var result = new Dictionary<string, string>();

        foreach (var mapping in mappings)
        {
            if (!string.IsNullOrEmpty(mapping.FieldKey) && fieldValues.TryGetValue(mapping.FieldKey, out var rawValue))
            {
                var formattedValue = ApplyFormatOrDefault(rawValue, mapping);
                result[mapping.Placeholder] = formattedValue;
            }
            else if (!string.IsNullOrEmpty(mapping.DefaultValue))
            {
                result[mapping.Placeholder] = mapping.DefaultValue;
            }
            // 无数据且无 defaultValue → 不添加映射，模板保持原样
        }

        return result;
    }

    private static string ApplyFormatOrDefault(string value, TemplateFieldMapping mapping)
    {
        if (!string.IsNullOrEmpty(mapping.Format))
        {
            var formatted = TemplateFieldMapping.ApplyFormat(value, mapping.Format);
            return string.IsNullOrEmpty(formatted) ? "" : formatted;
        }

        return value ?? "";
    }

    private static List<Dictionary<string, string>> MapRowsToPlaceholders(
        List<Dictionary<string, string>> rowFieldValues,
        List<TemplateFieldMapping> columnMappings)
    {
        var result = new List<Dictionary<string, string>>();

        foreach (var row in rowFieldValues)
        {
            var placeholderRow = new Dictionary<string, string>();
            foreach (var colMapping in columnMappings)
            {
                if (!string.IsNullOrEmpty(colMapping.FieldKey) && row.TryGetValue(colMapping.FieldKey, out var cellValue))
                {
                    placeholderRow[colMapping.Placeholder] = cellValue;
                }
            }
            result.Add(placeholderRow);
        }

        return result;
    }
}
