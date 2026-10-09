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
public partial class PrintService : BaseService, IPrintService
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
        var configResult = await GetTemplateConfigAsync(templateId, ct);
        if (configResult.IsFailure)
            return Result.Failure<bool>(configResult.ErrorCode!, configResult.Message!);

        var config = configResult.Value;
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

}
