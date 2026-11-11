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
using NewCosmos.Services.System;
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
    private readonly IRegionService _regionService;
    private readonly IDictionaryService _dictionaryService;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    /// <summary>打印留痕业务类型（权限映射见 PrintExecuteService.PrintPermissionMap → PRINT_LOWINCOMEPROOF）</summary>
    private const string StatementBusinessType = PrintBusinessTypes.DeathStatement;

    private VillageScope? _scope;
    private DeathStatementContext? _context;

    /// <summary>字典选项已加载（每 VM 实例一次；联动跳转返回不重复查库）</summary>
    private bool _formOptionsLoaded;

    /// <summary>首次进入默认值已应用（只补空；带出/手工录入各自显式调用）</summary>
    private bool _formDefaultsApplied;

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
        IElderlyApplicationService elderlyApplicationService,
        IRegionService regionService,
        IDictionaryService dictionaryService)
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
        _regionService = regionService;
        _dictionaryService = dictionaryService;

        Title = "死亡情况说明";
        SearchFrom = DateTime.Today.AddMonths(-12);
        SearchTo = DateTime.Today;
        foreach (var type in DeathDeclarationConstants.DeclaredByTypes)
            DeclaredByTypeOptions.Add(type);

        // Picker 选项（关系下拉为常量；死因/死亡地点/现场所见/业务事项走字典，见 LoadFormOptionsAsync）
        foreach (var rel in RelationChoiceDisplays)
            RelationOptions.Add(rel);
        // 规范化格式选项（证明类型 / 处置分支 / 矛盾纠纷）——固定小集合，常量单一来源
        foreach (var certType in DeathDeclarationConstants.CertTypeOptions)
            CertTypeOptions.Add(certType);
        foreach (var branch in DeathDeclarationConstants.DispositionOptions)
            DispositionOptions.Add(branch);
        foreach (var dispute in DeathDeclarationConstants.DisputeStatusOptions)
            DisputeOptions.Add(dispute);

        // 死亡日期与时刻不预设默认值：组件未选择前保持 null，「必填」由提交校验真实把关
    }

    /// <summary>
    /// 关系下拉选项（申报人关系 / 报告人关系共用）：亲属关系字典口径
    /// （同 ApplicationFormViewModel.MemberRelationOptions 显示文本，存中文与台账存量兼容），
    /// 另加「邻居/朋友/村干部/其他」覆盖报告人非亲属场景。
    /// </summary>
    private static readonly string[] RelationChoiceDisplays =
    {
        "配偶", "儿子", "女儿", "孙子女", "父母", "祖父母", "兄弟姐妹",
        "邻居", "朋友", "村干部", "其他"
    };

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

    #region 生命周期

    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        await LoadScopeAsync();
        await LoadTemplateAsync();
        await LoadPrintPermissionAsync();
        LoadPrinterList();

        // 表单预设选项（字典）→ 首次进入补默认值（只补空；带出/手工录入各自显式调用）
        await LoadFormOptionsAsync();
        if (!_formDefaultsApplied)
        {
            _formDefaultsApplied = true;
            ApplyFormDefaults();
        }

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

            // 落款「所在村」：村级取组织村；镇级降级时组织无村 → 留空由表单下拉选择（提示见黄条）
            DeclaredVillage = _scope.IsTownScope ? string.Empty : _scope.VillageName;
            IsTownScope = _scope.IsTownScope;
            if (_scope.IsTownScope && _scope.TownId is { } townIdForPicker)
                _ = LoadVillageOptionsAsync(townIdForPicker);

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

    /// <summary>
    /// 镇级降级时加载本镇村社区列表（「所在村」下拉数据源）。
    /// 失败只记日志不阻断表单——所在村仍可提交校验把关（必填）。
    /// </summary>
    private async Task LoadVillageOptionsAsync(int townId)
    {
        try
        {
            var result = await _regionService.GetVillagesByTownIdAsync(townId, CancellationToken);
            VillageOptions.Clear();
            if (result.IsFailure || result.Value == null)
            {
                _logger.Warn($"死亡情况说明-加载村列表失败: {result.Message}");
                return;
            }

            foreach (var v in result.Value.Where(v => v.IsActive))
                VillageOptions.Add(v.VillageName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "死亡情况说明-加载村列表异常");
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

    #region 三步线性向导（①申报信息 =原①②③合并 → ②核实与汇总 = 原④⑤合并 → ③文书输出）

    /// <summary>步骤标题单一来源（PC 步进条标签 / 手机步文字共用；2026-11 六步→三步简化）</summary>
    private static readonly string[] StepNames =
    [
        "申报信息", "核实与汇总", "文书输出"
    ];

    /// <summary>总步数（StepFlowBar / MobileStepperBar 的 StepCount 共用）</summary>
    public int TotalSteps => StepNames.Length;

    /// <summary>步骤标题集合（PC 步进条 Labels 绑定）</summary>
    public IReadOnlyList<string> StepTitles => StepNames;

    /// <summary>当前步（1..3）。2026-11 简化：原六步中②③④⑤无门禁纯切屏，合并为三步（方案已拍板）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStep1))]
    [NotifyPropertyChangedFor(nameof(IsStep2))]
    [NotifyPropertyChangedFor(nameof(IsStep3))]
    [NotifyPropertyChangedFor(nameof(IsSteps1To2))]
    [NotifyPropertyChangedFor(nameof(IsPreviousVisible))]
    [NotifyPropertyChangedFor(nameof(NextStepText))]
    [NotifyPropertyChangedFor(nameof(StepTitle))]
    private int _currentStep = 1;

    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public bool IsStep3 => CurrentStep == 3;

    /// <summary>底部按钮组一（第 1-2 步：上一步 / 下一步 / 提交申报）可见</summary>
    public bool IsSteps1To2 => CurrentStep <= 2;

    public bool IsPreviousVisible => CurrentStep > 1;

    /// <summary>下一步按钮文案：第 2 步提示下一段是什么（进段门禁统一在 SwitchStep 拦截）</summary>
    public string NextStepText => CurrentStep switch
    {
        2 => "下一步：文书输出",
        _ => "下一步"
    };

    /// <summary>当前步骤标题（手机端步进条下方说明文字）</summary>
    public string StepTitle => $"第 {CurrentStep} 步 · {StepNames[CurrentStep - 1]}";

    [RelayCommand]
    private void SwitchStep(string? step)
    {
        if (!int.TryParse(step, out var target) || target < 1 || target > StepNames.Length) return;
        if (target == CurrentStep) return;
        if (!EnsureCanEnterStep(target)) return;
        CurrentStep = target;
        OnStepEntered(target);
    }

    /// <summary>下一步（第 1 步先校验姓名+身份证；其余直进，进段门禁由 EnsureCanEnterStep 统一把关）</summary>
    [RelayCommand]
    private async Task NextStepAsync()
    {
        if (CurrentStep == 1 && !await ValidateStep1Async()) return;
        if (CurrentStep >= StepNames.Length) return;
        SwitchStep((CurrentStep + 1).ToString());
    }

    [RelayCommand]
    private void PreviousStep()
    {
        if (CurrentStep > 1) CurrentStep--;
    }

    /// <summary>进段门禁：第 3 步（文书输出）必须已提交申报——台账先行，文书后出（2026-10-29 设计确认）</summary>
    private bool EnsureCanEnterStep(int target)
    {
        if (target == 3 && !IsDeclared)
        {
            _ = ShowTipAsync("请先提交申报，申报成功后方可进入文书输出");
            return false;
        }
        return true;
    }

    /// <summary>进入步骤的副作用：第 2 步（核实与汇总，含原身份清单）若已填身份证但还没汇总，自动补一次（不打断、不覆盖已选）</summary>
    private void OnStepEntered(int target)
    {
        if (target == 2 && _context == null && !string.IsNullOrWhiteSpace(IdCard))
            SafeFireAndForget(BuildManualContextAsync);
    }

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
