using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.DeathStatement;
using NewCosmos.Models.Entities;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Results;
using NewCosmos.Pages.ChangeManagement;
using NewCosmos.Pages.ElderlyBenefits;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.Domain.Printing;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.Platform;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.ArchiveManagement;

/// <summary>
/// 死亡情况说明 ViewModel（村内死亡申报 → 身份聚合 → 数据提醒 → 联动 → 文书输出）。
///
/// 三件事：
///   ① 按本村搜索申报台账 / 档案死亡登记（含「未申报」候选）；
///   ② 手工录入兜底（不依赖库内已有数据）+ 提交申报（死亡确切时刻必填）；
///   ③ 选模板预览/打印/保存《证明_死亡情况说明》。
///
/// 跨流程联动只读 + 预填跳转，不跨域写（见 DeathStatementService.BuildLinksAsync）。
/// </summary>
public partial class DeathStatementViewModel : ViewModelBase
{
    private readonly IDeathStatementService _deathStatementService;
    private readonly ITemplateService _templateService;
    private readonly IPrintExecuteService _printExecuteService;
    private readonly IPrinterService _printerService;
    private readonly IDialogService _dialogService;
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly INewPermissionService _permissionService;
    private readonly IPrintJobFactory _printJobFactory;
    private readonly IElderlyApplicationService _elderlyApplicationService;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    /// <summary>打印留痕业务类型（权限映射见 PrintExecuteService.PrintPermissionMap → PRINT_LOWINCOMEPROOF）</summary>
    private const string StatementBusinessType = "DeathStatement";

    private VillageScope? _scope;
    private DeathStatementContext? _context;

    public DeathStatementViewModel(
        IDeathStatementService deathStatementService,
        ITemplateService templateService,
        IPrintExecuteService printExecuteService,
        IPrinterService printerService,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        INewPermissionService permissionService,
        IPrintJobFactory printJobFactory,
        IElderlyApplicationService elderlyApplicationService)
    {
        _deathStatementService = deathStatementService;
        _templateService = templateService;
        _printExecuteService = printExecuteService;
        _printerService = printerService;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _permissionService = permissionService;
        _printJobFactory = printJobFactory;
        _elderlyApplicationService = elderlyApplicationService;

        Title = "死亡情况说明";
        SearchFrom = DateTime.Today.AddMonths(-12);
        SearchTo = DateTime.Today;
        foreach (var type in DeathDeclarationConstants.DeclaredByTypes)
            DeclaredByTypeOptions.Add(type);
        DeathDate = DateTime.Today;
        DeathTime = new TimeSpan(12, 0, 0);
    }

    #region 页头（本村/本镇上下文）

    [ObservableProperty]
    private string _villageLabel = "本村范围解析中…";

    [ObservableProperty]
    private string _townLabel = string.Empty;

    [ObservableProperty]
    private string _operatorLabel = string.Empty;

    [ObservableProperty]
    private bool _scopeReady;

    /// <summary>范围解析失败原因（显式提示，不静默放开全库；仅真失败时非空 → 红条）</summary>
    [ObservableProperty]
    private string _scopeError = string.Empty;

    /// <summary>
    /// 范围降级提示（黄条）：账号组织未绑定村社区、按全镇范围查询时给出，
    /// 与 ScopeError（红条，两级都取不到的真失败）分离，避免把降级当错误吓用户。
    /// </summary>
    [ObservableProperty]
    private string _scopeNotice = string.Empty;

    #endregion

    #region 搜索区

    [ObservableProperty]
    private string _searchKeyword = string.Empty;

    [ObservableProperty]
    private DateTime _searchFrom;

    [ObservableProperty]
    private DateTime _searchTo;

    /// <summary>显示全部（含作废申报）</summary>
    [ObservableProperty]
    private bool _showAll;

    [ObservableProperty]
    private ObservableCollection<DeathSearchItem> _searchResults = new();

    [ObservableProperty]
    private bool _hasSearchResults;

    [ObservableProperty]
    private string _searchSummary = string.Empty;

    #endregion

    #region 死者与申报（表单 + 展示共用）

    [ObservableProperty]
    private bool _hasSelected;

    /// <summary>手工录入模式（不依赖库内已有数据）</summary>
    [ObservableProperty]
    private bool _isManualMode;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _idCard = string.Empty;

    [ObservableProperty]
    private DateTime _deathDate = DateTime.Today;

    [ObservableProperty]
    private TimeSpan _deathTime;

    [ObservableProperty]
    private string _deathReason = string.Empty;

    [ObservableProperty]
    private string _deathCertNo = string.Empty;

    [ObservableProperty]
    private string _deathPlace = string.Empty;

    [ObservableProperty]
    private string _relation = string.Empty;

    [ObservableProperty]
    private string _declaredByName = string.Empty;

    [ObservableProperty]
    private string _declaredByIdCard = string.Empty;

    [ObservableProperty]
    private string _declaredByPhone = string.Empty;

    [ObservableProperty]
    private string _declaredByType = string.Empty;

    [ObservableProperty]
    private string _declaredRelation = string.Empty;

    [ObservableProperty]
    private string _declaredAtText = string.Empty;

    #region 核实说明要素（《死亡情况核实说明》，2026-10-26）

    /// <summary>所在村（镇级降级时手填，落款 {所在村}；村级默认取组织村）</summary>
    [ObservableProperty]
    private string _declaredVillage = string.Empty;

    /// <summary>死者性别（档案 Gender，为空时按身份证推导；只读展示）</summary>
    [ObservableProperty]
    private string _deathGender = string.Empty;

    /// <summary>死者民族（档案 Ethnicity 转显示名；只读展示）</summary>
    [ObservableProperty]
    private string _deathEthnicity = string.Empty;

    /// <summary>发现（报告）人姓名——与申报人独立手填</summary>
    [ObservableProperty]
    private string _reporterName = string.Empty;

    [ObservableProperty]
    private string _reporterRelation = string.Empty;

    [ObservableProperty]
    private string _reporterPhone = string.Empty;

    /// <summary>现场所见情况描述（长文本）</summary>
    [ObservableProperty]
    private string _situationDesc = string.Empty;

    /// <summary>遗体安置情况（长文本）</summary>
    [ObservableProperty]
    private string _remainsArrangement = string.Empty;

    [ObservableProperty]
    private string _businessAgent = string.Empty;

    /// <summary>相关业务事项（默认「申请居民死亡医学证明」）</summary>
    [ObservableProperty]
    private string _businessMatter = "申请居民死亡医学证明";

    #endregion

    #region 三联出具（推送单位三份不同，不能用份数复制）

    /// <summary>三联推送单位（默认按组织镇名推导，可在 Tab③ 改；值用 Preferences 记忆）</summary>
    [ObservableProperty]
    private string _pushUnitRetain = string.Empty;

    [ObservableProperty]
    private string _pushUnitGov = string.Empty;

    [ObservableProperty]
    private string _pushUnitClinic = string.Empty;

    [ObservableProperty]
    private bool _printRetain = true;

    [ObservableProperty]
    private bool _printGov = true;

    [ObservableProperty]
    private bool _printClinic = true;

    #endregion

    [ObservableProperty]
    private string _statusName = string.Empty;

