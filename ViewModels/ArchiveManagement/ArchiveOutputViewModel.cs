using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Enums;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Results;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Domain.AssetVerification;
using NewCosmos.Services.Domain.ChangeManagement;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.Domain.Printing;
using NewCosmos.Services.Domain.Recovery;
using NewCosmos.Services.Core;
using NewCosmos.Services.Platform;
using NewCosmos.Services.Utilities;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;

namespace NewCosmos.ViewModels.ArchiveManagement;

public partial class ArchiveOutputViewModel : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider = null!;
    private readonly ITemplateService _templateService = null!;
    private readonly IPrintExecuteService _printExecuteService = null!;
    private readonly IPrintRecordService _printRecordService = null!;
    private readonly IDialogService _dialogService = null!;
    private readonly ILoggerService _logger = null!;
    private readonly IPrinterService _printerService = null!;
    private readonly IFileService _fileService = null!;
    private readonly IPdfVerificationService _pdfVerificationService = null!;
    private readonly IBusinessTimelineService _timelineService = null!;
    private readonly IAssetVerificationService _assetVerificationService = null!;
    private readonly IChangeDocumentApplicabilityService _changeDocApplicability = null!;

    /// <summary>
    /// 由 Page 注入：WebView2 打印 PDF 的回调（pdfPath → Task）。用于核查报告等非 Office/WPS 格式打印。
    /// </summary>
    public Func<string, Task>? WebViewPrintPdfFunc { get; set; }

    private byte[] _currentPdfData = null!;
    private string _batchNo = string.Empty;
    private string _currentTempPreviewPath = string.Empty;

    /// <summary>
    /// 初始化上下文指纹（2026-11 性能收口）：与上次成功初始化一致时跳过重建。
    /// OnAppearing 在「push 预览/打印子页后返回」时重复触发 InitializeAsync → 全量重建模板清单
    /// （20~30 条模板 + 事实查询），纯浪费且会丢失用户已勾选状态。
    /// VM 为 Transient：同实例重复 OnAppearing 只发生在子页返回（上下文必相同）；
    /// 跨记录/跨入口必是新实例或上下文变化（指纹含 BusinessId 与文书模式四字段），照常重建。
    /// </summary>
    private string _lastInitContextKey = string.Empty;

    /// <summary>
    /// InitializeAsync 单飞闸：补打中心「选中记录准备」与「输出分类切换」两条入口可能并发触发重建，
    /// 并发执行会让两轮 Templates.Clear/Add 交错，清单被截断（模板凭空消失）。串行后后一轮覆盖前一轮。
    /// </summary>
    private readonly SemaphoreSlim _initializeGate = new(1, 1);

    public ArchiveOutputViewModel(
        IServiceProvider serviceProvider,
        ITemplateService templateService,
        IPrintExecuteService printExecuteService,
        IPrintRecordService printRecordService,
        IDialogService dialogService,
        ILoggerService logger,
        IPrinterService printerService,
        IFileService fileService,
        IPdfVerificationService pdfVerificationService,
        IBusinessTimelineService timelineService,
        IAssetVerificationService assetVerificationService,
        IChangeDocumentApplicabilityService changeDocApplicability)
    {
        _serviceProvider = serviceProvider;
        _templateService = templateService;
        _printExecuteService = printExecuteService;
        _printRecordService = printRecordService;
        _dialogService = dialogService;
        _logger = logger;
        _printerService = printerService;
        _fileService = fileService;
        _pdfVerificationService = pdfVerificationService;
        _timelineService = timelineService;
        _assetVerificationService = assetVerificationService;
        _changeDocApplicability = changeDocApplicability;
    }

    #region 基础实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    // ---- 模板列表 ----
    [ObservableProperty]
    private ObservableCollection<TemplateSelectItem> _templates = new();

    [ObservableProperty]
    private TemplateSelectItem _selectedPreviewTemplate = null!;

    /// <summary>
    /// 仅出文书模式（PrintNavigationData.TemplateFilter 非空）：
    /// 模板列表已收窄，隐藏「完成归档」——渐退草稿/仅出文书不得 CompleteArchiveAsync。
    /// </summary>
    [ObservableProperty]
    private bool _isTemplateFilterMode;

    /// <summary>
    /// 文书直出文档模式（OutputCategories/OperationOverride/Prefilter 任一非空且非过滤模式）：
    /// 同样隐藏「完成归档」——渐退/户主死亡 Draft 不得被 CompleteArchiveAsync 误置 Approved。
    /// </summary>
    [ObservableProperty]
    private bool _isDocumentMode;

    /// <summary>近亲属备案独立输出域（列表页"输出档案"入口）：无归档/工单语义，两个完成按钮均隐藏</summary>
    private static bool IsStandaloneOutput => string.Equals(
        PrintNavigationData.BusinessType, NearRelativeConstants.BusinessType, StringComparison.OrdinalIgnoreCase);

    /// <summary>「完成归档」按钮可见：既非过滤模式也非文档模式，且非独立输出域</summary>
    public bool IsCompleteArchiveVisible => !IsTemplateFilterMode && !IsDocumentMode && !IsStandaloneOutput;

    /// <summary>「完成工单」按钮可见：与「完成归档」互斥（文档/过滤模式下替代留痕办理完成；独立输出域均隐藏）</summary>
    public bool IsWorkOrderCompleteVisible => (IsTemplateFilterMode || IsDocumentMode) && !IsStandaloneOutput;

    partial void OnIsTemplateFilterModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCompleteArchiveVisible));
        OnPropertyChanged(nameof(IsWorkOrderCompleteVisible));
    }

    partial void OnIsDocumentModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsCompleteArchiveVisible));
        OnPropertyChanged(nameof(IsWorkOrderCompleteVisible));
    }

    /// <summary>工单已办理完成（会话内状态：按钮置灰防重复提交）</summary>
    [ObservableProperty]
    private bool _isWorkOrderCompleted;

    // ---- 输出文件 ----
    [ObservableProperty]
    private ObservableCollection<OutputFileItem> _outputFiles = new();

    // ---- 生成进度 ----
    [ObservableProperty]
    private bool _isGenerating;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _progressText = string.Empty;

    // ---- 打印设置 ----
    [ObservableProperty]
    private ObservableCollection<string> _printerNames = new();

    [ObservableProperty]
    private string _selectedPrinter = string.Empty;

    [ObservableProperty]
    private int _copies = 1;

    [ObservableProperty]
    private bool _isDuplex = true;

    // ---- 预览 ----
    [ObservableProperty]
    private string _pdfFilePath = string.Empty;

    [ObservableProperty]
    private bool _hasPdf;

    [ObservableProperty]
    private string _statusText = string.Empty;

    // ---- 数量验证 ----
    public bool CanDecreaseCopies => Copies > 1;

    public bool CanIncreaseCopies => Copies < 99;

    partial void OnCopiesChanged(int value)
    {
        var clamped = Math.Clamp(value, 1, 99);
        if (clamped != value)
        {
            Copies = clamped;
            OnPropertyChanged(nameof(Copies));
        }
        OnPropertyChanged(nameof(CanDecreaseCopies));
        OnPropertyChanged(nameof(CanIncreaseCopies));
    }

    [RelayCommand]
    private void IncreaseCopies()
    {
        if (Copies < 99) Copies = Copies + 1;
    }

    [RelayCommand]
    private void DecreaseCopies()
    {
        if (Copies > 1) Copies = Copies - 1;
    }

    // ========================
    //  初始化
    // ========================

    /// <summary>
    /// 解析核查报告的 check_id：
    /// 资产核查档：BusinessId 即核查记录 id，优先使用；无 BusinessId 时按申请人身份证反查。
    /// 其他档案（如低收入人口家庭认定）：BusinessId 是业务表 id 而非核查记录 id，
    /// 一律按 FieldData 中的申请人身份证反查最新未删除核查记录。
    /// </summary>
    private async Task<long?> ResolveCheckIdAsync(
        string? businessType, long? businessId, Dictionary<string, string>? fieldData, CancellationToken ct)
    {
        // 资产核查档：BusinessId 即 nc_biz_asset_checks.id
        if (businessType == "AssetVerification" && businessId.HasValue)
            return businessId.Value;

        try
        {
            // 从 FieldData 中获取身份证号（兼容两种键名），用于反查该人最新核查记录
            if (fieldData == null) return null;
            string? idCard = null;
            if (!fieldData.TryGetValue(FieldKeys.APPLICANT_ID_CARD, out idCard)
                && !fieldData.TryGetValue("ApplicantIdCard", out idCard))
                return null;
            if (string.IsNullOrWhiteSpace(idCard)) return null;

            var result = await _assetVerificationService.GetLatestIdByIdCardAsync(idCard, ct);
            if (result.IsSuccess && result.Value.HasValue)
                return result.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "解析核查报告 check_id 失败");
        }
        return null;
    }

    /// <summary>
    /// 加载模板清单（单飞串行，见 <see cref="_initializeGate"/>）。
    /// </summary>
    public async Task InitializeAsync()
    {
        await _initializeGate.WaitAsync();
        try
        {
            // 上下文指纹守卫（2026-11 性能收口）：子页（预览/打印）返回本页时 OnAppearing 重复触发，
            // 上下文未变则跳过全量重建（20~30 模板 + 事实查询）；跨记录/文书模式切换指纹必变，照常重建。
            var contextKey = BuildInitContextKey();
            if (contextKey == _lastInitContextKey && Templates.Count > 0)
            {
                _logger.Debug("档案输出初始化跳过（上下文未变）");
                return;
            }

            await InitializeCoreAsync();
            _lastInitContextKey = contextKey;
        }
        finally
        {
            _initializeGate.Release();
        }
    }

    /// <summary>
    /// 初始化上下文指纹 = 影响模板清单/预勾选/适用性的全部输入（与 InitializeCoreAsync 快照字段同集）。
    /// 含 FAMILY_LAND_AREA（模板 17/18 二选一依据）与赡养人表条数（赡养费承诺书适用性依据）。
    /// </summary>
    private static string BuildInitContextKey() =>
        $"{PrintNavigationData.BusinessType}|{PrintNavigationData.BusinessId}|{PrintNavigationData.Classification}|" +
        $"{PrintNavigationData.Status}|{PrintNavigationData.FieldData?.Count}|{PrintNavigationData.FieldData?.GetValueOrDefault(FieldKeys.FAMILY_LAND_AREA)}|" +
        $"{PrintNavigationData.SupporterTableData?.Count}|{string.Join(';', PrintNavigationData.OutputCategories ?? [])}|" +
        $"{PrintNavigationData.OperationOverride}|{PrintNavigationData.TemplateFilter?.Length}|{PrintNavigationData.PrefilterTemplateNames?.Length}";

    private async Task InitializeCoreAsync()
    {
        Title = "档案输出";
        _batchNo = Guid.NewGuid().ToString("N").ToUpper();

        // 文书模式护栏：OutputCategories / OperationOverride / TemplateFilter / PrefilterTemplateNames
        // 仅低收入人口域（FamilyApplication / EconomicReview，含补打中心动态记录）才有效；
        // 其余域（普惠高龄、临时救助、追缴、资产核查、月报…）一律忽略，回退按 BusinessType 取分类。
        // 起因：低保/变更「仅出文书」写入的静态上下文若未被清理（如从档案制作页直接返回），
        // 会被后续高龄停发等整档流程继承 → 误出「档案_渐退期审批表」等变动文书（方向认定错）。
        var docModeAllowed = PrintNavigationData.BusinessType is "FamilyApplication" or "EconomicReview";
        var outCats = docModeAllowed ? PrintNavigationData.OutputCategories : null;
        var operationOverride = docModeAllowed ? PrintNavigationData.OperationOverride : null;
        var templateFilter = docModeAllowed ? PrintNavigationData.TemplateFilter : null;
        var preselect = docModeAllowed ? PrintNavigationData.PrefilterTemplateNames : null;

        // 数据快照：下方模板条件剔除（土地/赡养人/一事一议/近亲属）与停保告知书追加都发生在多次 await 之后，
        // 期间静态 PrintNavigationData 可能被 Clear()（页面 OnDisappearing/输出归档）或其它流程覆盖。
        // 与分类集同一时刻取快照，保证清单判定与档案制作同口径（曾致补打清单丢「村级土地说明/赡养费承诺书」）。
        var businessType = PrintNavigationData.BusinessType;
        var businessId = PrintNavigationData.BusinessId;
        var fieldData = PrintNavigationData.FieldData;
        var supporterData = PrintNavigationData.SupporterTableData;

        var categories = outCats is { Length: > 0 } ? outCats
            : GetCategoriesByBusinessType(businessType, operationOverride);
        var classification = PrintNavigationData.Classification;
        IsTemplateFilterMode = templateFilter is { Length: > 0 };
        IsDocumentMode = !IsTemplateFilterMode
            && (outCats is { Length: > 0 }
                || operationOverride != null
                || preselect is { Length: > 0 });
        var isDocumentMode = IsDocumentMode;
            _logger.LogBusiness("加载打印模板",
                ("BusinessType", businessType),
                ("Categories", string.Join(",", categories)),
                ("Classification", classification),
                ("TemplateFilter", templateFilter == null ? "(整档)" : string.Join("|", templateFilter)),
                ("Prefilter", preselect == null ? "(无)" : string.Join("|", preselect)),
                // 诊断：土地/赡养人剔除判定依据（补打清单缺「村级土地说明/赡养费承诺书」排查用）
                ("FamilyLandArea", fieldData?.GetValueOrDefault(FieldKeys.FAMILY_LAND_AREA) ?? "(null)"),
                ("SupporterCount", supporterData?.Count.ToString() ?? "(null)"),
                ("FieldCount", fieldData?.Count.ToString() ?? "(null)"));

        try
        {
            IsBusy = true;
            StatusText = "正在加载模板...";

            // 变动文书事实快照（低收入人口域）：清单过滤与告知书追加门共用同一口径
            //（ChangeDocumentApplicabilityService 单点，与申请表预勾选同源）。
            // 查询失败 → docFacts=null → 清单不做过滤（宁多打不漏打）、告知书不追加（沿用原失败降级语义）。
            var factsBusinessId = businessId.GetValueOrDefault();
            ChangeDocumentFacts? docFacts = null;
            if (businessType is "FamilyApplication" or "EconomicReview" && factsBusinessId > 0)
            {
                var factsResult = await _changeDocApplicability.GetFactsAsync(factsBusinessId, classification, CancellationToken);
                if (factsResult.IsSuccess)
                {
                    docFacts = factsResult.Value;
                    // 可观测（2026-11）：事实快照一次性入 biz 日志——"变动文书缺某张模板"从此可直接查证
                    _logger.LogBusiness("变动文书事实快照",
                        ("ApplicationId", factsBusinessId),
                        ("Classification", docFacts.Classification ?? "(null)"),
                        ("NoticeApplicable", docFacts.NoticeApplicable),
                        ("IncomeStop", docFacts.IsIncomeStopChange),
                        ("FundChangeRegistration", docFacts.HasFundChangeRegistration),
                        ("ArchiveRefusedStop", docFacts.IsArchiveRefusedStop),
                        ("HouseholdDeathStop", docFacts.IsHouseholdDeathStop),
                        ("MemberChange", docFacts.HasMemberChange),
                        ("GracePeriod", docFacts.HasActiveGracePeriod),
                        ("GrantReduce", docFacts.HasGrantReduce),
                        ("EconomicReview", docFacts.HasEconomicReviewRecord),
                        ("DeathStoppedArchive", docFacts.IsDeathStoppedArchive));
                }
                else
                {
                    _logger.Warn($"变动文书事实查询失败（清单不做过滤）: BusinessId={factsBusinessId}, {factsResult.ErrorCode} {factsResult.Message}");
                }
            }
            else if (businessType is "FamilyApplication" or "EconomicReview")
            {
                // 静默分支改告警（2026-11 缺陷：BusinessId 为空时事实全不判 → 告知书等变动文书
                // 静默不追加、日志零痕迹，曾导致"变动文书没有告知书打印"排查无从下手）
                _logger.Warn($"变动文书事实查询被跳过（BusinessId 为空）: BusinessType={businessType}, Classification={classification}");
            }

            // 近亲属备案独立输出（列表页"输出档案"入口）：无申请分类可依据，按模板名直取两张表
            List<Template> templates;
            if (string.Equals(businessType, NearRelativeConstants.BusinessType, StringComparison.OrdinalIgnoreCase))
            {
                templates = new List<Template>();
                foreach (var templateName in new[] { NearRelativeConstants.TemplateLinkInfo, NearRelativeConstants.TemplateObjectInfo })
                {
                    var nameResult = await _templateService.GetByNameAsync(templateName);
                    if (nameResult.IsFailure)
                        throw new BusinessException(nameResult.ErrorCode!, nameResult.Message!);
                    if (nameResult.Value != null)
                        templates.Add(nameResult.Value);
                }
            }
            else
            {
                templates = await _templateService.GetByCategoriesAsync(categories);
            }
            Templates.Clear();

            foreach (var t in templates)
            {
                // 仅出文书：分类模板先按白名单收窄
                if (IsTemplateFilterMode && !templateFilter!.Contains(t.Name, StringComparer.Ordinal))
                    continue;

                var config = ResolveTemplateConfig(t.ConfigJson, classification);
                var item = new TemplateSelectItem
                {
                    TemplateId = t.Id,
                    Name = t.Name,
                    FileType = t.FileType,
                    IsSelected = true,
                    BaseCopies = config.Copies,
                    IsDirectoryTemplate = config.IsDirectory,
                    DirectoryDateField = config.DirectoryDateField,
                    IsCoverTemplate = Constants.DocumentTemplateNames.IsCover(t.Name),
                    SortOrder = t.SortOrder
                };

                // 模板17/18（无土地证明/村级土地说明）按是否有土地二选一
                if (t.Name.Contains("无土地证明"))
                {
                    var hasLand = HasFamilyLand(fieldData);
                    if (hasLand)
                    {
                        item.IsApplicable = false;
                        item.IsSelected = false;
                    }
                }
                else if (t.Name.Contains("村级土地说明"))
                {
                    var hasLand = HasFamilyLand(fieldData);
                    if (!hasLand)
                    {
                        item.IsApplicable = false;
                        item.IsSelected = false;
                    }
                }
                // 一事一议申报表：仅当该申请为一事一议申请（存在申报表数据）时显示
                else if (t.Name.Contains("一事一议申报表"))
                {
                    var hasSpecialApprovalData = fieldData != null
                        && fieldData.TryGetValue(FieldKeys.SPECIAL_APPROVAL_MATTERS, out var matters)
                        && !string.IsNullOrWhiteSpace(matters);
                    if (!hasSpecialApprovalData)
                    {
                        item.IsApplicable = false;
                        item.IsSelected = false;
                    }
                }
                // 近亲属两表：仅当该申请存在关联备案时显示
                else if (t.Name.Contains("近亲属"))
                {
                    var hasNearRelativeData = fieldData != null
                        && fieldData.TryGetValue(FieldKeys.NEAR_RELATIVE_HAS_DATA, out var flag)
                        && flag == "1";
                    if (!hasNearRelativeData)
                    {
                        item.IsApplicable = false;
                        item.IsSelected = false;
                    }
                }

                // 变动文书按人员属性/变动事实过滤：无渐退期不出渐退审批表、无减发不出保障金减少、
                // 无成员变动不出增减员表、无停保/资金变更事实不出告知书等（单点口径见
                // ChangeDocumentApplicabilityService；docFacts=null=事实查询失败，全放行不误杀）。
                // 补打与正常输出共用本清单加载，即共用本过滤（原补打侧无预勾选名单致全量默认打印）。
                if (docFacts != null && !IsTemplateFilterMode
                    && !_changeDocApplicability.IsTemplateApplicable(t.Categories, docFacts))
                {
                    item.IsApplicable = false;
                    item.IsSelected = false;
                }

                Templates.Add(item);
            }

            // 文书模式按档案类型补挂入户调查表：变动分类集（DocumentOperationCategories）不含分类码，
            // 而「档案_入户调查表」「档案_特困入户调查表_特困人员」挂的是 12 个档案分类码 → 变动文书清单取不到。
            // 取数走 GetByCategoriesAsync([classification])：低保/边缘/刚性 → 档案_入户调查表，特困 → 特困版，
            // 选哪张由模板 categories 决定（跟档案类型挂钩），不硬编码模板名映射。仅列出处供下方预勾选强制勾选。
            // 死亡停保旧档（绝户/停旧建新旧档）不补挂：家庭已不存在，无从入户调查（与变动文书过滤同口径）。
            var addedSurveyNames = new List<string>();
            if (!IsTemplateFilterMode && isDocumentMode
                && businessType is "FamilyApplication" or "EconomicReview"
                && !string.IsNullOrEmpty(classification)
                && !(docFacts?.IsDeathStoppedArchive ?? false))
            {
                var byClassification = await _templateService.GetByCategoriesAsync(new[] { classification }, CancellationToken);
                foreach (var t in byClassification)
                {
                    if (!t.Name.Contains("入户调查") || Templates.Any(x => x.TemplateId == t.Id)) continue;
                    var surveyConfig = ResolveTemplateConfig(t.ConfigJson, classification);
                    Templates.Add(new TemplateSelectItem
                    {
                        TemplateId = t.Id,
                        Name = t.Name,
                        FileType = t.FileType,
                        IsSelected = true,
                        BaseCopies = surveyConfig.Copies,
                        IsDirectoryTemplate = surveyConfig.IsDirectory,
                        DirectoryDateField = surveyConfig.DirectoryDateField,
                        IsCoverTemplate = Constants.DocumentTemplateNames.IsCover(t.Name),
                        SortOrder = t.SortOrder
                    });
                    addedSurveyNames.Add(t.Name);
                }
            }

            // 排序：封面 → 目录 → 其余（按 sort_order）；不适用模板不入列表
            var coverTemplates = Templates.Where(t => t.IsCoverTemplate && t.IsApplicable).ToList();
            var directoryTemplates = Templates.Where(t => t.IsDirectoryTemplate && !t.IsCoverTemplate && t.IsApplicable).ToList();
            var otherTemplates = Templates.Where(t => !t.IsDirectoryTemplate && !t.IsCoverTemplate && t.IsApplicable)
                                          .OrderBy(t => t.SortOrder).ToList();
            Templates.Clear();
            foreach (var t in coverTemplates) Templates.Add(t);
            foreach (var t in directoryTemplates) Templates.Add(t);
            foreach (var t in otherTemplates) Templates.Add(t);

            // 检查赡养费承诺书模板是否适用（无赡养人时跳过）
            var supporterTemplate = Templates.FirstOrDefault(t => Constants.DocumentTemplateNames.IsSupporterPledge(t.Name));
            if (supporterTemplate != null)
            {
                var hasSupporters = supporterData != null && supporterData.Count > 0;
                if (!hasSupporters)
                {
                    supporterTemplate.IsApplicable = false;
                    supporterTemplate.IsSelected = false;
                    Templates.Remove(supporterTemplate);
                }
            }

            // 低收入人口家庭认定等救助申请档案（FamilyApplication）：在列表末尾添加核查报告条目
            //（仅出文书模式不追加）。
            // 资产核查（AssetVerification）自身的档案制作**不追加**：核查报告的打印/保存归
            // 「月度审核」（MonthlyAssetAuditViewModel，落 {输出根}\核查报告\），档案制作流程不重复出现。
            if (!IsTemplateFilterMode
                && businessType == "FamilyApplication")
            {
                var checkId = await ResolveCheckIdAsync(businessType, businessId, fieldData, CancellationToken);
                if (checkId.HasValue)
                {
                    var reportExists = await _pdfVerificationService.ReportExistsByCheckIdAsync(checkId.Value, CancellationToken);
                    var hasReport = reportExists.IsSuccess && reportExists.Value;
                    Templates.Add(new TemplateSelectItem
                    {
                        IsVerificationReport = true,
                        CheckId = checkId.Value,
                        Name = "核查报告",
                        ReportStatus = hasReport ? "已上传" : "未上传",
                        IsSelected = hasReport,
                        IsApplicable = hasReport,
                        BaseCopies = 1,
                        SortOrder = 999
                    });
                }
            }

            // 《档案_变更告知书》：**模板全库唯一（nc_biz_templates 241），"停保 / 不予认定 / 终止 / 待遇调整"
            // 都是同一个模板的不同方向设定**——方向由装配端 ArchiveProductionViewModel 按事实分支填 NOTICE_*
            //（死亡停止 → 停保·不予认定(含归档终结拒保兜底) → 待遇调整 → 三者皆空即空白告知书）。
            // 本页只决定"这个模板出不出现在清单里"，不碰方向。
            // 出处两条：① 基础分类命中（模板挂变动叶子「档案_变更告知书」后由 GetByCategoriesAsync 带出，
            //   再由 IsTemplateApplicable 按 NoticeApplicable 过滤）；② 按名追加（下方门，仅基础清单未命中时补）。
            var noticeAllowed = !IsTemplateFilterMode
                || templateFilter!.Contains(DocumentTemplateNames.ChangeNotice, StringComparer.Ordinal);
            // 适用事实（与装配端方向一一对应，单点权威 = ChangeDocumentFacts.NoticeApplicable）：
            //   ① 户主死亡整户停止 → IsHouseholdDeathStop（整户已死，须告知终止；
            //      **不受"单人保排除"限制**——死亡是客观事件，无论原分类是否单人保都应告知）；
            //   ② 停保/不予认定 → IsIncomeStopChange（复核/成员变更收入超标 triggered_stop），
            //      或归档终结拒保兜底 IsArchiveRefusedStop（只置 Stopped、无变更记录，app95 同型）；
            //   ③ 待遇调整 → HasFundChangeRegistration（链上有"保障金变更登记"，旧/新同属低保且
            //      户月保障金差 ≥ 阈值，仍在享受待遇时同样需要告知书）。
            // 单人保不产生停保、也不写保障金变更登记 → ②③ 受单人保排除，无需单独放行。
            // 快照查询失败/未取到 → 各事实按 false，等价原 catch→false 降级，仅白名单模式照常放行。
            // 判定提为局部变量：两分支各记一条 biz 日志，把"静默不成立"变成可观测（2026-11）。
            // 口径单点化：事实分支一律取 ChangeDocumentFacts.NoticeApplicable（含归档终结拒保兜底、
            // 死亡整户停、停保/待遇调整 + 单人保排除），此处只叠加"白名单模式下用户主动选择即放行"——
            // 与原三元 OR 表达式逐案等价（N || (白名单 && 非单人保)），改事实口径只须改 NoticeApplicable 一处。
            var appendChangeNotice = noticeAllowed
                && businessType is "FamilyApplication" or "EconomicReview"
                && ((docFacts?.NoticeApplicable ?? false)
                    || (IsTemplateFilterMode
                        && !ClassificationConstants.IsCodeSingleRescue(classification)));
            if (appendChangeNotice)
            {
                var changeNoticeResult = await _templateService.GetByNameAsync(DocumentTemplateNames.ChangeNotice);
                if (changeNoticeResult.IsFailure)
                    throw new BusinessException(changeNoticeResult.ErrorCode!, changeNoticeResult.Message!);
                var changeNotice = changeNoticeResult.Value;
                if (changeNotice == null)
                {
                    _logger.Warn($"模板不存在，告知书无法追加: {DocumentTemplateNames.ChangeNotice}");
                }
                else if (Templates.Any(t => t.TemplateId == changeNotice.Id))
                {
                    // 基础分类已命中（模板挂了变动叶子「档案_变更告知书」）→ 去重跳过，非异常
                    _logger.LogBusiness("告知书已在输出清单（基础分类命中，无需追加）",
                        ("ApplicationId", factsBusinessId));
                }
                else
                {
                    var config = ResolveTemplateConfig(changeNotice.ConfigJson, classification);
                    Templates.Add(new TemplateSelectItem
                    {
                        TemplateId = changeNotice.Id,
                        Name = changeNotice.Name,
                        FileType = changeNotice.FileType,
                        IsSelected = true,
                        BaseCopies = config.Copies,
                        SortOrder = 800
                    });
                    _logger.LogBusiness("告知书已追加入输出清单",
                        ("ApplicationId", factsBusinessId),
                        ("Mode", IsTemplateFilterMode ? "白名单" : "事实门"));
                }
            }
            else if (businessType is "FamilyApplication" or "EconomicReview")
            {
                // 仅低收入人口域记（其余域本就不产告知书，避免每次输出刷一条噪音）
                _logger.LogBusiness("告知书未追加（清单门未成立）",
                    ("ApplicationId", factsBusinessId),
                    ("NoticeAllowed", noticeAllowed),
                    ("FactsLoaded", docFacts != null),
                    ("IncomeStop", docFacts?.IsIncomeStopChange ?? false),
                    ("FundChangeRegistration", docFacts?.HasFundChangeRegistration ?? false),
                    ("ArchiveRefusedStop", docFacts?.IsArchiveRefusedStop ?? false),
                    ("HouseholdDeathStop", docFacts?.IsHouseholdDeathStop ?? false),
                    ("TemplateFilterMode", IsTemplateFilterMode),
                    ("Classification", classification));
            }

            // 白名单外的模板（核查报告等）一律剔除
            if (IsTemplateFilterMode)
            {
                var allowed = new HashSet<string>(templateFilter!, StringComparer.Ordinal);
                var extra = Templates.Where(t => !allowed.Contains(t.Name)).ToList();
                foreach (var x in extra) Templates.Remove(x);
            }

            // 输出文书预勾选：列表仍展示分类内模板，仅按名单控制勾选状态
            // 告知书若在列表中恒勾（业务必选）
            if (!IsTemplateFilterMode && preselect is { Length: > 0 })
            {
                var pre = new HashSet<string>(preselect, StringComparer.Ordinal);
                foreach (var t in Templates)
                    t.IsSelected = pre.Contains(t.Name);
                var notice = Templates.FirstOrDefault(t => t.Name == DocumentTemplateNames.ChangeNotice);
                if (notice != null) notice.IsSelected = true;
                // 补挂的入户调查表恒勾（与告知书同待遇）：变更流程 Prefilter 名单不含它，不强制会被置为不勾
                foreach (var surveyName in addedSurveyNames)
                {
                    var survey = Templates.FirstOrDefault(t => t.Name == surveyName);
                    if (survey != null) survey.IsSelected = true;
                }
            }

            if (Templates.Count > 0)
            {
                SelectedPreviewTemplate = Templates.FirstOrDefault(t => t.IsSelected) ?? Templates[0];
                var totalBaseCopies = Templates.Where(x => x.IsSelected).Sum(x => x.BaseCopies);
                var dirCount = directoryTemplates.Count;
                StatusText = IsTemplateFilterMode
                    ? $"仅出文书：{Templates.Count} 个模板"
                    : isDocumentMode
                        ? $"输出文书：{Templates.Count} 个模板，已勾选 {Templates.Count(x => x.IsSelected)} 个"
                        : dirCount > 0
                            ? $"找到 {Templates.Count} 个模板（含{dirCount}个目录），总打印份数合计 {totalBaseCopies}"
                            : $"找到 {Templates.Count} 个模板，总打印份数合计 {totalBaseCopies}";
            }
            else
            {
                var missing = IsTemplateFilterMode
                    ? $"模板未上传：{string.Join("、", templateFilter!)}"
                    : IsStandaloneOutput
                        ? $"模板未上传：{NearRelativeConstants.TemplateLinkInfo}、{NearRelativeConstants.TemplateObjectInfo}"
                        : "未找到匹配模板";
                StatusText = missing;
                await _dialogService.DisplayAlertAsync("提示",
                    IsTemplateFilterMode || IsStandaloneOutput ? missing : "未找到匹配的打印模板", "确定");
            }

            LoadPrinterList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "失败");
            StatusText = $"加载模板失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 从已取回的 config_json 解析模板配置（无 DB 往返）。
    /// 起因：GetByCategoriesAsync/GetAllAsync/GetByNameAsync 的 SELECT 已含 config_json，
    /// 而旧实现逐模板 GetByIdAsync 回查 = 每次进输出页 20~30 条纯浪费 N+1（2026-11 性能收口）。
    /// 非法/损坏 config_json 一律回退默认值 (1, false, "")，不向外抛异常。
    /// </summary>
    private static (int Copies, bool IsDirectory, string DirectoryDateField) ResolveTemplateConfig(string? configJson, string classification)
    {
        if (string.IsNullOrEmpty(configJson))
            return (1, false, string.Empty);

        var config = NewCosmos.Services.Templates.TemplateConfig.FromJson(configJson);

        // 份数口径唯一来源：分类映射 copiesByClassification → 模板级默认 copies → 1
        //（TemplateConfig.ResolveCopies，与手机推送打印同源；见 Helpers\TemplateCopiesResolver）
        return (config.ResolveCopies(classification), config.IsDirectoryTemplate, config.DirectoryDateField ?? string.Empty);
    }

    /// <summary>
    /// 判断家庭是否有土地（依据档案字段 FAMILY_LAND_AREA，非 0/非空即有土地）。
    /// 入参取自 InitializeCoreAsync 开头的快照，避免 await 期间静态上下文被清导致误判。
    /// </summary>
    private static bool HasFamilyLand(Dictionary<string, string>? fieldData)
    {
        if (fieldData == null) return false;
        if (!fieldData.TryGetValue(FieldKeys.FAMILY_LAND_AREA, out var area))
            return false;
        if (string.IsNullOrWhiteSpace(area)) return false;
        return area != "0" && decimal.TryParse(area, out var value) && value > 0;
    }

    private void LoadPrinterList()
    {
        try
        {
            var printers = _printerService.GetInstalledPrinters();
            PrinterNames.Clear();
            foreach (var p in printers)
            {
                PrinterNames.Add(p);
            }

            var defaultPrinter = _printerService.GetDefaultPrinter();
            if (!string.IsNullOrEmpty(defaultPrinter) && PrinterNames.Contains(defaultPrinter))
            {
                SelectedPrinter = defaultPrinter;
            }
            else if (PrinterNames.Count > 0)
            {
                SelectedPrinter = PrinterNames[0];
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "失败");
        }
    }

    // ========================
    //  预览
    // ========================

    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (SelectedPreviewTemplate == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择一个模板", "确定");
            return;
        }

        // 核查报告（PDF 非模板产物）：走专用预览路径（TemplateId=0，按模板渲染必失败）
        if (SelectedPreviewTemplate.IsVerificationReport)
        {
            await PreviewReportAsync(SelectedPreviewTemplate);
            return;
        }

        if (PrintNavigationData.FieldData == null || PrintNavigationData.FieldData.Count == 0)
        {
            await _dialogService.DisplayAlertAsync("提示", "没有可用的打印数据", "确定");
            return;
        }

        try
        {
            IsBusy = true;
            StatusText = "正在生成预览...";

            // 目录模板：使用目录专属字段和行数据
            // 传副本，防止 Service 层展平索引槽时原地污染共享的 PrintNavigationData.FieldData
            var fields = new Dictionary<string, string>(PrintNavigationData.FieldData, StringComparer.Ordinal);
            var tableRows = PrintNavigationData.TableData ?? new();
            if (SelectedPreviewTemplate.IsDirectoryTemplate)
            {
                var tl = _timelineService.GetTimelineForApplicationDate(await ResolveApplicationDateForTimelineAsync(), TimelineType.BusinessProcess);
                fields = await BuildDirectoryFieldsAsync(fields);
                tableRows = BuildDirectoryRows(tl.MeetingDate);
            }
            // 赡养费承诺书：使用赡养人数据
            else if (Constants.DocumentTemplateNames.IsSupporterPledge(SelectedPreviewTemplate.Name))
            {
                tableRows = PrintNavigationData.SupporterTableData ?? new();
            }
            // 近亲属备案两表：使用备案对数据（每对一页，copiesBySupporter 模式）
            else if (SelectedPreviewTemplate.Name.Contains("近亲属"))
            {
                tableRows = PrintNavigationData.NearRelativePairs ?? new();
            }

            var pdfResult = await _printExecuteService.GeneratePreviewPdfAsync(
                SelectedPreviewTemplate.TemplateId,
                fields,
                tableRows,
                CancellationToken);

            if (pdfResult.IsSuccess && pdfResult.Value != null)
            {
                _currentPdfData = pdfResult.Value;
                TryDeleteFile(_currentTempPreviewPath);
                var tempPath = Path.Combine(OutputPathHelper.GetTempDirectory(), $"preview_{Guid.NewGuid()}.pdf");
                await _fileService.WriteAllBytesAsync(tempPath, pdfResult.Value, CancellationToken);
                _currentTempPreviewPath = tempPath;
                PdfFilePath = tempPath;
                HasPdf = true;
                StatusText = "预览已生成";
                _logger.LogBusiness("生成预览成功",
                    ("Template", SelectedPreviewTemplate.Name),
                    ("BatchNo", _batchNo));
                // 预览只是临时 PDF（落 {输出根}/temp，会被清理），不进「输出文件」列表——
                // 列表只收录真正落盘的产物；「保存」动作才走生成不打印的正式路径。
            }
            else
            {
                await _dialogService.DisplayAlertAsync("错误", $"生成预览失败: {pdfResult.Message}", "确定");
                StatusText = "预览生成失败";
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "失败");
            await _dialogService.DisplayAlertAsync("错误", $"预览异常: {ex.Message}", "确定");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ========================
    //  打印
    // ========================

    [RelayCommand]
    private async Task PrintAsync()
    {
        if (SelectedPreviewTemplate == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择一个模板", "确定");
            return;
        }
        await PrintCoreAsync(SelectedPreviewTemplate, interactive: true);
    }

    /// <summary>「保存」（单模板，F1 工具栏 / F3 补打面板）：当前选择模板仅生成落盘，不打印</summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (SelectedPreviewTemplate == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择一个模板", "确定");
            return;
        }
        await PrintCoreAsync(SelectedPreviewTemplate, interactive: true, printToPrinter: false);
    }

    /// <summary>
    /// 单模板打印/生成核心（interactive=false 供统一补打中心批量静默调用：无封面确认/失败弹窗，仅记录状态）。
    /// 直接以传入 template 打印，不依赖 SelectedPreviewTemplate，避免批量时报"请先选择一个模板"。
    /// printToPrinter=false 为「保存」动作：同一条生成链路但不调打印机（四件套语义见 DocumentActionText）。
    /// </summary>
    public async Task<bool> PrintCoreAsync(TemplateSelectItem template, bool interactive, bool printToPrinter = true)
    {
        var action = printToPrinter ? "打印" : "保存";
        if (template == null || PrintNavigationData.FieldData == null)
        {
            if (interactive)
                await _dialogService.DisplayAlertAsync("提示", "没有可用的打印数据", "确定");
            else
                _logger.Warn("批量打印：模板或打印数据为空，已跳过");
            return false;
        }

        // 核查报告（PDF 非模板产物）：走专用落盘+WebView2 打印路径（TemplateId=0，按模板打印必失败）
        if (template.IsVerificationReport)
        {
            var reportFailedItems = new List<(string Name, string Message)>();
            var reportOk = await PrintVerificationReportAsync(template, reportFailedItems, printToPrinter);
            if (!reportOk && interactive)
                await _dialogService.DisplayAlertAsync("失败",
                    reportFailedItems.FirstOrDefault().Message ?? "核查报告处理失败", "确定");
            return reportOk;
        }

        // 封面模板打印前确认：需先插入牛皮纸（批量静默模式与「保存」跳过——保存不调打印机）
        if (interactive && printToPrinter && Constants.DocumentTemplateNames.IsCover(template.Name))
        {
            var confirmed = await _dialogService.DisplayAlertAsync(
                "打印确认",
                "请确认已插入牛皮纸，是否继续打印封面？",
                "继续",
                "取消");
            if (!confirmed) return false;
        }

        try
        {
            IsBusy = true;
            StatusText = $"正在{action}...";

            var fields = new Dictionary<string, string>(PrintNavigationData.FieldData, StringComparer.Ordinal);
            var tableRows = PrintNavigationData.TableData ?? new();

            // 目录模板：使用目录专属字段和行数据
            if (template.IsDirectoryTemplate)
            {
                var tl = _timelineService.GetTimelineForApplicationDate(await ResolveApplicationDateForTimelineAsync(), TimelineType.BusinessProcess);
                fields = await BuildDirectoryFieldsAsync(fields);
                tableRows = BuildDirectoryRows(tl.MeetingDate);
            }
            // 赡养费承诺书：使用赡养人数据
            else if (Constants.DocumentTemplateNames.IsSupporterPledge(template.Name))
            {
                tableRows = PrintNavigationData.SupporterTableData ?? new();
            }
            // 近亲属备案两表：使用备案对数据（每对一页，copiesBySupporter 模式）
            else if (template.Name.Contains("近亲属"))
            {
                tableRows = PrintNavigationData.NearRelativePairs ?? new();
            }

            var applicantName = fields.TryGetValue("APPLICANT_NAME", out var name) ? name : "未知";
            var applicantIdCard = fields.TryGetValue("APPLICANT_ID_CARD", out var idCard) ? idCard : "000000000000000000";
            applicantIdCard = applicantIdCard.Length > 6 ? applicantIdCard : "000000000000000000";

            var actualCopies = template.BaseCopies * Copies;
            _logger.Info($"{action}: 模板={template.Name}");

            var result = await _printExecuteService.ExecutePrintAsync(
                template.TemplateId,
                template.Name,
                PrintNavigationData.BusinessType,
                PrintNavigationData.BusinessId,
                _batchNo,
                fields,
                tableRows,
                applicantName,
                applicantIdCard,
                printToPrinter ? SelectedPrinter : null!,
                actualCopies,
                IsDuplex,
                printToPrinter: printToPrinter,
                CancellationToken);

            if (result.IsSuccess)
            {
                _logger.LogBusiness($"{action}完成",
                    ("Template", template.Name),
                    ("BatchNo", _batchNo),
                    ("RecordId", result.Value.Id));
                StatusText = $"{action}完成";
                AddOutputFiles(template.TemplateId, template.Name, result.Value.FilePath, result.Value.PdfPath);
                return true;
            }

            StatusText = $"{action}失败: {result.Message}";
            if (interactive)
                await _dialogService.DisplayAlertAsync("错误", $"{action}失败: {result.Message}", "确定");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "失败");
            StatusText = $"{action}异常: {ex.Message}";
            if (interactive)
                await _dialogService.DisplayAlertAsync("错误", $"{action}异常: {ex.Message}", "确定");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>把生成产物（源文件/PDF）加入输出列表（打印与保存共用）</summary>
    private void AddOutputFiles(long templateId, string templateName, string? sourceFilePath, string? pdfPath)
    {
        // 将源文件（Excel/Word）加入输出列表
        if (!string.IsNullOrEmpty(sourceFilePath) && _fileService.FileExists(sourceFilePath))
        {
            var sourceExt = Path.GetExtension(sourceFilePath);
            var sourceType = sourceExt.ToLowerInvariant() switch
            {
                ".xlsx" => "Excel",
                ".docx" => "Word",
                _ => "文件"
            };
            OutputFiles.Add(new OutputFileItem
            {
                TemplateId = templateId,
                FileName = Path.GetFileName(sourceFilePath),
                FileType = sourceType,
                Status = "已生成",
                IsCompleted = true,
                FilePath = sourceFilePath,
                TemplateName = templateName
            });
        }

        // 将PDF也加入输出列表
        if (!string.IsNullOrEmpty(pdfPath) && _fileService.FileExists(pdfPath))
        {
            OutputFiles.Add(new OutputFileItem
            {
                TemplateId = templateId,
                FileName = Path.GetFileName(pdfPath),
                FileType = "PDF",
                Status = "已生成",
                IsCompleted = true,
                FilePath = pdfPath,
                TemplateName = templateName
            });
        }
    }

}

