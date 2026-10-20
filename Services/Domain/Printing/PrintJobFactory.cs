using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.Domain.TempRelief;
using NewCosmos.Services.Utilities;

namespace NewCosmos.Services.Domain.Printing;

/// <summary>
/// 推送打印任务构建服务实现。
/// 模板选择复用 <see cref="ArchiveCategoryResolver"/>（与档案输出/补打中心同源分类），
/// 字段构建复用各域 <c>*PrintDataBuilder</c>，保证推送打印与手动打印口径一致。
/// </summary>
public class PrintJobFactory : BaseService, IPrintJobFactory
{
    protected override string ServiceName => "PrintJobFactory";

    private readonly IPrintQueueService _queueService;
    private readonly ITemplateService _templateService;
    private readonly ITempReliefService _tempReliefService;
    private readonly IElderlyApplicationService _elderlyService;
    private readonly IBusinessTimelineService _timelineService;

    public PrintJobFactory(
        IPrintQueueService queueService,
        ITemplateService templateService,
        ITempReliefService tempReliefService,
        IElderlyApplicationService elderlyService,
        IBusinessTimelineService timelineService,
        ILoggerService logger) : base(logger)
    {
        _queueService = queueService;
        _templateService = templateService;
        _tempReliefService = tempReliefService;
        _elderlyService = elderlyService;
        _timelineService = timelineService;
    }

