using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.Domain.TempRelief;
using NewCosmos.Services.UserManagement;
using NewCosmos.Services.Utilities;

namespace NewCosmos.Services.Domain.Printing;

/// <summary>
/// 推送打印任务构建服务实现。
/// 模板选择复用 <see cref="ArchiveCategoryResolver"/>（与档案输出/补打中心同源分类），
/// 字段构建复用各域 <c>*PrintDataBuilder</c>，保证推送打印与手动打印口径一致。
/// 方案A（2026-11-28）：Queue 模式下入队前固化 target_device_id，交由对应 Windows Agent 认领；
/// 入队前补打印权限校验，与 <see cref="PrintExecuteService.ExecutePrintAsync"/> 同口径。
/// </summary>
public class PrintJobFactory : BaseService, IPrintJobFactory
{
    protected override string ServiceName => "PrintJobFactory";

    private readonly IPrintQueueService _queueService;
    private readonly ITemplateService _templateService;
    private readonly ITempReliefService _tempReliefService;
    private readonly IElderlyApplicationService _elderlyService;
    private readonly IBusinessTimelineService _timelineService;
    private readonly IDatabaseService _db;
    private readonly IConfigService _configService;
    private readonly INewPermissionService _permissionService;

    public PrintJobFactory(
        IPrintQueueService queueService,
        ITemplateService templateService,
        ITempReliefService tempReliefService,
        IElderlyApplicationService elderlyService,
        IBusinessTimelineService timelineService,
        IDatabaseService db,
        IConfigService configService,
        INewPermissionService permissionService,
        ILoggerService logger) : base(logger)
    {
        _queueService = queueService;
        _templateService = templateService;
        _tempReliefService = tempReliefService;
        _elderlyService = elderlyService;
        _timelineService = timelineService;
        _db = db;
        _configService = configService;
        _permissionService = permissionService;
    }