    [ObservableProperty]
    private bool _isDeclared;

    [ObservableProperty]
    private string _archiveInfo = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _declaredByTypeOptions = new();

    #endregion

    #region 身份 / 提醒 / 联动

    [ObservableProperty]
    private ObservableCollection<DeathIdentityItem> _identities = new();

    [ObservableProperty]
    private bool _hasIdentities;

    [ObservableProperty]
    private string _identitySummary = string.Empty;

    [ObservableProperty]
    private ObservableCollection<DeathReminderItem> _reminders = new();

    [ObservableProperty]
    private bool _hasReminders;

    [ObservableProperty]
    private ObservableCollection<DeathLinkAction> _links = new();

    [ObservableProperty]
    private bool _hasLinks;

    [ObservableProperty]
    private int _pendingLinkCount;

    #endregion

    #region 模板与打印

    private Template? _statementTemplate;

    [ObservableProperty]
    private string _selectedTemplateName = string.Empty;

    [ObservableProperty]
    private bool _hasTemplate;

    [ObservableProperty]
    private string _noTemplateHint = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _printerNames = new();

    [ObservableProperty]
    private string? _selectedPrinter;

    [ObservableProperty]
    private int _copies = 1;

    partial void OnCopiesChanged(int value) => Copies = Math.Clamp(value, 1, 99);

    [RelayCommand]
    private void IncreaseCopies() => Copies = Math.Min(99, Copies + 1);

    [RelayCommand]
    private void DecreaseCopies() => Copies = Math.Max(1, Copies - 1);

    [ObservableProperty]
    private bool _isDuplex;

    [ObservableProperty]
    private string _pdfFilePath = string.Empty;

    [ObservableProperty]
    private bool _hasPdf;

    [ObservableProperty]
    private bool _isPreviewBusy;

    [ObservableProperty]
    private bool _isPrinting;

    /// <summary>打印/保存权限（PRINT_LOWINCOMEPROOF，手机端推送按钮门控）</summary>
    [ObservableProperty]
    private bool _canPrint;

    public bool IsOverallBusy => IsBusy || IsPreviewBusy || IsPrinting;

    protected override void OnBusyStateChanged() => OnPropertyChanged(nameof(IsOverallBusy));

    #endregion

    #region 生命周期

    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        await LoadScopeAsync();
        await LoadTemplateAsync();
        await LoadPrintPermissionAsync();
        LoadPrinterList();