public partial class TemplateSelectItem : ObservableObject
{
    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private long _templateId;

    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>模板类型规范值（xlsx/docx），来自 nc_biz_templates.file_type</summary>
    [ObservableProperty]
    private string _fileType = string.Empty;

    [ObservableProperty]
    private bool _isApplicable = true;

    [ObservableProperty]
    private int _baseCopies = 1;

    [ObservableProperty]
    private bool _isDirectoryTemplate;

    [ObservableProperty]
    private string _directoryDateField = string.Empty;

    [ObservableProperty]
    private bool _isCoverTemplate;

    [ObservableProperty]
    private int _sortOrder;

    /// <summary>是否为核查报告条目（非模板系统产物）</summary>
    [ObservableProperty]
    private bool _isVerificationReport;

    /// <summary>关联的核查记录ID（nc_biz_asset_checks.id）</summary>
    [ObservableProperty]
    private long _checkId;

    /// <summary>核查报告状态：已上传 / 未上传</summary>
    [ObservableProperty]
    private string _reportStatus = string.Empty;
}

public partial class OutputFileItem : ObservableObject
{
    [ObservableProperty]
    private long _templateId;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _fileType = string.Empty;

    [ObservableProperty]
    private string _status = "未处理";

    [ObservableProperty]
    private bool _isCompleted;

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private int _baseCopies = 1;

    [ObservableProperty]
    private string _templateName = string.Empty;

    /// <summary>是否 PDF 行（仅 PDF 可页面内预览）；构造时 FileType 即定，无需变更通知</summary>
    public bool IsPdf => string.Equals(FileType, "PDF", System.StringComparison.OrdinalIgnoreCase);
}
