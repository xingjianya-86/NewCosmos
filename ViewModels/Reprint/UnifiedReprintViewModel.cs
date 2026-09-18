using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Enums;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Domain.Printing;
using NewCosmos.Services.Platform;
using NewCosmos.Services.Utilities;
using NewCosmos.ViewModels.ArchiveManagement;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.Reprint.Providers;

namespace NewCosmos.ViewModels.Reprint;

/// <summary>
/// 统一补打中心 ViewModel（替代原低收入/临时救助/普惠高龄/资产核查/动态管理五个分散补打入口）：
/// Tab1 按人补打：左栏跨域按人搜索（身份证聚合）→ 分类分支（支持月份过滤）→
///   中栏文书清单/历史打印记录/打印设置 → 右栏 PDF 预览；
/// Tab2 批量打印：域 + 月份/日期范围 + 关键词 + 模板 → 逐户渲染打印 → 进度/失败汇总。
/// ArchiveSet 域组合 ArchiveOutputViewModel（模板集/预览/打印/一键打印复用其逻辑）。
/// </summary>
public partial class UnifiedReprintViewModel : ViewModelBase
{
    private readonly IReadOnlyList<IReprintDomainProvider> _providers;
    private readonly IPrintRecordService _printRecordService;
    private readonly IDialogService _dialogService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerService _logger;
    private readonly IPrinterService _printerService;
    private readonly ITemplateService _templateService;
    private readonly IBusinessTimelineService _timelineService;

    /// <summary>组合的档案输出 VM：ArchiveSet 域的模板加载/预览/打印/一键打印全部复用其逻辑</summary>
    public ArchiveOutputViewModel Output { get; }

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    #region 域与模式

    /// <summary>域 Provider（注册顺序 = 分支展示顺序）</summary>
    public IReadOnlyList<IReprintDomainProvider> Providers => _providers;

    private IReprintDomainProvider? _activeProvider;
    private IAssetVerificationReprintCapability? AssetCapability =>
        _activeProvider as IAssetVerificationReprintCapability;
    private IDynamicRecordReprintCapability? DynamicCapability =>
        _activeProvider as IDynamicRecordReprintCapability;

    #endregion

    #region Tab 切换

    /// <summary>是否批量打印 Tab（false=按人补打）</summary>
    [ObservableProperty]
    private bool _isBatchMode;

    [RelayCommand]
    private void SwitchTab(string tab)
    {
        IsBatchMode = tab == "batch";
        if (IsBatchMode)
            _ = EnsureBatchInitializedAsync();
    }

    #endregion

    #region 按人补打：搜索与聚合

    [ObservableProperty]
    private string _searchKeyword = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ReprintPerson> _personResults = new();

    [ObservableProperty]
    private ReprintPerson? _selectedPerson;

    [ObservableProperty]
    private ObservableCollection<ReprintBranch> _branches = new();

    [ObservableProperty]
    private ReprintBranch? _selectedBranch;

    [ObservableProperty]
    private ObservableCollection<ReprintArchiveItem> _branchRecords = new();

    [ObservableProperty]
    private ReprintArchiveItem? _selectedRecord;

    /// <summary>月份过滤索引（0=全部，1..12=某月；按各域业务时间过滤）</summary>
    [ObservableProperty]
    private int _monthFilterIndex;

    /// <summary>过滤年份（默认当前年）</summary>
    [ObservableProperty]
    private int _filterYear = DateTime.Now.Year;

    public List<int> FilterYearOptions { get; } = new() { DateTime.Now.Year - 1, DateTime.Now.Year, DateTime.Now.Year + 1 };

    /// <summary>分类筛选索引（0=全部，1..N=各业务域，顺序同 Providers）</summary>
    [ObservableProperty]
    private int _domainFilterIndex;

    /// <summary>分类筛选选项（"全部" + 各域显示名，构造时按 Providers 顺序生成）</summary>
    public List<string> DomainFilterOptions { get; }

    /// <summary>当前分类筛中的域（null=全部）</summary>
    private IReprintDomainProvider? ActiveDomainFilter =>
        DomainFilterIndex > 0 && DomainFilterIndex <= _providers.Count ? _providers[DomainFilterIndex - 1] : null;

    partial void OnDomainFilterIndexChanged(int value)
    {
        OnPropertyChanged(nameof(MonthFilterHint));
        // 分类切换 = 仅查询所选域（全部=五域），并重建名单
        _ = RefreshPersonsAsync();
    }

    /// <summary>本次搜索参与的域：选中分类时仅该域，否则全部</summary>
    private List<IReprintDomainProvider> GetActiveProviders()
        => ActiveDomainFilter is { } domain ? new List<IReprintDomainProvider> { domain } : _providers.ToList();

    /// <summary>过滤提示（名单卡片下方，明确分类/月份当前结果与数据口径）</summary>
    public string MonthFilterHint
    {
        get
        {
            var scope = ActiveDomainFilter is { } d ? d.DisplayName : "全部业务";
            if (MonthFilterIndex <= 0)
                return $"{scope} · 当前名单 {PersonResults.Count} 人（全部月份，默认各业务最近记录）";
            var monthPart = _personBusinessRange is { } r
                ? $"{FilterYear}年{MonthFilterIndex}月（B线 {r.From:yyyy-MM-dd}~{r.To:yyyy-MM-dd}）"
                : $"{FilterYear}年{MonthFilterIndex}月";
            return $"{scope} · {monthPart}：命中 {PersonResults.Count} 人";
        }
    }

    /// <summary>月份选项显示（"全部"、"1月".."12月"），经 SelectedIndex 绑定 MonthFilterIndex（int↔int，稳定）</summary>
    public List<string> MonthOptions { get; } = new List<string> { "全部" }.Concat(Enumerable.Range(1, 12).Select(m => $"{m}月")).ToList();

    /// <summary>最近一次搜索的原始记录（月份过滤作用于名单本身：按月重建 PersonResults）</summary>
    private List<ReprintArchiveItem> _cachedRecords = new();

    /// <summary>最近一次按月搜索所用的 B 线周期区间（FilterByMonth 同步过滤用）</summary>
    private (DateTime From, DateTime To)? _personBusinessRange;

    public int SelectedMonthIndex
    {
        get => MonthFilterIndex;
        set => MonthFilterIndex = value;
    }

    partial void OnMonthFilterIndexChanged(int value)
    {
        OnPropertyChanged(nameof(MonthFilterHint));
        // 月份切换 = 服务端按月整月查询（覆盖"最近 N 条"缓存之外的记录）；"全部"回默认名单
        _ = RefreshPersonsAsync();
    }

    partial void OnFilterYearChanged(int value)
    {
        OnPropertyChanged(nameof(MonthFilterHint));
        if (MonthFilterIndex > 0)
            _ = SearchPersonsByMonthAsync();
    }

    /// <summary>统一刷新入口：选中月=服务端按月查询，否则默认名单/关键词搜索</summary>
    private Task RefreshPersonsAsync() => MonthFilterIndex > 0 ? SearchPersonsByMonthAsync() : SearchPersonsAsync();

