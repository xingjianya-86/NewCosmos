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

/// <summary>
/// 打印服务实现
/// </summary>
public class PrintService : BaseService, IPrintService
{
    protected override string ServiceName => "PrintService";
    private readonly IDatabaseService _db;
    private readonly ITemplateEngineFactory _engineFactory;
    private readonly ITemplateService _templateService;
    private readonly IMonthlyReportService _monthlyReportService;
    private readonly IBusinessTimelineService _timelineService;
    private readonly IElderlyApplicationService _elderlyApplicationService;
    private readonly INearRelativeService _nearRelativeService;

    public PrintService(IDatabaseService db, ILoggerService logger, ITemplateEngineFactory engineFactory, ITemplateService templateService, IMonthlyReportService monthlyReportService, IBusinessTimelineService timelineService, IElderlyApplicationService elderlyApplicationService, INearRelativeService nearRelativeService) : base(logger)
    {
        _db = db;
        _engineFactory = engineFactory;
        _templateService = templateService;
        _monthlyReportService = monthlyReportService;
        _timelineService = timelineService;
        _elderlyApplicationService = elderlyApplicationService;
        _nearRelativeService = nearRelativeService;
    }

    public async Task<Result<byte[]>> PrintArchiveAsync(long archiveId, CancellationToken ct = default)
    {
        LogInfo("开始打印档案");

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
            ["{分类结果}"] = archive.ClassificationResult ?? "",
            ["{保障金额}"] = archive.TotalGuaranteeAmount.ToString("F2"),
            ["{创建日期}"] = archive.CreatedAt.ToString("yyyy-MM-dd")
        };

        if (archive.IsInGracePeriod)
        {
            fields["{渐退期开始}"] = (archive.GracePeriodStartDate ?? DateTime.Now).ToString("yyyy-MM-dd");
            fields["{渐退期结束}"] = (archive.GracePeriodEndDate ?? DateTime.Now).ToString("yyyy-MM-dd");
            fields["{原分类}"] = archive.OriginalClassificationResult ?? "";
            fields["{原金额}"] = (archive.OriginalGuaranteeAmount ?? 0).ToString("N2");
        }

        var templateId = await ResolveTemplateIdByNameAsync("Archive", ct);
        var pdfResult = await GeneratePdfAsync(templateId, fields, ct);
        if (pdfResult.IsFailure)
            return Result.Failure<byte[]>(pdfResult.ErrorCode!, pdfResult.Message!);

        LogInfo("打印档案完成");
        Logger.LogBusiness("打印档案", ("ArchiveId", archiveId));
        return Result.Success(pdfResult.Value);
    }

    public async Task<Result<byte[]>> PrintChangeConfirmationAsync(long changeRecordId, CancellationToken ct = default)
    {
        LogInfo("开始打印变更确认书");

        var sql = @"SELECT cr.*, a.applicant_name
                    FROM nc_biz_change_records cr
                    LEFT JOIN nc_biz_archives a ON cr.archive_id = a.id
                    WHERE cr.id = $1";

        var changeResult = await _db.QuerySingleAsync<ChangeRecordPrintData>(sql, ct, changeRecordId);
        if (changeResult.IsFailure)
            return Result.Failure<byte[]>(changeResult.ErrorCode!, changeResult.Message!);

        var change = changeResult.Value;
        if (change == null)
            return Result.Failure<byte[]>(ErrorCodes.NOT_FOUND, "变更记录不存在");

        var fields = new Dictionary<string, string>
        {
            ["{变更编号}"] = changeRecordId.ToString(),
            ["{户主姓名}"] = change.ApplicantName ?? "",
            ["{变更类型}"] = change.ChangeType ?? "",
            ["{变更原因}"] = change.ChangeReason ?? "",
            ["{变更前值}"] = change.BeforeValue ?? "",
            ["{变更后值}"] = change.AfterValue ?? "",
            ["{变更日期}"] = change.CreatedAt.ToString("yyyy-MM-dd")
        };

        var templateId = await ResolveTemplateIdByNameAsync("ChangeConfirmation", ct);
        var pdfResult = await GeneratePdfAsync(templateId, fields, ct);
        if (pdfResult.IsFailure)
            return Result.Failure<byte[]>(pdfResult.ErrorCode!, pdfResult.Message!);

        LogInfo("变更确认书打印完成");
        return Result.Success(pdfResult.Value);
    }