    public async Task<Result<PrintJob>> EnqueueAsync(PrintJobRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.BusinessType))
            return Result.Failure<PrintJob>(ErrorCodes.VALIDATION_FAILED, "业务类型不能为空");

        // 权限校验：与 PrintExecuteService.ExecutePrintAsync 同口径（登录 → 业务类型登记 → 权限码），
        // 此前入队无校验，无打印权限的用户能推成任务、到打印时才失败。
        var currentUserId = App.CurrentUserId;
        if (!currentUserId.HasValue)
            return Result.Failure<PrintJob>(ErrorCodes.AUTHENTICATION_FAILED, "用户未登录");

        if (!PrintExecuteService.TryGetPrintPermissionCode(request.BusinessType, out var permissionCode))
        {
            LogError($"未登记的打印业务类型: {request.BusinessType}");
            return Result.Failure<PrintJob>(ErrorCodes.PERMISSION_DENIED, $"未登记的打印业务类型: {request.BusinessType}");
        }

        if (!await _permissionService.HasPermissionAsync(currentUserId.Value, permissionCode, ct))
        {
            LogError($"用户无打印权限, UserId={currentUserId}, Permission={permissionCode}");
            return Result.Failure<PrintJob>(ErrorCodes.PERMISSION_DENIED, $"无权打印该文档（缺少权限: {permissionCode}）");
        }

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

        // 方案A：Queue 模式固化定向目标；Local 模式 / 含签名占位 / 本机未注册 Agent → target 留 NULL，
        // 由本机打印代理消费（回退本地代理/本地打印，Local 模式零回归）。
        if (!request.SkipTargetResolution && request.TargetDeviceId is null)
            await TryResolveTargetAsync(request, ct);

        var result = await _queueService.EnqueueAsync(request, ct);
        if (result.IsSuccess)
        {
            // JobNo 以队列服务生成/回读的为准（工厂侧不再另造号码，避免两处时间戳不一致）
            Logger.LogBusiness("打印任务已推送",
                ("BusinessType", request.BusinessType),
                ("BusinessId", request.BusinessId?.ToString() ?? string.Empty),
                ("Template", request.TemplateName ?? string.Empty),
                ("JobNo", result.Value?.JobNo ?? string.Empty),
                ("TargetDeviceId", request.TargetDeviceId?.ToString() ?? "NULL"));
        }
        return result;
    }

    /// <summary>
    /// 解析并固化定向推送目标（方案A）。
    /// 仅 OutputMode=Queue 时查 nc_sys_print_devices；Local 模式 / 模板或字段含 SIGNATURE_* 占位 /
    /// 本机未注册打印代理 → 返回 false，target_* 保持 NULL 交由本机打印代理消费。
    /// </summary>
    public async Task<bool> TryResolveTargetAsync(PrintJobRequest request, CancellationToken ct = default)
    {
        if (request.SkipTargetResolution || request.TargetDeviceId is not null)
            return request.TargetDeviceId is not null;

        var options = _configService.GetAppOptions();
        if (!string.Equals(options.PrintOutputMode, PrintQueueConstants.OutputModeQueue, StringComparison.OrdinalIgnoreCase))
            return false; // Local 模式：不解析，保持现状（target 留 NULL，本机代理消费）

        if (PrintPushGuard.HasSignatureField(request))
        {
            LogInfo($"字段含 SIGNATURE_* 占位，拒绝定向推送，回退本地打印: {request.BusinessType}");
            return false;
        }

        // 模板 config 的签名占位：Windows Agent 跳过 SIGNATURE_* 不内嵌，推过去会丢签名
        var template = await LoadTemplateForSignatureCheckAsync(request, ct);
        if (template == null)
            return false; // 模板缺失：留痕由后续 EnqueueAsync / 本地打印统一报错

        if (PrintPushGuard.HasSignaturePlaceholder(template))
        {
            LogInfo($"模板含 SIGNATURE_* 占位，拒绝定向推送，回退本地打印: {template.Name}");
            return false;
        }

        var deviceId = await ResolveAgentDeviceIdAsync(ct);
        if (deviceId is null)
            return false;

        request.TargetDeviceId = deviceId;
        request.TargetUserId ??= App.CurrentUserId;
        request.TargetPrinter = string.IsNullOrWhiteSpace(request.PrinterName) ? null : request.PrinterName;
        return true;
    }

    /// <summary>
    /// 查本机注册的打印代理设备（machine_name = 本机名，取最近心跳的一台）。
    /// 查不到 / 查询失败 → null（调用方回退本地代理/本地打印，只告警不阻断入队）。
    /// </summary>
    private async Task<long?> ResolveAgentDeviceIdAsync(CancellationToken ct)
    {
        const string sql = @"SELECT id FROM nc_sys_print_devices
                             WHERE machine_name = $1 AND is_active IS NOT FALSE
                             ORDER BY last_seen_at DESC
                             LIMIT 1";

        var result = await _db.ExecuteScalarAsync<long>(sql, ct, Environment.MachineName);
        if (result.IsFailure)
        {
            LogWarn($"查询本机打印代理设备失败，target 留 NULL 回退本地代理/本地打印: {result.ErrorCode} {result.Message}");
            return null;
        }

        // 无行时泛型 ExecuteScalarAsync<T> 返回 Success(default)=0；设备 id 为 BIGSERIAL 恒 > 0
        if (result.Value <= 0)
        {
            LogWarn($"本机未注册打印代理设备（nc_sys_print_devices），target 留 NULL 回退本地代理/本地打印: {Environment.MachineName}");
            return null;
        }

        return result.Value;
    }

    /// <summary>取模板做签名占位判定；TemplateId 已定时直接读，否则按业务类型+分类解析并写回 request（避免 EnqueueAsync 重复解析）。</summary>
    private async Task<Template?> LoadTemplateForSignatureCheckAsync(PrintJobRequest request, CancellationToken ct)
    {
        if (request.TemplateId is > 0)
        {
            var byId = await _templateService.GetByIdAsync(request.TemplateId.Value, ct);
            if (byId.IsFailure)
            {
                LogWarn($"读取模板失败，无法判定签名占位: {request.TemplateId}, {byId.ErrorCode} {byId.Message}");
                return null;
            }
            return byId.Value;
        }

        var resolved = await ResolveTemplateAsync(request.BusinessType, request.Classification, ct);
        if (resolved == null)
        {
            LogWarn($"未找到匹配模板，无法判定签名占位: {request.BusinessType} / {request.Classification}");
            return null;
        }

        request.TemplateId = resolved.Id;
        request.TemplateName ??= resolved.Name;
        return resolved;
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
        if (diseases.IsFailure)
            return Result.Failure<PrintJob>(ErrorCodes.NOT_FOUND, $"疾病明细加载失败，未生成打印任务: {diseases.Message}");
        fullApp.Diseases = diseases.Value ?? new();

        var accidents = await _tempReliefService.GetAccidentsByApplicationIdAsync(applicationId, ct);
        if (accidents.IsFailure)
            return Result.Failure<PrintJob>(ErrorCodes.NOT_FOUND, $"灾害明细加载失败，未生成打印任务: {accidents.Message}");
        fullApp.Accidents = accidents.Value ?? new();

        var educations = await _tempReliefService.GetEducationsByApplicationIdAsync(applicationId, ct);
        if (educations.IsFailure)
            return Result.Failure<PrintJob>(ErrorCodes.NOT_FOUND, $"教育明细加载失败，未生成打印任务: {educations.Message}");
        fullApp.Educations = educations.Value ?? new();

        var membersResult = await _tempReliefService.GetMembersByApplicationIdAsync(applicationId, ct);
        if (membersResult.IsFailure)
            return Result.Failure<PrintJob>(ErrorCodes.NOT_FOUND, $"成员明细加载失败，未生成打印任务: {membersResult.Message}");
        var memberList = membersResult.Value ?? new();

        var (acceptanceDate, investigationDate) =
            await TempReliefPrintDataBuilder.ResolveScheduleAsync(_timelineService, fullApp);
        var phoneResult = await _tempReliefService.GetReportUnitPhoneAsync(fullApp.ReportUnit, ct);
        if (phoneResult.IsFailure)
            Serilog.Log.Warning("[PrintJobFactory] 报告单位电话读取失败，文书该字段将为空: {Message}", phoneResult.Message);
        var contactUnitPhone = phoneResult.Value ?? string.Empty;
        var fields = TempReliefPrintDataBuilder.BuildSingleFields(
            fullApp, memberList, contactUnitPhone, acceptanceDate, investigationDate);

        // 手机推送打印的模板与份数（业务确认：与桌面端同打「临时救助_…审核审批表」、默认 2 份）。
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
    /// 手机推送打印选模板（唯一口径）：优先「临时救助审核审批表」（业务确认的主单，doc_type 精准匹配，
    /// 2026-11-18 方案B 取代原 Name.Contains("审核审批表") 子串），未命中回退第一张 Word 模板
    ///（与 <see cref="ResolveTemplateAsync"/> 同序）。2026-10-21 起大额/小额分表，
    /// 调用方传入的分类已互斥（ReliefType→CategoryLarge/CategorySmall），故类型命中的就是本救助类型的那一张。
    /// </summary>
    private static Template? PickPushPrintTemplate(IReadOnlyList<Template> candidates)
    {
        if (candidates.Count == 0) return null;
        return candidates.FirstOrDefault(t => t.DocType == TemplateDocTypes.TempReliefAuditApproval)
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
            BusinessType = PrintBusinessTypes.ElderlyBenefits,
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