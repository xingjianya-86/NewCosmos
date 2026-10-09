using NewCosmos.Constants;
using NewCosmos.Models.Enums;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.Domain.NearRelative;
using NewCosmos.Services.Templates;
using NewCosmos.Services.Utilities;

namespace NewCosmos.Services.Domain.Reporting;

public partial class PrintService
{
    /// <summary>
    /// 合并多份 PDF（PDFsharp 真合并）
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
            Logger.LogError(ex, $"PDF 合并失败，降级返回第一份（共 {pdfs.Count} 份）");
            return pdfs[0];
        }
    }

    /// <summary>
    /// 组织机构名（填报单位）
    /// </summary>
    private class OrgNameRow
    {
        public string? Name { get; set; }
    }

    /// <summary>
    /// 组织机构批复机构信息（分类施保加发批复单位解析）
    /// </summary>
    private class ReportOrgRow
    {
        public string? Name { get; set; }
        public string? TownName { get; set; }
        public string? ParentName { get; set; }
    }

    /// <summary>
    /// 解析"普惠高龄/月报"分类下的新增/停止明细表模板 ID
    /// </summary>
    private async Task<Result<long>> ResolveElderlyMonthlyTemplateIdAsync(bool isStop, CancellationToken ct)
    {
        var name = isStop ? "普惠高龄停止明细表" : "普惠高龄新增明细表";
        var templates = await _templateService.GetByCategoriesAsync(new[] { "普惠高龄/月报" }, ct);
        var template = templates.FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (template == null)
            return Result.Failure<long>(ErrorCodes.NOT_FOUND, $"普惠高龄明细表模板不存在: {name}");
        return Result.Success(template.Id);
    }

    /// <summary>
    /// 组装普惠高龄新增/停止明细表字段数据（自然月查询；每页 13 行，超行拆多页）
    /// categoryCode: 享受类别代码（CAT1-CAT4），空 = 全部分类
    /// </summary>
    private async Task<Result<List<Dictionary<string, string>>>> BuildElderlyMonthlyPagesAsync(int year, int month, bool isStop, string categoryCode, CancellationToken ct)
    {
        // 仅"新增"方向排除导入库补录人员（其 apply_date 为补录当天而非真实受理时间）；
        // "停止"方向按 actual_stop_date（真实停发事件）查询，补录人员本月实际停发的必须照常显示
        var recordsResult = await _elderlyApplicationService.GetByMonthAsync(year, month, isStop, categoryCode, excludeImported: !isStop, ct);
        if (recordsResult.IsFailure)
            return Result.Failure<List<Dictionary<string, string>>>(recordsResult.ErrorCode!, recordsResult.Message!);
        var records = recordsResult.Value ?? new List<Models.Entities.ElderlyApplication>();
        if (records.Count == 0)
            return Result.Failure<List<Dictionary<string, string>>>(ErrorCodes.NOT_FOUND, "该分类无数据");

        // 停止明细表：实际发放列读取历史档案发放金额（缺失回退计发金额）；新增明细表不查
        Dictionary<string, decimal>? historyAmounts = null;
        if (isStop)
        {
            var historyResult = await _elderlyApplicationService.GetHistorySubsidyAmountsAsync(
                records.Select(r => r.IdCard), ct);
            if (historyResult.IsSuccess)
                historyAmounts = historyResult.Value;
            else
                LogWarn($"历史档案发放金额读取失败（停止明细表实际发放列回退计发金额）: {historyResult.Message}");
        }

        // 填报单位 = 当前机构名（无机构回退"全部"）
        var fillUnit = await ResolveReportUnitAsync("", ct);

        // 标题月份 = 周期结束日的次月（与月报口径一致；跨年处理）
        var titleYear = month == 12 ? year + 1 : year;
        var titleMonth = month == 12 ? 1 : month + 1;
        // 标题用中文类别名（CAT 代码不出现在文档标题），如 CAT3 → "90-99周岁老人"
        var title = $"{titleYear}年{titleMonth}月普惠高龄{ElderlyBenefitConstants.GetCategoryName(categoryCode)}{(isStop ? "停止" : "新增")}人员明细表";

        const int rowsPerPage = 13;
        var pages = new List<Dictionary<string, string>>();
        for (var i = 0; i < records.Count; i += rowsPerPage)
        {
            var pageRecords = records.Skip(i).Take(rowsPerPage).ToList();
            var fields = new Dictionary<string, string>
            {
                ["REPORT_TITLE"] = title,
                ["REPORT_UNIT"] = fillUnit,
                ["REPORT_DATE"] = $"{year}年{month}月"
            };
            foreach (var kv in ElderlyPrintDataBuilder.BuildDetailFields(pageRecords, isStop, rowsPerPage, historyAmounts))
                fields[kv.Key] = kv.Value;
            pages.Add(fields);
        }
        return Result.Success(pages);
    }

    /// <summary>
    /// 渲染普惠高龄新增/停止明细表为 PDF（自然月；超 13 人分页合并）
    /// categoryCode: 享受类别代码（CAT1-CAT4），空 = 全部分类
    /// </summary>
    public async Task<Result<byte[]>> RenderElderlyMonthlyFormAsync(int year, int month, bool isStop, string categoryCode = "", CancellationToken ct = default)
    {
        LogInfo($"渲染普惠高龄{(isStop ? "停止" : "新增")}明细表: {year}年{month}月 分类={categoryCode}");

        var templateResult = await ResolveElderlyMonthlyTemplateIdAsync(isStop, ct);
        if (templateResult.IsFailure)
            return Result.Failure<byte[]>(templateResult.ErrorCode!, templateResult.Message!);
        var templateId = templateResult.Value;

        var pagesResult = await BuildElderlyMonthlyPagesAsync(year, month, isStop, categoryCode, ct);
        if (pagesResult.IsFailure)
            return Result.Failure<byte[]>(pagesResult.ErrorCode!, pagesResult.Message!);

        var pdfs = new List<byte[]>();
        foreach (var fields in pagesResult.Value)
        {
            var pdfResult = await GeneratePdfAsync(templateId, fields, ct);
            if (pdfResult.IsFailure)
                return pdfResult;
            pdfs.Add(pdfResult.Value);
        }
        var merged = pdfs.Count == 1 ? pdfs[0] : MergePdfBytes(pdfs);
        LogInfo($"普惠高龄明细表渲染完成: {pagesResult.Value.Count} 页");
        return Result.Success(merged);
    }

    /// <summary>
    /// 批量渲染普惠高龄明细表并合并为一份 PDF（勾选列表预览）；无数据表单自动跳过
    /// </summary>
    public async Task<Result<byte[]>> RenderElderlyMonthlyFormsMergedAsync(int year, int month, IEnumerable<string> formKeys, CancellationToken ct = default)
    {
        LogInfo($"渲染普惠高龄多表单合并预览: 表单数={formKeys.Count()}");

        var pdfs = new List<byte[]>();
        var skipped = new List<string>();
        foreach (var key in formKeys.Distinct())
        {
            var result = ElderlyBenefitConstants.IsAge90Form(key)
                ? await RenderElderlyAge90FormAsync(year, month, ct)
                : await RenderElderlyMonthlyFormAsync(
                    year, month, ElderlyBenefitConstants.IsStopForm(key),
                    ElderlyBenefitConstants.GetCategoryCodeFromFormKey(key), ct);
            if (result.IsFailure)
            {
                if (result.ErrorCode == ErrorCodes.NOT_FOUND && result.Message?.Contains("无数据") == true)
                {
                    skipped.Add(key);
                    continue;
                }
                return Result.Failure<byte[]>(result.ErrorCode!, result.Message!);
            }
            pdfs.Add(result.Value);
        }

        if (pdfs.Count == 0)
        {
            if (skipped.Count > 0)
            {
                LogInfo($"勾选表单均无数据，已跳过: {string.Join(",", skipped)}");
                return Result.Success(Array.Empty<byte>());
            }
            return Result.Failure<byte[]>(ErrorCodes.NOT_FOUND, "未选择任何表单");
        }
        if (skipped.Count > 0)
            LogInfo($"无数据已跳过: {string.Join(",", skipped)}");
        return Result.Success(pdfs.Count == 1 ? pdfs[0] : MergePdfBytes(pdfs));
    }

    /// <summary>
    /// 渲染普惠高龄明细表为源文件字节（xlsx，供导出；超 13 人拆多页文件）
    /// categoryCode: 享受类别代码（CAT1-CAT4），空 = 全部分类
    /// </summary>
    public async Task<Result<List<MonthlySourceFile>>> RenderElderlyMonthlyFormToSourceAsync(int year, int month, bool isStop, string categoryCode = "", CancellationToken ct = default)
    {
        LogInfo($"渲染普惠高龄{(isStop ? "停止" : "新增")}明细表源文件: {year}年{month}月 分类={categoryCode}");

        var templateResult = await ResolveElderlyMonthlyTemplateIdAsync(isStop, ct);
        if (templateResult.IsFailure)
            return Result.Failure<List<MonthlySourceFile>>(templateResult.ErrorCode!, templateResult.Message!);
        var templateId = templateResult.Value;

        var pagesResult = await BuildElderlyMonthlyPagesAsync(year, month, isStop, categoryCode, ct);
        if (pagesResult.IsFailure)
            return Result.Failure<List<MonthlySourceFile>>(pagesResult.ErrorCode!, pagesResult.Message!);

        var files = new List<MonthlySourceFile>();
        for (var i = 0; i < pagesResult.Value.Count; i++)
        {
            using var engine = await _engineFactory.CreateFromTemplateIdAsync(templateId, ct);
            var fillResult = await FillTemplateEngineAsync(engine, templateId, pagesResult.Value[i], new List<Dictionary<string, string>>(), ct);
            if (fillResult.IsFailure)
                return Result.Failure<List<MonthlySourceFile>>(fillResult.ErrorCode!, fillResult.Message!);
            var bytes = await engine.SaveAsync(ct);
            var pageSuffix = pagesResult.Value.Count > 1 ? $"_{i + 1}" : "";
            files.Add(new MonthlySourceFile
            {
                FileName = $"{year:D4}{month:D2}_{categoryCode}{(isStop ? "停止" : "新增")}明细表{pageSuffix}",
                Extension = ".xlsx",
                Bytes = bytes
            });
        }
        LogInfo($"普惠高龄明细表源文件渲染完成: {files.Count} 个文件");
        return Result.Success(files);
    }

    /// <summary>
    /// 打印普惠高龄新增/停止明细表（渲染源文件后走 Office COM 打印；超 13 人按页打印）
    /// categoryCode: 享受类别代码（CAT1-CAT4），空 = 全部分类
    /// </summary>
    public async Task<Result<bool>> PrintElderlyMonthlyFormAsync(int year, int month, bool isStop, string categoryCode, string printerName, int copies = 1, CancellationToken ct = default)
    {
        LogInfo($"打印普惠高龄{(isStop ? "停止" : "新增")}明细表: {year}年{month}月 分类={categoryCode} 打印机={printerName}");

        var templateResult = await ResolveElderlyMonthlyTemplateIdAsync(isStop, ct);
        if (templateResult.IsFailure)
            return Result.Failure<bool>(templateResult.ErrorCode!, templateResult.Message!);
        var templateId = templateResult.Value;

        var pagesResult = await BuildElderlyMonthlyPagesAsync(year, month, isStop, categoryCode, ct);
        if (pagesResult.IsFailure)
            return Result.Failure<bool>(pagesResult.ErrorCode!, pagesResult.Message!);

        foreach (var fields in pagesResult.Value)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"nc_elderly_{Guid.NewGuid():N}.xlsx");
            try
            {
                using var engine = await _engineFactory.CreateFromTemplateIdAsync(templateId, ct);
                engine.PrinterName = printerName;
                var fillResult = await FillTemplateEngineAsync(engine, templateId, fields, new List<Dictionary<string, string>>(), ct);
                if (fillResult.IsFailure)
                    return Result.Failure<bool>(fillResult.ErrorCode!, fillResult.Message!);
                await engine.SaveToFileAsync(tempPath, ct);
                await engine.PrintFromFileAsync(tempPath, copies, ct);
            }
            catch (Exception ex)
            {
                LogError($"普惠高龄明细表打印失败: {ex.Message}");
                return Result.Failure<bool>(ErrorCodes.DOCUMENT_GENERATION_FAILED, $"普惠高龄明细表打印失败: {ex.Message}");
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }
        LogInfo($"普惠高龄明细表打印完成: {pagesResult.Value.Count} 页");
        return Result.Success(true);
    }

    /// <summary>解析《普惠高龄津贴调整备案表》模板（分类：普惠高龄/变更）</summary>
    private async Task<Result<long>> ResolveAge90TemplateIdAsync(CancellationToken ct)
    {
        var templateResult = await _templateService.GetByNameAsync("普惠高龄津贴调整备案表", ct);
        if (templateResult.IsFailure)
            return Result.Failure<long>(templateResult.ErrorCode!, templateResult.Message!);
        var template = templateResult.Value;
        if (template == null)
            return Result.Failure<long>(ErrorCodes.NOT_FOUND, "模板不存在：普惠高龄津贴调整备案表");
        return Result.Success(template.Id);
    }

    /// <summary>
    /// 组装「满90周岁调整备案表」逐人页字段；首次渲染时自动把名单写入待复核队列（失败不阻断打印）。
    /// </summary>
    private async Task<Result<List<(Dictionary<string, string> Fields, string Name)>>> BuildAge90FormDataAsync(int year, int month, CancellationToken ct)
    {
        var rowsResult = await _elderlyApplicationService.GetAge90AdjustRowsAsync(year, month, ct);
        if (rowsResult.IsFailure)
            return Result.Failure<List<(Dictionary<string, string> Fields, string Name)>>(rowsResult.ErrorCode!, rowsResult.Message!);
        var rows = rowsResult.Value ?? new List<Models.Entities.ElderlyAge90Row>();
        if (rows.Count == 0)
            return Result.Failure<List<(Dictionary<string, string> Fields, string Name)>>(
                ErrorCodes.NOT_FOUND, "该月无满90周岁人员（无数据）");

        var enqueue = await _elderlyApplicationService.EnqueueAge90ReviewsAsync(year, month, ct);
        if (enqueue.IsFailure)
            LogWarn($"满90周岁自动入队失败（不阻断打印）: {enqueue.Message}");

        var monthText = $"{year}年{month}月";
        var acceptDate = DateTime.Today.ToString("yyyy年M月d日");
        var pages = rows.Select(r => (
            ElderlyPrintDataBuilder.BuildAge90AdjustFields(r, acceptDate, monthText),
            string.IsNullOrWhiteSpace(r.Name) ? r.IdCard : r.Name)).ToList();
        return Result.Success(pages);
    }

    /// <summary>渲染《普惠高龄津贴调整备案表》为 PDF（满90周岁，逐人一页合并）</summary>
    public async Task<Result<byte[]>> RenderElderlyAge90FormAsync(int year, int month, CancellationToken ct = default)
    {
        LogInfo($"渲染满90周岁调整备案表: {year}年{month}月");

        var templateResult = await ResolveAge90TemplateIdAsync(ct);
        if (templateResult.IsFailure)
            return Result.Failure<byte[]>(templateResult.ErrorCode!, templateResult.Message!);

        var dataResult = await BuildAge90FormDataAsync(year, month, ct);
        if (dataResult.IsFailure)
            return Result.Failure<byte[]>(dataResult.ErrorCode!, dataResult.Message!);

        var pdfs = new List<byte[]>();
        foreach (var (fields, _) in dataResult.Value)
        {
            var pdfResult = await GeneratePdfAsync(templateResult.Value, fields, ct);
            if (pdfResult.IsFailure)
                return pdfResult;
            pdfs.Add(pdfResult.Value);
        }
        var merged = pdfs.Count == 1 ? pdfs[0] : MergePdfBytes(pdfs);
        LogInfo($"满90周岁调整备案表渲染完成: {pdfs.Count} 页");
        return Result.Success(merged);
    }

    /// <summary>渲染《普惠高龄津贴调整备案表》源文件（满90周岁，逐人一个 xlsx）</summary>
    public async Task<Result<List<MonthlySourceFile>>> RenderElderlyAge90FormToSourceAsync(int year, int month, CancellationToken ct = default)
    {
        LogInfo($"渲染满90周岁调整备案表源文件: {year}年{month}月");

        var templateResult = await ResolveAge90TemplateIdAsync(ct);
        if (templateResult.IsFailure)
            return Result.Failure<List<MonthlySourceFile>>(templateResult.ErrorCode!, templateResult.Message!);

        var dataResult = await BuildAge90FormDataAsync(year, month, ct);
        if (dataResult.IsFailure)
            return Result.Failure<List<MonthlySourceFile>>(dataResult.ErrorCode!, dataResult.Message!);

        var files = new List<MonthlySourceFile>();
        foreach (var (fields, name) in dataResult.Value)
        {
            using var engine = await _engineFactory.CreateFromTemplateIdAsync(templateResult.Value, ct);
            var fillResult = await FillTemplateEngineAsync(engine, templateResult.Value, fields, new List<Dictionary<string, string>>(), ct);
            if (fillResult.IsFailure)
                return Result.Failure<List<MonthlySourceFile>>(fillResult.ErrorCode!, fillResult.Message!);
            var bytes = await engine.SaveAsync(ct);
            files.Add(new MonthlySourceFile
            {
                FileName = $"{year:D4}{month:D2}_{name}_调整备案表",
                Extension = ".xlsx",
                Bytes = bytes
            });
        }
        LogInfo($"满90周岁调整备案表源文件渲染完成: {files.Count} 个文件");
        return Result.Success(files);
    }

    /// <summary>打印《普惠高龄津贴调整备案表》（满90周岁，逐人一页）</summary>
    public async Task<Result<bool>> PrintElderlyAge90FormAsync(int year, int month, string printerName, int copies = 1, CancellationToken ct = default)
    {
        LogInfo($"打印满90周岁调整备案表: {year}年{month}月 打印机={printerName}");

        var templateResult = await ResolveAge90TemplateIdAsync(ct);
        if (templateResult.IsFailure)
            return Result.Failure<bool>(templateResult.ErrorCode!, templateResult.Message!);

        var dataResult = await BuildAge90FormDataAsync(year, month, ct);
        if (dataResult.IsFailure)
            return Result.Failure<bool>(dataResult.ErrorCode!, dataResult.Message!);

        foreach (var (fields, _) in dataResult.Value)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"nc_elderly_age90_{Guid.NewGuid():N}.xlsx");
            try
            {
                using var engine = await _engineFactory.CreateFromTemplateIdAsync(templateResult.Value, ct);
                engine.PrinterName = printerName;
                var fillResult = await FillTemplateEngineAsync(engine, templateResult.Value, fields, new List<Dictionary<string, string>>(), ct);
                if (fillResult.IsFailure)
                    return Result.Failure<bool>(fillResult.ErrorCode!, fillResult.Message!);
                await engine.SaveToFileAsync(tempPath, ct);
                await engine.PrintFromFileAsync(tempPath, copies, ct);
            }
            catch (Exception ex)
            {
                LogError($"满90周岁调整备案表打印失败: {ex.Message}");
                return Result.Failure<bool>(ErrorCodes.DOCUMENT_GENERATION_FAILED, $"满90周岁调整备案表打印失败: {ex.Message}");
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }
        LogInfo($"满90周岁调整备案表打印完成: {dataResult.Value.Count} 页");
        return Result.Success(true);
    }

    public async Task<Result<byte[]>> PrintLowIncomeProofAsync(long archiveId, CancellationToken ct = default)
    {
        LogInfo("开始打印低保证明");

        var sql = @"SELECT a.*, fm.name as applicant_name, fm.id_card as applicant_id_card
                    FROM nc_biz_archives a
                    LEFT JOIN nc_biz_family_members fm ON a.id = fm.archive_id AND fm.is_applicant = true
                    WHERE a.id = $1";

        var archiveResult = await _db.QuerySingleAsync<ArchivePrintData>(sql, ct, archiveId);
        if (archiveResult.IsFailure)
            return Result.Failure<byte[]>(archiveResult.ErrorCode!, archiveResult.Message!);

        var archive = archiveResult.Value;
        if (archive == null)
            return Result.Failure<byte[]>(ErrorCodes.ARCHIVE_NOT_FOUND, "档案不存在");

        var fields = new Dictionary<string, string>
        {
            ["{档案编号}"] = archiveId.ToString(),
            ["{户主姓名}"] = archive.ApplicantName ?? "",
            ["{身份证号}"] = archive.ApplicantIdCard ?? "",
            ["{分类结果}"] = archive.ClassificationResult ?? "",
            ["{保障金额}"] = archive.TotalGuaranteeAmount.ToString("F2"),
            ["{起始日期}"] = archive.CreatedAt.ToString("yyyy-MM-dd"),
            ["{打印日期}"] = DateTime.Now.ToString("yyyy-MM-dd")
        };

        var templateId = await ResolveTemplateIdByNameAsync("LowIncomeProof", ct);
        var pdfResult = await GeneratePdfAsync(templateId, fields, ct);
        if (pdfResult.IsFailure)
            return Result.Failure<byte[]>(pdfResult.ErrorCode!, pdfResult.Message!);

        LogInfo("低保证明打印完成");
        Logger.LogBusiness("打印低保证明", ("ArchiveId", archiveId));
        return Result.Success(pdfResult.Value);
    }

    public async Task<Result<byte[]>> GeneratePdfAsync(long templateId, Dictionary<string, string> fields, CancellationToken ct = default)
    {
        LogInfo("开始生成PDF");

        try
        {
            var configResult = await GetTemplateConfigAsync(templateId, ct);
            // 配置损坏 → 显式失败，禁止降级为裸字段替换（占位符不被替换 = 打印缺字）
            if (configResult.IsFailure)
            {
                LogError($"模板配置解析失败，终止PDF生成: {configResult.Message}");
                return Result.Failure<byte[]>(configResult.ErrorCode!, configResult.Message!);
            }
            var config = configResult.Value;

            using var engine = await _engineFactory.CreateFromTemplateIdAsync(templateId, ct);

            if (config != null && config.Fields.Count > 0 && fields.Count > 0)
            {
            var placeholderFields = MapFieldsToPlaceholders(fields, config.Fields, config.IndexShifts);
            engine.ReplaceFields(placeholderFields);
            }
            else
            {
                engine.ReplaceFields(fields ?? new Dictionary<string, string>());
            }

            var pdfBytes = await engine.ExportPdfAsync(ct);

            LogInfo("PDF生成完成");
            return Result.Success(pdfBytes);
        }
        catch (Exception ex)
        {
            LogError($"PDF生成失败: {ex.Message}");
            return Result.Failure<byte[]>(ErrorCodes.DOCUMENT_GENERATION_FAILED, $"PDF生成失败: {ex.Message}");
        }
    }

    public async Task<Result<byte[]>> GeneratePdfWithTableAsync(long templateId, Dictionary<string, string> fields, List<Dictionary<string, string>> tableRows, CancellationToken ct = default, IReadOnlyCollection<string>? removeParagraphPlaceholders = null)
    {
        LogInfo("开始生成带表格PDF");

        try
        {
            var configResult = await GetTemplateConfigAsync(templateId, ct);
            if (configResult.IsFailure)
            {
                LogError($"模板配置解析失败: TemplateId={templateId}, {configResult.Message}");
                return Result.Failure<byte[]>(configResult.ErrorCode!, configResult.Message!);
            }
            var config = configResult.Value;
            if (config == null)
            {
                LogError($"模板配置不存在或解析失败: TemplateId={templateId}");
                return Result.Failure<byte[]>(ErrorCodes.DOCUMENT_GENERATION_FAILED, "模板配置不存在或解析失败，无法生成带表格PDF");
            }

            LogInfo($"模板配置: Fields={config.Fields.Count}, Tables={config.Tables.Count}");
            LogInfo($"输入字段数: {fields?.Count ?? 0}, 表格行数: {tableRows?.Count ?? 0}");

            // 记录索引字段
            var indexedFields = fields?.Where(kv => kv.Key.Contains('_') && int.TryParse(kv.Key.Split('_').Last(), out _)).Take(10).ToList();
            if (indexedFields != null && indexedFields.Count > 0)
            {
                LogInfo($"索引字段示例: {string.Join(", ", indexedFields.Select(kv => $"{kv.Key}={kv.Value}"))}");
            }

            fields = FlattenTableRowsToFields(fields!, tableRows!, config);
            LogInfo($"展平后字段数: {fields?.Count ?? 0}");

            // 确保所有配置字段都有默认空值（避免占位符未替换）
            if (config.Fields != null)
            {
                foreach (var mapping in config.Fields)
                {
                    if (!string.IsNullOrEmpty(mapping.FieldKey) && fields != null && !fields.ContainsKey(mapping.FieldKey))
                    {
                        fields[mapping.FieldKey] = mapping.DefaultValue ?? string.Empty;
                    }
                }
                LogInfo($"补充默认值后字段数: {fields?.Count ?? 0}");
            }

            // 记录展平后的索引字段
            if (fields != null)
            {
                var indexedAfterFlatten = fields.Where(kv => kv.Key.Contains('_') && int.TryParse(kv.Key.Split('_').Last(), out _)).Take(15).ToList();
                if (indexedAfterFlatten.Count > 0)
                {
                    LogInfo($"展平后索引字段: {string.Join(", ", indexedAfterFlatten.Select(kv => $"{kv.Key}={kv.Value}"))}");
                }
            }

            var placeholderFields = MapFieldsToPlaceholders(fields!, config.Fields!, config.IndexShifts);
            LogInfo($"映射后占位符数: {placeholderFields?.Count ?? 0}");

            // 记录映射结果
            if (placeholderFields != null)
            {
                var sampleFields = placeholderFields.Take(10).ToList();
                LogInfo($"占位符示例: {string.Join(", ", sampleFields.Select(kv => $"{kv.Key}={kv.Value}"))}");
            }

            using var engine = await _engineFactory.CreateFromTemplateIdAsync(templateId, ct);

            // 先删多余占位符整段（段内仍为占位符原文），再替换其余字段
            if (removeParagraphPlaceholders is { Count: > 0 })
                engine.RemovePlaceholderParagraphs(removeParagraphPlaceholders);

            engine.ReplaceFields(placeholderFields!);

            if (tableRows != null && tableRows.Count > 0 && config.Tables.Count > 0)
            {
                var tableConfig = config.Tables[0];
                var placeholderRows = MapRowsToPlaceholders(tableRows, tableConfig.Columns);
                engine.ReplaceTableByPlaceholder(tableConfig.StartMarker, tableConfig.EndMarker, placeholderRows);
            }

            var pdfBytes = await engine.ExportPdfAsync(ct);

            LogInfo("带表格PDF生成完成");
            return Result.Success(pdfBytes);
        }
        catch (Exception ex)
        {
            LogError($"带表格PDF生成失败: {ex.Message}");
            return Result.Failure<byte[]>(ErrorCodes.DOCUMENT_GENERATION_FAILED, $"带表格PDF生成失败: {ex.Message}");
        }
    }

    private Dictionary<string, string> FlattenTableRowsToFields(
        Dictionary<string, string> fields,
        List<Dictionary<string, string>> tableRows,
        TemplateConfig config)
    {
        if (config == null)
            return fields;

        // 始终检测 config.Fields 中是否有索引字段（不依赖 config.Tables）
        var hasIndexedFields = config.Fields.Any(f =>
        {
            var idx = f.FieldKey.LastIndexOf('_');
            return idx > 0 && idx < f.FieldKey.Length - 1
                && int.TryParse(f.FieldKey[(idx + 1)..], out _);
        });
        if (!hasIndexedFields)
            return fields;

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
            return fields;

        // 非破坏性：在副本上操作，绝不原地修改调用方字典。
        // 否则分块预览时第一页展平出的索引数据会残留在共享字段中，
        // 第二页命中"已有索引数据"分支后复用第一页数据，导致两页一模一样。
        var result = new Dictionary<string, string>(fields, StringComparer.Ordinal);

        // tableRows 非空时以 tableRows 为权威：先清空各 base 全部槽位再重建，
        // 防止字段中残留的旧索引数据（如上一分页的展平结果）覆盖当前页数据。
        // tableRows 为空时保留字段中预填的索引（档案生产路径由 ViewModel 预填户主/成员索引）。
        // 清空范围收窄到"行键实际出现的组"：SUPPORTER_*/SHARED_* 由 ViewModel 预填、
        // 与行数据无关（经济财产声明书），若被一并清空会导致预览缺赡养人/共同成员信息。
        if (tableRows is { Count: > 0 })
        {
            var rowBaseKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in tableRows)
            {
                foreach (var key in row.Keys)
                {
                    if (maxIndexByBase.ContainsKey(key))
                        rowBaseKeys.Add(key);
                }
            }

            foreach (var (baseKey, maxIdx) in maxIndexByBase)
            {
                if (!rowBaseKeys.Contains(baseKey)) continue;
                for (var i = 1; i <= maxIdx; i++)
                    result.Remove($"{baseKey}_{i}");
            }

            for (var i = 0; i < tableRows.Count; i++)
            {
                var suffix = i + 1;
                foreach (var kv in tableRows[i])
                {
                    if (maxIndexByBase.ContainsKey(kv.Key))
                        result[$"{kv.Key}_{suffix}"] = kv.Value;
                }
            }
        }

        // 填充空缺的索引（含 tableRows 不足槽位时的剩余空槽）
        foreach (var (baseKey, maxIdx) in maxIndexByBase)
        {
            for (var i = 1; i <= maxIdx; i++)
            {
                var key = $"{baseKey}_{i}";
                if (!result.ContainsKey(key))
                    result[key] = string.Empty;
            }
        }

        return result;
    }

    /// <summary>
    /// 取模板 ConfigJson 解析结果。
    /// Success(null) = 模板不存在或未配置 ConfigJson，调用方可按"无配置"降级（占位符与字段同名，属正常形态）；
    /// Failure = ConfigJson 存在但解析失败（配置损坏），调用方必须显式失败并终止渲染——
    /// 若降级为空配置会走"裸字段替换"分支，模板占位符残留 → 打印缺字。
    /// </summary>
    private async Task<Result<TemplateConfig?>> GetTemplateConfigAsync(long templateId, CancellationToken ct)
    {
        try
        {
            var templateResult = await _templateService.GetByIdAsync(templateId, ct);
            if (templateResult.IsFailure)
                return Result.Failure<TemplateConfig?>(
                    templateResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    $"读取模板失败: TemplateId={templateId}, {templateResult.Message}");
            var template = templateResult.Value;
            if (template == null || string.IsNullOrEmpty(template.ConfigJson)) return Result.Success<TemplateConfig?>(null);

            return Result.Success<TemplateConfig?>(TemplateConfig.FromJson(template.ConfigJson));
        }
        catch (Exception ex)
        {
            LogError($"模板配置解析失败: TemplateId={templateId}, Error={ex.Message}");
            return Result.Failure<TemplateConfig?>(ErrorCodes.FILE_FORMAT_ERROR, $"模板配置解析失败: TemplateId={templateId}");
        }
    }

    /// <summary>
    /// 将字段字典映射为占位符字典。可选传入 indexShifts 规则执行索引投影（方案C）。
    /// </summary>
    private Dictionary<string, string> MapFieldsToPlaceholders(
        Dictionary<string, string> fieldValues,
        List<TemplateFieldMapping> mappings,
        IReadOnlyList<IndexShiftRule>? indexShifts = null)
    {
        fieldValues = TemplateFieldResolver.Project(fieldValues, indexShifts);
        var result = new Dictionary<string, string>();
        var missingFields = new List<string>();

        foreach (var mapping in mappings)
        {
            if (!string.IsNullOrEmpty(mapping.FieldKey) && fieldValues.TryGetValue(mapping.FieldKey, out var rawValue))
            {
                // 根据字段类型格式化
                var formattedValue = FormatFieldValue(rawValue, mapping);
                result[mapping.Placeholder] = formattedValue;
            }
            else
            {
                // 字段不存在时，根据类型设置默认值
                result[mapping.Placeholder] = GetDefaultValue(mapping);
                missingFields.Add($"{mapping.Placeholder}={mapping.FieldKey}");
            }
        }

        if (missingFields.Count > 0)
        {
            LogWarn($"未映射的字段 ({missingFields.Count}): {string.Join(", ", missingFields.Take(20))}");
        }

        return result;
    }

    private string FormatFieldValue(string value, TemplateFieldMapping mapping)
    {
        // 如果有自定义格式，使用自定义格式
        if (!string.IsNullOrEmpty(mapping.Format))
        {
            return TemplateFieldMapping.ApplyFormat(value, mapping.Format);
        }

        // 数值类型字段：空值显示 "0"
        if (IsNumericField(mapping.FieldKey))
        {
            return string.IsNullOrEmpty(value) ? "0" : value;
        }

        // 文本类型字段：空值显示 "-"
        return string.IsNullOrEmpty(value) ? "-" : value;
    }

    private string GetDefaultValue(TemplateFieldMapping mapping)
    {
        // 数值类型字段默认 "0"
        if (IsNumericField(mapping.FieldKey))
        {
            return mapping.DefaultValue ?? "0";
        }

        // 文本类型字段默认 "-"
        return mapping.DefaultValue ?? "-";
    }

    private bool IsNumericField(string fieldKey)
    {
        // 数值类型字段列表
        var numericFields = new[]
        {
            "HEAD_FAMILY_SIZE", "APPLICANT_COUNT",
            "SHARED_MEMBER_AGE", "SUPPORTER_AGE", "SUPPORTER_AMOUNT",
            "PROPERTY_BUILDING_AREA", "PROPERTY_DEPOSIT", "PROPERTY_SECURITY",
            "PROPERTY_FUND", "PROPERTY_INSURANCE", "PROPERTY_BOND", "PROPERTY_OTHER",
            "INCOME_BREEDING", "INCOME_LABOR", "INCOME_BUSINESS", "INCOME_PROPERTY",
            "INCOME_TRANSFER", "INCOME_ALIMONY", "INCOME_OTHER",
            "INCOME_TOTAL", "INCOME_RIGID_EXPENDITURE",
            "INCOME_SUBSIDY", "INCOME_LAND",
            "LAND_INCOME_TOTAL", "TOTAL_CONFIRMED_LAND_AREA",
            "FAMILY_LAND_AREA", "SELF_FARMED_LAND_AREA",
            "SUBLEASED_LAND_AREA", "CONTRACTED_LAND_AREA"
        };

        // 文本描述类字段（_DESC 后缀）不作为数值处理，避免被 baseKey 截断误判
        if (fieldKey.EndsWith("_DESC", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 检查是否是索引字段（如 LABOR_LOCATION_1）
        var baseKey = fieldKey.Contains('_') 
            ? fieldKey[..fieldKey.LastIndexOf('_')] 
            : fieldKey;

        return numericFields.Contains(baseKey);
    }

    private List<Dictionary<string, string>> MapRowsToPlaceholders(List<Dictionary<string, string>> rowFieldValues, List<TemplateFieldMapping> columnMappings)
    {
        var result = new List<Dictionary<string, string>>();

        foreach (var row in rowFieldValues)
        {
            var placeholderRow = new Dictionary<string, string>();
            foreach (var colMapping in columnMappings)
            {
                if (!string.IsNullOrEmpty(colMapping.FieldKey) && row.TryGetValue(colMapping.FieldKey, out var cellValue))
                {
                    var formattedValue = TemplateFieldMapping.ApplyFormat(cellValue, colMapping.Format);
                    placeholderRow[colMapping.Placeholder] = formattedValue;
                }
            }
            result.Add(placeholderRow);
        }

        return result;
    }

    public async Task<Result<List<PrintTemplate>>> GetTemplatesAsync(CancellationToken ct = default)
    {
        LogInfo("获取模板列表");

        try
        {
            var templatesResult = await _templateService.GetAllAsync(ct);
            if (templatesResult.IsFailure)
                return Result.Failure<List<PrintTemplate>>(
                    templatesResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    $"获取模板列表失败: {templatesResult.Message}");

            var result = (templatesResult.Value ?? new List<Models.Entities.Template>()).Select(t => new PrintTemplate
            {
                Name = t.Name,
                Code = t.Id.ToString(),
                Description = t.FileType,
                Status = ApplicationStatusCodes.ACTIVE,
                CreatedAt = t.CreatedAt
            }).ToList();

            return Result.Success(result);
        }
        catch (Exception ex)
        {
            LogError($"获取模板列表失败: {ex.Message}");
            return Result.Failure<List<PrintTemplate>>(ErrorCodes.DB_QUERY_ERROR, $"获取模板列表失败: {ex.Message}");
        }
    }

    public ITemplateEngineFactory GetEngineFactory()
    {
        return _engineFactory;
    }

    private async Task<long> ResolveTemplateIdByNameAsync(string templateName, CancellationToken ct)
    {
        var templatesResult = await _templateService.GetAllAsync(ct);
        if (templatesResult.IsFailure)
            throw new BusinessException(templatesResult.ErrorCode!, templatesResult.Message!);
        var template = (templatesResult.Value ?? new List<Models.Entities.Template>())
            .FirstOrDefault(t => string.Equals(t.Name, templateName, StringComparison.OrdinalIgnoreCase));
        if (template == null)
            throw new InvalidOperationException($"找不到模板: {templateName}");
        return template.Id;
    }

    private class ArchivePrintData
    {
        public long Id { get; set; }
        public string ApplicantName { get; set; } = string.Empty;
        public string ApplicantIdCard { get; set; } = string.Empty;
        public string ClassificationResult { get; set; } = string.Empty;
        public decimal TotalGuaranteeAmount { get; set; }
        public bool IsInGracePeriod { get; set; }
        public DateTime? GracePeriodStartDate { get; set; }
        public DateTime? GracePeriodEndDate { get; set; }
        public string OriginalClassificationResult { get; set; } = string.Empty;
        public decimal? OriginalGuaranteeAmount { get; set; }
        public DateTime CreatedAt { get; set; }
    }

}