    public async Task<Result<PrintJob>> EnqueueAsync(PrintJobRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.BusinessType))
            return Result.Failure<PrintJob>(ErrorCodes.VALIDATION_FAILED, "业务类型不能为空");

        // 未显式指定模板时，按业务类型 + 分类解析
        if (request.TemplateId is null or <= 0)
        {
            var resolved = await ResolveTemplateAsync(request.BusinessType, request.Classification, ct);
            if (resolved == null)
                return Result.Failure<PrintJob>(ErrorCodes.TEMPLATE_NOT_FOUND,
                    $"未找到匹配模板（{request.BusinessType} / {request.Classification}）");
            request.TemplateId = resolved.Id;
            request.TemplateName = resolved.Name;
        }

        request.RequestedBy ??= App.CurrentUserId;
        request.RequestedByName ??= App.CurrentUserName;

        var result = await _queueService.EnqueueAsync(request, ct);
        if (result.IsSuccess)
        {
            Logger.LogBusiness("打印任务已推送",
                ("BusinessType", request.BusinessType),
                ("BusinessId", request.BusinessId?.ToString() ?? string.Empty),
                ("Template", request.TemplateName ?? string.Empty));
        }
        return result;
    }

    public async Task<Result<PrintJob>> EnqueueTempReliefAsync(
        long applicationId,
        int? copies = null,
        bool isDuplex = false,
        string? printerName = null,
        CancellationToken ct = default)
    {
        var fullResult = await _tempReliefService.GetByIdAsync(applicationId, ct);
        if (fullResult.IsFailure || fullResult.Value == null)
            return Result.Failure<PrintJob>(ErrorCodes.RECORD_NOT_FOUND, "加载临时救助档案失败");

        var fullApp = fullResult.Value;

        var diseases = await _tempReliefService.GetDiseasesByApplicationIdAsync(applicationId, ct);
        if (diseases.IsSuccess && diseases.Value != null) fullApp.Diseases = diseases.Value;

        var accidents = await _tempReliefService.GetAccidentsByApplicationIdAsync(applicationId, ct);
        if (accidents.IsSuccess && accidents.Value != null) fullApp.Accidents = accidents.Value;

        var educations = await _tempReliefService.GetEducationsByApplicationIdAsync(applicationId, ct);
        if (educations.IsSuccess && educations.Value != null) fullApp.Educations = educations.Value;

        var membersResult = await _tempReliefService.GetMembersByApplicationIdAsync(applicationId, ct);
        var memberList = membersResult.IsSuccess ? membersResult.Value ?? new() : new List<TempReliefMember>();

        var (acceptanceDate, investigationDate) =
            await TempReliefPrintDataBuilder.ResolveScheduleAsync(_timelineService, fullApp);
        var contactUnitPhone = (await _tempReliefService.GetReportUnitPhoneAsync(fullApp.ReportUnit, ct)).Value ?? string.Empty;
        var fields = TempReliefPrintDataBuilder.BuildSingleFields(
            fullApp, memberList, contactUnitPhone, acceptanceDate, investigationDate);

        // 手机推送打印的模板与份数（业务确认：与桌面端同打「临时救助_审核审批表*」、默认 2 份）。
        // 旧口径是 EnqueueAsync 里按 sort_order 取第一张 Word 模板 —— 实际会取到「临时救助_信息公示」，
        // 不是审核审批表，故在此显式选模板并把 TemplateId 写进请求（EnqueueAsync 不再二次解析）。
        var categories = ArchiveCategoryResolver.GetRecordCategories(TempReliefConstants.BusinessType, fullApp.ReliefType);
        var candidates = await _templateService.GetByCategoriesAsync(categories, ct);
        var template = PickPushPrintTemplate(candidates);
        if (template == null)
            return Result.Failure<PrintJob>(ErrorCodes.TEMPLATE_NOT_FOUND,
                $"未找到临时救助打印模板（{string.Join(" | ", categories)}）");

        // 份数：调用方显式传入 > 传入值，否则按模板配置解析（与档案输出页同一口径 TemplateCopiesResolver）
        var effectiveCopies = copies ?? TemplateCopiesResolver.Resolve(template.ConfigJson, fullApp.ReliefType);
        LogInfo($"临时救助推送打印: 模板={template.Name} 份数={effectiveCopies} 分类={fullApp.ReliefType}");

        var request = new PrintJobRequest
        {
            BusinessType = TempReliefConstants.BusinessType,
            BusinessId = applicationId,
            Classification = fullApp.ReliefType,
            TemplateId = template.Id,
            TemplateName = template.Name,
            ApplicantName = fullApp.ApplicantName,
            ApplicantIdCard = fullApp.ApplicantIdCard,
            PrinterName = printerName,
            Copies = effectiveCopies,
            IsDuplex = isDuplex,
            Fields = fields,
            TableRows = new(),
            SupporterTableData = null
        };

        return await EnqueueAsync(request, ct);
    }

    /// <summary>
    /// 手机推送打印选模板（唯一口径）：优先「临时救助_审核审批表*」（业务确认的主单），
    /// 未命中回退第一张 Word 模板（与 <see cref="ResolveTemplateAsync"/> 同序）。
    /// </summary>
    private static Template? PickPushPrintTemplate(IReadOnlyList<Template> candidates)
    {
        if (candidates.Count == 0) return null;
        return candidates.FirstOrDefault(t => t.Name.StartsWith(TempReliefConstants.TemplateAuditApprovalPrefix, StringComparison.Ordinal))
               ?? candidates.FirstOrDefault(t => !string.Equals(t.FileType, "xlsx", StringComparison.OrdinalIgnoreCase))
               ?? candidates[0];
    }

    public async Task<Result<PrintJob>> EnqueueElderlyAsync(
        long applicationId,
        string classification,
        int copies = 1,
        bool isDuplex = false,
        string? printerName = null,
        CancellationToken ct = default)
    {
        var appResult = await _elderlyService.GetByIdAsync(applicationId, ct);
        if (appResult.IsFailure || appResult.Value == null)
            return Result.Failure<PrintJob>(ErrorCodes.RECORD_NOT_FOUND, "加载高龄津贴档案失败");

        var app = appResult.Value;
        var fields = ElderlyPrintDataBuilder.BuildSingleFields(app);

        var request = new PrintJobRequest
        {
            BusinessType = "ElderlyBenefits",
            BusinessId = applicationId,
            Classification = classification,
            ApplicantName = app.Name,
            ApplicantIdCard = app.IdCard,
            PrinterName = printerName,
            Copies = copies,
            IsDuplex = isDuplex,
            Fields = fields,
            TableRows = new(),
            SupporterTableData = null
        };

        return await EnqueueAsync(request, ct);
    }

    private async Task<Template?> ResolveTemplateAsync(string businessType, string? classification, CancellationToken ct)
    {
        var categories = ArchiveCategoryResolver.GetRecordCategories(businessType, classification);
        var templates = await _templateService.GetByCategoriesAsync(categories, ct);
        if (templates.Count == 0) return null;

        // 单条记录推送：优先 Word 模板（无配置时取排序最前），与档案输出默认逐条口径一致
        return templates.FirstOrDefault(t => !string.Equals(t.FileType, "xlsx", StringComparison.OrdinalIgnoreCase))
               ?? templates[0];
    }
}
