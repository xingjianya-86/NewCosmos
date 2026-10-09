using System.Globalization;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.Templates;
using NewCosmos.Services.Utilities;

namespace NewCosmos.Services.Domain.ChangeManagement;

/// <summary>
/// 社会救助对象动态管理记录服务：
/// 变更人群每户一份《社会救助对象动态管理记录》档案（模板：模板_社会救助对象动态管理记录）。
/// 字段口径见 Constants\FieldKeys.cs DM_* 常量注释：
/// - 收入列全部年值口径：收入=主表年总收入，净收入=收入−刚性年值−就业成本（就业成本为空按 0，显示"-"）；
/// - 货币财产=现金+银行存款+有价证券+商业保险金额，人均=总额÷人数（一次舍入到分）；
/// - 赡养义务人取前 3 名，职业=就业状况字典显示，赡养费=年值；
/// - 动态管理周期固定"一年期"；乡镇（街道）意见=入户调查结论。
/// 档案链归一：变更记录挂在停旧建新的旧档案上，打印前必须归一到链尾现役档案。
/// 打印留痕：写入 nc_biz_print_records（BusinessType=DynamicManagementRecord），单户存 pdf_data、批量存路径。
/// </summary>
public interface IDynamicManagementRecordService
{
    /// <summary>装配单户 38 个模板字段</summary>
    Task<Result<Dictionary<string, string>>> BuildFieldsAsync(long applicationId, CancellationToken ct = default);

    /// <summary>某年某月有变更记录的档案 ID（按档案链归一到现役、去重、剔除已删除档案）</summary>
    Task<Result<List<long>>> GetMonthlyChangedApplicationIdsAsync(int? year = null, int? month = null, CancellationToken ct = default);

    /// <summary>按日期区间(含端点)有变更记录的档案 ID（B线周期区间用；档案链归一到现役、去重、剔除已删除档案）</summary>
    Task<Result<List<long>>> GetChangedApplicationIdsInRangeAsync(DateTime fromInclusive, DateTime toInclusive, CancellationToken ct = default);

    /// <summary>单户导出 PDF，返回文件路径（打印留痕含 pdf_data，供原样补打）</summary>
    Task<Result<string>> ExportSingleAsync(long applicationId, CancellationToken ct = default);

    /// <summary>单户导出 PDF：normalizeToLatest=true 归一到现役档案，false 按所选档案版本原样生成</summary>
    Task<Result<string>> ExportSingleAsync(long applicationId, bool normalizeToLatest, CancellationToken ct = default);

    /// <summary>变更档案搜索（不归一，逐档案返回：含历史旧档案与现役档案，供新旧选择）</summary>
    Task<Result<List<ChangedArchiveSummary>>> SearchChangedArchivesRawAsync(string keyword, int limit = 20, CancellationToken ct = default);

    /// <summary>某月变更人群批量导出（逐户渲染后合并为一份 PDF），返回文件路径（留痕存路径）</summary>
    Task<Result<string>> ExportMonthlyBatchAsync(int? year = null, int? month = null, CancellationToken ct = default);

    /// <summary>按日期区间(含端点)变更人群批量导出（逐户渲染合并为一份 PDF），返回文件路径（B线周期区间用）</summary>
    Task<Result<string>> ExportRangeBatchAsync(DateTime fromInclusive, DateTime toInclusive, CancellationToken ct = default);

    /// <summary>按档案ID逐户按现势数据渲染并合并为一份 PDF（单户即单份），用于页内预览</summary>
    Task<Result<byte[]>> RenderMergedPdfAsync(IReadOnlyList<long> applicationIds, CancellationToken ct = default);

    /// <summary>按档案ID逐户打印（每户生成源文件+PDF并送指定打印机，写留痕），返回成功数与失败明细</summary>
    Task<Result<(int SuccessCount, List<(string Name, string Error)> Failed)>> PrintAsync(
        IReadOnlyList<long> applicationIds, string printerName, int copies,
        Action<int, int, string>? progress = null, CancellationToken ct = default);

    /// <summary>按关键词（姓名/身份证）搜索有变更记录的现役档案（统一补打页动态管理域用）</summary>
    Task<Result<List<ChangedArchiveSummary>>> SearchChangedArchivesAsync(string keyword, int limit = 20, CancellationToken ct = default);

    /// <summary>某年某月变更人群摘要（档案链归一到现役，统一补打页批量预览用）</summary>
    Task<Result<List<ChangedArchiveSummary>>> SearchMonthlyChangedArchivesAsync(int? year = null, int? month = null, CancellationToken ct = default);

    /// <summary>档案链归一：沿 original_application_id 递归到链尾现役档案（链尾已删 → 显式失败）</summary>
    Task<Result<long>> NormalizeToLatestApplicationIdAsync(long applicationId, CancellationToken ct = default);
}

/// <summary>
/// 有变更记录的现役档案摘要（统一补打页动态管理域分支/列表）。
/// 注意：必须是带无参构造的普通类型——数据库行映射 MapRow&lt;T&gt; 依赖 Activator.CreateInstance&lt;T&gt;()，
/// 位置参数 record 无无参构造会导致 QueryAsync 抛 MissingMethodException。
/// </summary>
public class ChangedArchiveSummary
{
    public long ApplicationId { get; set; }
    public string HeadName { get; set; } = string.Empty;
    public string IdCard { get; set; } = string.Empty;
    public string ApplicationNo { get; set; } = string.Empty;
    public string Classification { get; set; } = string.Empty;

    /// <summary>档案状态（Stopped=历史档案，其余=现状档案；供补打中心旧/新选择）</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>档案链类型（chain_type，用于补打中心版本标签）</summary>
    public string ChainType { get; set; } = string.Empty;

    public DateTime LastChangeDate { get; set; }
}

public class DynamicManagementRecordService : BaseService, IDynamicManagementRecordService
{
    protected override string ServiceName => "DynamicManagementRecordService";

