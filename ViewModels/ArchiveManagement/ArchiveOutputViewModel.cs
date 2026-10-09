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
    private readonly IChangeService _changeService = null!;

    /// <summary>
    /// 由 Page 注入：WebView2 打印 PDF 的回调（pdfPath → Task）。用于核查报告等非 Office/WPS 格式打印。
    /// </summary>
    public Func<string, Task>? WebViewPrintPdfFunc { get; set; }

    private byte[] _currentPdfData = null!;
    private string _batchNo = string.Empty;
    private string _currentTempPreviewPath = string.Empty;

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
        IChangeService changeService)
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
        _changeService = changeService;
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

    /// <summary>「完成归档」按钮可见：既非过滤模式也非文档模式</summary>
    public bool IsCompleteArchiveVisible => !IsTemplateFilterMode && !IsDocumentMode;

    /// <summary>「完成工单」按钮可见：与「完成归档」互斥（文档/过滤模式下替代留痕办理完成）</summary>
    public bool IsWorkOrderCompleteVisible => IsTemplateFilterMode || IsDocumentMode;

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
    /// 判定该档案是否为"复核/成员变更收入超标停保"（CategoryStop 且 triggered_stop=true 且新分类为停保类）。
    /// 记录挂在旧档案上：既按 application_id 匹配（经济复核输出旧档案），
    /// 也按 new_application_id 匹配（成员变更输出停旧建新后的新档案）。
    /// new_classification ∈ 停保类 是护栏：跨类转入/历史误标（triggered_stop=true）不会误发告知书。
    /// 用于决定是否追加《档案_变更告知书》模板，避免户主死亡/降档误生成。
    /// </summary>
    private async Task<bool> IsIncomeStopChangeAsync(long? applicationId)
    {
        if (applicationId is not > 0) return false;
        try
        {
            var result = await _changeService.HasTriggeredCategoryStopAsync(
                applicationId.Value, ClassificationConstants.StopCategoryCodes, CancellationToken);
            return result.IsSuccess && result.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "判定停保变更失败");
            return false;
        }
    }

    /// <summary>
    /// 加载模板清单（单飞串行，见 <see cref="_initializeGate"/>）。
    /// </summary>
    public async Task InitializeAsync()
    {
        await _initializeGate.WaitAsync();
        try
        {
            await InitializeCoreAsync();
        }
        finally
        {
            _initializeGate.Release();
        }
    }

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

            var templates = await _templateService.GetByCategoriesAsync(categories);
            Templates.Clear();

            foreach (var t in templates)
            {
                // 仅出文书：分类模板先按白名单收窄
                if (IsTemplateFilterMode && !templateFilter!.Contains(t.Name, StringComparer.Ordinal))
                    continue;

                var config = await LoadTemplateConfigAsync(t.Id, classification);
                var item = new TemplateSelectItem
                {
                    TemplateId = t.Id,
                    Name = t.Name,
                    FileType = t.FileType,
                    IsSelected = true,
                    BaseCopies = config.Copies,
                    IsDirectoryTemplate = config.IsDirectory,
                    DirectoryDateField = config.DirectoryDateField,
                    IsCoverTemplate = t.Name.Contains("封面"),
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

                Templates.Add(item);
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
            var supporterTemplate = Templates.FirstOrDefault(t => t.Name.Contains("赡养费承诺书"));
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

            // 资产核查档/低收入人口家庭认定等救助申请档案：在列表末尾添加核查报告条目（仅出文书模式不追加）
            if (!IsTemplateFilterMode
                && businessType is "AssetVerification" or "FamilyApplication")
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

            // 停保/停保变更档案（含经济复核/成员变更停保流）：额外加载"档案_变更告知书"模板（其 categories 仅含停发/不符合分类码）
            // 仅"实际停保"（复核/成员变更收入超标）追加，排除户主死亡与降档；
            // 不按加载档案的 Status 判断：成员变更输出的是停旧建新后的 Draft 新档案，停保事实由变更记录承载
            // 单人保不产生停保，排除其变更告知书
            // 仅出文书模式：告知书若在白名单内则无条件追加（用户主动选择，不走停保三重门）
            var noticeAllowed = !IsTemplateFilterMode
                || templateFilter!.Contains(DocumentTemplateNames.ChangeNotice, StringComparer.Ordinal);
            if (noticeAllowed
                && businessType is "FamilyApplication" or "EconomicReview"
                && !ClassificationConstants.IsCodeSingleRescue(classification)
                && (IsTemplateFilterMode || await IsIncomeStopChangeAsync(businessId)))
            {
                var changeNoticeResult = await _templateService.GetByNameAsync(DocumentTemplateNames.ChangeNotice);
                if (changeNoticeResult.IsFailure)
                    throw new BusinessException(changeNoticeResult.ErrorCode!, changeNoticeResult.Message!);
                var changeNotice = changeNoticeResult.Value;
                if (changeNotice != null && !Templates.Any(t => t.TemplateId == changeNotice.Id))
                {
                    var config = await LoadTemplateConfigAsync(changeNotice.Id, classification);
                    Templates.Add(new TemplateSelectItem
                    {
                        TemplateId = changeNotice.Id,
                        Name = changeNotice.Name,
                        FileType = changeNotice.FileType,
                        IsSelected = true,
                        BaseCopies = config.Copies,
                        SortOrder = 800
                    });
                }
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
                    : "未找到匹配模板";
                StatusText = missing;
                await _dialogService.DisplayAlertAsync("提示",
                    IsTemplateFilterMode ? missing : "未找到匹配的打印模板", "确定");
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

    private async Task<(int Copies, bool IsDirectory, string DirectoryDateField)> LoadTemplateConfigAsync(long templateId, string classification)
    {
        try
        {
            var templateResult = await _templateService.GetByIdAsync(templateId);
            if (templateResult.IsFailure)
                throw new BusinessException(templateResult.ErrorCode!, templateResult.Message!);
            var template = templateResult.Value;
            if (template == null || string.IsNullOrEmpty(template.ConfigJson))
                return (1, false, string.Empty);

            var config = NewCosmos.Services.Templates.TemplateConfig.FromJson(template.ConfigJson);

            var copies = 1;
            if (config.CopiesByClassification != null && config.CopiesByClassification.Count > 0
                && !string.IsNullOrEmpty(classification)
                && config.CopiesByClassification.TryGetValue(classification, out var configuredCopies))
            {
                copies = configuredCopies;
            }

            return (copies, config.IsDirectoryTemplate, config.DirectoryDateField ?? string.Empty);
        }
        catch
        {
            return (1, false, string.Empty);
        }
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
                var tl = _timelineService.GetTimelineForDate(await ResolveArchiveAnchorAsync(), TimelineType.BusinessProcess);
                fields = await BuildDirectoryFieldsAsync(fields);
                tableRows = BuildDirectoryRows(tl.MeetingDate);
            }
            // 赡养费承诺书：使用赡养人数据
            else if (SelectedPreviewTemplate.Name.Contains("赡养费承诺书"))
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
        if (interactive && printToPrinter && template.Name.Contains("封面"))
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
                var tl = _timelineService.GetTimelineForDate(await ResolveArchiveAnchorAsync(), TimelineType.BusinessProcess);
                fields = await BuildDirectoryFieldsAsync(fields);
                tableRows = BuildDirectoryRows(tl.MeetingDate);
            }
            // 赡养费承诺书：使用赡养人数据
            else if (template.Name.Contains("赡养费承诺书"))
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