/// <summary>
    /// 渲染月报表单为合并 PDF（单页/双页按表单类型），供页面预览
    /// </summary>
    public async Task<Result<byte[]>> RenderMonthlyFormAsync(int year, int month, string formKey, string town, CancellationToken ct = default)
    {
        LogInfo($"渲染月报表单: {year}年{month}月 formKey={formKey} 乡镇={town ?? "全部"}");

        var (baseForm, category) = SplitMonthlyFormKey(formKey);
        var templateResult = await ResolveMonthlyTemplateIdAsync(baseForm, category, ct);
        if (templateResult.IsFailure)
            return Result.Failure<byte[]>(templateResult.ErrorCode!, templateResult.Message!);
        var templateId = templateResult.Value;

        var pagesResult = await BuildMonthlyFormDataAsync(year, month, baseForm, category, town, ct);
        if (pagesResult.IsFailure)
            return Result.Failure<byte[]>(pagesResult.ErrorCode!, pagesResult.Message!);

        var pdfs = new List<byte[]>();
        foreach (var page in pagesResult.Value)
        {
            var pdfResult = await GeneratePdfWithTableAsync(templateId, page.Fields, page.Rows, ct, page.RemoveParagraphPlaceholders);
            if (pdfResult.IsFailure)
                return pdfResult;
            pdfs.Add(pdfResult.Value);
        }

        if (pdfs.Count == 0)
            return Result.Failure<byte[]>(ErrorCodes.NOT_FOUND, $"月报表单无数据: {formKey}");

        var merged = pdfs.Count == 1 ? pdfs[0] : MergePdfBytes(pdfs);
        LogInfo($"月报表单渲染成功: {formKey}");
        return Result.Success(merged);
    }

    /// <summary>
    /// 批量渲染多张月报表单并合并为一份 PDF（勾选列表预览）；无数据表单自动跳过
    /// </summary>
    public async Task<Result<byte[]>> RenderMonthlyFormsMergedAsync(int year, int month, IEnumerable<string> formKeys, string town, CancellationToken ct = default)
    {
        LogInfo($"渲染月报表多表单合并预览: 表单数={formKeys.Count()}");

        var pdfs = new List<byte[]>();
        var skipped = new List<string>();
        foreach (var formKey in formKeys.Distinct())
        {
            var result = await RenderMonthlyFormAsync(year, month, formKey, town, ct);
            if (result.IsFailure)
            {
                // 无数据表单跳过（消息含"无数据"），其余真实错误终止
                if (result.ErrorCode == ErrorCodes.NOT_FOUND && result.Message?.Contains("无数据") == true)
                {
                    skipped.Add(formKey);
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
    /// 渲染月报表单为源文件字节（xlsx/docx，供导出；双页表单拆多页文件）
    /// </summary>
    public async Task<Result<List<MonthlySourceFile>>> RenderMonthlyFormToSourceAsync(int year, int month, string formKey, string town, CancellationToken ct = default)
    {
        LogInfo($"渲染月报表单源文件: {year}年{month}月 formKey={formKey}");

        var (baseForm, category) = SplitMonthlyFormKey(formKey);
        var templateResult = await ResolveMonthlyTemplateIdAsync(baseForm, category, ct);
        if (templateResult.IsFailure)
            return Result.Failure<List<MonthlySourceFile>>(templateResult.ErrorCode!, templateResult.Message!);
        var templateId = templateResult.Value;

        var pagesResult = await BuildMonthlyFormDataAsync(year, month, baseForm, category, town, ct);
        if (pagesResult.IsFailure)
            return Result.Failure<List<MonthlySourceFile>>(pagesResult.ErrorCode!, pagesResult.Message!);

        var extension = baseForm is "会议记录" or "会议记录_一事一议" ? ".docx" : ".xlsx";
        var files = new List<MonthlySourceFile>();
        foreach (var page in pagesResult.Value)
        {
            using var engine = await _engineFactory.CreateFromTemplateIdAsync(templateId, ct);
            var fillResult = await FillTemplateEngineAsync(engine, templateId, page.Fields, page.Rows, ct, page.RemoveParagraphPlaceholders);
            if (fillResult.IsFailure)
                return Result.Failure<List<MonthlySourceFile>>(fillResult.ErrorCode!, fillResult.Message!);
            var bytes = await engine.SaveAsync(ct);
            files.Add(new MonthlySourceFile
            {
                FileName = $"{year:D4}{month:D2}_{formKey}{page.TitleSuffix}",
                Extension = extension,
                Bytes = bytes
            });
        }
        LogInfo($"月报表单源文件渲染成功: {formKey} 共{files.Count}个文件");
        return Result.Success(files);
    }

    /// <summary>
    /// 打印月报表单（渲染源文件后走 Office COM 打印；双页表单按页打印）
    /// </summary>
    public async Task<Result<bool>> PrintMonthlyFormAsync(int year, int month, string formKey, string town, string printerName, int copies = 1, CancellationToken ct = default)
    {
        LogInfo($"打印月报表单: {year}年{month}月 formKey={formKey} 打印机={printerName} 份数={copies}");

        var (baseForm, category) = SplitMonthlyFormKey(formKey);
        var templateResult = await ResolveMonthlyTemplateIdAsync(baseForm, category, ct);
        if (templateResult.IsFailure)
            return Result.Failure<bool>(templateResult.ErrorCode!, templateResult.Message!);
        var templateId = templateResult.Value;

        var pagesResult = await BuildMonthlyFormDataAsync(year, month, baseForm, category, town, ct);
        if (pagesResult.IsFailure)
            return Result.Failure<bool>(pagesResult.ErrorCode!, pagesResult.Message!);

        var extension = baseForm is "会议记录" or "会议记录_一事一议" ? ".docx" : ".xlsx";
        foreach (var page in pagesResult.Value)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), $"nc_monthly_{Guid.NewGuid():N}{extension}");
            try
            {
                using var engine = await _engineFactory.CreateFromTemplateIdAsync(templateId, ct);
                engine.PrinterName = printerName;
                var fillResult = await FillTemplateEngineAsync(engine, templateId, page.Fields, page.Rows, ct, page.RemoveParagraphPlaceholders);
                if (fillResult.IsFailure)
                    return Result.Failure<bool>(fillResult.ErrorCode!, fillResult.Message!);
                await engine.SaveToFileAsync(tempPath, ct);
                await engine.PrintFromFileAsync(tempPath, copies, ct);
            }
            catch (Exception ex)
            {
                LogError($"月报表单打印失败: {ex.Message}");
                return Result.Failure<bool>(ErrorCodes.DOCUMENT_GENERATION_FAILED, $"月报表单打印失败: {ex.Message}");
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            }
        }
        LogInfo($"月报表单打印完成: {formKey} 共{pagesResult.Value.Count}页");
        return Result.Success(true);
    }

    /// <summary>
    /// 将字段/行数据填充到渲染引擎（字段映射 + 表格拆行替换）
    /// </summary>
    private async Task<Result<bool>> FillTemplateEngineAsync(ITemplateEngine engine, long templateId, Dictionary<string, string> fields, List<Dictionary<string, string>> tableRows, CancellationToken ct, IReadOnlyCollection<string>? removeParagraphPlaceholders = null)
    {
        var config = await GetTemplateConfigAsync(templateId, ct);
        if (config == null)
            return Result.Failure<bool>(ErrorCodes.DOCUMENT_GENERATION_FAILED, "模板配置不存在或解析失败");

        fields = FlattenTableRowsToFields(fields, tableRows, config);
        if (config.Fields != null)
        {
            foreach (var mapping in config.Fields)
            {
                if (!string.IsNullOrEmpty(mapping.FieldKey) && !fields.ContainsKey(mapping.FieldKey))
                    fields[mapping.FieldKey] = mapping.DefaultValue ?? string.Empty;
            }
        }

        // 先删多余占位符整段（段内仍为占位符原文），再替换其余字段
        if (removeParagraphPlaceholders is { Count: > 0 })
            engine.RemovePlaceholderParagraphs(removeParagraphPlaceholders);

        var placeholderFields = MapFieldsToPlaceholders(fields, config.Fields ?? new List<TemplateFieldMapping>(), config.IndexShifts);
        engine.ReplaceFields(placeholderFields);

        if (tableRows.Count > 0 && config.Tables.Count > 0)
        {
            var tableConfig = config.Tables[0];
            var placeholderRows = MapRowsToPlaceholders(tableRows, tableConfig.Columns);
            engine.ReplaceTableByPlaceholder(tableConfig.StartMarker, tableConfig.EndMarker, placeholderRows);
        }
        return Result.Success(true);
    }

    /// <summary>
    /// 月报表单数据页（双页表单 = 农村/城市两页）
    /// </summary>
    private class MonthlyFormPageData
    {
        public Dictionary<string, string> Fields { get; set; } = new();
        public List<Dictionary<string, string>> Rows { get; set; } = new();
        public string TitleSuffix { get; set; } = string.Empty;

        /// <summary>内容仅为单个待删占位符的整段（docx 会议记录多余成员行，渲染前删除）</summary>
        public List<string> RemoveParagraphPlaceholders { get; set; } = new();
    }

    /// <summary>
    /// 组装月报表单分页数据（字段 + 表格行）。新增/停保拆为"农村/城市"两页，其余表单一页。
    /// </summary>
    private async Task<Result<List<MonthlyFormPageData>>> BuildMonthlyFormDataAsync(int year, int month, string baseForm, string? category, string? town, CancellationToken ct = default)
    {
        var townSafe = town ?? string.Empty;
        LogInfo($"组装月报表表单数据: {year}年{month}月 form={baseForm} 乡镇={townSafe}");

        // 填报单位 = 当前机构名（无机构时回退乡镇）
        var fillUnit = await ResolveReportUnitAsync(townSafe, ct);

        // 业务截止期（填报/审批时间）= B 时间轴（business_process）周期结束日（默认本月15号，遇节假日调整）
        var timelineResult = await _timelineService.CalculateTimelineAsync(year, month, TimelineType.BusinessProcess);
        var fillDate = timelineResult.CycleEndDate.ToString("yyyy-MM-dd");

        // 标题月份 = 周期结束日的次月（B 线周期上月15~本月15，报表标注次月业务；跨年处理）
        var titleYear = month == 12 ? year + 1 : year;
        var titleMonth = month == 12 ? 1 : month + 1;
        var formName = baseForm switch
        {
            "新增救助明细" => "新增救助明细",
            "停保汇总表" => "停保汇总",
            "保障金增发表" => "保障金增发",
            "保障金减发表" => "保障金减发",
            "施保金减发" => "分类施保金减发人员表（最低生活保障）",
            "分类施保增加" => "分类施保金增发人员表（最低生活保障）",
            "自然减员表" => "人员变动_自然减员月报表",
            "档案_退出对象兜底情况纠治表" => "退出对象兜底情况纠治表（批量）",
            "会议记录" => "会议记录",
            "会议记录_一事一议" => "会议记录（一事一议）",
            NearRelativeConstants.FormKeyStaffBatch => "近亲属备案（工作人员批量）",
            NearRelativeConstants.FormKeySummary => "近亲属备案汇总",
            _ => "救助业务"
        };
        // 名称已含"月报表"（如人员变动_自然减员月报表）时不再追加后缀
        var reportTit = formName.EndsWith("月报表", StringComparison.Ordinal)
            ? $"{titleYear}年{titleMonth}月{formName}"
            : $"{titleYear}年{titleMonth}月{formName}月报表";

        var pages = new List<MonthlyFormPageData>();

        switch (baseForm)
        {
            case "新增救助明细":
            case "停保汇总表":
            {
                // 标题分类名不显示农村/城市前缀（城乡分页仅用 titleSuffix 区分，标题文字统一）
                var catTag = string.IsNullOrWhiteSpace(category) ? "" : category;
                var areaTag = string.IsNullOrEmpty(catTag) ? "" : $"（{catTag}）";
                // 最低生活保障边缘家庭：停止、减员汇总表（固定标题，含县名与"停止、减员"）
                var isEdgeFamilyStopForm = baseForm == "停保汇总表"
                    && string.Equals(category, "最低生活保障边缘家庭", StringComparison.Ordinal);
                foreach (var rural in new[] { true, false })
                {
                    var titleSuffix = rural ? "_农村" : "_城市";
                    var fields = new Dictionary<string, string>
                    {
                        ["REPORT_TITLE"] = isEdgeFamilyStopForm
                            ? $"林口县{titleYear}年{titleMonth}月城乡低保边缘家庭停止、减员汇总表"
                            : reportTit + areaTag,
                        ["REPORT_UNIT"] = fillUnit,
                        ["REPORT_DATE"] = fillDate
                    };
                    var rows = new List<Dictionary<string, string>>();

                    if (baseForm == "新增救助明细")
                    {
                        var src = await _monthlyReportService.GetAddedRowsAsync(year, month, townSafe, category, ct);
                        if (src.IsFailure)
                            return Result.Failure<List<MonthlyFormPageData>>(src.ErrorCode!, src.Message!);
                        foreach (var r in src.Value.Where(x => x.IsRural == rural))
                        {
                            rows.Add(new Dictionary<string, string>
                            {
                                ["NEW_AUDIT_DATE"] = fillDate,
                                ["NEW_NAME"] = r.Name,
                                ["NEW_ID_CARD"] = r.IdCard,
                                ["NEW_AGE"] = r.Age,
                                ["NEW_GENDER"] = r.Gender,
                                ["NEW_ADDRESS"] = r.Address,
                                ["NEW_FAMILY_SIZE"] = r.FamilySize,
                                ["NEW_CATEGORY"] = r.Classification,
                                // 最低生活保障边缘家庭模板专用列（杜昌兰等最低生活保障边缘家庭户数据此前未读取）
                                ["NEW_MARITAL_STATUS"] = r.MaritalStatus,
                                ["NEW_ANNUAL_INCOME"] = r.AnnualIncome.ToString("F2"),
                                ["NEW_RELATION"] = r.Relation,
                                ["NEW_HEALTH"] = r.HealthStatus,
                                ["NEW_BANK_ACCOUNT"] = r.BankAccount
                            });
                        }
                    }
                    else
                    {
                        var src = await _monthlyReportService.GetStoppedRowsAsync(year, month, townSafe, category, ct);
                        if (src.IsFailure)
                            return Result.Failure<List<MonthlyFormPageData>>(src.ErrorCode!, src.Message!);
                        foreach (var r in src.Value.Where(x => x.IsRural == rural))
                        {
                            rows.Add(new Dictionary<string, string>
                            {
                                ["HEAD_NAME"] = r.HeadName,
                                ["HEAD_ID_CARD"] = r.HeadIdCard,
                                ["HEAD_FAMILY_SIZE"] = r.FamilySize,
                                ["HEAD_ADDRESS"] = r.Address,
                                ["CATEGORY"] = r.Category,
                                ["CLASSIFICATION"] = r.Classification,
                                ["MONTH_AMOUNT"] = r.MonthAmount,
                                ["STOP_REASON"] = r.StopReason,
                                ["STOP_DATE"] = r.StopDate,
                                // 边缘家庭"停止、减员汇总表"专用：业务类别=停止/减员（其他停保模板无此占位符）
                                ["BIZ_TYPE"] = r.BizType
                            });
                        }
                    }

                    if (rows.Count > 0)
                        pages.Add(new MonthlyFormPageData { Fields = fields, Rows = rows, TitleSuffix = titleSuffix });
                }
                break;
            }
            case "保障金增发表":
            {
                var src = await _monthlyReportService.GetIncreaseRowsAsync(year, month, townSafe, category, ct);
                if (src.IsFailure)
                    return Result.Failure<List<MonthlyFormPageData>>(src.ErrorCode!, src.Message!);
                var fields = new Dictionary<string, string>
                {
                    ["REPORT_TITLE"] = reportTit + (string.IsNullOrWhiteSpace(category) ? "" : $"（{category}）"),
                    ["REPORT_UNIT"] = fillUnit,
                    ["REPORT_DATE"] = fillDate
                };
                var rows = new List<Dictionary<string, string>>();
                foreach (var r in src.Value)
                {
                    rows.Add(new Dictionary<string, string>
                    {
                        ["HEAD_NAME"] = r.HeadName,
                        ["HEAD_BIRTH"] = r.HeadBirth,
                        ["HEAD_FAMILY_SIZE"] = r.FamilySize,
                        ["HEAD_ADDRESS"] = r.Address,
                        ["CATEGORY"] = r.Category,
                        ["ENJOY_DATE"] = r.EnjoyDate,
                        ["OLD_AMOUNT"] = r.OldAmount,
                        ["NEW_AMOUNT"] = r.NewAmount,
                        ["NEW_CLASSIFICATION"] = r.Classification,
                        ["INCREASE_AMOUNT"] = r.ChangeAmount,
                        ["INCREASE_REASON"] = r.Reason
                    });
                }
                if (rows.Count > 0)
                    pages.Add(new MonthlyFormPageData { Fields = fields, Rows = rows });
                break;
            }
            case "保障金减发表":
            {
                var src = await _monthlyReportService.GetDecreaseRowsAsync(year, month, townSafe, category, ct);
                if (src.IsFailure)
                    return Result.Failure<List<MonthlyFormPageData>>(src.ErrorCode!, src.Message!);
                var fields = new Dictionary<string, string>
                {
                    ["REPORT_TITLE"] = reportTit + (string.IsNullOrWhiteSpace(category) ? "" : $"（{category}）"),
                    ["REPORT_UNIT"] = fillUnit,
                    ["REPORT_DATE"] = fillDate
                };
                var rows = new List<Dictionary<string, string>>();
                foreach (var r in src.Value)
                {
                    rows.Add(new Dictionary<string, string>
                    {
                        ["HEAD_NAME"] = r.HeadName,
                        ["HEAD_BIRTH"] = r.HeadBirth,
                        ["OLD_FAMILY_SIZE"] = r.OldFamilySize,
                        ["HEAD_ADDRESS"] = r.Address,
                        ["CATEGORY"] = r.Category,
                        ["ENJOY_DATE"] = r.EnjoyDate,
                        ["OLD_AMOUNT"] = r.OldAmount,
                        ["CURRENT_AMOUNT"] = r.NewAmount,
                        ["CURRENT_CLASSIFICATION"] = r.ClassifiedSubsidyAmount.ToString("F2"),
                        ["CURRENT_FAMILY_SIZE"] = r.FamilySize,
                        ["DECREASE_PERSON"] = (r.DecreasePerson > 0 ? r.DecreasePerson.ToString() : "0"),
                        ["DECREASE_MEMBER_REASON"] = r.Reason,
                        ["DECREASE_AMOUNT"] = r.ChangeAmount
                    });
                }
                if (rows.Count > 0)
                    pages.Add(new MonthlyFormPageData { Fields = fields, Rows = rows });
                break;
            }
            case "施保金减发":
            {
                var src = await _monthlyReportService.GetShiBaoReductionRowsAsync(year, month, ct);
                if (src.IsFailure)
                    return Result.Failure<List<MonthlyFormPageData>>(src.ErrorCode!, src.Message!);
                var fields = new Dictionary<string, string>
                {
                    ["REPORT_TITLE"] = reportTit + (string.IsNullOrWhiteSpace(category) ? "" : $"（{category}）"),
                    ["REPORT_UNIT"] = fillUnit,
                    ["REPORT_DATE"] = fillDate
                };
                var rows = new List<Dictionary<string, string>>();
                foreach (var r in src.Value)
                {
                    rows.Add(new Dictionary<string, string>
                    {
                        ["HEAD_NAME"] = r.HeadName,
                        ["HEAD_ADDRESS"] = r.Address,
                        ["CATEGORY"] = r.Category,
                        ["ENJOY_DATE"] = r.EnjoyDate,
                        ["DECREASE_MEMBER"] = r.MemberName,
                        ["DECREASE_AMOUNT"] = r.DecreaseAmount,
                        ["OLD_AMOUNT"] = r.OriginalAmount,
                        ["NEW_AMOUNT"] = r.CurrentAmount,
                        ["OLD_CLASSIFICATION"] = r.OriginalClassified,
                        ["NEW_CLASSIFICATION"] = r.CurrentClassified,
                        ["DECREASE_REASON"] = r.Reason
                    });
                }
                if (rows.Count > 0)
                    pages.Add(new MonthlyFormPageData { Fields = fields, Rows = rows });
                break;
            }
            case "自然减员表":
            {
                var src = await _monthlyReportService.GetDeathRowsAsync(year, month, townSafe, category, ct);
                if (src.IsFailure)
                    return Result.Failure<List<MonthlyFormPageData>>(src.ErrorCode!, src.Message!);
                var fields = new Dictionary<string, string>
                {
                    ["REPORT_TITLE"] = reportTit + (string.IsNullOrWhiteSpace(category) ? "" : $"（{category}）"),
                    ["REPORT_UNIT"] = fillUnit,
                    ["REPORT_DATE"] = fillDate
                };
                var rows = new List<Dictionary<string, string>>();
                foreach (var r in src.Value)
                {
                    rows.Add(new Dictionary<string, string>
                    {
                        ["HEAD_NAME"] = r.HeadName,
                        ["HEAD_ID_CARD"] = r.HeadIdCard,
                        ["HEAD_ADDRESS"] = r.Address,
                        ["DECEASED_NAME"] = r.DeceasedName,
                        ["DECEASED_DATE"] = r.DeceasedDate,
                        ["ENJOY_DATE"] = r.EnjoyDate
                    });
                }
                if (rows.Count > 0)
                    pages.Add(new MonthlyFormPageData { Fields = fields, Rows = rows });
                break;
            }
            case "档案_退出对象兜底情况纠治表":
            {
                var src = await _monthlyReportService.GetExitRectificationRowsAsync(year, month, townSafe, ct);
                if (src.IsFailure)
                    return Result.Failure<List<MonthlyFormPageData>>(src.ErrorCode!, src.Message!);
                var fields = new Dictionary<string, string>
                {
                    ["REPORT_UNIT"] = fillUnit
                };
                var rows = new List<Dictionary<string, string>>();
                var seq = 1;
                foreach (var r in src.Value)
                {
                    rows.Add(new Dictionary<string, string>
                    {
                        ["SEQ"] = (seq++).ToString(),
                        ["NAME"] = r.Name,
                        ["ID_CARD"] = r.IdCard,
                        ["CATEGORY"] = r.Category,
                        ["FAMILY_SIZE"] = r.FamilySize,
                        ["STOP_MONTH"] = r.StopMonth,
                        ["STOP_REASON"] = r.StopReason,
                        ["GRACE_PERIOD"] = r.GracePeriod,
                        ["INTO_DIBAO"] = r.IntoDibao,
                        ["INTO_TEKU"] = r.IntoTeku,
                        ["INTO_EDGE"] = r.IntoEdge,
                        ["INTO_RIGID"] = r.IntoRigid,
                        ["DIRECT_EXIT"] = r.DirectExit
                    });
                }
                if (rows.Count > 0)
                    pages.Add(new MonthlyFormPageData { Fields = fields, Rows = rows });
                break;
            }
            case "临时救助新增汇总表":
            {
                var src = await _monthlyReportService.GetTempReliefSummaryRowsAsync(year, month, townSafe, ct);
                if (src.IsFailure)
                    return Result.Failure<List<MonthlyFormPageData>>(src.ErrorCode!, src.Message!);

                // 报表标注月（B线周期次月），模板标题为"{申报月份}临时救助发放汇总表"
                var reportMonth = $"{titleYear}年{titleMonth}月";

                // 分页：模板表体槽位 1..10，超 10 条拆多页
                const int pageCapacity = 10;
                var all = src.Value;
                for (var pageIndex = 0; pageIndex * pageCapacity < all.Count; pageIndex++)
                {
                    var pageRows = all.Skip(pageIndex * pageCapacity).Take(pageCapacity).ToList();
                    var fields = new Dictionary<string, string>
                    {
                        [FieldKeys.TR_REPORT_MONTH] = reportMonth,
                        ["REPORT_UNIT"] = fillUnit,
                        ["REPORT_DATE"] = fillDate
                    };
                    var rows = new List<Dictionary<string, string>>();
                    var seq = pageIndex * pageCapacity + 1;
                    foreach (var r in pageRows)
                    {
                        rows.Add(new Dictionary<string, string>
                        {
                            [FieldKeys.TR_SEQ] = (seq++).ToString(),
                            [FieldKeys.TR_NAME] = r.Name,
                            [FieldKeys.TR_ID_CARD] = r.IdCard,
                            [FieldKeys.TR_AGE] = r.Age,
                            [FieldKeys.TR_GENDER] = r.Gender,
                            [FieldKeys.TR_FAMILY_SIZE] = r.FamilySize,
                            [FieldKeys.TR_ADDRESS] = r.Address,
                            [FieldKeys.TR_CATEGORY] = r.Category,
                            [FieldKeys.TR_PHONE] = r.Phone,
                            [FieldKeys.TR_AMOUNT] = r.Amount,
                            [FieldKeys.TR_AUDIT_DATE] = r.AuditDate,
                            [FieldKeys.TR_REASON] = r.Reason,
                            [FieldKeys.TR_SELF_PAY] = r.SelfPay,
                            [FieldKeys.TR_BANK_ACCOUNT] = r.BankAccount
                        });
                    }
                    pages.Add(new MonthlyFormPageData { Fields = fields, Rows = rows });
                }
                break;
            }
            case "会议记录":
            case "会议记录_一事一议":
            {
                var isSpecial = baseForm == "会议记录_一事一议";
                var src = await _monthlyReportService.GetMeetingAsync(year, month, townSafe, isSpecial, ct);
                if (src.IsFailure)
                    return Result.Failure<List<MonthlyFormPageData>>(src.ErrorCode!, src.Message!);
                var m = src.Value;
                var lines = m.MemberInfo.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(s => s.Trim())
                                        .Where(s => !string.IsNullOrEmpty(s))
                                        .ToList();
                var fields = new Dictionary<string, string>
                {
                    ["MEETING_TIME"] = m.MeetingTime,
                    ["HOST"] = m.Host,
                    ["RECORDER"] = m.Recorder,
                    // 出席/缺席：填写时按行录入，输出时以顿号连接成一行
                    ["ATTENDEES"] = string.Join("、", SplitLines(m.Attendees)),
                    ["ABSENTEES"] = string.Join("、", SplitLines(m.Absentees)),
                    ["MEMBER_COUNT"] = lines.Count.ToString(),
                    ["APPLY_CATEGORY"] = m.ApplyCategory,
                    ["NEW_MEMBER_INFO"] = m.MemberInfo,
                    // 决议段姓名列表；无新增人员时显式"无"，避免空括号
                    ["RESOLUTION_NAMES"] = string.IsNullOrWhiteSpace(m.MemberNames) ? "无" : m.MemberNames
                };
                // 多余成员行（模板预留 30 行，仅填实际行数）：整段删除，不留空行
                var removeParagraphPlaceholders = new List<string>();
                for (var i = 0; i < lines.Count && i < 30; i++)
                {
                    fields[$"NEW_MEMBER_INFO_{i + 1}"] = lines[i];
                }
                for (var i = lines.Count + 1; i <= 30; i++)
                {
                    removeParagraphPlaceholders.Add($"{{新增成员信息{i}}}");
                }
                pages.Add(new MonthlyFormPageData { Fields = fields, RemoveParagraphPlaceholders = removeParagraphPlaceholders });
                break;
            }
            case "分类施保增加":
            {
                var src = await _monthlyReportService.GetClassifiedSubsidyAddRowsAsync(year, month, ct);
                if (src.IsFailure)
                    return Result.Failure<List<MonthlyFormPageData>>(src.ErrorCode!, src.Message!);

                // 批复机构（乡镇名/单位名，与档案模板15 OrganizationInfoDto 同源）
                var (orgTown, orgUnit) = await ResolveReportOrganizationAsync(ct);

                foreach (var r in src.Value)
                {
                    var fields = new Dictionary<string, string>
                    {
                        ["REPORT_TITLE"] = reportTit,
                        ["REPORT_UNIT"] = fillUnit,
                        ["REPORT_DATE"] = fillDate,
                        ["OPERATOR_UNIT"] = fillUnit,
                        ["APPLICANT_NAME"] = r.ApplicantName,
                        ["APPLICANT_GENDER"] = r.ApplicantGender,
                        ["APPLICANT_BIRTH_DATE"] = ConvertChineseDateToIso(r.ApplicantBirthDate),
                        ["NATIONALITY"] = r.Nationality,
                        ["HEALTH_STATUS"] = r.HealthStatus,
                        ["AUDIT_CLASSIFICATION"] = r.Classification,
                        ["FAMILY_SIZE"] = r.FamilySize,
                        ["CONTACT_PHONE"] = r.Phone,
                        ["FAMILY_ADDRESS"] = r.Address,
                        ["APPLICANT_ID_CARD"] = r.ApplicantIdCard,
                        ["CLASSIFIED_SUBSIDY_TYPE"] = "☑60周岁以上老人 □重病 □重残 □18周岁以下未成年人",
                        ["CLASSIFIED_TOTAL_COUNT"] = $"共计 {r.Count} 人，其中 60周岁以上老人",
                        ["CLASSIFIED_TOTAL_AMOUNT"] = $"经 {orgTown}（{orgUnit}）批准加发 {r.Amount:N2} 元"
                    };
                    // 家庭成员 1~6（不足留空，模板占位符替换为空串）
                    for (var i = 0; i < 6; i++)
                    {
                        var m = i < r.Members.Count ? r.Members[i] : null;
                        var idx = i + 1;
                        fields[$"CLASSIFIED_MEMBER_NAME_{idx}"] = m?.Name ?? "";
                        fields[$"CLASSIFIED_MEMBER_GENDER_{idx}"] = m?.Gender ?? "";
                        fields[$"CLASSIFIED_MEMBER_BIRTH_DATE_{idx}"] = ConvertChineseDateToIso(m?.BirthDate);
                        fields[$"CLASSIFIED_MEMBER_RELATION_{idx}"] = m?.Relation ?? "";
                        fields[$"CLASSIFIED_MEMBER_HEALTH_{idx}"] = m?.Health ?? "";
                        fields[$"CLASSIFIED_MEMBER_INCOME_{idx}"] = m is { AnnualIncome: > 0 } ? m.AnnualIncome.ToString("N2") : "";
                    }
                    pages.Add(new MonthlyFormPageData { Fields = fields, TitleSuffix = $"_{r.ApplicantName}" });
                }
                break;
            }
            case NearRelativeConstants.FormKeyStaffBatch:
            {
                // 近亲属备案（工作人员批量）：每名工作人员一页，对象行 5 行/页，超出自动续页；忽略年月全量
                var entriesResult = await _nearRelativeService.GetAllForPrintAsync(townSafe, ct);
                if (entriesResult.IsFailure)
                    return Result.Failure<List<MonthlyFormPageData>>(entriesResult.ErrorCode!, entriesResult.Message!);

                var auditTime = fillDate;
                foreach (var entry in entriesResult.Value)
                {
                    var links = entry.Links;
                    if (links.Count == 0) continue;
                    var pageCount = (int)Math.Ceiling(links.Count / (double)NearRelativeConstants.StaffPageRowLimit);
                    for (var p = 0; p < pageCount; p++)
                    {
                        var pageLinks = links.Skip(p * NearRelativeConstants.StaffPageRowLimit)
                                             .Take(NearRelativeConstants.StaffPageRowLimit)
                                             .ToList();
                        var fields = NearRelativePrintDataBuilder.BuildStaffBatchPageFields(entry.Staff, pageLinks, auditTime);
                        var suffix = pageCount > 1 ? $"_{entry.Staff.StaffName}_{p + 1}" : $"_{entry.Staff.StaffName}";
                        pages.Add(new MonthlyFormPageData { Fields = fields, TitleSuffix = suffix });
                    }
                }
                break;
            }
            case NearRelativeConstants.FormKeySummary:
            {
                // 近亲属备案汇总：全量对 9 组/页，超出自动续页；忽略年月全量
                var entriesResult = await _nearRelativeService.GetAllForPrintAsync(townSafe, ct);
                if (entriesResult.IsFailure)
                    return Result.Failure<List<MonthlyFormPageData>>(entriesResult.ErrorCode!, entriesResult.Message!);

                var pairs = NearRelativePrintDataBuilder.FlattenPairs(entriesResult.Value);
                if (pairs.Count == 0) break;

                var reportDate = $"{titleYear}年{titleMonth}月";
                var pageCount = (int)Math.Ceiling(pairs.Count / (double)NearRelativeConstants.SummaryPageRowLimit);
                for (var p = 0; p < pageCount; p++)
                {
                    var pagePairs = pairs.Skip(p * NearRelativeConstants.SummaryPageRowLimit)
                                         .Take(NearRelativeConstants.SummaryPageRowLimit)
                                         .ToList();
                    var fields = NearRelativePrintDataBuilder.BuildSummaryPageFields(pagePairs, reportDate, fillUnit);
                    pages.Add(new MonthlyFormPageData { Fields = fields, TitleSuffix = pageCount > 1 ? $"_{p + 1}" : string.Empty });
                }
                break;
            }
            default:
                return Result.Failure<List<MonthlyFormPageData>>(ErrorCodes.NOT_FOUND, "未知月报表表单");
        }

        if (pages.Count == 0)
            return Result.Failure<List<MonthlyFormPageData>>(ErrorCodes.NOT_FOUND, "月报表表单无数据");

        return Result.Success(pages);
    }

    /// <summary>
    /// 按行拆分文本并去空行（出席/缺席等换行录入 → 顿号连接用）
    /// </summary>
    private static List<string> SplitLines(string? text)
        => (text ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

    /// <summary>
    /// 拆分 "基础名_分类"（分类: 最低生活保障/最低生活保障边缘家庭/特困人员/刚性支出困难家庭）；无分类后缀则原样返回
    /// </summary>
    private static (string BaseForm, string? Category) SplitMonthlyFormKey(string formKey)
    {
        var tail = formKey.LastIndexOf('_');
        if (tail > 0)
        {
            var suffix = formKey[(tail + 1)..];
            if (suffix is "最低生活保障" or "最低生活保障边缘家庭" or "特困人员" or "刚性支出困难家庭")
                return (formKey[..tail], suffix);
        }
        return (formKey, null);
    }

    /// <summary>
    /// 填报单位：当前机构名称；机构缺失时回退乡镇参数
    /// </summary>
    private async Task<string> ResolveReportUnitAsync(string town, CancellationToken ct)
    {
        var orgId = App.CurrentUserOrganizationId;
        if (orgId is > 0)
        {
            var org = await _db.QuerySingleAsync<OrgNameRow>(
                "SELECT name FROM nc_sys_organizations WHERE id = $1", ct, orgId.Value);
            if (org.IsSuccess && org.Value != null && !string.IsNullOrWhiteSpace(org.Value.Name))
                return org.Value.Name;
        }
        return string.IsNullOrEmpty(town) ? "全部" : town;
    }

    /// <summary>
    /// 分类施保加发批复机构（与档案模板15 OrganizationInfoDto 同源）：
    /// 乡镇名 = 当前机构所属乡镇（town_name），缺失时回退父级机构名；单位名 = 当前机构名称。
    /// </summary>
    private async Task<(string Town, string UnitName)> ResolveReportOrganizationAsync(CancellationToken ct)
    {
        var orgId = App.CurrentUserOrganizationId;
        if (orgId is not > 0)
            return ("全部", string.Empty);

        var org = await _db.QuerySingleAsync<ReportOrgRow>(
            @"SELECT o.name, rt.town_name, p.name AS parent_name
              FROM nc_sys_organizations o
              LEFT JOIN nc_regions_towns rt ON o.town_id = rt.id
              LEFT JOIN nc_sys_organizations p ON o.parent_id = p.id
              WHERE o.id = $1", ct, orgId.Value);
        if (org.IsFailure || org.Value == null)
            return ("全部", string.Empty);

        var town = !string.IsNullOrWhiteSpace(org.Value.TownName)
            ? org.Value.TownName
            : (!string.IsNullOrWhiteSpace(org.Value.ParentName) ? org.Value.ParentName : "全部");
        return (town, org.Value.Name ?? string.Empty);
    }

    /// <summary>
    /// 中文日期（yyyy年MM月dd日）→ ISO（yyyy-MM-dd）；解析失败回退原值（与档案模板15 出生日期格式一致）
    /// </summary>
    private static string ConvertChineseDateToIso(string? chineseDate)
    {
        if (string.IsNullOrWhiteSpace(chineseDate)) return string.Empty;
        var match = global::System.Text.RegularExpressions.Regex.Match(chineseDate,
            @"^(\d{4})年(\d{1,2})月(\d{1,2})日$");
        if (!match.Success) return chineseDate;
        return $"{match.Groups[1].Value.PadLeft(4, '0')}-{match.Groups[2].Value.PadLeft(2, '0')}-{match.Groups[3].Value.PadLeft(2, '0')}";
    }

    /// <summary>
    /// 解析"月报表"分类下的模板 ID（如 月报_新增救助明细）
    /// 最低生活保障/最低生活保障边缘家庭有专用模板（新增、停保），特困人员/刚性支出困难家庭复用最低生活保障版式
    /// </summary>
    private async Task<Result<long>> ResolveMonthlyTemplateIdAsync(string baseForm, string? category, CancellationToken ct)
    {
        var name = baseForm switch
        {
            "新增救助明细" => category == "最低生活保障边缘家庭" ? "月报_新增救助明细_最低生活保障边缘家庭" : "月报_新增救助明细",
            "停保汇总表" => category == "最低生活保障边缘家庭" ? "月报_停保汇总表_最低生活保障边缘家庭" : "月报_停保汇总表",
            "保障金增发表" => "月报_保障金增发表",
            "保障金减发表" => "月报_保障金减发表",
            "施保金减发" => "月报_施保金减发",
            "分类施保增加" => "月报_分类施保增发",
            "自然减员表" => "月报_自然减员表",
            "会议记录" => "月报_会议记录",
            "会议记录_一事一议" => "月报_会议记录_一事一议",
            "临时救助新增汇总表" => "月报_临时救助新增汇总表",
            _ => $"月报_{baseForm}"
        };
        var templates = await _templateService.GetByCategoriesAsync(new[] { "月报表" }, ct);
        var template = templates.FirstOrDefault(t =>
            string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
        if (template == null)
            return Result.Failure<long>(ErrorCodes.NOT_FOUND, $"月报模板不存在: {name}");
        return Result.Success(template.Id);
    }

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
        var template = await _templateService.GetByNameAsync("普惠高龄津贴调整备案表", ct);
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
            var config = await GetTemplateConfigAsync(templateId, ct);

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
            var config = await GetTemplateConfigAsync(templateId, ct);
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

    private async Task<TemplateConfig?> GetTemplateConfigAsync(long templateId, CancellationToken ct)
    {
        var template = await _templateService.GetByIdAsync(templateId, ct);
        if (template == null || string.IsNullOrEmpty(template.ConfigJson)) return null;

        try
        {
            return TemplateConfig.FromJson(template.ConfigJson);
        }
        catch (Exception ex)
        {
            LogError($"模板配置解析失败: TemplateId={templateId}, Error={ex.Message}");
            return null;
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
            var templates = await _templateService.GetAllAsync(ct);

            var result = templates.Select(t => new PrintTemplate
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
            return Result.Success(new List<PrintTemplate>());
        }
    }

    public ITemplateEngineFactory GetEngineFactory()
    {
        return _engineFactory;
    }

    private async Task<long> ResolveTemplateIdByNameAsync(string templateName, CancellationToken ct)
    {
        var templates = await _templateService.GetAllAsync(ct);
        var template = templates.FirstOrDefault(t =>
            string.Equals(t.Name, templateName, StringComparison.OrdinalIgnoreCase));
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

    private class ChangeRecordPrintData
    {
        public long Id { get; set; }
        public string ApplicantName { get; set; } = string.Empty;
        public string ChangeType { get; set; } = string.Empty;
        public string ChangeReason { get; set; } = string.Empty;
        public string BeforeValue { get; set; } = string.Empty;
        public string AfterValue { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}