    private const string TemplateName = "模板_社会救助对象动态管理记录";
    private const string OutputCategory = "动态管理档案";
    private const string CycleText = "一年期";

    /// <summary>打印留痕业务类型键（统一补打页/打印记录查询共用）</summary>
    public const string BusinessTypeKey = "DynamicManagementRecord";

    private readonly IDatabaseService _db;
    private readonly IApplicationService _applicationService;
    private readonly ISupporterService _supporterService;
    private readonly IEconomicDetailService _economicDetailService;
    private readonly IHouseholdSurveyService _householdSurveyService;
    private readonly IOrganizationService _organizationService;
    private readonly ITemplateService _templateService;
    private readonly Services.Domain.Reporting.IPrintService _printService;
    private readonly Services.Domain.Printing.IPrintExecuteService _printExecuteService;
    private readonly IDictCacheService _dictCacheService;
    private readonly Services.Domain.Printing.IPrintRecordService _printRecordService;

    public DynamicManagementRecordService(
        IDatabaseService db,
        IApplicationService applicationService,
        ISupporterService supporterService,
        IEconomicDetailService economicDetailService,
        IHouseholdSurveyService householdSurveyService,
        IOrganizationService organizationService,
        ITemplateService templateService,
        Services.Domain.Reporting.IPrintService printService,
        Services.Domain.Printing.IPrintExecuteService printExecuteService,
        IDictCacheService dictCacheService,
        Services.Domain.Printing.IPrintRecordService printRecordService,
        ILoggerService logger) : base(logger)
    {
        _db = db;
        _applicationService = applicationService;
        _supporterService = supporterService;
        _economicDetailService = economicDetailService;
        _householdSurveyService = householdSurveyService;
        _organizationService = organizationService;
        _templateService = templateService;
        _printService = printService;
        _printExecuteService = printExecuteService;
        _dictCacheService = dictCacheService;
        _printRecordService = printRecordService;
    }

