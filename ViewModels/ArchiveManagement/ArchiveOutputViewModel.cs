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
    private async Task<long?> ResolveCheckIdAsync(CancellationToken ct)
    {
        // 资产核查档：BusinessId 即 nc_biz_asset_checks.id
        if (PrintNavigationData.BusinessType == "AssetVerification" && PrintNavigationData.BusinessId.HasValue)
            return PrintNavigationData.BusinessId.Value;

        try
        {
            // 从 FieldData 中获取身份证号（兼容两种键名），用于反查该人最新核查记录
            if (PrintNavigationData.FieldData == null) return null;
            string? idCard = null;
            if (!PrintNavigationData.FieldData.TryGetValue(FieldKeys.APPLICANT_ID_CARD, out idCard)
                && !PrintNavigationData.FieldData.TryGetValue("ApplicantIdCard", out idCard))
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

    public async Task InitializeAsync()
    {
        Title = "档案输出";
        _batchNo = Guid.NewGuid().ToString("N").ToUpper();

        var categories = PrintNavigationData.OutputCategories is { Length: > 0 } outCats
            ? outCats
            : GetCategoriesByBusinessType(PrintNavigationData.BusinessType);
        var classification = PrintNavigationData.Classification;
        var templateFilter = PrintNavigationData.TemplateFilter;
        var preselect = PrintNavigationData.PrefilterTemplateNames;
        IsTemplateFilterMode = templateFilter is { Length: > 0 };
        IsDocumentMode = !IsTemplateFilterMode
            && (PrintNavigationData.OutputCategories is { Length: > 0 }
                || PrintNavigationData.OperationOverride != null
                || preselect is { Length: > 0 });
        var isDocumentMode = IsDocumentMode;
        _logger.LogBusiness("加载打印模板",
            ("BusinessType", PrintNavigationData.BusinessType),
            ("Categories", string.Join(",", categories)),
            ("Classification", classification),
            ("TemplateFilter", templateFilter == null ? "(整档)" : string.Join("|", templateFilter)),
            ("Prefilter", preselect == null ? "(无)" : string.Join("|", preselect)));

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
                    var hasLand = HasFamilyLand();
                    if (hasLand)
                    {
                        item.IsApplicable = false;
                        item.IsSelected = false;
                    }
                }
                else if (t.Name.Contains("村级土地说明"))
                {
                    var hasLand = HasFamilyLand();
                    if (!hasLand)
                    {
                        item.IsApplicable = false;
                        item.IsSelected = false;
                    }
                }
                // 一事一议申报表：仅当该申请为一事一议申请（存在申报表数据）时显示
                else if (t.Name.Contains("一事一议申报表"))
                {
                    var hasSpecialApprovalData = PrintNavigationData.FieldData != null
                        && PrintNavigationData.FieldData.TryGetValue(FieldKeys.SPECIAL_APPROVAL_MATTERS, out var matters)
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
                    var hasNearRelativeData = PrintNavigationData.FieldData != null
                        && PrintNavigationData.FieldData.TryGetValue(FieldKeys.NEAR_RELATIVE_HAS_DATA, out var flag)
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
                var supporterData = PrintNavigationData.SupporterTableData;
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
                && PrintNavigationData.BusinessType is "AssetVerification" or "FamilyApplication")
            {
                var checkId = await ResolveCheckIdAsync(CancellationToken);
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
                && (PrintNavigationData.BusinessType is "FamilyApplication" or "EconomicReview")
                && !ClassificationConstants.IsCodeSingleRescue(classification)
                && (IsTemplateFilterMode || await IsIncomeStopChangeAsync(PrintNavigationData.BusinessId)))
            {
                var changeNotice = await _templateService.GetByNameAsync(DocumentTemplateNames.ChangeNotice);
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
            var template = await _templateService.GetByIdAsync(templateId);
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
    /// 判断家庭是否有土地（依据档案字段 FAMILY_LAND_AREA，非 0/非空即有土地）
    /// </summary>
    private bool HasFamilyLand()
    {
        if (PrintNavigationData.FieldData == null) return false;
        if (!PrintNavigationData.FieldData.TryGetValue(FieldKeys.FAMILY_LAND_AREA, out var area))
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

    // ========================
    //  批量（打印选中 / 全部保存）
    // ========================

    [RelayCommand]
    private Task PrintAllAsync() => PrintAllCoreAsync(interactive: true);

    /// <summary>
    /// 批量打印：直接以指定模板静默打印（不走命令、不依赖 SelectedPreviewTemplate 空校验，
    /// 避免统一补打中心批量时误报「请先选择一个模板」）。template 为空/无效时返回 false。
    /// </summary>
    public async Task<bool> PrintTemplateForBatchAsync(TemplateSelectItem? template)
    {
        if (template == null || template.TemplateId <= 0)
        {
            _logger.Warn("批量打印指定模板为空或无效，已跳过");
            return false;
        }
        SelectedPreviewTemplate = template;
        return await PrintCoreAsync(template, interactive: false);
    }

    /// <summary>
    /// 一键打印/全部保存核心（可编程调用）：处理全部勾选模板（封面先行）。
    /// interactive=true 保持原有交互确认（牛皮纸提示/核查报告跳过/结果弹窗）；
    /// interactive=false 供统一补打页批量模式逐户静默调用（封面直接打印、失败仅记录不弹窗）。
    /// printToPrinter=false 为「全部保存」：同一批模板全部仅生成落盘、不调打印机。
    /// </summary>
    public async Task PrintAllCoreAsync(bool interactive = true, bool printToPrinter = true)
    {
        var action = printToPrinter ? "打印" : "保存";
        var selectedTemplates = Templates.Where(t => t.IsSelected && t.IsApplicable).ToList();
        if (selectedTemplates.Count == 0)
        {
            // 批量静默模式：模板集为空时自动选全部适用模板，避免逐户弹「请至少选择一个模板」；
            // 确实无可用模板则记录并跳过（不弹窗）。
            var applicable = Templates.Where(t => t.IsApplicable).ToList();
            if (!interactive)
            {
                if (applicable.Count == 0)
                {
                    _logger.Warn("批量打印：该档案未匹配到可用模板，已跳过");
                    return;
                }
                foreach (var t in applicable)
                    t.IsSelected = true;
                selectedTemplates = applicable;
                _logger.Warn($"批量打印：未显式选择模板，已自动选全部适用模板 {applicable.Count} 个");
            }
            else
            {
                await _dialogService.DisplayAlertAsync("提示", "请至少选择一个模板", "确定");
                return;
            }
        }

        var failedItems = new List<(string Name, string Message)>();
        var successCount = 0;

        // 资产核查档：检查核查报告是否已上传（使用 check_id 解析逻辑）
        if (PrintNavigationData.BusinessType == "AssetVerification")
        {
            var checkId = await ResolveCheckIdAsync(CancellationToken);
            if (checkId.HasValue)
            {
                var reportItem = Templates.FirstOrDefault(t => t.IsVerificationReport && t.CheckId == checkId.Value);
                if (reportItem != null && !reportItem.IsSelected)
                {
                    // 核查报告未勾选：提示用户（批量静默模式直接跳过）
                    var skipReport = interactive
                        ? await _dialogService.DisplayAlertAsync("提示",
                            $"核查报告未勾选，是否跳过核查报告{action}？", "跳过", $"取消{action}")
                        : true;
                    if (!skipReport) return;
                }
            }
        }

        // 封面单独队列：必须先打印封面，只有封面成功后才能打印后续页面
        var coverTemplate = selectedTemplates.FirstOrDefault(t => t.Name.Contains("封面"));
        var restTemplates = selectedTemplates.Where(t => !t.Name.Contains("封面")).ToList();

        try
        {
            IsGenerating = true;
            Progress = 0;
            ProgressText = $"准备{action}...";

            if (coverTemplate != null)
            {
                // 封面牛皮纸确认只在真打印时弹出（保存不调打印机，无需插纸）
                var confirmed = !interactive || !printToPrinter || await _dialogService.DisplayAlertAsync(
                    "打印确认",
                    "请确认已插入牛皮纸，是否开始打印封面？",
                    "继续",
                    "取消");
                if (!confirmed)
                {
                    Progress = 1;
                    StatusText = "已取消打印";
                    return;
                }

                CancellationToken.ThrowIfCancellationRequested();
                ProgressText = $"执行{action}: {coverTemplate.Name}";
                var coverResult = await PrintSingleTemplateAsync(coverTemplate, failedItems, printToPrinter);
                if (coverResult)
                {
                    successCount++;
                    Progress = (double)1 / Math.Max(selectedTemplates.Count, 1);
                }
                else
                {
                    // 封面打印失败：中止整个批量，后续页面不打印
                    Progress = 1;
                    StatusText = $"批量{action}中止: 封面{action}失败 ({failedItems[^1].Message})";
                    _logger.LogBusiness($"批量{action}中止: 封面失败",
                        ("BatchNo", _batchNo), ("Message", failedItems[^1].Message));
                    if (interactive)
                    {
                        await _dialogService.DisplayAlertAsync(
                            $"{action}失败",
                            $"封面{action}失败：{failedItems[^1].Message}\n已中止批量{action}，请处理后再试。",
                            "确定");
                    }
                    IsGenerating = false;
                    return;
                }
            }

            var totalSteps = restTemplates.Count;
            var currentStep = 0;

            foreach (var template in restTemplates)
            {
                CancellationToken.ThrowIfCancellationRequested();

                currentStep++;
                Progress = (double)currentStep / totalSteps;
                ProgressText = $"执行{action}: {template.Name}";

                // 核查报告走浏览器打印路径
                if (template.IsVerificationReport)
                {
                    var reportPrinted = await PrintVerificationReportAsync(template, failedItems, printToPrinter);
                    if (reportPrinted) successCount++;
                    continue;
                }

                if (await PrintSingleTemplateAsync(template, failedItems, printToPrinter))
                    successCount++;
            }

            Progress = 1;
            StatusText = $"批量{action}完成: 成功 {successCount} 个" + (failedItems.Count > 0 ? $", 失败 {failedItems.Count} 个" : "");
            _logger.LogBusiness($"批量{action}完成", ("BatchNo", _batchNo), ("Success", successCount), ("Failed", failedItems.Count));

            if (!interactive)
            {
                // 批量静默模式：结果仅体现在状态文本，由调用方汇总展示
                ProgressText = failedItems.Count == 0
                    ? $"批量{action}完成，共 {successCount} 个模板"
                    : $"批量{action}: 成功 {successCount} 个 失败 {failedItems.Count} 个";
                return;
            }

            if (failedItems.Count == 0)
            {
                ProgressText = $"批量{action}完成，共 {successCount} 个模板";
            }
            else if (successCount > 0)
            {
                ProgressText = $"批量{action}: 成功 {successCount} 个 失败 {failedItems.Count} 个";
                var failedList = failedItems.Select(f => $"{f.Name}: {f.Message}").Take(3);
                var moreCount = failedItems.Count > 3 ? failedItems.Count - 3 : 0;
                var message = $"成功: {successCount} 个\n失败: {failedItems.Count} 个\n\n失败详情:\n{string.Join("\n", failedList)}";
                if (moreCount > 0) message += $"\n...还有 {moreCount} 个失败";
                await _dialogService.DisplayAlertAsync($"{action}结果", message, "确定");
            }
            else
            {
                ProgressText = $"批量{action}失败: {failedItems.Count} 个";
                var failedList = failedItems.Select(f => $"{f.Name}: {f.Message}").Take(5);
                var moreCount = failedItems.Count > 5 ? failedItems.Count - 5 : 0;
                var message = $"全部{action}失败 ({failedItems.Count} 个)\n\n失败详情:\n{string.Join("\n", failedList)}";
                if (moreCount > 0) message += $"\n...还有 {moreCount} 个失败";
                await _dialogService.DisplayAlertAsync($"{action}失败", message, "确定");
            }
        }
        catch (OperationCanceledException)
        {
            ProgressText = $"{action}已取消";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "失败");
            await _dialogService.DisplayAlertAsync("错误", $"批量{action}失败: {ex.Message}", "确定");
        }
        finally
        {
            IsGenerating = false;
        }
    }

    /// <summary>
    /// 执行单个模板打印（含字段/行数据构建，成功时收集输出文件）
    /// </summary>
    private async Task<bool> PrintSingleTemplateAsync(TemplateSelectItem template, List<(string Name, string Message)> failedItems, bool printToPrinter = true)
    {
        var action = printToPrinter ? "打印" : "保存";
        var fields = new Dictionary<string, string>(PrintNavigationData.FieldData!, StringComparer.Ordinal);
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
        _logger.Info($"批量{action}: 模板={template.Name}");

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
            AddOutputFiles(template.TemplateId, template.Name, result.Value.FilePath, result.Value.PdfPath);
            return true;
        }
        else
        {
            failedItems.Add((template.Name, result.Message ?? "未知错误"));
            _logger.Warn($"批量{action}失败");
            return false;
        }
    }

    // ========================
    //  输出文件管理
    // ========================

    [RelayCommand]
    private async Task PreviewFileAsync(OutputFileItem file)
    {
        if (file == null || string.IsNullOrEmpty(file.FilePath))
        {
            await _dialogService.DisplayAlertAsync("提示", "文件路径无效", "确定");
            return;
        }

        if (!file.IsPdf)
        {
            await _dialogService.DisplayAlertAsync("提示", "仅 PDF 文件可在页面内预览", "确定");
            return;
        }

        try
        {
            PdfFilePath = file.FilePath;
            HasPdf = true;
            StatusText = $"预览: {file.FileName}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "失败");
            await _dialogService.DisplayAlertAsync("错误", $"预览失败: {ex.Message}", "确定");
        }
    }

    /// <summary>行内「打印」：该文件对应模板重新生成并调打印机</summary>
    [RelayCommand]
    private Task PrintFileAsync(OutputFileItem file) => ExecuteFileActionAsync(file, printToPrinter: true);

    /// <summary>
    /// 行内动作公共链（打印/保存共用）：按模板重新生成该文件；
    /// printToPrinter=false 即四件套里的「保存」——生成落盘到输出根、不调打印机。
    /// </summary>
    private async Task ExecuteFileActionAsync(OutputFileItem? file, bool printToPrinter)
    {
        var action = printToPrinter ? "打印" : "保存";
        if (file == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请选择要操作的文件", "确定");
            return;
        }

        // 无模板产物（核查报告等）：文件已在输出根，无需重新生成
        if (file.TemplateId <= 0)
        {
            if (printToPrinter)
            {
                if (WebViewPrintPdfFunc != null)
                    await WebViewPrintPdfFunc(file.FilePath);
                else
                    ShellOpenPdf(file.FilePath);
                StatusText = $"已发送打印: {file.FileName}";
            }
            else
            {
                StatusText = $"{file.FileName} 已在输出目录";
            }
            return;
        }

        if (PrintNavigationData.FieldData == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "没有可用的打印数据", "确定");
            return;
        }

        // 封面模板打印前确认：需先插入牛皮纸（保存不调打印机，跳过）
        if (printToPrinter && file.TemplateName.Contains("封面"))
        {
            var confirmed = await _dialogService.DisplayAlertAsync(
                "打印确认",
                "请确认已插入牛皮纸，是否继续打印封面？",
                "继续",
                "取消");
            if (!confirmed) return;
        }

        try
        {
            IsBusy = true;
            StatusText = $"正在{action}: {file.FileName}";

            var fields = new Dictionary<string, string>(PrintNavigationData.FieldData, StringComparer.Ordinal);
            var tableRows = PrintNavigationData.TableData ?? new();

            // 目录模板：使用目录专属字段和行数据
            if (file.TemplateName.Contains("目录"))
            {
                var tl = _timelineService.GetTimelineForDate(await ResolveArchiveAnchorAsync(), TimelineType.BusinessProcess);
                fields = await BuildDirectoryFieldsAsync(fields);
                tableRows = BuildDirectoryRows(tl.MeetingDate);
            }
            // 赡养费承诺书：使用赡养人数据
            else if (file.TemplateName.Contains("赡养费承诺书"))
            {
                tableRows = PrintNavigationData.SupporterTableData ?? new();
            }
            // 近亲属备案两表：使用备案对数据（每对一页，copiesBySupporter 模式）
            else if (file.TemplateName.Contains("近亲属"))
            {
                tableRows = PrintNavigationData.NearRelativePairs ?? new();
            }

            var applicantName = fields.TryGetValue("APPLICANT_NAME", out var name) ? name : "未知";
            var applicantIdCard = fields.TryGetValue("APPLICANT_ID_CARD", out var idCard) ? idCard : "000000000000000000";
            applicantIdCard = applicantIdCard.Length > 6 ? applicantIdCard : "000000000000000000";

            var actualCopies = file.BaseCopies * Copies;

            var result = await _printExecuteService.ExecutePrintAsync(
                file.TemplateId,
                file.TemplateName,
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
                _logger.LogBusiness($"单文件{action}完成",
                    ("Template", file.TemplateName),
                    ("BatchNo", _batchNo),
                    ("Copies", actualCopies));
                StatusText = $"{action}完成";
                AddOutputFiles(file.TemplateId, file.TemplateName, result.Value.FilePath, result.Value.PdfPath);
            }
            else
            {
                StatusText = $"{action}失败: {result.Message}";
                await _dialogService.DisplayAlertAsync("错误", $"{action}失败: {result.Message}", "确定");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "失败");
            StatusText = $"{action}异常: {ex.Message}";
            await _dialogService.DisplayAlertAsync("错误", $"{action}异常: {ex.Message}", "确定");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ========================
    //  完成归档 / 返回
    // ========================

    /// <summary>
    /// 完成工单：打印→保存→完成 流的「完成」动作。
    /// 仅记录办理完成留痕（本批打印追痕 remark + 业务日志），不改档案状态、不导航；
    /// 文档/过滤模式下替代被隐藏的「完成归档」。
    /// </summary>
    [RelayCommand]
    private async Task CompleteWorkOrderAsync()
    {
        if (IsWorkOrderCompleted) return;

        if (OutputFiles.Count == 0 || !OutputFiles.Any(f => f.IsCompleted))
        {
            await _dialogService.DisplayAlertAsync("提示", "请至少生成一个输出文件", "确定");
            return;
        }

        var confirm = await _dialogService.DisplayAlertAsync(
            "确认", "确认完成该文书办理工单？\n仅记录办理完成，不影响档案状态。", "确认", "取消");
        if (!confirm) return;

        try
        {
            if (!string.IsNullOrEmpty(_batchNo))
            {
                var remark = $"工单办理完成 {DateTime.Now:yyyy-MM-dd HH:mm}";
                var markResult = await _printRecordService.MarkBatchWorkOrderCompletedAsync(
                    _batchNo, remark, CancellationToken.None);
                if (markResult.IsFailure)
                {
                    await _dialogService.DisplayAlertAsync("错误", $"工单完成留痕失败: {markResult.Message}", "确定");
                    return;
                }
            }

            _logger.LogBusiness("文书工单完成",
                ("BusinessType", PrintNavigationData.BusinessType),
                ("BusinessId", PrintNavigationData.BusinessId?.ToString() ?? ""),
                ("BatchNo", _batchNo),
                ("FileCount", OutputFiles.Count));

            IsWorkOrderCompleted = true;
            StatusText = "工单已完成";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "完成工单失败");
            await _dialogService.DisplayAlertAsync("错误", $"完成工单失败: {ex.Message}", "确定");
        }
    }

    [RelayCommand]
    private async Task CompleteAsync()
    {
        // 仅出文书/文书直出文档模式：禁止完成归档（会把渐退草稿 Draft 强行置 Approved/step6）
        if (IsTemplateFilterMode || IsDocumentMode)
        {
            await _dialogService.DisplayAlertAsync("提示", "仅出文书模式不支持完成归档", "确定");
            return;
        }

        if (OutputFiles.Count == 0 || !OutputFiles.Any(f => f.IsCompleted))
        {
            await _dialogService.DisplayAlertAsync("提示", "请至少生成一个输出文件", "确定");
            return;
        }

        var confirm = await _dialogService.DisplayAlertAsync("确认", "确认完成归档操作？", "确认", "取消");
        if (!confirm) return;

        // 归档完成：current_step 推进到 6（已完结）并将 status 置 Approved（已审批/在保）
        // 系统无独立审批工作流，归档即视为审批通过（跳过状态机 Draft→Approved 校验）
        if (PrintNavigationData.BusinessId.HasValue)
        {
            try
            {
                if (PrintNavigationData.BusinessType == "Recovery")
                {
                    var recoveryService = _serviceProvider.GetRequiredService<IRecoveryService>();
                    var recoveryResult = await recoveryService.UpdateRecoveryRecordStatusAsync(
                        PrintNavigationData.BusinessId.Value, RecoveryConstants.STATUS_PRINTED, CancellationToken.None);
                    if (recoveryResult.IsSuccess)
                        _logger.LogBusiness("追缴记录归档完成，状态置已打印", ("RecoveryId", PrintNavigationData.BusinessId.Value));
                    else
                        _logger.Error($"追缴记录归档完成但更新状态失败: {recoveryResult.Message}");
                }
                else
                {
                    var appService = _serviceProvider.GetRequiredService<IApplicationService>();
                    var stepResult = await appService.CompleteArchiveAsync(PrintNavigationData.BusinessId.Value, CancellationToken.None);
                    if (stepResult.IsSuccess)
                        _logger.LogBusiness("归档完成，current_step=6 且已置已审批", ("ApplicationId", PrintNavigationData.BusinessId.Value));
                    else
                        _logger.Error($"归档完成但更新状态失败: {stepResult.Message}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"归档完成更新状态异常: {ex.Message}");
            }
        }

        _logger.LogBusiness("完成归档操作", ("BatchNo", _batchNo), ("FileCount", OutputFiles.Count));
        Cleanup();
        var navigation = Helpers.WindowNavigator.CurrentNavigation;
        if (navigation != null)
        {
            await navigation.PopToRootAsync();
            // PopToRoot 直接回到主首页，必须恢复窗口标题（历史 BUG：标题残留档案输出页）
            RestoreWindowTitleFromNavigation();
        }
    }

    /// <summary>
    /// 清理预览临时文件、PdfFilePath 与静态打印数据（组合场景：历史补打页返回时调用，
    /// 不再走 ArchiveOutputPage 的 OnDisappearing 清理）
    /// </summary>
    public void Cleanup()
    {
        TryDeleteFile(_currentTempPreviewPath);
        _currentTempPreviewPath = string.Empty;
        PdfFilePath = string.Empty;
        HasPdf = false;
        _currentPdfData = null;
        PrintNavigationData.Clear();
    }

    /// <summary>返回上一页：先清理预览临时文件与打印数据，再走基类返回逻辑</summary>
    public override async Task GoBackAsync()
    {
        try
        {
            Cleanup();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "归档输出返回前清理失败");
        }
        await base.GoBackAsync();
    }

    private void TryDeleteFile(string filePath)
    {
        _fileService.DeleteFile(filePath);
    }

    /// <summary>
    /// 使用系统默认 PDF 应用（首选 verb=print）打印核查报告
    /// </summary>
    private void ShellPrintPdf(string pdfPath)
    {
        try
        {
            var psi = new ProcessStartInfo(pdfPath)
            {
                UseShellExecute = true,
                Verb = "print"
            };
            using var process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"PDF ShellPrint 失败: {pdfPath}");
            throw;
        }
    }

    /// <summary>
    /// 使用系统默认 PDF 应用（verb=open）预览核查报告
    /// </summary>
    private void ShellOpenPdf(string pdfPath)
    {
        try
        {
            var psi = new ProcessStartInfo(pdfPath)
            {
                UseShellExecute = true,
                Verb = "open"
            };
            using var process = Process.Start(psi);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"PDF ShellOpen 失败: {pdfPath}");
            throw;
        }
    }

    /// <summary>
    /// 生成核查报告（PDF 落输出根/核查报告/；printToPrinter=true 时再经浏览器/WebView2 执行打印）
    /// </summary>
    private async Task<bool> PrintVerificationReportAsync(TemplateSelectItem reportItem, List<(string Name, string Message)> failedItems, bool printToPrinter = true)
    {
        try
        {
            var reportResult = await _pdfVerificationService.GetReportDataByCheckIdAsync(
                reportItem.CheckId, CancellationToken);
            if (reportResult.IsFailure || reportResult.Value is not { Length: > 0 })
            {
                failedItems.Add(("核查报告", $"获取报告数据失败: {reportResult.Message}"));
                return false;
            }

            // 报告是正式产物，与四件套口径一致：落输出根（不再进会被清理的 temp）
            var reportDir = Path.Combine(OutputPathHelper.OutputRoot, "核查报告");
            Directory.CreateDirectory(reportDir);
            var reportName = $"核查报告_{DateTime.Now:yyyyMMddHHmmssfff}.pdf";
            var reportPath = Path.Combine(reportDir, reportName);
            await _fileService.WriteAllBytesAsync(reportPath, reportResult.Value, CancellationToken);

            AddOutputFiles(0, "核查报告", null, reportPath);

            if (printToPrinter)
            {
                // 通过 WebView2 打印（复用 PdfJs 渲染 + 系统打印对话框，不依赖默认 PDF 关联）
                if (WebViewPrintPdfFunc != null)
                {
                    await WebViewPrintPdfFunc(reportPath);
                }
                else
                {
                    // 回退：仅打开预览（无打印）
                    ShellOpenPdf(reportPath);
                }
                _logger.LogBusiness("核查报告已发送打印",
                    ("CheckId", reportItem.CheckId.ToString()),
                    ("BatchNo", _batchNo));
            }
            else
            {
                _logger.LogBusiness("核查报告已生成(未打印)",
                    ("CheckId", reportItem.CheckId.ToString()),
                    ("BatchNo", _batchNo));
            }
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "核查报告打印失败");
            failedItems.Add(("核查报告", ex.Message));
            return false;
        }
    }

    /// <summary>
    /// 预览核查报告（通过浏览器打开）
    /// </summary>
    [RelayCommand]
    private async Task PreviewReportAsync(TemplateSelectItem? reportItem)
    {
        if (reportItem == null || !reportItem.IsVerificationReport) return;

        try
        {
            IsBusy = true;
            var reportResult = await _pdfVerificationService.GetReportDataByCheckIdAsync(
                reportItem.CheckId, CancellationToken);
            if (reportResult.IsFailure || reportResult.Value is not { Length: > 0 })
            {
                await _dialogService.DisplayAlertAsync("提示", $"获取报告数据失败: {reportResult.Message}", "确定");
                return;
            }

            var reportPath = Path.Combine(OutputPathHelper.GetTempDirectory(), $"check_report_preview_{Guid.NewGuid():N}.pdf");
            await _fileService.WriteAllBytesAsync(reportPath, reportResult.Value, CancellationToken);

            // 通过浏览器打开预览
            ShellOpenPdf(reportPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "核查报告预览失败");
            await _dialogService.DisplayAlertAsync("错误", $"核查报告预览失败: {ex.Message}", "确定");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>按业务类型解析档案模板分类（统一走 ArchiveCategoryResolver，补打中心同源）</summary>
    private string[] GetCategoriesByBusinessType(string businessType)
        => ArchiveCategoryResolver.GetRecordCategories(
            businessType, PrintNavigationData.Classification, PrintNavigationData.OperationOverride);

    // ========================
    //  保存（四件套：仅生成落盘，不打印）
    // ========================

    /// <summary>行内「保存」：按模板重新生成该文件到输出根，不调打印机</summary>
    [RelayCommand]
    private Task SaveFileAsync(OutputFileItem file) => ExecuteFileActionAsync(file, printToPrinter: false);

    /// <summary>「保存选中」：勾选模板全部仅生成落盘（不打印）</summary>
    [RelayCommand]
    private async Task SaveSelectedAsync()
        => await PrintAllCoreAsync(interactive: true, printToPrinter: false);

    /// <summary>
    /// 「全部保存」：全部适用模板仅生成落盘（不打印）。
    /// 输出已固定到 config\document_output.yaml 的 output.base_directory，不再弹目录选择。
    /// </summary>
    [RelayCommand]
    private async Task SaveAllFilesAsync()
    {
        var applicable = Templates.Where(t => t.IsApplicable).ToList();
        if (applicable.Count == 0)
        {
            await _dialogService.DisplayAlertAsync("提示", "没有可用模板", "确定");
            return;
        }

        foreach (var t in applicable)
            t.IsSelected = true;

        await PrintAllCoreAsync(interactive: true, printToPrinter: false);
        TryOpenFolder(OutputPathHelper.OutputRoot);
    }

    // ========================
    //  目录模板数据构建
    // ========================

    private async Task<Dictionary<string, string>> BuildDirectoryFieldsAsync(Dictionary<string, string> originalFields)
    {
        // 临时救助档案：目录头部字段走 TEMP_* 专用口径（下方低保硬编码会取错值）
        if (PrintNavigationData.BusinessType == TempReliefConstants.BusinessType)
            return await BuildTempReliefDirectoryFieldsAsync(originalFields);

        var fields = new Dictionary<string, string>(originalFields, StringComparer.Ordinal);

        // 申请年份：从 APPLICATION_DATE 提取，无则取当前年
        if (!fields.TryGetValue(FieldKeys.APPLICATION_DATE, out var appDate) || string.IsNullOrEmpty(appDate))
            appDate = DateTime.Now.ToString("yyyy-MM-dd");
        var year = appDate.Length >= 4 ? appDate[..4] : DateTime.Now.Year.ToString();
        fields[FieldKeys.APPLICATION_YEAR] = year;

        // 业务终结时间：当天
        fields[FieldKeys.BUSINESS_END_DATE] = DateTime.Now.ToString("yyyy年MM月dd日");

        // 案卷题名：姓名+社会救助档案
        var applicantName = fields.TryGetValue(FieldKeys.APPLICANT_NAME, out var n) ? n : "未知";
        fields[FieldKeys.ARCHIVE_TITLE] = $"{applicantName}社会救助档案";

        // 保管期限：留空
        fields[FieldKeys.RETENTION_PERIOD] = "";

        // 社会救助类型：当前分类全称
        var classification = fields.TryGetValue(FieldKeys.CLASSIFICATION_RESULT, out var cls) ? cls : "";
        var fullName = ClassificationConstants.ConvertToFullName(classification);
        fields[FieldKeys.SOCIAL_ASSISTANCE_TYPE] = fullName;

        // 档案卷号：B线结算日次月1日，格式 yyyy-M-d
        var volumeDate = await CalculateBLineVolumeDateAsync();
        fields[FieldKeys.ARCHIVE_VOLUME_NO] = volumeDate;
        fields[FieldKeys.ARCHIVE_VOLUME] = volumeDate;  // 模板配置兼容

        // 版权信息：根据户主（申请人）身份证出生月份取对应电信号，1-12 月对应第 1-12 号，无效/越界取第 13 号（昔涟）兜底
        fields.TryGetValue(FieldKeys.APPLICANT_ID_CARD, out var idCardForCopyright);
        fields[FieldKeys.COPYRIGHT_INFO] = CopyrightHelper.BuildCopyrightInfo(idCardForCopyright);

        return fields;
    }

    /// <summary>
    /// 临时救助目录头部字段：复用档案_目录同名占位符，值取自 TEMP_* 打印数据
    /// </summary>
    private async Task<Dictionary<string, string>> BuildTempReliefDirectoryFieldsAsync(Dictionary<string, string> originalFields)
    {
        var fields = new Dictionary<string, string>(originalFields, StringComparer.Ordinal);

        var applicantName = fields.TryGetValue(FieldKeys.TEMP_APPLICANT_NAME, out var n) && !string.IsNullOrWhiteSpace(n) ? n : "未知";
        fields.TryGetValue(FieldKeys.TEMP_APPLY_DATE, out var applyDate);

        // 申请年份：从申请日期提取，无则取当前年
        fields[FieldKeys.APPLICATION_YEAR] = !string.IsNullOrEmpty(applyDate) && applyDate.Length >= 4
            ? applyDate[..4] : DateTime.Now.Year.ToString();

        // 业务终结时间：当天
        fields[FieldKeys.BUSINESS_END_DATE] = DateTime.Now.ToString("yyyy年MM月dd日");

        // 案卷题名：姓名+临时救助档案
        fields[FieldKeys.ARCHIVE_TITLE] = $"{applicantName}临时救助档案";

        // 保管期限：留空；社会救助类型固定"临时救助"
        fields[FieldKeys.RETENTION_PERIOD] = "";
        fields[FieldKeys.SOCIAL_ASSISTANCE_TYPE] = "临时救助";

        // 档案卷号：沿用 B 线结算日次月 1 日算法
        var volumeDate = await CalculateBLineVolumeDateAsync();
        fields[FieldKeys.ARCHIVE_VOLUME_NO] = volumeDate;
        fields[FieldKeys.ARCHIVE_VOLUME] = volumeDate;

        // 组卷单位：填报单位
        if (!fields.TryGetValue(FieldKeys.OPERATOR_UNIT, out var opUnit) || string.IsNullOrWhiteSpace(opUnit))
            fields[FieldKeys.OPERATOR_UNIT] = fields.TryGetValue(FieldKeys.TEMP_REPORT_UNIT, out var ru) ? ru : string.Empty;

        // 版权信息：按申请人身份证出生月映射
        fields.TryGetValue(FieldKeys.TEMP_APPLICANT_ID_CARD, out var idCard);
        fields[FieldKeys.COPYRIGHT_INFO] = CopyrightHelper.BuildCopyrightInfo(idCard);

        return fields;
    }

    private List<Dictionary<string, string>> BuildDirectoryRows(DateTime meetingDate)
    {
        // 临时救助档案：固定 7 条佐证材料行
        if (PrintNavigationData.BusinessType == TempReliefConstants.BusinessType)
            return BuildTempReliefDirectoryRows();

        var rows = new List<Dictionary<string, string>>();

        // 从 FieldData 获取基础信息
        var fieldData = PrintNavigationData.FieldData ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var applicantName = fieldData.TryGetValue(FieldKeys.APPLICANT_NAME, out var name) ? name : "未知";
        var appDateRaw = fieldData.TryGetValue(FieldKeys.APPLICATION_DATE, out var d) ? d : DateTime.Now.ToString("yyyy-MM-dd");
        var appDate = NormalizeDirectoryDate(appDateRaw);

        // 页号累计（首文档从第1页起）
        var currentPage = 1;

        // 静态预填条目（无模板，仅在目录中列出），责任人 = 申请人姓名
        var staticItems = new (string Title, int Copies)[]
        {
            ("户口簿（必要）", 1),
            ("身份证（必要）", 1),
            ("赡养、抚养、扶养义务人相关材料", 1),
            ("残疾人证或其他证明残疾人的材料", 1),
            ("住院病案或相关诊断材料", 1),
            ("结婚证或离婚证、离婚协议", 1),
            ("在校就读相关佐证材料", 1),
            ("申请前12个月内家庭刚性支出材料", 1),
            ("其他相关材料", 1),
        };
        foreach (var (title, copies) in staticItems)
        {
            var startPage = currentPage;
            var endPage = currentPage + copies - 1;
            rows.Add(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldKeys.DIRECTORY_PERSON] = applicantName,
                [FieldKeys.DIRECTORY_TITLE] = title,
                [FieldKeys.DIRECTORY_DATE] = appDate,
                [FieldKeys.DIRECTORY_PAGE] = $"{startPage}-{endPage}"
            });
            currentPage = endPage + 1;
        }

        // 特殊条目（无模板，仅在目录中列出），责任人 = 民政办，日期 = B线会议时间
        var specialItems = new (string Title, int Copies)[]
        {
            ("会议记录", 1),
            ("资产核查报告", 1),
        };
        foreach (var (title, copies) in specialItems)
        {
            var startPage = currentPage;
            var endPage = currentPage + copies - 1;
            rows.Add(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldKeys.DIRECTORY_PERSON] = "民政办",
                [FieldKeys.DIRECTORY_TITLE] = title,
                [FieldKeys.DIRECTORY_DATE] = NormalizeDirectoryDate(meetingDate.ToString("yyyy-MM-dd")),
                [FieldKeys.DIRECTORY_PAGE] = $"{startPage}-{endPage}"
            });
            currentPage = endPage + 1;
        }

        // 取选中的非目录模板，按 SortOrder+Name 排序
        var sortedTemplates = Templates
            .Where(t => t.IsSelected && t.IsApplicable && !t.IsDirectoryTemplate)
            .OrderBy(t => t.SortOrder)
            .ThenBy(t => t.Name)
            .ToList();

        foreach (var template in sortedTemplates)
        {
            // 题名过滤：去除"档案_"或"通用_"前缀，去除"封面"和"目录"
            var title = template.Name;
            if (title.StartsWith("档案_"))
                title = title["档案_".Length..];
            else if (title.StartsWith("通用_"))
                title = title["通用_".Length..];
            title = title.Replace("封面", "").Replace("目录", "");
            if (string.IsNullOrWhiteSpace(title))
                continue;  // 过滤结果为空则跳过（封面和目录本身不应出现在目录行中）

            var startPage = currentPage;
            var endPage = currentPage + template.BaseCopies - 1;

            // 目录日期：依据模板类型取对应日期字段值，缺失时回退申请日期
            var directoryDate = appDate;
            if (!string.IsNullOrEmpty(template.DirectoryDateField)
                && fieldData.TryGetValue(template.DirectoryDateField, out var templateDate)
                && !string.IsNullOrWhiteSpace(templateDate))
            {
                directoryDate = NormalizeDirectoryDate(templateDate);
            }

            rows.Add(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldKeys.DIRECTORY_PERSON] = "民政办",
                [FieldKeys.DIRECTORY_TITLE] = title,
                [FieldKeys.DIRECTORY_DATE] = directoryDate,
                [FieldKeys.DIRECTORY_PAGE] = $"{startPage}-{endPage}"
            });

            currentPage = endPage + 1;
        }

        return rows;
    }

    /// <summary>
    /// 临时救助目录行：7 条固定佐证材料（必要材料 + 佐证 + 公示照片），
    /// 责任人=申请人，日期=申请日期，页号逐条顺延（每条 1 页）
    /// </summary>
    private List<Dictionary<string, string>> BuildTempReliefDirectoryRows()
    {
        var fieldData = PrintNavigationData.FieldData ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var applicantName = fieldData.TryGetValue(FieldKeys.TEMP_APPLICANT_NAME, out var n) && !string.IsNullOrWhiteSpace(n)
            ? n : "未知";
        fieldData.TryGetValue(FieldKeys.TEMP_APPLY_DATE, out var appDateRaw);
        var appDate = NormalizeDirectoryDate(string.IsNullOrWhiteSpace(appDateRaw)
            ? DateTime.Now.ToString("yyyy-MM-dd")
            : appDateRaw);

        var rows = new List<Dictionary<string, string>>();
        var staticItems = new[]
        {
            "身份证（必要）",
            "户口本（必要）",
            "受残疾人证或其他证明残疾人的材料",
            "住院病历或相关诊断材料",
            "在校就读相关佐证材料",
            "因灾造成家庭损失相关佐证材料",
            "公示照片"
        };

        var currentPage = 1;
        foreach (var title in staticItems)
        {
            var startPage = currentPage;
            var endPage = currentPage;
            rows.Add(new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FieldKeys.DIRECTORY_PERSON] = applicantName,
                [FieldKeys.DIRECTORY_TITLE] = title,
                [FieldKeys.DIRECTORY_DATE] = appDate,
                [FieldKeys.DIRECTORY_PAGE] = $"{startPage}-{endPage}"
            });
            currentPage = endPage + 1;
        }

        return rows;
    }

    /// <summary>
    /// 统一目录中的日期格式为 yyyy-MM-dd
    /// 兼容 yyyy-MM-dd / yyyy/M/d / yyyy年M月d日 / yyyy年MM月dd日 / 含时间 等格式；无法解析时保留原值
    /// </summary>
    private static string NormalizeDirectoryDate(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;

        var formats = new[]
        {
            "yyyy-MM-dd", "yyyy/M/d", "yyyy/M/dd", "yyyy/MM/d", "yyyy/MM/dd",
            "yyyy年M月d日", "yyyy年MM月d日", "yyyy年M月dd日", "yyyy年MM月dd日",
            "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss"
        };
        if (DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
        {
            return parsed.ToString("yyyy-MM-dd");
        }
        return value;
    }

    /// <summary>
    /// B线结算日的次月1日，格式 yyyy-M-d（用于档案卷号）。
    /// 按该档案自身业务日期锚定周期（补全完成→首次审批→更新→创建），保证档案补打时卷号稳定、不随当天漂移。
    /// </summary>
    private async Task<string> CalculateBLineVolumeDateAsync()
    {
        var anchor = await ResolveArchiveAnchorAsync();
        var tl = _timelineService.GetTimelineForDate(anchor, TimelineType.BusinessProcess);
        return new DateTime(tl.CycleEndDate.Year, tl.CycleEndDate.Month, 1).AddMonths(1).ToString("yyyy-M-d");
    }

    /// <summary>目录卷号锚定日期：申请类档案按该申请业务日期；其它域回退当天。</summary>
    private async Task<DateTime> ResolveArchiveAnchorAsync()
    {
        if (PrintNavigationData.BusinessType is "FamilyApplication" or "EconomicReview"
            && PrintNavigationData.BusinessId is > 0)
        {
            try
            {
                var appService = _serviceProvider.GetRequiredService<IApplicationService>();
                var result = await appService.GetByIdAsync(PrintNavigationData.BusinessId.Value, CancellationToken);
                var app = result.IsSuccess ? result.Value : null;
                if (app != null)
                {
                    if (app.DataCompletedAt is { } completed && completed != default) return completed;
                    if (app.FirstApprovedAt is { } approved && approved != default) return approved;
                    if (app.UpdatedAt != default) return app.UpdatedAt;
                    if (app.CreatedAt != default) return app.CreatedAt;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"目录卷号锚定日期解析失败，回退当天: {ex.Message}");
            }
        }
        return DateTime.Today;
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