        if (!HasSelected && SearchResults.Count == 0 && ScopeReady)
            await SearchAsync();
    }

    private async Task LoadScopeAsync()
    {
        try
        {
            var result = await _deathStatementService.ResolveScopeAsync(CancellationToken);
            if (result.IsFailure || result.Value == null)
            {
                ScopeReady = false;
                ScopeError = result.Message ?? "无法确定查询范围";
                ScopeNotice = string.Empty;
                VillageLabel = "未绑定区划";
                _logger.Warn($"死亡情况说明-范围解析失败: {ScopeError}");
                return;
            }

            _scope = result.Value;
            ScopeReady = true;
            ScopeError = string.Empty;
            VillageLabel = _scope.ScopeLabel;
            TownLabel = string.IsNullOrEmpty(_scope.TownName) ? string.Empty : $"乡镇：{_scope.TownName}";
            OperatorLabel = $"经办：{(string.IsNullOrEmpty(App.CurrentUserFullName) ? App.CurrentUserName : App.CurrentUserFullName)}";

            // 落款「所在村」：村级取组织村；镇级降级时组织无村 → 留空由表单手填（提示见黄条）
            DeclaredVillage = _scope.IsTownScope ? string.Empty : _scope.VillageName;

            // 三联推送单位默认值（按组织镇/村名推导；Preferences 记住上次手填值）
            InitPushUnitDefaults();

            // 镇级降级 = 黄条提示（不是错误）：口径来自组织机构，组织没绑村才降级
            ScopeNotice = _scope.IsTownScope
                ? $"当前组织未绑定村/社区，已按全镇范围查询：{_scope.TownName}。如需按村口径，请到「经办机构管理」补齐区划。"
                : string.Empty;

            if (_scope.IsTownScope)
                _logger.Warn($"死亡情况说明-范围降级为镇级: {_scope.TownName}（组织未绑定村社区）");
        }
        catch (Exception ex)
        {
            ScopeReady = false;
            ScopeError = "查询范围解析异常";
            ScopeNotice = string.Empty;
            _logger.LogError(ex, "死亡情况说明-范围解析异常");
        }
    }

    /// <summary>模板：按名精确定位「证明_死亡情况说明」，退回「证明文件」分类中名称含「死亡情况说明」者</summary>
    private async Task LoadTemplateAsync()
    {
        try
        {
            _statementTemplate = null;
            HasTemplate = false;
            SelectedTemplateName = string.Empty;

            Template? byName = null;
            try
            {
                var nameResult = await _templateService.GetByNameAsync(DocumentTemplateNames.DeathStatement, CancellationToken);
                if (nameResult.IsSuccess) byName = nameResult.Value;
                else if (nameResult.IsFailure)
                    _logger.Warn($"按名查模板失败: {nameResult.Message}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "按名查模板异常");
            }

            if (byName != null)
            {
                _statementTemplate = byName;
            }
            else
            {
                List<Template> generic;
                try
                {
                    generic = await _templateService.GetByCategoriesAsync(
                        new[] { ProofUnitTemplateService.ProofCategory }, CancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "加载证明文件分类模板失败");
                    generic = [];
                }

                _statementTemplate = generic.FirstOrDefault(t =>
                    t.Name.Contains("死亡情况说明", StringComparison.Ordinal));
            }

            HasTemplate = _statementTemplate != null;
            SelectedTemplateName = _statementTemplate?.Name ?? string.Empty;
            NoTemplateHint = HasTemplate
                ? string.Empty
                : $"模板未上传：{DocumentTemplateNames.DeathStatement}（请到「模板管理」上传并勾选「证明文件」分类）";
            OnPropertyChanged(nameof(NoTemplateHint));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "死亡情况说明-加载模板异常");
            HasTemplate = false;
        }
    }

    private async Task LoadPrintPermissionAsync()
    {
        try
        {
            var userId = App.CurrentUserId ?? 0;
            CanPrint = userId > 0 && await _permissionService.HasPermissionAsync(
                userId, PermissionCodes.PRINT_LOWINCOMEPROOF, CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载打印权限失败");
            CanPrint = false;
        }
    }

    private void LoadPrinterList()
    {
        try
        {
            var printers = _printerService.GetInstalledPrinters();
            PrinterNames.Clear();
            foreach (var p in printers)
                PrinterNames.Add(p);

            var defaultPrinter = _printerService.GetDefaultPrinter();
            if (!string.IsNullOrEmpty(defaultPrinter) && PrinterNames.Contains(defaultPrinter))
                SelectedPrinter = defaultPrinter;
            else if (PrinterNames.Count > 0)
                SelectedPrinter = PrinterNames[0];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载打印机列表失败");
        }
    }

    #endregion

    #region Tab 与分段（单页三 Tab：①申报填写 ②身份与提醒 ③文书输出）

    /// <summary>当前顶层 Tab（1=申报填写 / 2=身份与提醒 / 3=文书输出）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsTab1))]
    [NotifyPropertyChangedFor(nameof(IsTab2))]
    [NotifyPropertyChangedFor(nameof(IsTab3))]
    private int _activeTab = 1;

    public bool IsTab1 => ActiveTab == 1;
    public bool IsTab2 => ActiveTab == 2;
    public bool IsTab3 => ActiveTab == 3;

    /// <summary>Tab1 内的填写分段（1=基本信息 / 2=死亡与申报，卡片式步骤指示器）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep1))]
    [NotifyPropertyChangedFor(nameof(IsStep2))]
    [NotifyPropertyChangedFor(nameof(IsPreviousVisible))]
    [NotifyPropertyChangedFor(nameof(IsNextVisible))]
    private int _step = 1;

    public bool IsStep1 => Step == 1;
    public bool IsStep2 => Step == 2;
    public bool IsPreviousVisible => Step == 2;
    public bool IsNextVisible => Step == 1;

    [RelayCommand]
    private void SwitchTab(string? tab)
    {
        var target = tab == "2" ? 2 : tab == "3" ? 3 : 1;
        if (target == ActiveTab) return;
        ActiveTab = target;

        // 切到「身份与提醒」时若已填身份证但还没汇总，自动补一次（不打断、不覆盖已选）
        if (target == 2 && _context == null && !string.IsNullOrWhiteSpace(IdCard))
            SafeFireAndForget(BuildManualContextAsync);
    }

    [RelayCommand]
    private void SwitchStep(string? step) => Step = step == "2" ? 2 : 1;

    /// <summary>下一步（Step1 → Step2，先校验基本信息）</summary>
    [RelayCommand]
    private async Task NextStepAsync()
    {
        if (!await ValidateStep1Async()) return;
        Step = 2;
    }

    [RelayCommand]
    private void PreviousStep() => Step = 1;

    /// <summary>Step1 校验：姓名 + 身份证（死亡与申报的必填校验留在提交时）</summary>
    private async Task<bool> ValidateStep1Async()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            await ShowTipAsync("请填写死者姓名");
            return false;
        }
        if (string.IsNullOrWhiteSpace(IdCard))
        {
            await ShowTipAsync("请填写死者身份证号");
            return false;
        }
        return true;
    }

    #endregion

    #region 搜索与选择

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (!EnsureScopeReady()) return;

        await ExecuteAsync(async ct =>
        {
            _logger.LogBusiness("死亡情况说明-搜索",
                ("Keyword", SearchKeyword?.Trim() ?? string.Empty),
                ("From", SearchFrom.ToString("yyyy-MM-dd")), ("To", SearchTo.ToString("yyyy-MM-dd")));

            var result = await _deathStatementService.SearchAsync(
                _scope!, SearchKeyword, SearchFrom, SearchTo, ShowAll, ct);

            if (result.IsFailure)
            {
                await ShowTipAsync($"搜索失败：{result.Message}");
                return;
            }

            SearchResults.Clear();
            foreach (var item in result.Value ?? [])
                SearchResults.Add(item);

            HasSearchResults = SearchResults.Count > 0;
            SearchSummary = HasSearchResults ? $"共 {SearchResults.Count} 条（本村）" : "本村无匹配记录";
            if (!HasSearchResults)
                await ShowTipAsync("未找到匹配记录。可点「手工录入」直接申报，或调整姓名/身份证/日期范围。");
        }, "正在搜索…");
    }

    [RelayCommand]
    private async Task SelectItemAsync(DeathSearchItem item)
    {
        if (item == null || !EnsureScopeReady()) return;

        IsManualMode = false;
        await LoadContextAsync(new DeathSearchItem
        {
            DeclarationId = item.DeclarationId,
            DeathRecordId = item.DeathRecordId,
            ApplicationId = item.ApplicationId,
            Name = item.Name,
            IdCard = item.IdCard,
            DeathDateText = item.DeathDateText,
            DeathTimeText = item.DeathTimeText,
            DeathReason = item.DeathReason,
            DeathCertNo = item.DeathCertNo,
            Relation = item.Relation,
            DeclaredByName = item.DeclaredByName,
            DeclaredByType = item.DeclaredByType,
            DeclaredAtText = item.DeclaredAtText,
            Status = item.Status,
            StatusName = item.StatusName,
            VillageName = item.VillageName,
            HasDeclaration = item.HasDeclaration
        });
    }

    /// <summary>手工录入（不依赖库内已有数据）</summary>
    [RelayCommand]
    private void ManualEntry()
    {
        if (!EnsureScopeReady()) return;

        IsManualMode = true;
        HasSelected = false;
        HasPdf = false;
        PdfFilePath = string.Empty;
        _context = null;

        Name = string.Empty;
        IdCard = string.Empty;
        DeathDate = DateTime.Today;
        DeathTime = new TimeSpan(12, 0, 0);
        DeathReason = string.Empty;
        DeathCertNo = string.Empty;
        DeathPlace = string.Empty;
        Relation = string.Empty;

        // 核实说明要素重置（手工录入 = 全部重填；所在村/业务事项保留默认）
        DeathGender = string.Empty;
        DeathEthnicity = string.Empty;
        ReporterName = string.Empty;
        ReporterRelation = string.Empty;
        ReporterPhone = string.Empty;
        SituationDesc = string.Empty;
        RemainsArrangement = string.Empty;
        BusinessAgent = App.CurrentUserFullName ?? string.Empty;
        BusinessMatter = "申请居民死亡医学证明";

        DeclaredByIdCard = string.Empty;
        DeclaredByPhone = string.Empty;
        DeclaredRelation = string.Empty;
        DeclaredAtText = string.Empty;
        StatusName = string.Empty;
        IsDeclared = false;
        ArchiveInfo = string.Empty;
        Identities.Clear();
        Reminders.Clear();
        Links.Clear();
        HasIdentities = false;
        HasReminders = false;
        HasLinks = false;
        PendingLinkCount = 0;
        IdentitySummary = string.Empty;
        if (string.IsNullOrEmpty(DeclaredByType))
            DeclaredByType = DeathDeclarationConstants.DeclaredByVillage;
        if (string.IsNullOrEmpty(DeclaredByName))
            DeclaredByName = App.CurrentUserFullName ?? string.Empty;
    }

    private async Task LoadContextAsync(DeathSearchItem item)
    {
        await ExecuteAsync(async ct =>
        {
            var result = await _deathStatementService.BuildContextAsync(_scope!, item, ct);
            if (result.IsFailure)
            {
                await ShowTipAsync($"汇总失败：{result.Message}");
                return;
            }

            ApplyContext(result.Value);
        }, "正在汇总身份与提醒…");
    }

    private void ApplyContext(DeathStatementContext context)
    {
        _context = context;
        HasSelected = true;

        Name = context.Name;
        IdCard = context.IdCard;
        if (DateTime.TryParse(context.DeathDateText, out var deathDate)) DeathDate = deathDate;
        if (TimeSpan.TryParse(context.DeathTimeText, out var deathTime)) DeathTime = deathTime;
        DeathReason = context.DeathReason;
        DeathCertNo = context.DeathCertNo;
        DeathPlace = context.DeathPlace;
        Relation = context.Relation;
        DeclaredByName = context.DeclaredByName;
        DeclaredByIdCard = context.DeclaredByIdCard;
        DeclaredByPhone = context.DeclaredByPhone;
        DeclaredByType = context.DeclaredByType;
        DeclaredRelation = context.DeclaredRelation;
        DeclaredAtText = context.DeclaredAtText;
        StatusName = context.StatusName;
        IsDeclared = context.HasDeclaration;

        // 核实说明要素：档案带出的性别/民族 + 申报行里的报告人/情况描述/遗体安置/业务两项
        DeathGender = !string.IsNullOrWhiteSpace(context.Gender)
            ? context.Gender
            : (IdCardValidator.ExtractGender(context.IdCard) ?? string.Empty);
        DeathEthnicity = context.Ethnicity;
        ReporterName = context.ReporterName;
        ReporterRelation = context.ReporterRelation;
        ReporterPhone = context.ReporterPhone;
        SituationDesc = context.SituationDesc;
        RemainsArrangement = context.RemainsArrangement;
        BusinessAgent = context.BusinessAgent;
        if (!string.IsNullOrWhiteSpace(context.BusinessMatter)) BusinessMatter = context.BusinessMatter;

        ArchiveInfo = string.Join(" · ", new[]
        {
            string.IsNullOrEmpty(context.Classification) ? null : context.Classification,
            string.IsNullOrEmpty(context.Address) ? null : context.Address,
            string.IsNullOrEmpty(context.Relation) ? null : $"与户主关系：{context.Relation}"
        }.Where(s => !string.IsNullOrEmpty(s)));

        Identities.Clear();
        foreach (var i in context.Identities) Identities.Add(i);
        HasIdentities = Identities.Count > 0;
        IdentitySummary = HasIdentities
            ? $"系统内命中 {context.Identities.Count} 项身份" +
              (context.CrossVillageCount > 0 ? $"（另有 {context.CrossVillageCount} 条跨村记录已剔除）" : string.Empty)
            : "系统内无该人身份记录";

        Reminders.Clear();
        foreach (var r in context.Reminders) Reminders.Add(r);
        HasReminders = Reminders.Count > 0;

        Links.Clear();
        foreach (var l in context.Links) Links.Add(l);
        HasLinks = Links.Count > 0;
        PendingLinkCount = context.PendingLinkCount;

        HasPdf = false;
        PdfFilePath = string.Empty;

        _logger.LogBusiness("死亡情况说明-构建上下文",
            ("Name", DataMasker.MaskName(context.Name)),
            ("IdCard", DataMasker.MaskIdCard(context.IdCard)),
            ("Identities", context.Identities.Count.ToString()),
            ("Reminders", context.Reminders.Count.ToString()));
    }

    #endregion

    #region 申报（提交 / 核实 / 作废）

    /// <summary>提交申报：先查窗口期重复 → 确认 → 写台账 → 刷新选中</summary>
    [RelayCommand]
    private async Task DeclareAsync()
    {
        if (!EnsureScopeReady()) return;
        if (string.IsNullOrWhiteSpace(Name))
        {
            await ShowTipAsync("请填写死者姓名");
            return;
        }
        if (string.IsNullOrWhiteSpace(IdCard))
        {
            await ShowTipAsync("请填写死者身份证号");
            return;
        }
        if (DeathDate == default)
        {
            await ShowTipAsync("请选择死亡日期");
            return;
        }
        if (string.IsNullOrWhiteSpace(DeclaredByName))
        {
            await ShowTipAsync("请填写申报人姓名");
            return;
        }
        if (string.IsNullOrWhiteSpace(DeclaredByType))
        {
            await ShowTipAsync("请选择申报人类型");
            return;
        }
        // 核实说明正文要写报告人；镇级降级时组织无村，落款「所在村」必须手填
        if (string.IsNullOrWhiteSpace(ReporterName))
        {
            await ShowTipAsync("请填写发现（报告）人姓名");
            return;
        }
        if (_scope != null && _scope.IsTownScope && string.IsNullOrWhiteSpace(DeclaredVillage))
        {
            await ShowTipAsync("当前组织未绑定村社区，请填写「所在村」（文书落款与抬头需要）");
            return;
        }

        // 重复申报确认（窗口期 ±3 天）
        var countResult = await _deathStatementService.CountRecentValidAsync(IdCard, DeathDate, CancellationToken);
        if (countResult.IsFailure)
        {
            await ShowTipAsync($"重复申报检查失败：{countResult.Message}");
            return;
        }
        if (countResult.Value > 0)
        {
            var confirm = await _dialogService.DisplayAlertAsync("重复申报确认",
                $"该死者在死亡日期前后 {DeathDeclarationConstants.DuplicateWindowDays} 天内已有 {countResult.Value} 条有效申报。\n仍要提交本次申报吗？",
                "仍要提交", "取消");
            if (!confirm) return;
        }

        var request = new DeathDeclareRequest
        {
            Name = Name.Trim(),
            IdCard = IdCard.Trim(),
            DeathDate = DeathDate.Date,
            DeathTime = DeathTime,
            DeathReason = DeathReason?.Trim() ?? string.Empty,
            DeathCertNo = DeathCertNo?.Trim() ?? string.Empty,
            DeathPlace = DeathPlace?.Trim() ?? string.Empty,
            Relation = DeclaredRelation?.Trim() ?? string.Empty,
            DeclaredByName = DeclaredByName.Trim(),
            DeclaredByIdCard = DeclaredByIdCard?.Trim() ?? string.Empty,
            DeclaredByPhone = DeclaredByPhone?.Trim() ?? string.Empty,
            DeclaredByType = DeclaredByType,
            ReporterName = ReporterName?.Trim() ?? string.Empty,
            ReporterRelation = ReporterRelation?.Trim() ?? string.Empty,
            ReporterPhone = ReporterPhone?.Trim() ?? string.Empty,
            SituationDesc = SituationDesc?.Trim() ?? string.Empty,
            RemainsArrangement = RemainsArrangement?.Trim() ?? string.Empty,
            BusinessAgent = BusinessAgent?.Trim() ?? string.Empty,
            BusinessMatter = BusinessMatter?.Trim() ?? string.Empty,
            ApplicationId = _context?.ApplicationId,
            VillageId = _scope!.IsTownScope ? null : _scope.VillageId,
            // 镇级降级：组织无村，落款村名取表单手填的「所在村」（写入 village_name 列）
            VillageName = _scope!.IsTownScope ? (DeclaredVillage?.Trim() ?? string.Empty) : _scope.VillageName,
            TownId = _scope!.TownId,
            OperatorName = string.IsNullOrEmpty(App.CurrentUserName) ? "System" : App.CurrentUserName,
            Channel = DeathDeclarationConstants.ResolveChannel()
        };

        await ExecuteAsync(async ct =>
        {
            var result = await _deathStatementService.DeclareAsync(request, ct);
            if (result.IsFailure)
            {
                await ShowTipAsync($"申报提交失败：{result.Message}");
                return;
            }

            var info = result.Value!;
            IsDeclared = true;
            StatusName = DeathDeclarationConstants.GetStatusName(DeathDeclarationConstants.StatusPending);
            DeclaredAtText = info.DeclaredAt.ToString("yyyy-MM-dd HH:mm");

            // 刷新列表并按新申报重新装载上下文
            var searchResult = await _deathStatementService.SearchAsync(
                _scope!, SearchKeyword, SearchFrom, SearchTo, ShowAll, ct);
            if (searchResult.IsSuccess && searchResult.Value != null)
            {
                SearchResults.Clear();
                foreach (var i in searchResult.Value) SearchResults.Add(i);
                HasSearchResults = SearchResults.Count > 0;
                SearchSummary = HasSearchResults ? $"共 {SearchResults.Count} 条（本村）" : "本村无匹配记录";
            }

            var created = searchResult.Value?.FirstOrDefault(i => i.DeclarationId == info.Id);
            if (created != null)
                await LoadContextAsync(created);

            await ShowTipAsync($"申报已提交（编号 {info.Id}，受理时间 {info.DeclaredAt:yyyy-MM-dd HH:mm}）");
        }, "正在提交申报…");
    }

    [RelayCommand]
    private async Task VerifyAsync()
    {
        if (_context?.DeclarationId is not { } declarationId) return;

        await ExecuteAsync(async ct =>
        {
            var result = await _deathStatementService.VerifyAsync(declarationId, ct);
            if (result.IsFailure)
            {
                await ShowTipAsync($"核实失败：{result.Message}");
                return;
            }

            StatusName = DeathDeclarationConstants.GetStatusName(DeathDeclarationConstants.StatusVerified);
            await ShowTipAsync("该申报已核实");
        }, "正在核实…");
    }

    [RelayCommand]
    private async Task RevokeAsync()
    {
        if (_context?.DeclarationId is not { } declarationId) return;

        var reason = await _dialogService.DisplayPromptAsync("作废申报",
            "请填写作废原因（将记入台账与审计日志）", "作废", "取消");
        if (string.IsNullOrWhiteSpace(reason))
        {
            if (reason == null) return;
            await ShowTipAsync("作废原因不能为空");
            return;
        }

        await ExecuteAsync(async ct =>
        {
            var result = await _deathStatementService.RevokeAsync(declarationId, reason, ct);
            if (result.IsFailure)
            {
                await ShowTipAsync($"作废失败：{result.Message}");
                return;
            }

            IsDeclared = false;
            StatusName = DeathDeclarationConstants.GetStatusName(DeathDeclarationConstants.StatusRevoked);
            await ShowTipAsync("该申报已作废");
        }, "正在作废…");
    }

    #endregion

    #region 跨流程联动（预填跳转）

    [RelayCommand]
    private async Task OpenLinkAsync(DeathLinkAction link)
    {
        if (link == null || string.IsNullOrEmpty(link.NavKey) || link.NavKey == "None") return;

        try
        {
            switch (link.NavKey)
            {
                case "ElderlyStop":
                    if (link.ElderlyApplicationId is not { } elderlyId)
                    {
                        await ShowTipAsync("缺少高龄档案ID，无法跳转");
                        return;
                    }
                    var appResult = await _elderlyApplicationService.GetByIdAsync(elderlyId, CancellationToken);
                    if (appResult.IsFailure || appResult.Value == null)
                    {
                        await ShowTipAsync($"高龄档案加载失败：{appResult.Message}");
                        return;
                    }
                    await NavigateToPageAsync<ElderlyStopPage, ElderlyStopPrefill>(new ElderlyStopPrefill(
                        appResult.Value, ParseDate(link.DeathDateText), link.DeathTimeText, link.DeathReason));
                    break;

                case "HouseholdDeath":
                    if (link.ApplicationId is not { } headAppId)
                    {
                        await ShowTipAsync("缺少关联档案ID，无法跳转");
                        return;
                    }
                    await NavigateToPageAsync<HouseholdDeathChangePage, HouseholdDeathPrefill>(
                        new HouseholdDeathPrefill(headAppId, ParseDate(link.DeathDateText), link.DeathTimeText,
                            link.DeathReason, link.DeathCertNo));
                    break;

                case "MemberChange":
                    if (link.ApplicationId is not { } memberAppId)
                    {
                        await ShowTipAsync("缺少关联档案ID，无法跳转");
                        return;
                    }
                    await NavigateToPageAsync<ChangePage, long>(memberAppId);
                    break;

                case "College":
                    await NavigateToPageAsync<Pages.SocialAssistance.CollegeStudentManagementPage>("大学生管理");
                    break;

                case "Recovery":
                    await NavigateToPageAsync<Pages.Recovery.RecoverySearchPage>("后补追缴");
                    break;

                case "NearRelative":
                    await NavigateToPageAsync<Pages.SocialAssistance.NearRelativeListPage>("近亲属备案");
                    break;

                case "Declare":
                    ManualEntry();
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"联动跳转失败: {link.NavKey}");
            await ShowTipAsync($"跳转失败：{ex.Message}");
        }
    }

    private static DateTime? ParseDate(string? text) =>
        DateTime.TryParse(text, out var value) ? value : null;

    #endregion

    #region 文书输出（预览 / 打印 / 保存 / 推送）

    private bool EnsureCanProceed()
    {
        if (!ScopeReady)
        {
            _ = ShowTipAsync(ScopeError);
            return false;
        }
        if (!HasSelected && !IsManualMode)
        {
            _ = ShowTipAsync("请先选择死亡人员，或点「手工录入」");
            return false;
        }
        if (IsManualMode && string.IsNullOrWhiteSpace(Name))
        {
            _ = ShowTipAsync("请先填写死者姓名与身份证");
            return false;
        }
        if (_statementTemplate == null)
        {
            _ = ShowTipAsync(NoTemplateHint);
            return false;
        }
        return true;
    }

    /// <summary>
    /// 输出前置：基础校验 + 手工录入模式下自动汇总一次上下文
    /// （保证文书带身份清单与数据提醒，而不是空模板）。
    /// </summary>
    private async Task<bool> PrepareOutputAsync()
    {
        if (!EnsureCanProceed()) return false;

        if (IsManualMode && _context == null)
        {
            await BuildManualContextAsync();
            if (_context == null)
            {
                await ShowTipAsync("请先填写死者姓名与身份证号，并点「汇总身份与提醒」");
                return false;
            }
        }

        return _context != null || IsManualMode;
    }

    /// <summary>文书字段单点装配（ASCII 键；身份/提醒槽位全部显式供给，杜绝占位符残留）</summary>
    /// <summary>
    /// 文书字段单点装配（ASCII 键；身份/提醒槽位全部显式供给，杜绝占位符残留）。
    /// pushUnit：三联各份「推送单位」不同（村留存 / 镇政府 / 镇中心卫生院），由调用方按联次注入；
    ///           null → 取 <see cref="CurrentPushUnit"/>（预览默认展示的联次）。
    /// </summary>
    private Dictionary<string, string> BuildStatementFields(string? pushUnit = null)
    {
        var c = _context ?? BuildManualContext();

        // 落款五段：省/市/县区来自组织机构，乡镇/村来自范围与申报（镇级降级时村取表单手填值）
        var province = _scope?.ProvinceName ?? string.Empty;
        var city = _scope?.CityName ?? string.Empty;
        var district = _scope?.DistrictName ?? string.Empty;
        var town = string.IsNullOrWhiteSpace(c.TownName) ? (_scope?.TownName ?? string.Empty) : c.TownName;
        var village = !string.IsNullOrWhiteSpace(DeclaredVillage)
            ? DeclaredVillage.Trim()
            : (c.VillageName ?? string.Empty);

        // 性别/民族：表单值（档案带出或身份证推导）优先，上下文次之
        var gender = !string.IsNullOrWhiteSpace(DeathGender) ? DeathGender : (c.Gender ?? string.Empty);
        var ethnicity = !string.IsNullOrWhiteSpace(DeathEthnicity) ? DeathEthnicity : (c.Ethnicity ?? string.Empty);
        var push = string.IsNullOrWhiteSpace(pushUnit) ? CurrentPushUnit() : pushUnit.Trim();

        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FieldKeys.DEATH_NAME] = c.Name,
            [FieldKeys.DEATH_ID_CARD] = c.IdCard,
            [FieldKeys.DEATH_DATE] = c.DeathDateText,
            [FieldKeys.DEATH_TIME] = c.DeathTimeText,
            [FieldKeys.DEATH_DATETIME_TEXT] = c.DeathDateTimeText,
            [FieldKeys.DEATH_REASON] = c.DeathReason,
            [FieldKeys.DEATH_CERT_NO] = c.DeathCertNo,
            [FieldKeys.DEATH_PLACE] = c.DeathPlace,
            [FieldKeys.DEATH_RELATION] = c.Relation,
            [FieldKeys.DECLARED_RELATION] = c.DeclaredRelation,

            // 落款五段（模板：{所在省}{所在市}{所在县区}{所在乡镇}{所在村}村民委员会）
            [FieldKeys.PROVINCE] = province,
            [FieldKeys.CITY] = city,
            [FieldKeys.DISTRICT] = district,
            [FieldKeys.DEATH_TOWN] = town,
            [FieldKeys.DEATH_VILLAGE] = village,

            // 核实说明要素
            [FieldKeys.DEATH_GENDER] = gender,
            [FieldKeys.DEATH_ETHNICITY] = ethnicity,
            [FieldKeys.PUSH_UNIT] = push,
            [FieldKeys.DEATH_REPORTER_NAME] = ReporterName ?? string.Empty,
            [FieldKeys.DEATH_REPORTER_RELATION] = ReporterRelation ?? string.Empty,
            [FieldKeys.DEATH_REPORTER_PHONE] = ReporterPhone ?? string.Empty,
            [FieldKeys.DEATH_SITUATION_DESC] = SituationDesc ?? string.Empty,
            [FieldKeys.DEATH_REMAINS_ARRANGEMENT] = RemainsArrangement ?? string.Empty,
            [FieldKeys.BUSINESS_AGENT] = BusinessAgent ?? string.Empty,
            [FieldKeys.BUSINESS_MATTER] = BusinessMatter ?? string.Empty,

            [FieldKeys.DEATH_ADDRESS] = c.Address,
            [FieldKeys.DEATH_ARCHIVE_CLASSIFICATION] = c.Classification,
            [FieldKeys.DEATH_ARCHIVE_ID] = c.ApplicationId?.ToString() ?? string.Empty,
            [FieldKeys.DECLARED_BY_NAME] = c.DeclaredByName,
            [FieldKeys.DECLARED_BY_ID_CARD] = c.DeclaredByIdCard,
            [FieldKeys.DECLARED_BY_PHONE] = c.DeclaredByPhone,
            [FieldKeys.DECLARED_BY_TYPE] = c.DeclaredByType,
            [FieldKeys.DECLARED_AT] = c.DeclaredAtText,
            [FieldKeys.DEATH_ISSUE_UNIT] = _scope?.OrgName ?? string.Empty,
            [FieldKeys.DEATH_ISSUE_DATE] = DateTime.Today.ToString("yyyy年M月d日"),
            [FieldKeys.OPERATOR_NAME] = string.IsNullOrEmpty(App.CurrentUserFullName) ? App.CurrentUserName : App.CurrentUserFullName,
            [FieldKeys.OPERATOR_UNIT] = _scope?.OrgName ?? string.Empty,
            [FieldKeys.OPERATOR_UNIT_PHONE] = _scope?.OrgPhone ?? string.Empty,
            [FieldKeys.DEATH_IDENTITY_COUNT] = c.Identities.Count.ToString(),
            [FieldKeys.DEATH_REMIND_COUNT] = c.Reminders.Count.ToString()
        };

        // 身份清单槽（_1.._12，每行 5 键；不足位清空防残留）
        for (var i = 1; i <= FieldKeys.DEATH_IDENTITY_SLOTS; i++)
        {
            var item = i <= c.Identities.Count ? c.Identities[i - 1] : null;
            fields[$"{FieldKeys.DEATH_IDENTITY_TYPE_BASE}_{i}"] = item?.TypeLabel ?? string.Empty;
            fields[$"{FieldKeys.DEATH_IDENTITY_STATUS_BASE}_{i}"] = item?.StatusLabel ?? string.Empty;
            fields[$"{FieldKeys.DEATH_IDENTITY_PERIOD_BASE}_{i}"] = item?.Period ?? string.Empty;
            fields[$"{FieldKeys.DEATH_IDENTITY_SOURCE_BASE}_{i}"] = item?.SourceLabel ?? string.Empty;
            fields[$"{FieldKeys.DEATH_IDENTITY_NOTE_BASE}_{i}"] = item?.Note ?? string.Empty;
        }

        // 数据提醒槽（_1.._8，每行 2 键）
        for (var i = 1; i <= FieldKeys.DEATH_REMIND_SLOTS; i++)
        {
            var r = i <= c.Reminders.Count ? c.Reminders[i - 1] : null;
            fields[$"{FieldKeys.DEATH_REMIND_LEVEL_BASE}_{i}"] = r?.Level ?? string.Empty;
            fields[$"{FieldKeys.DEATH_REMIND_TEXT_BASE}_{i}"] = r == null
                ? string.Empty
                : $"{r.Message}{(string.IsNullOrEmpty(r.SuggestAction) ? string.Empty : "（" + r.SuggestAction + "）")}";
        }

        return fields;
    }

    /// <summary>手工录入未提交时的上下文（仅表单值，身份/提醒/联动走服务聚合）</summary>
    private DeathStatementContext BuildManualContext() => new()
    {
        Name = Name?.Trim() ?? string.Empty,
        IdCard = IdCard?.Trim() ?? string.Empty,
        DeathDateText = DeathDate.ToString("yyyy-MM-dd"),
        DeathTimeText = DeathTime.ToString(@"hh\:mm"),
        DeathDateTimeText = DeathStatementService.FormatDeathDateTime(
            DeathDate.ToString("yyyy-MM-dd"), DeathTime.ToString(@"hh\:mm")),
        DeathReason = DeathReason ?? string.Empty,
        DeathCertNo = DeathCertNo ?? string.Empty,
        DeathPlace = DeathPlace ?? string.Empty,
        DeclaredByName = DeclaredByName ?? string.Empty,
        DeclaredByIdCard = DeclaredByIdCard ?? string.Empty,
        DeclaredByPhone = DeclaredByPhone ?? string.Empty,
        DeclaredByType = DeclaredByType ?? string.Empty,
        DeclaredRelation = DeclaredRelation ?? string.Empty,
        DeclaredAtText = DeclaredAtText,
        VillageName = _scope?.VillageName ?? string.Empty,
        TownName = _scope?.TownName ?? string.Empty
    };

    /// <summary>手工录入模式下按表单重新聚合（不写库）</summary>
    [RelayCommand]
    private async Task BuildManualContextAsync()
    {
        if (!EnsureScopeReady()) return;
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(IdCard))
        {
            await ShowTipAsync("请先填写死者姓名与身份证号");
            return;
        }

        await LoadContextAsync(new DeathSearchItem
        {
            Name = Name.Trim(),
            IdCard = IdCard.Trim(),
            DeathDateText = DeathDate.ToString("yyyy-MM-dd"),
            DeathTimeText = DeathTime.ToString(@"hh\:mm"),
            DeathReason = DeathReason ?? string.Empty,
            DeathCertNo = DeathCertNo ?? string.Empty,
            Relation = Relation ?? string.Empty,
            DeclaredByName = DeclaredByName ?? string.Empty,
            DeclaredByType = DeclaredByType ?? string.Empty,
            DeclaredAtText = DeclaredAtText,
            HasDeclaration = IsDeclared
        });
        // 死亡地点不在 DeathSearchItem 快照里，聚合完成后由表单回填（文书 DEATH_PLACE 用）
        if (_context != null)
            _context.DeathPlace = DeathPlace ?? string.Empty;
        IsManualMode = true;
    }

    [RelayCommand]
    private async Task PreviewAsync()
    {
        if (!await PrepareOutputAsync()) return;

        IsPreviewBusy = true;
        try
        {
            var fields = BuildStatementFields();
            var result = await _printExecuteService.GeneratePreviewPdfAsync(
                _statementTemplate!.Id, fields, null, CancellationToken);
            if (result.IsFailure)
            {
                await ShowTipAsync($"文书生成失败: {result.Message}");
                return;
            }

            var tempPath = Path.Combine(OutputPathHelper.GetTempDirectory(),
                $"nc_death_stmt_{DateTime.Now:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}.pdf");
            await File.WriteAllBytesAsync(tempPath, result.Value, CancellationToken);
            PdfFilePath = tempPath;
            HasPdf = true;

            _logger.LogBusiness("死亡情况说明-预览",
                ("TemplateId", _statementTemplate!.Id.ToString()),
                ("Name", DataMasker.MaskName(Name ?? string.Empty)));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "死亡情况说明-预览失败");
            await ShowTipAsync($"预览失败: {ex.Message}");
        }
        finally
        {
            IsPreviewBusy = false;
        }
    }

    [RelayCommand]
    private Task PrintAsync() => ExecuteStatementActionAsync(printToPrinter: true);

    [RelayCommand]
    private Task SaveAsync() => ExecuteStatementActionAsync(printToPrinter: false);

    /// <summary>预览默认展示的联次推送单位（优先镇政府联，其次留存、卫生院）</summary>
    private string CurrentPushUnit()
    {
        if (PrintGov && !string.IsNullOrWhiteSpace(PushUnitGov)) return PushUnitGov.Trim();
        if (PrintRetain && !string.IsNullOrWhiteSpace(PushUnitRetain)) return PushUnitRetain.Trim();
        if (PrintClinic && !string.IsNullOrWhiteSpace(PushUnitClinic)) return PushUnitClinic.Trim();
        return PushUnitGov?.Trim() ?? string.Empty;
    }

    /// <summary>
    /// 三联出具：一份村里留存、一份交镇政府相关部门、一份给镇中心卫生院（用于申报申请死亡证明）。
    /// 三份「推送单位」不同 → <b>逐份生成，不能用份数复制</b>；每份独立批次号，
    /// 落盘文件由 OutputPathHelper 时间戳区分（同目录三个文件）。
    /// </summary>
    [RelayCommand]
    private async Task PrintTripleAsync()
    {
        if (!await PrepareOutputAsync()) return;

        var units = new List<(string Label, string Unit)>();
        if (PrintRetain && !string.IsNullOrWhiteSpace(PushUnitRetain)) units.Add(("村留存", PushUnitRetain.Trim()));
        if (PrintGov && !string.IsNullOrWhiteSpace(PushUnitGov)) units.Add(("镇政府", PushUnitGov.Trim()));
        if (PrintClinic && !string.IsNullOrWhiteSpace(PushUnitClinic)) units.Add(("卫生院", PushUnitClinic.Trim()));

        if (units.Count == 0)
        {
            await ShowTipAsync("请至少勾选一联（村留存 / 镇政府 / 卫生院）");
            return;
        }

        // 记住三联设置：同镇后续文书不必重填
        Preferences.Default.Set(PrefPushRetain, PushUnitRetain ?? string.Empty);
        Preferences.Default.Set(PrefPushGov, PushUnitGov ?? string.Empty);
        Preferences.Default.Set(PrefPushClinic, PushUnitClinic ?? string.Empty);

        // 只勾一联时等同单份打印
        if (units.Count == 1)
        {
            await ExecuteStatementActionAsync(printToPrinter: true, pushUnit: units[0].Unit);
            return;
        }

        var confirm = await _dialogService.DisplayAlertAsync("三联出具",
            $"将输出 {units.Count} 份《死亡情况核实说明》，推送单位分别为：\n"
            + string.Join("\n", units.Select(u => $"  · {u.Label}：{u.Unit}"))
            + "\n\n确认打印？", "打印", "取消");
        if (!confirm) return;

        IsPrinting = true;
        var ok = 0;
        try
        {
            foreach (var (label, unit) in units)
            {
                var result = await PrintOneAsync(Guid.NewGuid().ToString("N").ToUpper(), unit, printToPrinter: true, CancellationToken);
                if (result.IsSuccess)
                {
                    ok++;
                    _logger.LogBusiness("死亡情况说明-三联出件", ("Label", label), ("PushUnit", unit));
                }
                else
                {
                    await ShowTipAsync($"「{label}」出件失败: {result.Message}");
                }
            }
        }
        finally
        {
            IsPrinting = false;
        }

        await ShowTipAsync($"三联出具完成：{ok}/{units.Count} 份（各自独立批次号，可在打印留痕按模板名查询）");
    }

    private async Task ExecuteStatementActionAsync(bool printToPrinter, string? pushUnit = null)
    {
        if (!await PrepareOutputAsync()) return;

        var action = printToPrinter ? "打印" : "保存";
        IsPrinting = true;
        try
        {
            var result = await PrintOneAsync(Guid.NewGuid().ToString("N").ToUpper(), pushUnit, printToPrinter, CancellationToken);
            if (result.IsFailure)
            {
                await ShowTipAsync($"{action}失败: {result.Message}");
                return;
            }

            await ShowTipAsync(string.IsNullOrWhiteSpace(pushUnit)
                ? $"死亡情况说明{action}完成（{Name}）"
                : $"死亡情况说明{action}完成（{Name}），推送单位：{pushUnit}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"死亡情况说明{action}失败");
            await ShowTipAsync($"{action}失败: {ex.Message}");
        }
        finally
        {
            IsPrinting = false;
        }
    }

    /// <summary>单份出件：fields 按联次注入推送单位（调用方负责 IsPrinting 与前置校验）</summary>
    private async Task<Result> PrintOneAsync(string batchNo, string? pushUnit, bool printToPrinter, CancellationToken ct)
    {
        var fields = BuildStatementFields(pushUnit);
        return await _printExecuteService.ExecutePrintAsync(
            _statementTemplate!.Id,
            _statementTemplate.Name,
            StatementBusinessType,
            _context?.DeclarationId ?? _context?.DeathRecordId,
            batchNo,
            fields,
            null,
            Name ?? string.Empty,
            IdCard ?? string.Empty,
            printToPrinter ? SelectedPrinter ?? string.Empty : null!,
            Math.Max(1, Copies),
            IsDuplex,
            printToPrinter: printToPrinter,
            ct);
    }

    /// <summary>手机端推送打印（由 PC 打印代理执行）</summary>
    [RelayCommand]
    private async Task PushPrintAsync()
    {
        if (!await PrepareOutputAsync()) return;

        if (!CanPrint)
        {
            await ShowTipAsync("您没有打印证明的权限（PRINT_LOWINCOMEPROOF），请联系管理员");
            return;
        }

        await ExecuteAsync(async ct =>
        {
            var fields = BuildStatementFields();
            var request = new PrintJobRequest
            {
                BusinessType = StatementBusinessType,
                BusinessId = _context?.DeclarationId ?? _context?.DeathRecordId,
                Classification = StatementBusinessType,
                TemplateId = _statementTemplate!.Id,
                TemplateName = _statementTemplate.Name,
                ApplicantName = Name ?? string.Empty,
                ApplicantIdCard = IdCard ?? string.Empty,
                PrinterName = null,
                Copies = Math.Max(1, Copies),
                IsDuplex = IsDuplex,
                Fields = fields,
                TableRows = new()
            };

            var result = await _printJobFactory.EnqueueAsync(request, ct);
            if (result.IsFailure)
            {
                await ShowTipAsync($"推送打印失败: {result.Message}");
                return;
            }

            _logger.LogBusiness("死亡情况说明-推送打印",
                ("TemplateId", _statementTemplate!.Id.ToString()),
                ("JobNo", result.Value?.JobNo ?? string.Empty));
            await ShowTipAsync($"已推送打印（任务号 {result.Value?.JobNo}），将在电脑端打印。");
        }, "推送打印中…");
    }

    /// <summary>
    /// 手机端三联推送：逐份入队，由电脑端打印代理逐份执行（手机端不生成本地 PDF）。
    /// 三联「推送单位」不同 → 逐份 Enqueue，各自独立任务号。
    /// </summary>
    [RelayCommand]
    private async Task PushPrintTripleAsync()
    {
        if (!await PrepareOutputAsync()) return;

        if (!CanPrint)
        {
            await ShowTipAsync("您没有打印证明的权限（PRINT_LOWINCOMEPROOF），请联系管理员");
            return;
        }

        var units = new List<(string Label, string Unit)>();
        if (PrintRetain && !string.IsNullOrWhiteSpace(PushUnitRetain)) units.Add(("村留存", PushUnitRetain.Trim()));
        if (PrintGov && !string.IsNullOrWhiteSpace(PushUnitGov)) units.Add(("镇政府", PushUnitGov.Trim()));
        if (PrintClinic && !string.IsNullOrWhiteSpace(PushUnitClinic)) units.Add(("卫生院", PushUnitClinic.Trim()));

        if (units.Count == 0)
        {
            await ShowTipAsync("请至少勾选一联（村留存 / 镇政府 / 卫生院）");
            return;
        }

        Preferences.Default.Set(PrefPushRetain, PushUnitRetain ?? string.Empty);
        Preferences.Default.Set(PrefPushGov, PushUnitGov ?? string.Empty);
        Preferences.Default.Set(PrefPushClinic, PushUnitClinic ?? string.Empty);

        await ExecuteAsync(async ct =>
        {
            foreach (var (label, unit) in units)
            {
                var fields = BuildStatementFields(unit);
                var request = new PrintJobRequest
                {
                    BusinessType = StatementBusinessType,
                    BusinessId = _context?.DeclarationId ?? _context?.DeathRecordId,
                    Classification = StatementBusinessType,
                    TemplateId = _statementTemplate!.Id,
                    TemplateName = _statementTemplate.Name,
                    ApplicantName = Name ?? string.Empty,
                    ApplicantIdCard = IdCard ?? string.Empty,
                    PrinterName = null,
                    Copies = Math.Max(1, Copies),
                    IsDuplex = IsDuplex,
                    Fields = fields,
                    TableRows = new()
                };

                var result = await _printJobFactory.EnqueueAsync(request, ct);
                if (result.IsFailure)
                {
                    await ShowTipAsync($"「{label}」推送失败: {result.Message}");
                    return;
                }

                _logger.LogBusiness("死亡情况说明-三联推送",
                    ("Label", label), ("PushUnit", unit), ("JobNo", result.Value?.JobNo ?? string.Empty));
            }

            await ShowTipAsync($"三联已推送 {units.Count} 份，将在电脑端逐份打印。");
        }, "三联推送中…");
    }

    #endregion

    #region 辅助

    private bool EnsureScopeReady()
    {
        if (ScopeReady && _scope != null) return true;
        _ = ShowTipAsync(string.IsNullOrWhiteSpace(ScopeError)
            ? "当前账号未绑定村/社区或乡镇，无法确定查询范围，请联系管理员在「经办机构管理」补齐区划"
            : ScopeError);
        return false;
    }

    /// <summary>
    /// 三联推送单位默认值：按组织名推导（如"柳树镇"→"柳树镇人民政府"/"柳树镇中心卫生院"），
    /// Preferences 记住上次手填值（同镇多份文书不必每次重填）。
    /// </summary>
    private void InitPushUnitDefaults()
    {
        var town = _scope?.TownName?.Trim() ?? string.Empty;
        var village = _scope?.VillageName?.Trim() ?? string.Empty;

        var retainDefault = village.Length > 0 ? $"{village}村民委员会（本委存档）" : "（本委存档）";
        var govDefault = town.Length > 0 ? $"{town}人民政府" : string.Empty;
        var clinicDefault = town.Length > 0 ? $"{town}中心卫生院" : string.Empty;

        PushUnitRetain = Preferences.Default.Get(PrefPushRetain, retainDefault);
        PushUnitGov = Preferences.Default.Get(PrefPushGov, govDefault);
        PushUnitClinic = Preferences.Default.Get(PrefPushClinic, clinicDefault);
    }

    private const string PrefPushRetain = "DeathStatement.PushUnit.Retain";
    private const string PrefPushGov = "DeathStatement.PushUnit.Gov";
    private const string PrefPushClinic = "DeathStatement.PushUnit.Clinic";

    private Task ShowTipAsync(string message) =>
        _dialogService.DisplayAlertAsync("提示", message, "确定");

    #endregion
}