    public async Task<Result<Dictionary<string, string>>> BuildFieldsAsync(long applicationId, CancellationToken ct = default)
    {
        try
        {
            // 1. 档案主表
            var appResult = await _applicationService.GetByIdAsync(applicationId, ct);
            if (appResult.IsFailure)
                return Result.Failure<Dictionary<string, string>>(appResult.ErrorCode!, appResult.Message ?? "档案查询失败");
            var app = appResult.Value;
            if (app == null)
                return Result.Failure<Dictionary<string, string>>(ErrorCodes.APPLICATION_NOT_FOUND, "档案不存在或已删除");

            // 2. 共同生活家庭成员人数（口径同 ChangeViewModel.LoadFamilyMemberSummaryAsync：
            //    剔除 Support 类别后的成员行数 + 无户主记录时补 1——户主可能只存在于主表而无成员行）
            var sizeResult = await _db.ExecuteScalarAsync<long>(
                @"SELECT COUNT(*) + CASE WHEN COUNT(*) FILTER (WHERE is_applicant = TRUE
                                              OR relationship_to_head = 'Head'
                                              OR member_category IS NULL OR member_category = '') > 0
                                          THEN 0 ELSE 1 END
                  FROM nc_biz_family_members
                  WHERE application_id = $1 AND deleted_at IS NULL
                    AND (member_category IS NULL OR member_category <> 'Support')",
                ct, applicationId);
            if (sizeResult.IsFailure)
                return Result.Failure<Dictionary<string, string>>(sizeResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    sizeResult.Message ?? "家庭成员人数统计失败");
            var familySize = (int)sizeResult.Value;

            // 3. 赡养（扶、抚）义务人
            var supportersResult = await _supporterService.GetByApplicationIdAsync(applicationId, ct);
            if (supportersResult.IsFailure)
                return Result.Failure<Dictionary<string, string>>(supportersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    supportersResult.Message ?? "赡养抚养扶养人查询失败");
            var supporters = (supportersResult.Value ?? new List<Supporter>()).Take(3).ToList();

            // 4. 经济明细（加载失败必须显式失败——空集会被当作"无财产"写入档案）
            var econResult = await _economicDetailService.LoadAllAsync(applicationId, ct);
            if (econResult.IsFailure)
                return Result.Failure<Dictionary<string, string>>(econResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    econResult.Message ?? "经济明细加载失败");
            var econ = econResult.Value;

            // 5. 就业成本年值汇总（表当前无录入入口，恒为空 → 显示"-"、参与计算按 0）
            var empCostResult = await _db.ExecuteScalarAsync<decimal>(
                @"SELECT COALESCE(SUM(annual_amount), 0) FROM nc_biz_employment_costs
                  WHERE application_id = $1 AND deleted_at IS NULL",
                ct, applicationId);
            if (empCostResult.IsFailure)
                return Result.Failure<Dictionary<string, string>>(empCostResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    empCostResult.Message ?? "就业成本查询失败");
            var employmentCost = empCostResult.Value;

            // 6. 农机具（船舶/农机具列；系统无船舶台账，农机具兜底）
            var machineryResult = await _db.QueryAsync<dynamic>(
                @"SELECT machinery_type, brand, model, quantity FROM nc_biz_machineries
                  WHERE application_id = $1 AND deleted_at IS NULL
                  ORDER BY id",
                ct, applicationId);
            if (machineryResult.IsFailure)
                return Result.Failure<Dictionary<string, string>>(machineryResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    machineryResult.Message ?? "农机具查询失败");

            // 7. 近亲属备案（links 按 application_id 关联 staffs）
            var nearRelativeResult = await _db.QueryAsync<dynamic>(
                @"SELECT s.staff_name, l.relation, s.work_unit
                  FROM nc_biz_near_relative_links l
                  JOIN nc_biz_near_relative_staffs s ON s.id = l.staff_id AND s.deleted_at IS NULL
                  WHERE l.application_id = $1 AND l.deleted_at IS NULL
                  ORDER BY l.id",
                ct, applicationId);
            if (nearRelativeResult.IsFailure)
                return Result.Failure<Dictionary<string, string>>(nearRelativeResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    nearRelativeResult.Message ?? "近亲属备案查询失败");

            // 8. 入户调查结论（乡镇街道意见）
            var surveyResult = await _householdSurveyService.GetByApplicationIdAsync(applicationId, ct);
            var townOpinion = surveyResult.IsSuccess && surveyResult.Value != null
                ? surveyResult.Value.SurveyConclusion ?? ""
                : "";

            // 9. 填报单位（当前登录用户所在机构）
            var unitName = await ResolveCurrentUnitAsync(ct);

            // 10. 金额口径（§七·五：年值权威，一次舍入到分）
            var familyIncome = Math.Round(app.TotalAnnualIncome, 2);
            var rigidAnnual = Math.Round(app.RigidExpenditure * 12m, 2);
            var netIncome = Math.Round(familyIncome - rigidAnnual - employmentCost, 2);
            var perCapita = Math.Round(app.PerCapitaAnnualIncome, 2);

            // 货币财产 = 现金 + 银行存款 + 有价证券 + 商业保险
            var moneyAssets = econ.FinancialAssets?.Sum(f =>
                f.CashAmount + f.BankDepositAmount + f.SecuritiesAmount + f.CommercialInsuranceAmount) ?? 0m;
            var moneyAssetsTotal = Math.Round(moneyAssets, 2);
            var moneyAssetsPerCapita = familySize > 0 ? Math.Round(moneyAssetsTotal / familySize, 2) : 0m;

            var fields = new Dictionary<string, string>
            {
                [FieldKeys.DM_CURRENT_UNIT] = unitName,
                [FieldKeys.DM_REPORT_YEAR] = DateTime.Now.Year.ToString(CultureInfo.InvariantCulture),
                [FieldKeys.DM_CURRENT_USER] = App.CurrentUserFullName ?? "",
                [FieldKeys.DM_CURRENT_DATE] = DateTime.Now.ToString("yyyy年M月d日"),

                [FieldKeys.DM_HEAD_NAME] = app.ApplicantName ?? "",
                [FieldKeys.DM_FAMILY_SIZE] = familySize.ToString(CultureInfo.InvariantCulture),
                [FieldKeys.DM_HEAD_HUKOU] = app.HukouAddress ?? "",
                [FieldKeys.DM_HEAD_RESIDENCE] = $"{app.Town}{app.Community}{app.Address}",
                [FieldKeys.DM_CATEGORY] = ClassificationConstants.ConvertToFullName(app.ClassificationResult ?? ""),
                [FieldKeys.DM_CYCLE] = CycleText,

                [FieldKeys.DM_FAMILY_INCOME] = FormatMoney(familyIncome),
                [FieldKeys.DM_RIGID_EXPENDITURE] = FormatMoney(rigidAnnual),
                [FieldKeys.DM_EMPLOYMENT_COST] = employmentCost > 0 ? FormatMoney(employmentCost) : "-",
                [FieldKeys.DM_NET_INCOME] = FormatMoney(netIncome),
                [FieldKeys.DM_PER_CAPITA_INCOME] = FormatMoney(perCapita),
                [FieldKeys.DM_MONEY_ASSET_TOTAL] = FormatMoney(moneyAssetsTotal),
                [FieldKeys.DM_MONEY_ASSET_PER_CAPITA] = FormatMoney(moneyAssetsPerCapita),
                [FieldKeys.DM_REAL_ESTATE] = BuildRealEstateText(econ),
                [FieldKeys.DM_VEHICLES] = BuildVehicleText(econ),
                [FieldKeys.DM_MACHINERY] = BuildMachineryText(machineryResult.Value),

                [FieldKeys.DM_NEAR_RELATIVE] = BuildNearRelativeText(nearRelativeResult.Value),
                [FieldKeys.DM_OTHER_SITUATION] = "",
                [FieldKeys.DM_TOWN_OPINION] = townOpinion,
            };

            // 赡养人 3 槽位
            FillSupporterSlots(fields, supporters);
            return Result.Success(fields);
        }
        catch (Exception ex)
        {
            LogException(ex, "装配动态管理记录字段");
            return Result.FromException<Dictionary<string, string>>(ex);
        }
    }

    public Task<Result<List<long>>> GetMonthlyChangedApplicationIdsAsync(int? year = null, int? month = null, CancellationToken ct = default)
    {
        var now = DateTime.Now;
        var start = new DateTime(year ?? now.Year, month ?? now.Month, 1);
        return GetChangedApplicationIdsInRangeAsync(start, start.AddMonths(1).AddDays(-1), ct);
    }

    public async Task<Result<List<long>>> GetChangedApplicationIdsInRangeAsync(DateTime fromInclusive, DateTime toInclusive, CancellationToken ct = default)
    {
        try
        {
            // 变更人群窗口：change_date ∈ [起始日, 结束日+1)。
            // 档案链归一：经济复核/户主死亡的变更记录挂在已停止的旧档案上（停旧建新），
            // 递归 CTE 沿 original_application_id 找链尾（MAX(id)，新档案 id 严格递增）并只保留存活链尾。
            var rangeStart = fromInclusive.Date;
            var rangeEndExclusive = toInclusive.Date.AddDays(1);

            var result = await _db.QueryAsync<long>(
                @"WITH RECURSIVE base AS (
                      SELECT DISTINCT cr.application_id AS aid
                      FROM nc_biz_change_records cr
                      JOIN nc_biz_applications o ON o.id = cr.application_id AND o.deleted_at IS NULL
                      WHERE cr.deleted_at IS NULL
                        AND cr.change_date >= $1::date AND cr.change_date < $2::date
                  ),
                  chain AS (
                      SELECT aid AS start_id, aid AS cur_id FROM base
                      UNION ALL
                      SELECT c.start_id, a.id
                      FROM chain c
                      JOIN nc_biz_applications a ON a.original_application_id = c.cur_id
                  )
                  SELECT a.id
                  FROM (SELECT start_id, MAX(cur_id) AS tail_id FROM chain GROUP BY start_id) t
                  JOIN nc_biz_applications a ON a.id = t.tail_id AND a.deleted_at IS NULL
                  ORDER BY a.id",
                ct, rangeStart, rangeEndExclusive);
            if (result.IsFailure)
                return Result.Failure<List<long>>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    result.Message ?? "变更人群查询失败");
            return Result.Success(result.Value ?? new List<long>());
        }
        catch (Exception ex)
        {
            LogException(ex, "查询变更人群");
            return Result.FromException<List<long>>(ex);
        }
    }

    public Task<Result<string>> ExportSingleAsync(long applicationId, CancellationToken ct = default)
        => ExportSingleCoreAsync(applicationId, normalizeToLatest: true, ct);

    public Task<Result<string>> ExportSingleAsync(long applicationId, bool normalizeToLatest, CancellationToken ct = default)
        => ExportSingleCoreAsync(applicationId, normalizeToLatest, ct);

    private async Task<Result<string>> ExportSingleCoreAsync(long applicationId, bool normalizeToLatest, CancellationToken ct)
    {
        LogInfo($"导出动态管理记录: ApplicationId={applicationId}, Normalize={normalizeToLatest}");

        var latestId = applicationId;
        if (normalizeToLatest)
        {
            // 档案链归一：传入的可能是变更链上的旧档案，归一到链尾现役档案
            var normalizeResult = await NormalizeToLatestApplicationIdAsync(applicationId, ct);
            if (normalizeResult.IsFailure) return Result.Failure<string>(normalizeResult.ErrorCode!, normalizeResult.Message!);
            latestId = normalizeResult.Value;
            if (latestId != applicationId)
                LogInfo($"档案链归一: {applicationId} → {latestId}");
        }
        else
        {
            // 按所选档案版本原样生成：校验档案存在且未删
            var aliveResult = await _db.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL", ct, applicationId);
            if (aliveResult.IsFailure || aliveResult.Value == 0)
                return Result.Failure<string>(ErrorCodes.APPLICATION_NOT_FOUND, $"档案 {applicationId} 不存在或已删除");
        }

        var fieldsResult = await BuildFieldsAsync(latestId, ct);
        if (fieldsResult.IsFailure) return Result.Failure<string>(fieldsResult.ErrorCode!, fieldsResult.Message!);

        var templateIdResult = await ResolveTemplateIdAsync(ct);
        if (templateIdResult.IsFailure) return Result.Failure<string>(templateIdResult.ErrorCode!, templateIdResult.Message!);

        var pdfResult = await RenderPdfAsync(fieldsResult.Value, ct);
        if (pdfResult.IsFailure) return Result.Failure<string>(pdfResult.ErrorCode!, pdfResult.Message!);

        var path = OutputPathHelper.GetFilePath(OutputCategory,
            MaskNameForPath(fieldsResult.Value.GetValueOrDefault(FieldKeys.DM_HEAD_NAME)),
            latestId.ToString(CultureInfo.InvariantCulture),
            "社会救助对象动态管理记录", ".pdf");
        var writeResult = WriteFile(path, pdfResult.Value);
        if (writeResult.IsFailure) return writeResult;

        // 打印留痕：单户存 pdf_data（原样补打不依赖文件不被删）
        var batchNo = $"DMR{DateTime.Now:yyyyMMddHHmmssfff}-{latestId}";
        await WritePrintRecordAsync(latestId, templateIdResult.Value, pdfResult.Value, path, batchNo,
            "单户导出", storePdfData: true, ct);

        Logger.LogBusiness("导出动态管理记录", ("ApplicationId", latestId), ("OriginalId", applicationId), ("File", path));
        return Result.Success(path);
    }

    public Task<Result<string>> ExportMonthlyBatchAsync(int? year = null, int? month = null, CancellationToken ct = default)
    {
        var now = DateTime.Now;
        var start = new DateTime(year ?? now.Year, month ?? now.Month, 1);
        return ExportRangeBatchAsync(start, start.AddMonths(1).AddDays(-1), ct);
    }

    public async Task<Result<string>> ExportRangeBatchAsync(DateTime fromInclusive, DateTime toInclusive, CancellationToken ct = default)
    {
        var rangeLabel = $"{fromInclusive:yyyy-MM-dd}至{toInclusive:yyyy-MM-dd}";
        LogInfo($"批量导出动态管理记录: {rangeLabel}");

        var idsResult = await GetChangedApplicationIdsInRangeAsync(fromInclusive, toInclusive, ct);
        if (idsResult.IsFailure) return Result.Failure<string>(idsResult.ErrorCode!, idsResult.Message!);
        var ids = idsResult.Value;
        if (ids.Count == 0)
            return Result.Failure<string>(ErrorCodes.NOT_FOUND, $"{rangeLabel}无变更档案，无可打印的动态管理记录");

        var templateIdResult = await ResolveTemplateIdAsync(ct);
        if (templateIdResult.IsFailure) return Result.Failure<string>(templateIdResult.ErrorCode!, templateIdResult.Message!);

        var batchNo = $"DMR{DateTime.Now:yyyyMMddHHmmssfff}-B{ids.Count}";
        var pdfs = new List<byte[]>();
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            var fieldsResult = await BuildFieldsAsync(id, ct);
            if (fieldsResult.IsFailure)
                return Result.Failure<string>(fieldsResult.ErrorCode ?? ErrorCodes.DOCUMENT_GENERATION_FAILED,
                    $"档案 {id} 字段装配失败: {fieldsResult.Message}");
            var pdfResult = await RenderPdfAsync(fieldsResult.Value, ct);
            if (pdfResult.IsFailure)
                return Result.Failure<string>(pdfResult.ErrorCode ?? ErrorCodes.DOCUMENT_GENERATION_FAILED,
                    $"档案 {id} 渲染失败: {pdfResult.Message}");
            pdfs.Add(pdfResult.Value);
        }
        LogInfo($"批量渲染完成: {pdfs.Count}/{ids.Count} 份（批量 {batchNo}）");

        var merged = pdfs.Count == 1 ? pdfs[0] : MergePdfBytes(pdfs);
        var path = OutputPathHelper.GetFilePath(OutputCategory, "变更人群批量",
            $"{fromInclusive:yyyyMMdd}-{toInclusive:yyyyMMdd}_{ids.Count}户", "社会救助对象动态管理记录_批量", ".pdf");
        var writeResult = WriteFile(path, merged);
        if (writeResult.IsFailure) return writeResult;

        // 打印留痕：批量逐户一条（只存合并文件路径，防 BYTEA 膨胀），batch_no 标识本批
        foreach (var id in ids)
        {
            await WritePrintRecordAsync(id, templateIdResult.Value, null, path, batchNo,
                $"批量导出（{rangeLabel}，{ids.Count}户）", storePdfData: false, ct);
        }

        Logger.LogBusiness("批量导出动态管理记录", ("Count", ids.Count), ("Range", rangeLabel), ("File", path));
        return Result.Success(path);
    }

    /// <summary>按档案ID逐户渲染并合并为一份 PDF（单户即单份），供页内预览。</summary>
    public async Task<Result<byte[]>> RenderMergedPdfAsync(IReadOnlyList<long> applicationIds, CancellationToken ct = default)
    {
        if (applicationIds == null || applicationIds.Count == 0)
            return Result.Failure<byte[]>(ErrorCodes.VALIDATION_FAILED, "未选择档案");

        try
        {
            var pdfs = new List<byte[]>();
            foreach (var id in applicationIds)
            {
                ct.ThrowIfCancellationRequested();

                var normalizeResult = await NormalizeToLatestApplicationIdAsync(id, ct);
                if (normalizeResult.IsFailure)
                    return Result.Failure<byte[]>(normalizeResult.ErrorCode!, normalizeResult.Message!);

                var fieldsResult = await BuildFieldsAsync(normalizeResult.Value, ct);
                if (fieldsResult.IsFailure)
                    return Result.Failure<byte[]>(fieldsResult.ErrorCode!, fieldsResult.Message!);

                var pdfResult = await RenderPdfAsync(fieldsResult.Value, ct);
                if (pdfResult.IsFailure)
                    return Result.Failure<byte[]>(pdfResult.ErrorCode!, pdfResult.Message!);

                pdfs.Add(pdfResult.Value);
            }

            return Result.Success(pdfs.Count == 1 ? pdfs[0] : MergePdfBytes(pdfs));
        }
        catch (OperationCanceledException)
        {
            return Result.Failure<byte[]>(ErrorCodes.CANCELLED, "预览生成已取消");
        }
        catch (Exception ex)
        {
            LogException(ex, "生成动态管理记录预览");
            return Result.FromException<byte[]>(ex);
        }
    }

    /// <summary>按档案ID逐户打印（每户生成源文件+PDF并送指定打印机，写留痕）。</summary>
    public async Task<Result<(int SuccessCount, List<(string Name, string Error)> Failed)>> PrintAsync(
        IReadOnlyList<long> applicationIds, string printerName, int copies,
        Action<int, int, string>? progress = null, CancellationToken ct = default)
    {
        if (applicationIds == null || applicationIds.Count == 0)
            return Result.Failure<(int SuccessCount, List<(string Name, string Error)> Failed)>(
                ErrorCodes.VALIDATION_FAILED, "未选择档案");

        var templateIdResult = await ResolveTemplateIdAsync(ct);
        if (templateIdResult.IsFailure)
            return Result.Failure<(int SuccessCount, List<(string Name, string Error)> Failed)>(
                templateIdResult.ErrorCode!, templateIdResult.Message!);

        var batchNo = $"DMR{DateTime.Now:yyyyMMddHHmmssfff}-B{applicationIds.Count}";
        var successCount = 0;
        var failed = new List<(string Name, string Error)>();

        for (var i = 0; i < applicationIds.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var id = applicationIds[i];
            try
            {
                var normalizeResult = await NormalizeToLatestApplicationIdAsync(id, ct);
                if (normalizeResult.IsFailure)
                {
                    failed.Add((id.ToString(CultureInfo.InvariantCulture), normalizeResult.Message ?? "档案归一失败"));
                    continue;
                }
                var latestId = normalizeResult.Value;

                var appResult = await _applicationService.GetByIdAsync(latestId, ct);
                var name = appResult.IsSuccess && appResult.Value != null ? appResult.Value.ApplicantName ?? "" : "";
                var idCard = appResult.IsSuccess && appResult.Value != null ? appResult.Value.ApplicantIdCard ?? "" : "";

                var fieldsResult = await BuildFieldsAsync(latestId, ct);
                if (fieldsResult.IsFailure)
                {
                    failed.Add((name, fieldsResult.Message ?? "字段装配失败"));
                    continue;
                }

                progress?.Invoke(i + 1, applicationIds.Count, name);

                var printResult = await _printExecuteService.ExecutePrintAsync(
                    templateIdResult.Value, TemplateName, BusinessTypeKey, latestId, batchNo,
                    fieldsResult.Value, new List<Dictionary<string, string>>(),
                    name, idCard, printerName, Math.Max(1, copies), false, printToPrinter: true, ct);

                if (printResult.IsSuccess) successCount++;
                else failed.Add((string.IsNullOrEmpty(name) ? latestId.ToString(CultureInfo.InvariantCulture) : name,
                    printResult.Message ?? "打印失败"));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                LogException(ex, $"打印动态管理记录 {id}");
                failed.Add((id.ToString(CultureInfo.InvariantCulture), ex.Message));
            }
        }

        Logger.LogBusiness("批量打印动态管理记录",
            ("Count", applicationIds.Count), ("Success", successCount), ("Failed", failed.Count), ("Printer", printerName ?? "默认"));
        return Result.Success((successCount, failed));
    }

    public async Task<Result<List<ChangedArchiveSummary>>> SearchChangedArchivesAsync(string keyword, int limit = 20, CancellationToken ct = default)
    {
        try
        {
            var trimmedKeyword = keyword?.Trim() ?? string.Empty;
            var pattern = string.IsNullOrEmpty(trimmedKeyword) ? string.Empty : $"%{trimmedKeyword}%";
            // 与批量查询同构：档案链归一到链尾现役档案（避免同一户旧/新档案各出一条记录）。
            // 空关键词 = 默认最近名单（不过滤姓名/身份证）。
            var result = await _db.QueryAsync<ChangedArchiveSummary>(
                @"WITH RECURSIVE base AS (
                      SELECT DISTINCT cr.application_id AS aid
                      FROM nc_biz_change_records cr
                      JOIN nc_biz_applications o ON o.id = cr.application_id AND o.deleted_at IS NULL
                      WHERE cr.deleted_at IS NULL
                        AND ($1 = '' OR o.applicant_name LIKE $1 OR o.applicant_id_card LIKE $1)
                  ),
                  chain AS (
                      SELECT aid AS start_id, aid AS cur_id FROM base
                      UNION ALL
                      SELECT c.start_id, a.id
                      FROM chain c
                      JOIN nc_biz_applications a ON a.original_application_id = c.cur_id
                  )
                  SELECT a.id AS application_id,
                         a.applicant_name AS head_name,
                         a.applicant_id_card AS id_card,
                         a.application_no AS application_no,
                         a.classification_result AS classification,
                         MAX(cr.change_date) AS last_change_date
                  FROM (SELECT start_id, MAX(cur_id) AS tail_id FROM chain GROUP BY start_id) t
                  JOIN nc_biz_applications a ON a.id = t.tail_id AND a.deleted_at IS NULL
                  JOIN nc_biz_change_records cr ON cr.application_id = t.start_id AND cr.deleted_at IS NULL
                  GROUP BY a.id, a.applicant_name, a.applicant_id_card, a.application_no, a.classification_result
                  ORDER BY last_change_date DESC
                  LIMIT $2",
                ct, pattern, limit);
            if (result.IsFailure)
                return Result.Failure<List<ChangedArchiveSummary>>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    result.Message ?? "变更档案搜索失败");
            return Result.Success(result.Value ?? new List<ChangedArchiveSummary>());
        }
        catch (Exception ex)
        {
            LogException(ex, "搜索变更档案");
            return Result.FromException<List<ChangedArchiveSummary>>(ex);
        }
    }

    /// <summary>变更档案搜索（不归一，逐档案返回：含历史旧档案与现役档案，供新旧选择）。</summary>
    public async Task<Result<List<ChangedArchiveSummary>>> SearchChangedArchivesRawAsync(string keyword, int limit = 20, CancellationToken ct = default)
    {
        try
        {
            var trimmed = keyword?.Trim() ?? string.Empty;
            var pattern = string.IsNullOrEmpty(trimmed) ? string.Empty : $"%{trimmed}%";
            var result = await _db.QueryAsync<ChangedArchiveSummary>(
                @"SELECT a.id AS application_id,
                         a.applicant_name AS head_name,
                         a.applicant_id_card AS id_card,
                         a.application_no AS application_no,
                         a.classification_result AS classification,
                         a.status AS status,
                         a.chain_type AS chain_type,
                         MAX(cr.change_date) AS last_change_date
                  FROM nc_biz_change_records cr
                  JOIN nc_biz_applications a ON a.id = cr.application_id AND a.deleted_at IS NULL
                  WHERE cr.deleted_at IS NULL
                    AND ($1 = '' OR a.applicant_name LIKE $1 OR a.applicant_id_card LIKE $1)
                  GROUP BY a.id, a.applicant_name, a.applicant_id_card, a.application_no, a.classification_result, a.status, a.chain_type
                  ORDER BY last_change_date DESC
                  LIMIT $2",
                ct, pattern, limit);
            if (result.IsFailure)
                return Result.Failure<List<ChangedArchiveSummary>>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    result.Message ?? "变更档案（不归一）搜索失败");
            return Result.Success(result.Value ?? new List<ChangedArchiveSummary>());
        }
        catch (Exception ex)
        {
            LogException(ex, "搜索变更档案（不归一）");
            return Result.FromException<List<ChangedArchiveSummary>>(ex);
        }
    }

    public async Task<Result<List<ChangedArchiveSummary>>> SearchMonthlyChangedArchivesAsync(int? year = null, int? month = null, CancellationToken ct = default)
    {
        try
        {
            var now = DateTime.Now;
            var mStart = new DateTime(year ?? now.Year, month ?? now.Month, 1);
            var mEnd = mStart.AddMonths(1);

            var result = await _db.QueryAsync<ChangedArchiveSummary>(
                @"WITH RECURSIVE base AS (
                      SELECT DISTINCT cr.application_id AS aid
                      FROM nc_biz_change_records cr
                      JOIN nc_biz_applications o ON o.id = cr.application_id AND o.deleted_at IS NULL
                      WHERE cr.deleted_at IS NULL
                        AND cr.change_date >= $1::date AND cr.change_date < $2::date
                  ),
                  chain AS (
                      SELECT aid AS start_id, aid AS cur_id FROM base
                      UNION ALL
                      SELECT c.start_id, a.id
                      FROM chain c
                      JOIN nc_biz_applications a ON a.original_application_id = c.cur_id
                  )
                  SELECT a.id AS application_id,
                         a.applicant_name AS head_name,
                         a.applicant_id_card AS id_card,
                         a.application_no AS application_no,
                         a.classification_result AS classification,
                         a.status AS status,
                         a.chain_type AS chain_type,
                         MAX(cr.change_date) AS last_change_date
                  FROM (SELECT start_id, MAX(cur_id) AS tail_id FROM chain GROUP BY start_id) t
                  JOIN nc_biz_applications a ON a.id = t.tail_id AND a.deleted_at IS NULL
                  JOIN nc_biz_change_records cr ON cr.application_id = t.start_id AND cr.deleted_at IS NULL
                  GROUP BY a.id, a.applicant_name, a.applicant_id_card, a.application_no, a.classification_result, a.status, a.chain_type
                  ORDER BY a.id",
                ct, mStart, mEnd);
            if (result.IsFailure)
                return Result.Failure<List<ChangedArchiveSummary>>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    result.Message ?? "变更人群摘要查询失败");
            return Result.Success(result.Value ?? new List<ChangedArchiveSummary>());
        }
        catch (Exception ex)
        {
            LogException(ex, "变更人群摘要查询");
            return Result.FromException<List<ChangedArchiveSummary>>(ex);
        }
    }

    /// <summary>
    /// 档案链归一：沿 original_application_id 递归到链尾（MAX(id)，停旧建新新档案 id 严格递增）。
    /// 链尾已删除 → 显式失败（该户档案已被删除，不应继续打印）。
    /// </summary>
    public async Task<Result<long>> NormalizeToLatestApplicationIdAsync(long applicationId, CancellationToken ct = default)
    {
        var result = await _db.ExecuteScalarAsync<long>(
            @"WITH RECURSIVE chain AS (
                  SELECT id, original_application_id FROM nc_biz_applications WHERE id = $1
                  UNION ALL
                  SELECT a.id, a.original_application_id
                  FROM nc_biz_applications a
                  JOIN chain c ON a.original_application_id = c.id
              )
              SELECT COALESCE(MAX(id), 0) FROM chain",
            ct, applicationId);
        if (result.IsFailure)
            return Result.Failure<long>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "档案链归一查询失败");

        var tailId = result.Value;
        if (tailId <= 0)
            return Result.Failure<long>(ErrorCodes.APPLICATION_NOT_FOUND, $"档案 {applicationId} 不存在");

        var aliveResult = await _db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL",
            ct, tailId);
        if (aliveResult.IsFailure)
            return Result.Failure<long>(aliveResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                aliveResult.Message ?? "档案状态查询失败");
        if (aliveResult.Value == 0)
            return Result.Failure<long>(ErrorCodes.APPLICATION_NOT_FOUND, $"档案 {applicationId} 的现役档案（{tailId}）已被删除，无法打印");

        return Result.Success(tailId);
    }

    /// <summary>
    /// 写打印留痕（nc_biz_print_records）。留痕失败仅告警不阻断导出主流程（打印成品已生成）。
    /// </summary>
    private async Task WritePrintRecordAsync(long applicationId, long templateId, byte[]? pdfData, string pdfPath,
        string batchNo, string remark, bool storePdfData, CancellationToken ct)
    {
        try
        {
            var record = new PrintRecord
            {
                BatchNo = batchNo,
                BusinessType = BusinessTypeKey,
                BusinessId = applicationId,
                TemplateId = templateId,
                TemplateName = TemplateName,
                PdfData = storePdfData ? (pdfData ?? []) : [],
                PdfSize = storePdfData ? (pdfData?.Length ?? 0) : 0,
                SourceData = [],
                SourceType = "",
                FilePath = pdfPath,
                PdfPath = pdfPath,
                PrinterName = "",
                Copies = 1,
                OperatorId = App.CurrentUserId,
                OperatorName = App.CurrentUserFullName ?? "",
                Status = "Completed",
                Remark = remark
            };
            var saveResult = await _printRecordService.SaveAsync(record, ct);
            if (saveResult.IsFailure)
                LogWarn($"动态管理记录留痕写入失败: ApplicationId={applicationId}, {saveResult.Message}");
        }
        catch (Exception ex)
        {
            LogWarn($"动态管理记录留痕写入异常: ApplicationId={applicationId}, {ex.Message}");
        }
    }

    // ========================
    //  渲染
    // ========================

    private async Task<Result<long>> ResolveTemplateIdAsync(CancellationToken ct)
    {
        var templateResult = await _templateService.GetByNameAsync(TemplateName, ct);
        if (templateResult.IsFailure)
            return Result.Failure<long>(templateResult.ErrorCode!, templateResult.Message!);
        var template = templateResult.Value;
        if (template == null)
            return Result.Failure<long>(ErrorCodes.DOCUMENT_GENERATION_FAILED,
                $"模板未导入模板库: {TemplateName}（请先运行 Scripts\\import_dynamic_management_template.py）");
        return Result.Success(template.Id);
    }

    private async Task<Result<byte[]>> RenderPdfAsync(Dictionary<string, string> fields, CancellationToken ct)
    {
        var templateIdResult = await ResolveTemplateIdAsync(ct);
        if (templateIdResult.IsFailure) return Result.Failure<byte[]>(templateIdResult.ErrorCode!, templateIdResult.Message!);

        // 走标准渲染路径：由 config_json 的 fieldKey→placeholder 映射替换模板中的中文花括号占位符
        var pdfResult = await _printService.GeneratePdfAsync(templateIdResult.Value, fields, ct);
        if (pdfResult.IsFailure)
            return Result.Failure<byte[]>(pdfResult.ErrorCode ?? ErrorCodes.DOCUMENT_GENERATION_FAILED,
                pdfResult.Message ?? "渲染动态管理记录失败");
        return pdfResult;
    }

    private static byte[] MergePdfBytes(List<byte[]> pdfs)
    {
        using var output = new PdfSharp.Pdf.PdfDocument();
        foreach (var pdf in pdfs)
        {
            using var inputStream = new MemoryStream(pdf);
            using var input = PdfSharp.Pdf.IO.PdfReader.Open(inputStream, PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import);
            for (var i = 0; i < input.PageCount; i++)
                output.AddPage(input.Pages[i]);
        }
        using var ms = new MemoryStream();
        output.Save(ms);
        return ms.ToArray();
    }

    private Result<string> WriteFile(string path, byte[] bytes)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllBytes(path, bytes);
            return Result.Success(path);
        }
        catch (Exception ex)
        {
            LogException(ex, $"写入文件 {path}");
            return Result.FromException<string>(ex);
        }
    }

    // ========================
    //  字段构建辅助
    // ========================

    private async Task<string> ResolveCurrentUnitAsync(CancellationToken ct)
    {
        var orgId = App.CurrentUserOrganizationId;
        if (!orgId.HasValue) return "-";
        var orgResult = await _organizationService.GetByIdAsync(orgId.Value, ct);
        return orgResult.IsSuccess && orgResult.Value != null
            ? orgResult.Value.Name
            : "-";
    }

    /// <summary>赡养人 3 槽位字段键（占位符已含序号，禁止拼串生成键）</summary>
    private static readonly (string Name, string Relation, string Health, string Occupation, string Fee)[] SupporterSlots =
    {
        (FieldKeys.DM_SUPPORTER_NAME_1, FieldKeys.DM_SUPPORTER_RELATION_1, FieldKeys.DM_SUPPORTER_HEALTH_1, FieldKeys.DM_SUPPORTER_OCCUPATION_1, FieldKeys.DM_SUPPORTER_FEE_1),
        (FieldKeys.DM_SUPPORTER_NAME_2, FieldKeys.DM_SUPPORTER_RELATION_2, FieldKeys.DM_SUPPORTER_HEALTH_2, FieldKeys.DM_SUPPORTER_OCCUPATION_2, FieldKeys.DM_SUPPORTER_FEE_2),
        (FieldKeys.DM_SUPPORTER_NAME_3, FieldKeys.DM_SUPPORTER_RELATION_3, FieldKeys.DM_SUPPORTER_HEALTH_3, FieldKeys.DM_SUPPORTER_OCCUPATION_3, FieldKeys.DM_SUPPORTER_FEE_3)
    };

    private void FillSupporterSlots(Dictionary<string, string> fields, List<Supporter> supporters)
    {
        for (var i = 0; i < SupporterSlots.Length; i++)
        {
            var slot = SupporterSlots[i];
            var s = i < supporters.Count ? supporters[i] : null;
            fields[slot.Name] = s?.Name ?? "";
            fields[slot.Relation] = s == null ? "" : DictDisplay(DictionaryTypeCodes.FamilyRelationships, s.Relationship);
            fields[slot.Health] = s == null ? "" : DictDisplay(DictionaryTypeCodes.HealthStatuses, s.HealthStatus);
            fields[slot.Occupation] = s == null ? "" : DictDisplay(DictionaryTypeCodes.EmploymentStatuses, s.EmploymentStatus);
            fields[slot.Fee] = s == null || s.AnnualSupportFee <= 0 ? "" : FormatMoney(s.AnnualSupportFee);
        }
    }

    /// <summary>字典显示：命中字典用显示值，未命中回退原始值</summary>
    private string DictDisplay(string category, string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        var display = _dictCacheService.GetValue(category, code);
        return string.IsNullOrEmpty(display) ? code : display;
    }

    private static string FormatMoney(decimal value) => value.ToString("N2", CultureInfo.CurrentCulture);

    /// <summary>不动产详情：房产聚合文本，无 → "无"（与档案输出 BuildHouseDesc 同型）</summary>
    private static string BuildRealEstateText(EconomicDetailData econ)
    {
        var items = (econ.FamilyProperties ?? new List<FamilyProperty>()).Select(p =>
            $"{p.HousingStructure ?? "未知"}结构，面积{FormatMoney(p.Area)}㎡，{p.HousingNature ?? "未知"}");
        var text = string.Join("；", items);
        return string.IsNullOrEmpty(text) ? "无" : text;
    }

    /// <summary>机动车辆详情：车辆聚合文本，无 → "无"（与档案输出 BuildVehicleDesc 同型）</summary>
    private static string BuildVehicleText(EconomicDetailData econ)
    {
        var items = (econ.Vehicles ?? new List<Vehicle>()).Select(v =>
            $"{v.Brand} {v.Model}（{v.LicensePlate}），估值{FormatMoney(v.EstimatedValue)}元");
        var text = string.Join("；", items);
        return string.IsNullOrEmpty(text) ? "无" : text;
    }

    /// <summary>船舶/农机具详情：农机具聚合文本，无 → "无"</summary>
    private static string BuildMachineryText(List<dynamic>? machineries)
    {
        if (machineries == null || machineries.Count == 0) return "无";
        var items = machineries.Select(m =>
        {
            var type = (string?)m.machinery_type ?? "农机具";
            var brand = (string?)m.brand ?? "";
            var model = (string?)m.model ?? "";
            var qty = (int?)m.quantity ?? 1;
            var spec = string.Join(" ", new[] { brand, model }.Where(x => !string.IsNullOrWhiteSpace(x)));
            return string.IsNullOrEmpty(spec) ? $"{type}×{qty}" : $"{type} {spec}×{qty}";
        });
        return string.Join("；", items);
    }

    /// <summary>近亲属备案情况：无 → "无"</summary>
    private static string BuildNearRelativeText(List<dynamic>? links)
    {
        if (links == null || links.Count == 0) return "无";
        var items = links.Select(l =>
        {
            var staff = (string?)l.staff_name ?? "";
            var relation = (string?)l.relation ?? "";
            var unit = (string?)l.work_unit ?? "";
            var detail = string.Join("，", new[] { relation, unit }.Where(x => !string.IsNullOrWhiteSpace(x)));
            return string.IsNullOrEmpty(detail) ? $"工作人员{staff}已备案" : $"工作人员{staff}（{detail}）已备案";
        });
        return string.Join("；", items);
    }

    /// <summary>文件路径中的户主姓名：剔除路径非法字符</summary>
    private static string MaskNameForPath(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "未命名";
        var cleaned = name.Trim();
        foreach (var c in Path.GetInvalidFileNameChars())
            cleaned = cleaned.Replace(c, '_');
        return cleaned;
    }
}