    /// <summary>搜索命令（SearchBar/按钮入口；月份选中时自动走按月查询）</summary>
    [RelayCommand]
    private async Task RefreshPersons()
    {
        if (MonthFilterIndex > 0) { await SearchPersonsByMonthAsync(); return; }
        await SearchPersonsAsync();
    }

    /// <summary>
    /// 搜索结果落名单（跨域记录 → 按人聚合 → 重建 PersonResults → 保持/清空选中）。
    /// 全部失败且空集：keyword 非空或按月模式弹提示，默认名单静默。
    /// </summary>
    private void ApplyPersonsResult(List<ReprintArchiveItem> allRecords, string keyword, bool isMonthly)
    {
        if (allRecords.Count == 0)
        {
            _cachedRecords = new List<ReprintArchiveItem>();
            PersonResults.Clear();
            SelectedPerson = null;
            OnPropertyChanged(nameof(HasPersonResults));
            OnPropertyChanged(nameof(MonthFilterHint));
            if (!string.IsNullOrEmpty(keyword) || isMonthly)
            {
                var scope = isMonthly ? $"{FilterYear}年{MonthFilterIndex}月" : "该关键词";
                var _ = _dialogService.DisplayAlertAsync("提示", $"{scope}没有匹配记录", "确定");
            }
            return;
        }

        // 缓存全量记录，按当前月份过滤重建名单（月份切换即时生效）
        _cachedRecords = allRecords;
        ApplyMonthFilterToPersons();
        OnPropertyChanged(nameof(MonthFilterHint));
    }

    /// <summary>
    /// 月份过滤作用于人员名单本身：缓存全量记录按月重建 PersonResults。
    /// （此前仅过滤选中人的分支——选月后选中人分支为空，名单流程断裂。）
    /// </summary>
    private void ApplyMonthFilterToPersons()
    {
        var filtered = FilterByMonth(_cachedRecords);
        var persons = filtered
            .GroupBy(r => string.IsNullOrWhiteSpace(r.IdCard) ? "name:" + r.Name : r.IdCard, StringComparer.Ordinal)
            .Select(g => new ReprintPerson(
                g.Key.StartsWith("name:", StringComparison.Ordinal) ? "" : g.Key,
                g.First().Name,
                g.OrderByDescending(r => r.BusinessTime).ToList()))
            .OrderByDescending(p => p.LatestTime)
            .ToList();

        // 命中域显示名明细（替代"N 类"徽标，用户要求展示详细内容）
        var nameByDomainKey = _providers.ToDictionary(p => p.DomainKey, p => p.DisplayName);
        foreach (var p in persons)
            p.DomainSummary = string.Join("·",
                p.DomainKeys.Where(k => nameByDomainKey.ContainsKey(k)).Select(k => nameByDomainKey[k]));

        PersonResults.Clear();
        foreach (var p in persons)
            PersonResults.Add(p);
        OnPropertyChanged(nameof(HasPersonResults));

        // 原选中人在过滤后的名单中消失时清空下游选择；仍存在则保持选中
        if (SelectedPerson != null)
        {
            var kept = string.IsNullOrEmpty(SelectedPerson.IdCard)
                ? persons.FirstOrDefault(p => p.Name == SelectedPerson.Name)
                : persons.FirstOrDefault(p => p.IdCard == SelectedPerson.IdCard);
            SelectedPerson = kept;
        }
    }

    #endregion

    #region 按人补打：打印历史与动态管理留痕

    [ObservableProperty]
    private ObservableCollection<PrintRecordDisplayItem> _printRecords = new();

    [ObservableProperty]
    private ObservableCollection<DynamicPrintHistoryItem> _dynamicHistory = new();

    [ObservableProperty]
    private DynamicPrintHistoryItem? _selectedDynamicHistory;

    /// <summary>动态管理域：已安装打印机列表（档案输出 picker 模式）</summary>
    public ObservableCollection<string> DynamicPrinterNames { get; } = new();

    /// <summary>动态管理域：选中打印机</summary>
    [ObservableProperty]
    private string _selectedDynamicPrinter = string.Empty;

    /// <summary>动态管理域：打印份数</summary>
    [ObservableProperty]
    private int _dynamicCopies = 1;

    #endregion

    #region 资产核查域（按人补打模式）

    [ObservableProperty]
    private ObservableCollection<TemplateSelectItem> _assetTemplates = new();

    [ObservableProperty]
    private TemplateSelectItem? _selectedAssetTemplate;

    /// <summary>核查直打是否同时生成 PDF</summary>
    [ObservableProperty]
    private bool _isGeneratePdf;

    #endregion

    #region 批量打印

    /// <summary>批量域选中索引（Providers 顺序）</summary>
    [ObservableProperty]
    private int _batchDomainIndex;

    /// <summary>批量关键词（ArchiveSet/核查用；动态管理域按月不过滤关键词）</summary>
    [ObservableProperty]
    private string _batchKeyword = string.Empty;

    /// <summary>批量年份（默认当前年）</summary>
    [ObservableProperty]
    private int _batchYear = DateTime.Now.Year;

    /// <summary>批量月份索引（0=全部；>0 时按该月对应的 B 线周期区间筛选）</summary>
    [ObservableProperty]
    private int _batchMonthIndex;

    /// <summary>批量核查模板集</summary>
    [ObservableProperty]
    private ObservableCollection<TemplateSelectItem> _batchTemplates = new();

    [ObservableProperty]
    private TemplateSelectItem? _selectedBatchTemplate;

    /// <summary>批量筛选出的记录集</summary>
    [ObservableProperty]
    private ObservableCollection<ReprintArchiveItem> _batchRecords = new();

    [ObservableProperty]
    private bool _batchIsGenerating;

    [ObservableProperty]
    private double _batchProgress;

    [ObservableProperty]
    private string _batchProgressText = string.Empty;

    [ObservableProperty]
    private string _batchResultSummary = string.Empty;

    public List<int> BatchYearOptions { get; } = new() { DateTime.Now.Year - 1, DateTime.Now.Year, DateTime.Now.Year + 1 };

    /// <summary>批量月份选项（0=全部，1..12=B线业务月；月份筛选按 B 线周期区间：上月15日~本月15日）</summary>
    public List<string> BatchMonthOptions { get; } = new List<string> { "全部" }
        .Concat(Enumerable.Range(1, 12).Select(m => $"{m}月")).ToList();

    /// <summary>批量模式：0=整份档案（逐户全部适用模板），1=指定模板（逐户该模板）</summary>
    [ObservableProperty]
    private int _batchModeIndex;

    public List<string> BatchModeOptions { get; } = new() { "整份档案（逐户全部适用模板）", "指定模板（逐户该模板）" };

    /// <summary>是否「指定模板」模式</summary>
    public bool IsBatchTemplateMode => BatchModeIndex == 1;

    partial void OnBatchModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsBatchTemplateMode));
        OnPropertyChanged(nameof(ShowBatchTemplatePicker));
        // 切到「指定模板」而模板列表尚未加载时补加载（否则模板下拉为空，运行时报未选择模板）
        if (value == 1 && BatchTemplates.Count == 0)
            _ = LoadBatchTemplatesAsync();
    }

    /// <summary>模板 Picker 可见性：核查域恒显；档案集域仅「指定模板」模式；动态域不显</summary>
    public bool ShowBatchTemplatePicker =>
        !IsBatchDynamicMode && BatchTemplates.Count > 0 && (IsBatchAssetMode || BatchModeIndex == 1);

    /// <summary>批量域显示名列表（Picker）</summary>
    public List<string> BatchDomainOptions => _providers.Select(p => p.DisplayName).ToList();

    /// <summary>批量模式当前域是否为核查域（模板 Picker 可见性）</summary>
    public bool IsBatchAssetMode
    {
        get
        {
            var provider = _providers.ElementAtOrDefault(BatchDomainIndex);
            return provider?.Mode == ReprintDomainMode.AssetVerification;
        }
    }

    /// <summary>批量模式当前域是否为动态管理域（关键词不必填提示）</summary>
    public bool IsBatchDynamicMode
    {
        get
        {
            var provider = _providers.ElementAtOrDefault(BatchDomainIndex);
            return provider?.Mode == ReprintDomainMode.DynamicRecord;
        }
    }

    partial void OnBatchDomainIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsBatchAssetMode));
        OnPropertyChanged(nameof(IsBatchDynamicMode));
        OnPropertyChanged(nameof(HasBatchKeywordInput));
        OnPropertyChanged(nameof(ShowBatchTemplatePicker));
        if (BatchDomainIndex >= 0 && BatchDomainIndex < _providers.Count)
        {
            _ = LoadBatchTemplatesAsync();
            _ = BatchSearchAsync();
        }
    }

    partial void OnBatchYearChanged(int value) => _ = BatchSearchAsync();

    partial void OnBatchMonthIndexChanged(int value) => _ = BatchSearchAsync();

    /// <summary>批量关键词输入是否可见/可用（动态管理域按月名单，无需关键词）</summary>
    public bool HasBatchKeywordInput => !IsBatchDynamicMode;

    /// <summary>批量页月份过滤是否生效（0=全部，不加时间轴过滤）</summary>
    public bool HasBatchMonthFilter => BatchMonthIndex > 0;

    /// <summary>指定年月对应的 B 线周期区间（含端点）；month&lt;=0 返回 null（全部）</summary>
    private async Task<(DateTime From, DateTime To)?> GetBusinessRangeAsync(int year, int month)
    {
        if (month <= 0) return null;
        var tl = await _timelineService.CalculateTimelineAsync(year, month, TimelineType.BusinessProcess);
        return (tl.CycleStartDate.Date, tl.CycleEndDate.Date);
    }

    /// <summary>批量当月 B 线周期区间（含端点）；全部月份返回 null</summary>
    private Task<(DateTime From, DateTime To)?> GetBatchBusinessRangeAsync()
        => GetBusinessRangeAsync(BatchYear, BatchMonthIndex);

    /// <summary>B线区间跨的自然月列表（如 7/15~8/15 → (年,7)、(年,8)）</summary>
    private static IEnumerable<(int Year, int Month)> SpannedMonths(DateTime from, DateTime to)
    {
        var cur = new DateTime(from.Year, from.Month, 1);
        var last = new DateTime(to.Year, to.Month, 1);
        while (cur <= last)
        {
            yield return (cur.Year, cur.Month);
            cur = cur.AddMonths(1);
        }
    }

    /// <summary>批量页 B 线区间标签（摘要/日志用）</summary>
    private string BatchScopeText((DateTime From, DateTime To)? range)
        => range is { } r
            ? $"{BatchYear}年{BatchMonthIndex}月（B线 {r.From:yyyy-MM-dd}~{r.To:yyyy-MM-dd}）"
            : "全部月份";

    /// <summary>
    /// 进入批量 Tab / 页面出现时初始化：按当前域加载模板（自动选中）并拉取一次名单；
    /// 修复"进入批量页未加载模板，导致运行时报未选择模板"。
    /// </summary>
    public async Task EnsureBatchInitializedAsync()
    {
        if (BatchDomainIndex < 0 || BatchDomainIndex >= _providers.Count) return;
        if (BatchTemplates.Count == 0)
            await LoadBatchTemplatesAsync();
        if (BatchRecords.Count == 0)
            await BatchSearchAsync();
    }

    #endregion

    #region 计算属性

    /// <summary>整体忙碌（自身或组合的档案输出 VM 忙碌），供页面加载蒙版绑定</summary>
    public bool IsOverallBusy => IsBusy || Output.IsBusy;

    public bool HasPersonResults => PersonResults.Count > 0;
    public bool HasSelectedPerson => SelectedPerson != null;
    public bool HasSelectedBranch => SelectedBranch != null;
    public bool HasSelectedRecord => SelectedRecord != null;

    /// <summary>外层"历史打印记录"卡可见性：动态域下留痕在其专属卡内展示，避免重复</summary>
    public bool ShowPrintHistory => HasSelectedRecord && !IsDynamicMode;

    public string SelectedPersonDisplay => SelectedPerson == null
        ? ""
        : $"{DataMasker.MaskName(SelectedPerson.Name)}（命中 {SelectedPerson.DomainKeys.Count} 类）";

    public string SelectedRecordName => DataMasker.MaskName(SelectedRecord?.Name ?? "");
    public string SelectedRecordIdCard => DataMasker.MaskIdCard(SelectedRecord?.IdCard ?? "");
    /// <summary>选中记录的业务文号（名单投影 No 列）</summary>
    public string SelectedRecordNo => SelectedRecord?.No ?? "";
    /// <summary>选中记录的中文补充说明（分类全名/救助类型/核查月份等，中文化入口）</summary>
    public string SelectedRecordExtra => SelectedRecord?.Extra ?? "";

    /// <summary>当前模式可见性（XAML 区块分派）</summary>
    public bool IsArchiveSetMode => _activeProvider?.Mode == ReprintDomainMode.ArchiveSet;
    public bool IsAssetMode => _activeProvider?.Mode == ReprintDomainMode.AssetVerification;
    public bool IsDynamicMode => _activeProvider?.Mode == ReprintDomainMode.DynamicRecord;

    /// <summary>中栏标题（随域）</summary>
    public string BranchTitle => SelectedBranch == null ? "分类分支" : $"{SelectedBranch.DisplayName}（{BranchRecords.Count} 条）";

    #endregion

    public UnifiedReprintViewModel(
        IEnumerable<IReprintDomainProvider> providers,
        ArchiveOutputViewModel outputViewModel,
        IPrintRecordService printRecordService,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        IPrinterService printerService,
        IBusinessTimelineService timelineService)
    {
        _providers = providers.ToList();
        DomainFilterOptions = new List<string> { "全部" }
            .Concat(_providers.Select(p => p.DisplayName)).ToList();
        _printRecordService = printRecordService;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _printerService = printerService;
        _templateService = serviceProvider.GetRequiredService<ITemplateService>();
        _timelineService = timelineService;
        Output = outputViewModel;
        Title = "历史补打中心";

        Output.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ArchiveOutputViewModel.IsBusy))
                OnPropertyChanged(nameof(IsOverallBusy));
        };
    }

    protected override void OnBusyStateChanged() => OnPropertyChanged(nameof(IsOverallBusy));

    /// <summary>
    /// 带初始域进入（主页入口定向）：切回按人 Tab 并把批量域预置到该域
    /// （按人模式跨域聚合无需预设域；批量 Picker 预置方便直接批量）。
    /// </summary>
    public void PrepareForDomain(string domainKey)
    {
        IsBatchMode = false;
        var index = _providers.ToList().FindIndex(p => string.Equals(p.DomainKey, domainKey, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
            BatchDomainIndex = index;
    }

    /// <summary>进入页面不预加载名单：由用户选择月份后触发（OnMonthFilterIndexChanged → RefreshPersonsAsync）；
    /// 若直接落在批量 Tab，则初始化批量域模板与名单。</summary>
    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        if (IsBatchMode)
            await EnsureBatchInitializedAsync();
    }

    /// <summary>
    /// 返回上一页：清理 PII 与预览临时文件，再走基类返回逻辑
    /// </summary>
    public override async Task GoBackAsync()
    {
        Output.Cleanup();
        await base.GoBackAsync();
    }

    // ========================
    //  按人补打：搜索 → 人 → 分支 → 记录
    // ========================

    /// <summary>
    /// 跨域按人搜索（五源并行，身份证聚合去重）。
    /// 关键词为空 = 默认最近名单（各域按业务时间倒序 LIMIT 20，可只用月份筛选）。
    /// </summary>
    [RelayCommand]
    private async Task SearchPersonsAsync()
    {
        var keyword = SearchKeyword?.Trim() ?? string.Empty;

        try
        {
            if (IsBusy) return;
            IsBusy = true;
            _logger.LogBusiness("统一补打中心·按人搜索",
                string.IsNullOrEmpty(keyword) ? ("Mode", "默认名单") : ("Keyword", keyword));

            // 参与域并行（分类筛选时单域；单源失败容忍为该域无数据，全部失败才提示）
            var providerList = GetActiveProviders();
            var tasksArray = providerList.Select(p => p.SearchByPersonAsync(keyword, 20, CancellationToken)).ToArray();
            await Task.WhenAll(tasksArray);

            var allRecords = new List<ReprintArchiveItem>();
            for (var i = 0; i < tasksArray.Length; i++)
            {
                var r = await tasksArray[i];
                if (r.IsSuccess && r.Value != null)
                    allRecords.AddRange(r.Value);
                else
                    Logger?.Warn($"补打搜索·域 {providerList[i].DisplayName} 失败: {r.Message}");
            }

            ApplyPersonsResult(allRecords, keyword, isMonthly: false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "统一补打中心·按人搜索失败");
            await _dialogService.DisplayAlertAsync("错误", $"搜索失败: {ex.Message}", "确定");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 跨域按月搜索：按 B 线周期区间（上月15日~本月15日）合并两自然月查询后过滤；
    /// 关键词非空时在结果上客户端过滤。
    /// </summary>
    private async Task SearchPersonsByMonthAsync()
    {
        var keyword = SearchKeyword?.Trim() ?? string.Empty;

        try
        {
            if (IsBusy) return;
            IsBusy = true;

            var range = await GetBusinessRangeAsync(FilterYear, MonthFilterIndex);
            _personBusinessRange = range;
            OnPropertyChanged(nameof(MonthFilterHint));
            if (range == null) return;

            _logger.LogBusiness("统一补打中心·按月搜索(B线)",
                ("Year", FilterYear), ("Month", MonthFilterIndex),
                ("From", range.Value.From), ("To", range.Value.To));

            var providerList = GetActiveProviders();
            var tasksArray = providerList
                .Select(p => SearchByBusinessRangeAsync(p, range.Value.From, range.Value.To, CancellationToken))
                .ToArray();
            await Task.WhenAll(tasksArray);

            var allRecords = new List<ReprintArchiveItem>();
            for (var i = 0; i < tasksArray.Length; i++)
            {
                var r = await tasksArray[i];
                if (r.IsSuccess && r.Value != null)
                    allRecords.AddRange(r.Value);
                else
                    Logger?.Warn($"补打按月搜索·域 {providerList[i].DisplayName} 失败: {r.Message}");
            }

            if (!string.IsNullOrEmpty(keyword))
                allRecords = allRecords
                    .Where(r => (r.Name?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
                             || (r.IdCard?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
                    .ToList();

            ApplyPersonsResult(allRecords, keyword, isMonthly: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "统一补打中心·按月搜索失败");
            await _dialogService.DisplayAlertAsync("错误", $"按月查询失败: {ex.Message}", "确定");
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedPersonChanged(ReprintPerson? value)
    {
        // 选中高亮由 IsSelected 驱动（每次选择变化全列表刷新，根除 VSM 残留）
        foreach (var p in PersonResults)
            p.IsSelected = ReferenceEquals(p, value);

        OnPropertyChanged(nameof(HasSelectedPerson));
        OnPropertyChanged(nameof(SelectedPersonDisplay));
        RebuildBranches();
    }

    /// <summary>点选人员（BindableLayout 条目 TapGesture 用；赋值即触发既有钻取链）</summary>
    [RelayCommand]
    private void SelectPerson(ReprintPerson person)
    {
        if (person == null || ReferenceEquals(person, SelectedPerson)) return;
        _logger.LogBusiness("统一补打中心·选中人员", ("Name", DataMasker.MaskName(person.Name)));
        SelectedPerson = person;
    }

    /// <summary>点选分类分支（BindableLayout 条目 TapGesture 用）</summary>
    [RelayCommand]
    private void SelectBranch(ReprintBranch branch)
    {
        if (branch == null || ReferenceEquals(branch, SelectedBranch)) return;
        _logger.LogBusiness("统一补打中心·选中分类分支", ("Domain", branch.DisplayName));
        SelectedBranch = branch;
    }

    /// <summary>点选记录（BindableLayout 条目 TapGesture 用）</summary>
    [RelayCommand]
    private void SelectRecord(ReprintArchiveItem record)
    {
        if (record == null || ReferenceEquals(record, SelectedRecord)) return;
        _logger.LogBusiness("统一补打中心·选中记录", ("Domain", record.DomainKey), ("BusinessId", record.BusinessId.ToString()));
        SelectedRecord = record;
    }

    /// <summary>按选中人重建分类分支（月过滤作用于分支记录与计数）</summary>
    private void RebuildBranches()
    {
        Branches.Clear();
        BranchRecords.Clear();
        SelectedBranch = null;
        SelectedRecord = null;
        PrintRecords.Clear();
        DynamicHistory.Clear();
        AssetTemplates.Clear();
        ResetOutput();

        if (SelectedPerson == null)
        {
            OnPropertyChanged(nameof(HasSelectedBranch));
            return;
        }

        var filtered = FilterByMonth(SelectedPerson.Records);
        var branches = _providers
            .Select(p => new ReprintBranch(p.DomainKey, p.DisplayName, p.Mode,
                filtered.Where(r => r.DomainKey == p.DomainKey).ToList()))
            .Where(b => b.Count > 0)
            .ToList();

        foreach (var b in branches)
            Branches.Add(b);

        // 单分支自动选中，多分支等用户点选
        if (branches.Count == 1)
            SelectedBranch = branches[0];

        OnPropertyChanged(nameof(HasSelectedBranch));
    }

    /// <summary>月份过滤（0=全部；按 B 线周期区间过滤；BusinessTime 为空值时不过滤）</summary>
    private List<ReprintArchiveItem> FilterByMonth(IEnumerable<ReprintArchiveItem> records)
    {
        if (MonthFilterIndex <= 0)
            return records.ToList();

        if (_personBusinessRange is { } r)
        {
            var hiExclusive = r.To.Date.AddDays(1);
            return records
                .Where(x => x.BusinessTime == DateTime.MinValue
                            || (x.BusinessTime.Date >= r.From.Date && x.BusinessTime.Date < hiExclusive))
                .ToList();
        }

        // 兜底（理论上按月搜索后已有区间）：按自然月
        return records
            .Where(r => r.BusinessTime.Year == FilterYear && r.BusinessTime.Month == MonthFilterIndex
                        || r.BusinessTime == DateTime.MinValue)
            .ToList();
    }

    partial void OnSelectedBranchChanged(ReprintBranch? value)
    {
        // ⚠️ _activeProvider 必须先赋值再发模式通知——
        // 否则首次选中分支时 XAML 求值 IsArchiveSetMode/IsAssetMode/IsDynamicMode 读到旧值(null)，
        // 三个域的补打操作卡全部不显示（用户只见"历史打印记录"，误以为没有补打功能）。
        _activeProvider = value == null ? null : _providers.FirstOrDefault(p => p.DomainKey == value.DomainKey);

        OnPropertyChanged(nameof(BranchTitle));
        OnPropertyChanged(nameof(IsArchiveSetMode));
        OnPropertyChanged(nameof(IsAssetMode));
        OnPropertyChanged(nameof(IsDynamicMode));

        // 选中高亮由 IsSelected 驱动
        foreach (var b in Branches)
            b.IsSelected = ReferenceEquals(b, value);

        PrintRecords.Clear();
        DynamicHistory.Clear();
        AssetTemplates.Clear();
        ResetOutput();

        if (value == null || _activeProvider == null)
        {
            BranchRecords.Clear();
            SelectedRecord = null;
            return;
        }

        // 分支内记录（通常 1 条；同人多条时由用户选择）
        BranchRecords.Clear();
        foreach (var r in FilterByMonth(value.Records))
            BranchRecords.Add(r);

        if (BranchRecords.Count > 0)
            SelectedRecord = BranchRecords[0];
    }

    partial void OnSelectedRecordChanged(ReprintArchiveItem? value)
    {
        // 选中高亮由 IsSelected 驱动
        foreach (var r in BranchRecords)
            r.IsSelected = ReferenceEquals(r, value);

        OnPropertyChanged(nameof(HasSelectedRecord));
        OnPropertyChanged(nameof(SelectedRecordName));
        OnPropertyChanged(nameof(SelectedRecordIdCard));
        OnPropertyChanged(nameof(SelectedRecordNo));
        OnPropertyChanged(nameof(SelectedRecordExtra));
        OnPropertyChanged(nameof(ShowPrintHistory));
        PrintRecords.Clear();
        DynamicHistory.Clear();
        ResetOutput();

        if (value == null || _activeProvider == null) return;

        var record = value;
        SafeFireAndForget(async () => await PrepareSelectedRecordAsync(record));
    }

    /// <summary>选中记录准备流程：按模式分派（ArchiveSet→Output；核查→模板；动态→留痕）</summary>
    private async Task PrepareSelectedRecordAsync(ReprintArchiveItem record)
    {
        if (IsBusy) return; // 串行化：前一次准备未结束不重复触发
        IsBusy = true;
        try
        {
            switch (_activeProvider!.Mode)
            {
                case ReprintDomainMode.ArchiveSet:
                    await PrepareArchiveSetAsync(record);
                    break;
                case ReprintDomainMode.AssetVerification:
                    await PrepareAssetAsync(record);
                    break;
                case ReprintDomainMode.DynamicRecord:
                    await PrepareDynamicAsync(record);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "统一补打中心·选中记录准备失败");
            await _dialogService.DisplayAlertAsync("错误", $"加载打印数据失败: {ex.Message}", "确定");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task PrepareArchiveSetAsync(ReprintArchiveItem record)
    {
        var prepare = await _activeProvider!.PrepareAsync(record.BusinessId, CancellationToken);
        if (prepare.IsFailure || prepare.Value == null)
        {
            await _dialogService.DisplayAlertAsync("提示", prepare.Message ?? "加载打印数据失败", "确定");
            return;
        }
        var payload = prepare.Value;

        _logger.LogBusiness("统一补打中心·构建打印数据完成",
            ("Domain", payload.DomainKey),
            ("BusinessId", payload.BusinessId.ToString()),
            ("Applicant", DataMasker.MaskName(payload.Name)));

        PrintNavigationData.BusinessType = payload.DomainKey;
        PrintNavigationData.BusinessId = payload.BusinessId;
        PrintNavigationData.Classification = payload.Classification;
        PrintNavigationData.Status = string.IsNullOrEmpty(payload.Status) ? record.Status : payload.Status;
        PrintNavigationData.FieldData = payload.FieldData;
        PrintNavigationData.TableData = payload.TableData;
        PrintNavigationData.SupporterTableData = payload.SupporterTableData;

        await Output.InitializeAsync();
        await LoadPrintHistoryAsync(record);
    }

    private async Task PrepareAssetAsync(ReprintArchiveItem record)
    {
        // 核查域：加载核查类模板列表（单模板直打，预览/打印走能力接口）
        AssetTemplates.Clear();
        var templates = await (AssetCapability?.GetTemplatesAsync(CancellationToken) ?? Task.FromResult(new List<TemplateSelectItem>()));
        foreach (var t in templates)
            AssetTemplates.Add(t);
        SelectedAssetTemplate = templates.Count > 0 ? templates[0] : null;
        await LoadPrintHistoryAsync(record);
    }

    private void LoadDynamicPrinters()
    {
        try
        {
            DynamicPrinterNames.Clear();
            foreach (var p in _printerService.GetInstalledPrinters())
                DynamicPrinterNames.Add(p);
            var def = _printerService.GetDefaultPrinter();
            if (!string.IsNullOrEmpty(def) && DynamicPrinterNames.Contains(def))
                SelectedDynamicPrinter = def;
            else if (DynamicPrinterNames.Count > 0)
                SelectedDynamicPrinter = DynamicPrinterNames[0];
        }
        catch (Exception ex)
        {
            _logger.Warn($"加载打印机列表失败: {ex.Message}");
        }
    }

    private async Task PrepareDynamicAsync(ReprintArchiveItem record)
    {
        LoadDynamicPrinters();
        // 动态管理域：加载留痕列表（原样补打/重新生成）
        var history = DynamicCapability != null
            ? await DynamicCapability.GetPrintHistoryAsync(record.BusinessId, CancellationToken)
            : Result.Failure<List<DynamicPrintHistoryItem>>(ErrorCodes.NOT_FOUND, "动态管理补打能力未注册");
        DynamicHistory.Clear();
        if (history.IsSuccess && history.Value != null)
        {
            foreach (var h in history.Value)
                DynamicHistory.Add(h);
        }
        else if (history.IsFailure)
        {
            Logger?.Warn($"动态管理留痕加载失败: {history.Message}");
        }
    }

    /// <summary>ArchiveSet/核查域留痕查询（按域 BusinessType + BusinessId）</summary>
    private async Task LoadPrintHistoryAsync(ReprintArchiveItem record)
    {
        PrintRecords.Clear();
        var result = await _printRecordService.GetByBusinessAsync(record.DomainKey, record.BusinessId, CancellationToken);
        if (result.IsFailure)
        {
            _logger.Warn($"打印留痕查询失败: {result.Message}");
            return;
        }
        foreach (var pr in result.Value ?? [])
        {
            PrintRecords.Add(new PrintRecordDisplayItem(
                pr.Id,
                pr.TemplateName,
                pr.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                DataMasker.MaskName(pr.OperatorName),
                pr.PrinterName,
                pr.Status));
        }
    }

    private void ResetOutput()
    {
        Output.Templates.Clear();
        Output.SelectedPreviewTemplate = null!;
        Output.PdfFilePath = string.Empty;
        Output.HasPdf = false;
    }

    // ========================
    //  核查域：预览/直打（按人补打模式）
    // ========================

    [RelayCommand]
    private async Task PreviewAssetAsync()
    {
        if (SelectedRecord == null || SelectedAssetTemplate == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择核查记录与模板", "确定");
            return;
        }
        try
        {
            IsBusy = true;
            var result = await AssetCapability!.RenderPreviewAsync(SelectedRecord.BusinessId, SelectedAssetTemplate, CancellationToken);
            if (result.IsSuccess && result.Value != null)
            {
                Output.PdfFilePath = result.Value;
                Output.HasPdf = true;
            }
            else
            {
                await _dialogService.DisplayAlertAsync("错误", result.Message ?? "预览生成失败", "确定");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PrintAssetAsync()
    {
        if (SelectedRecord == null || SelectedAssetTemplate == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择核查记录与模板", "确定");
            return;
        }
        try
        {
            IsBusy = true;
            var result = await AssetCapability!.PrintSingleAsync(SelectedRecord.BusinessId, SelectedAssetTemplate, IsGeneratePdf, CancellationToken);
            if (result.IsSuccess)
            {
                await _dialogService.DisplayAlertAsync("打印完成", $"已发送到打印机: {SelectedAssetTemplate.Name}", "确定");
            }
            else
            {
                await _dialogService.DisplayAlertAsync("错误", result.Message ?? "打印失败", "确定");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ========================
    //  动态管理域：原样补打 / 重新生成（按人补打模式）
    // ========================

    [RelayCommand]
    private async Task ReprintOriginalAsync()
    {
        if (SelectedDynamicHistory == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择一条打印留痕", "确定");
            return;
        }
        try
        {
            IsBusy = true;
            var result = await DynamicCapability!.ReprintOriginalAsync(SelectedDynamicHistory.Id, CancellationToken);
            if (result.IsSuccess && result.Value != null)
            {
                Output.PdfFilePath = result.Value;
                Output.HasPdf = true;
            }
            else
            {
                await _dialogService.DisplayAlertAsync("提示", result.Message ?? "原样补打失败", "确定");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RegenerateAsync()
    {
        if (SelectedRecord == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择档案记录", "确定");
            return;
        }
        try
        {
            IsBusy = true;
            // 选中历史档案(Stopped)时按该版本原样生成；否则归一到现役
            var normalize = !string.Equals(SelectedRecord.Status, "Stopped", StringComparison.OrdinalIgnoreCase);
            var result = await DynamicCapability!.RegenerateAsync(SelectedRecord.BusinessId, normalize, CancellationToken);
            if (result.IsSuccess && result.Value != null)
            {
                Output.PdfFilePath = result.Value;
                Output.HasPdf = true;
                // 刷新留痕列表（预览已在右栏展示，不再弹出/打开外部程序）
                await PrepareDynamicAsync(SelectedRecord);
            }
            else
            {
                await _dialogService.DisplayAlertAsync("提示", result.Message ?? "重新生成失败", "确定");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>动态管理域：按选中打印机打印当前档案（生成源文件+PDF并留痕）</summary>
    [RelayCommand]
    private async Task PrintDynamicAsync()
    {
        if (SelectedRecord == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择档案记录", "确定");
            return;
        }
        if (string.IsNullOrWhiteSpace(SelectedDynamicPrinter))
        {
            await _dialogService.DisplayAlertAsync("提示", "请先选择打印机", "确定");
            return;
        }

        try
        {
            IsBusy = true;
            var result = await DynamicCapability!.PrintAsync(
                SelectedRecord.BusinessId, SelectedDynamicPrinter, Math.Max(1, DynamicCopies), CancellationToken);

            if (result.IsFailure)
            {
                await _dialogService.DisplayAlertAsync("错误", result.Message ?? "打印失败", "确定");
                return;
            }

            var (success, failed) = result.Value;
            var msg = $"已发送 {success} 户到打印机（{SelectedDynamicPrinter}）";
            if (failed.Count > 0)
                msg += $"\n失败 {failed.Count} 户";
            await _dialogService.DisplayAlertAsync("打印完成", msg, "确定");
            await PrepareDynamicAsync(SelectedRecord);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ========================
    //  批量打印（Tab2，五域通用）
    // ========================

    /// <summary>批量模板加载（核查域=核查类模板；ArchiveSet 域无需模板选择——逐户打全部适用模板；动态管理域=单模板月度合并）</summary>
    private async Task LoadBatchTemplatesAsync()
    {
        BatchTemplates.Clear();
        SelectedBatchTemplate = null;
        var provider = _providers.ElementAtOrDefault(BatchDomainIndex);
        if (provider == null) return;

        if (provider.Mode == ReprintDomainMode.AssetVerification)
        {
            if (provider is IAssetVerificationReprintCapability cap)
            {
                var templates = await cap.GetTemplatesAsync(CancellationToken);
                foreach (var t in templates)
                    BatchTemplates.Add(t);
                SelectedBatchTemplate = templates.Count > 0 ? templates[0] : null;
            }
            OnPropertyChanged(nameof(ShowBatchTemplatePicker));
            return;
        }

        if (provider.Mode == ReprintDomainMode.DynamicRecord)
        {
            OnPropertyChanged(nameof(ShowBatchTemplatePicker));
            return;
        }

        // ArchiveSet/其它域：按域限定加载模板库，供「指定模板」批量
        var all = await _templateService.GetAllAsync(CancellationToken);
        // 优先按模板分类（与档案制作同源）；未接入域回退名称前缀
        var scope = ArchiveCategoryResolver.GetReprintDomainCategories(provider.DomainKey);
        foreach (var t in all.Where(t => scope is null
                     ? MatchesDomainTemplate(t.Name, provider.DomainKey)
                     : t.Categories.Intersect(scope).Any()))
        {
            BatchTemplates.Add(new TemplateSelectItem
            {
                TemplateId = t.Id,
                Name = t.Name,
                FileType = t.FileType,
                IsSelected = true,
                IsApplicable = true,
                BaseCopies = 1
            });
        }
        SelectedBatchTemplate = BatchTemplates.Count > 0 ? BatchTemplates[0] : null;
        OnPropertyChanged(nameof(ShowBatchTemplatePicker));
    }

    /// <summary>域模板过滤兜底（名称前缀；已接入域走 ArchiveCategoryResolver.GetReprintDomainCategories）</summary>
    private static bool MatchesDomainTemplate(string name, string domainKey) => domainKey switch
    {
        "FamilyApplication" => name.StartsWith("档案_") || name.StartsWith("通用_"),
        "TempRelief" => name.StartsWith("临时救助"),
        "ElderlyBenefits" => name.StartsWith("普惠高龄"),
        "DynamicManagementRecord" => name.Contains("动态管理记录"),
        _ => true
    };

    /// <summary>批量筛选：按域 + 关键词 + 月份（B线周期区间）拉取记录集</summary>
    [RelayCommand]
    private async Task BatchSearchAsync()
    {
        var provider = _providers.ElementAtOrDefault(BatchDomainIndex);
        if (provider == null) return;

        try
        {
            IsBusy = true;
            BatchRecords.Clear();
            BatchResultSummary = string.Empty;

            var range = await GetBatchBusinessRangeAsync();
            var scope = BatchScopeText(range);

            if (provider.Mode == ReprintDomainMode.DynamicRecord)
            {
                // 动态管理批量：按 B 线区间变更人群（不按关键词过滤），预览户数后合并导出
                if (range == null)
                {
                    BatchResultSummary = "动态管理档案请选择具体月份（不支持「全部」）";
                    return;
                }
                var listResult = await SearchByBusinessRangeAsync(provider, range.Value.From, range.Value.To, CancellationToken);
                if (listResult.IsFailure)
                {
                    await _dialogService.DisplayAlertAsync("提示", listResult.Message ?? "变更人群查询失败", "确定");
                    return;
                }
                foreach (var r in listResult.Value)
                    BatchRecords.Add(r);
                BatchResultSummary = BatchRecords.Count == 0
                    ? $"{scope} 无变更档案"
                    : $"{scope} 变更人群 {BatchRecords.Count} 户（执行后合并为一份 PDF）";
                return;
            }

            var keyword = BatchKeyword?.Trim();
            List<ReprintArchiveItem> records;
            if (range == null)
            {
                // 全部月份：默认最近名单（关键词可空）
                var search = await provider.SearchByPersonAsync(keyword!, 500, CancellationToken);
                if (search.IsFailure)
                {
                    await _dialogService.DisplayAlertAsync("提示", search.Message ?? "批量筛选失败", "确定");
                    return;
                }
                records = search.Value ?? new List<ReprintArchiveItem>();
            }
            else
            {
                // 指定月份：按 B 线周期区间（上月15日~本月15日）合并两自然月查询后过滤
                var listResult = await SearchByBusinessRangeAsync(provider, range.Value.From, range.Value.To, CancellationToken);
                if (listResult.IsFailure)
                {
                    await _dialogService.DisplayAlertAsync("提示", listResult.Message ?? "批量筛选失败", "确定");
                    return;
                }
                records = listResult.Value;
                if (!string.IsNullOrEmpty(keyword))
                    records = records
                        .Where(r => (r.Name?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)
                                 || (r.IdCard?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false))
                        .ToList();
            }

            foreach (var r in records)
                BatchRecords.Add(r);

            BatchResultSummary = records.Count == 0
                ? $"{scope} 无记录"
                : $"{scope} 共 {records.Count} 条记录";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量筛选失败");
            await _dialogService.DisplayAlertAsync("错误", $"批量筛选失败: {ex.Message}", "确定");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 按 B 线周期区间查询：合并区间所跨自然月的服务端查询，再按 [from, to] 过滤并去重。
    /// （各域服务端仅有自然月查询，B线区间必然落在相邻两个月。）
    /// </summary>
    private async Task<Result<List<ReprintArchiveItem>>> SearchByBusinessRangeAsync(
        IReprintDomainProvider provider, DateTime from, DateTime to, CancellationToken ct)
    {
        var all = new List<ReprintArchiveItem>();
        var failed = false;
        string? failMsg = null;
        foreach (var (year, month) in SpannedMonths(from, to))
        {
            var r = await provider.SearchByMonthAsync(year, month, 500, ct);
            if (r.IsSuccess && r.Value != null) all.AddRange(r.Value);
            else { failed = true; failMsg = r.Message; }
        }
        if (failed && all.Count == 0)
            return Result.Failure<List<ReprintArchiveItem>>(ErrorCodes.DB_QUERY_ERROR, failMsg ?? "按月查询失败");

        var hiExclusive = to.Date.AddDays(1);
        var filtered = all
            .Where(x => x.BusinessTime == DateTime.MinValue
                        || (x.BusinessTime.Date >= from.Date && x.BusinessTime.Date < hiExclusive))
            .GroupBy(x => (x.DomainKey, x.BusinessId))
            .Select(g => g.First())
            .ToList();
        return Result.Success(filtered);
    }

    private Task<List<long>> GetMonthlyIdsAsync() => Task.FromResult(new List<long>());    /// <summary>批量执行：按域模式分派</summary>
    [RelayCommand]
    private async Task BatchRunAsync()
    {
        var provider = _providers.ElementAtOrDefault(BatchDomainIndex);
        if (provider == null) return;

        try
        {
            BatchIsGenerating = true;
            BatchResultSummary = string.Empty;

            switch (provider.Mode)
            {
                case ReprintDomainMode.AssetVerification:
                    await BatchRunAssetAsync(provider);
                    break;
                case ReprintDomainMode.DynamicRecord:
                    await BatchRunDynamicAsync(provider);
                    break;
                case ReprintDomainMode.ArchiveSet:
                    await BatchRunArchiveSetAsync(provider);
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            BatchResultSummary = "批量打印已取消";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "批量打印异常");
            BatchResultSummary = $"批量打印异常: {ex.Message}";
        }
        finally
        {
            BatchIsGenerating = false;
        }
    }

    /// <summary>核查域批量：单模板 × 筛选记录逐户直打（自 ReprintSelectionViewModel 平移）</summary>
    private async Task BatchRunAssetAsync(IReprintDomainProvider provider)
    {
        var capability = provider as IAssetVerificationReprintCapability;
        if (capability == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "资产核查批量能力未注册", "确定");
            return;
        }
        // 兜底：模板下拉尚未同步时自动取首个可用模板，避免误报"请选择模板"
        var template = SelectedBatchTemplate ?? BatchTemplates.FirstOrDefault();
        if (template == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "未找到可用的核查打印模板，请刷新名单后重试", "确定");
            return;
        }
        SelectedBatchTemplate ??= template;
        if (BatchRecords.Count == 0)
        {
            await _dialogService.DisplayAlertAsync("提示", "当前筛选范围内没有记录", "确定");
            return;
        }

        var confirm = await _dialogService.DisplayAlertAsync("确认",
            $"即将批量打印 {BatchRecords.Count} 条记录的「{template.Name}」\n是否继续？", "确定", "取消");
        if (!confirm) return;

        var ids = BatchRecords.Select(r => r.BusinessId).ToList();
        var result = await capability.BatchPrintAsync(ids, template, IsGeneratePdf,
            (current, total, name) =>
            {
                BatchProgress = (double)current / Math.Max(total, 1);
                BatchProgressText = $"正在打印 ({current}/{total}): {name}";
            }, CancellationToken);

        if (result.IsSuccess)
        {
            var (successCount, failed) = result.Value;
            var message = $"打印完成: 成功 {successCount} 个";
            if (failed.Count > 0)
            {
                message += $"\n失败 {failed.Count} 个:\n{string.Join("\n", failed.Take(5).Select(f => $"{f.Name}: {f.Error}"))}";
                if (failed.Count > 5) message += $"\n...等 {failed.Count - 5} 个";
            }
            BatchResultSummary = message;
            _logger.LogBusiness("统一补打中心·核查批量完成", ("Success", successCount), ("Failed", failed.Count));
        }
        else
        {
            BatchResultSummary = $"批量打印失败: {result.Message}";
        }
    }

    /// <summary>动态管理域批量：按 B 线周期区间变更人群合并导出</summary>
    private async Task BatchRunDynamicAsync(IReprintDomainProvider provider)
    {
        var capability = provider as IDynamicRecordReprintCapability;
        if (capability == null)
        {
            BatchResultSummary = "动态管理批量能力未注册";
            return;
        }

        var range = await GetBatchBusinessRangeAsync();
        if (range == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "动态管理档案请选择具体月份（B线周期）后再批量导出", "确定");
            return;
        }

        BatchProgressText = $"正在按 {BatchScopeText(range)} 变更人群导出...";
        var result = await capability.BatchExportRangeAsync(range.Value.From, range.Value.To, CancellationToken);
        if (result.IsSuccess && result.Value != null)
        {
            BatchProgress = 1;
            BatchProgressText = "批量导出完成";
            BatchResultSummary = $"已合并导出 {BatchRecords.Count} 户：\n{result.Value}";
        }
        else
        {
            BatchResultSummary = $"批量导出失败: {result.Message}";
        }
    }

    /// <summary>ArchiveSet 域批量：逐户构建打印数据 → 全适用模板静默打印（PrintAllCoreAsync）</summary>
    private async Task BatchRunArchiveSetAsync(IReprintDomainProvider provider)
    {
        if (BatchRecords.Count == 0)
        {
            await _dialogService.DisplayAlertAsync("提示", "当前筛选范围内没有记录", "确定");
            return;
        }

        // 「指定模板」模式：运行前解析模板（下拉未同步时兜底取首个），避免误报未选择模板
        var batchTemplate = IsBatchTemplateMode
            ? (SelectedBatchTemplate ?? BatchTemplates.FirstOrDefault())
            : null;
        if (IsBatchTemplateMode && batchTemplate == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请在「模板」下拉选择要批量打印的模板", "确定");
            return;
        }
        if (IsBatchTemplateMode && SelectedBatchTemplate == null)
            SelectedBatchTemplate = batchTemplate;

        var confirm = await _dialogService.DisplayAlertAsync("确认",
            IsBatchTemplateMode && batchTemplate != null
                ? $"即将逐户打印 {BatchRecords.Count} 户的「{batchTemplate.Name}」\n是否继续？"
                : $"即将逐户打印 {BatchRecords.Count} 户的全部适用模板（{provider.DisplayName}）\n是否继续？",
            "确定", "取消");
        if (!confirm) return;

        var successCount = 0;
        var failedItems = new List<(string Name, string Error)>();

        for (var i = 0; i < BatchRecords.Count; i++)
        {
            CancellationToken.ThrowIfCancellationRequested();
            var record = BatchRecords[i];
            BatchProgress = (double)i / BatchRecords.Count;
            BatchProgressText = $"正在打印 ({i + 1}/{BatchRecords.Count}): {record.Name}";

            try
            {
                var prepare = await provider.PrepareAsync(record.BusinessId, CancellationToken);
                if (prepare.IsFailure || prepare.Value == null)
                {
                    failedItems.Add((record.Name, prepare.Message ?? "构建打印数据失败"));
                    continue;
                }
                var payload = prepare.Value;

                PrintNavigationData.BusinessType = payload.DomainKey;
                PrintNavigationData.BusinessId = payload.BusinessId;
                PrintNavigationData.Classification = payload.Classification;
                PrintNavigationData.Status = string.IsNullOrEmpty(payload.Status) ? record.Status : payload.Status;
                PrintNavigationData.FieldData = payload.FieldData;
                PrintNavigationData.TableData = payload.TableData;
                PrintNavigationData.SupporterTableData = payload.SupporterTableData;

                await Output.InitializeAsync();
                _logger.LogBusiness("统一补打中心·批量单户模板",
                    ("BusinessType", payload.DomainKey),
                    ("Classification", payload.Classification ?? ""),
                    ("Templates", Output.Templates.Count),
                    ("Selected", Output.Templates.Count(t => t.IsSelected && t.IsApplicable)),
                    ("Mode", IsBatchTemplateMode ? "指定模板" : "整份档案"));
                if (batchTemplate != null)
                    await Output.PrintTemplateForBatchAsync(batchTemplate);
                else
                    await Output.PrintAllCoreAsync(interactive: false);
                successCount++;
            }
            catch (Exception ex)
            {
                failedItems.Add((record.Name, ex.Message));
                _logger.LogError(ex, $"批量打印单户失败: {record.Name}");
            }
        }

        BatchProgress = 1;
        var summary = $"打印完成: 成功 {successCount} 户";
        if (failedItems.Count > 0)
        {
            summary += $"\n失败 {failedItems.Count} 户:\n{string.Join("\n", failedItems.Take(5).Select(f => $"{f.Name}: {f.Error}"))}";
            if (failedItems.Count > 5) summary += $"\n...等 {failedItems.Count - 5} 户";
        }
        BatchResultSummary = summary;
        _logger.LogBusiness("统一补打中心·档案集批量完成", ("Success", successCount), ("Failed", failedItems.Count));
    }
}
