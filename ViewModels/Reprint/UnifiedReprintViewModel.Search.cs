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

public partial class UnifiedReprintViewModel
{
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

    /// <summary>输出分类选项：按档案分类（默认整档）/ 变动文书（DocumentOperationCategories 单独出变动模板）</summary>
    public List<string> OutputCategoryOptions { get; } = new() { "按档案分类", "变动文书" };

    /// <summary>输出分类索引（0=按档案分类，1=变动文书）</summary>
    [ObservableProperty]
    private int _outputCategoryIndex;

    partial void OnOutputCategoryIndexChanged(int value)
    {
        // 已选中记录时切换输出分类 → 立即重建模板列表
        //（ArchiveSet 域 / 动态域文书清单两种情况都要重建）
        if (SelectedRecord is { } rec)
        {
            if (IsArchiveSetMode)
                _ = PrepareArchiveSetAsync(rec);
            else if (ShowDynamicDocs)
                _ = PrepareDynamicDocsAsync(rec);
        }
    }

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
            var monthPart = _personSingleDomain && _personBusinessRange is { } r
                ? $"{FilterYear}年{MonthFilterIndex}月（{_personWindowLabel} {r.From:yyyy-MM-dd}~{r.To:yyyy-MM-dd}）"
                : $"{FilterYear}年{MonthFilterIndex}月（各域按自身月份口径）";
            return $"{scope} · {monthPart}：命中 {PersonResults.Count} 人";
        }
    }

    /// <summary>月份选项显示（"全部"、"1月".."12月"），经 SelectedIndex 绑定 MonthFilterIndex（int↔int，稳定）</summary>
    public List<string> MonthOptions { get; } = new List<string> { "全部" }.Concat(Enumerable.Range(1, 12).Select(m => $"{m}月")).ToList();

    /// <summary>最近一次搜索的原始记录（月份过滤作用于名单本身：按月重建 PersonResults）</summary>
    private List<ReprintArchiveItem> _cachedRecords = new();

    /// <summary>最近一次按月搜索所用月份窗口（首个域；提示展示用）</summary>
    private (DateTime From, DateTime To)? _personBusinessRange;

    /// <summary>各域本次月份窗口（FilterByMonth 按域过滤用）</summary>
    private Dictionary<string, (DateTime From, DateTime To)> _personDomainWindows = new();

    /// <summary>首个域的窗口口径显示名（提示用）</summary>
    private string _personWindowLabel = "月份";

    /// <summary>当前是否仅单域筛选（提示是否展示具体区间）</summary>
    private bool _personSingleDomain;

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

    /// <summary>
    /// 动态管理记录的文书清单面板开关：选中动态域记录且全档文书数据（含变动文书）准备成功时为 true。
    /// 让动态管理档案域不离开本域即可输出渐退审批表等变动文书（不再依赖"搜索→低收入域"深路径）。
    /// </summary>
    [ObservableProperty]
    private bool _showDynamicDocs;

    /// <summary>文书清单面板（输出分类/模板 + 打印设置/预览/打印）可见：ArchiveSet 域或动态域文书准备成功</summary>
    public bool ShowDocsPanel => IsArchiveSetMode || ShowDynamicDocs;

    /// <summary>
    /// 当前记录域支持「变动文书」输出分类（仅低收入人口域 FamilyApplication/EconomicReview）。
    /// false 时隐藏输出分类下拉——高龄/临救等域没有变动文书，选了也只会被 ArchiveOutput 域护栏忽略。
    /// </summary>
    [ObservableProperty]
    private bool _supportsDocMode;

    partial void OnShowDynamicDocsChanged(bool value) => OnPropertyChanged(nameof(ShowDocsPanel));

    /// <summary>中栏标题（随域）</summary>
    public string BranchTitle => SelectedBranch == null ? "分类分支" : $"{SelectedBranch.DisplayName}（{BranchRecords.Count} 条）";

    #endregion

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
    /// 跨域按月搜索：各域按自身时间轴类型（A/B/C线）计算周期区间，合并查询后过滤；
    /// 关键词非空时在结果上客户端过滤。
    /// </summary>
    private async Task SearchPersonsByMonthAsync()
    {
        var keyword = SearchKeyword?.Trim() ?? string.Empty;

        try
        {
            if (IsBusy) return;
            IsBusy = true;

            var providerList = GetActiveProviders();
            if (providerList.Count == 0) return;

            // 预计算各域月份窗口（FilterByMonth 按域过滤用）
            var windows = new Dictionary<string, (DateTime From, DateTime To)>();
            foreach (var p in providerList)
            {
                var w = await GetMonthWindowAsync(FilterYear, MonthFilterIndex, p.MonthWindow);
                if (w.HasValue) windows[p.DomainKey] = (w.Value.From, w.Value.To);
            }
            _personDomainWindows = windows;
            _personSingleDomain = ActiveDomainFilter != null;
            _personWindowLabel = WindowLabel(providerList[0].MonthWindow);
            _personBusinessRange = windows.TryGetValue(providerList[0].DomainKey, out var firstWindow) ? firstWindow : null;
            OnPropertyChanged(nameof(MonthFilterHint));

            // 各域按自身月份窗口分别搜索
            var tasksArray = providerList
                .Select(p => SearchByProviderMonthAsync(p, FilterYear, MonthFilterIndex, CancellationToken))
                .ToArray();
            await Task.WhenAll(tasksArray);

            _logger.LogBusiness("统一补打中心·按月搜索",
                ("Year", FilterYear), ("Month", MonthFilterIndex));

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

    /// <summary>月份过滤（0=全部；按各域自身月份窗口过滤；BusinessTime 为空值时不过滤）</summary>
    private List<ReprintArchiveItem> FilterByMonth(IEnumerable<ReprintArchiveItem> records)
    {
        if (MonthFilterIndex <= 0)
            return records.ToList();

        return records.Where(x =>
        {
            if (x.BusinessTime == DateTime.MinValue) return true;
            if (!_personDomainWindows.TryGetValue(x.DomainKey, out var w)) return true;
            var hiExclusive = w.To.Date.AddDays(1);
            return x.BusinessTime.Date >= w.From.Date && x.BusinessTime.Date < hiExclusive;
        }).ToList();
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
        OnPropertyChanged(nameof(ShowDocsPanel));

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
                    // 进入动态记录默认输出分类「变动文书」（此刻 ShowDynamicDocs=false，
                    // 赋值触发的 OnOutputCategoryIndexChanged 重备条件不命中，无自循环）
                    if (OutputCategoryIndex != 1) OutputCategoryIndex = 1;
                    // 文书清单（输出分类/模板/预览）：低收入域字段构建同源；失败静默降级，留痕不受影响
                    await PrepareDynamicDocsAsync(record);
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

        ApplyArchivePayloadToNav(payload, record);
        await Output.InitializeAsync();
        await LoadPrintHistoryAsync(record);
    }

    /// <summary>
    /// 把档案字段 payload 写入 PrintNavigationData（ArchiveSet 域与动态域文书清单共用；
    /// OutputCategories 每次显式赋值含置 null，防跨记录残留）。
    /// </summary>
    private void ApplyArchivePayloadToNav(ReprintArchivePayload payload, ReprintArchiveItem record)
    {
        PrintNavigationData.BusinessType = payload.DomainKey;
        PrintNavigationData.BusinessId = payload.BusinessId;
        PrintNavigationData.Classification = payload.Classification;
        PrintNavigationData.Status = string.IsNullOrEmpty(payload.Status) ? record.Status : payload.Status;
        PrintNavigationData.FieldData = payload.FieldData;
        PrintNavigationData.TableData = payload.TableData;
        PrintNavigationData.SupporterTableData = payload.SupporterTableData;

        // 「变动文书」仅低收入人口域（FamilyApplication/动态记录）可用；
        // 普惠高龄/临时救助等 ArchiveSet 域切换时强制复位，防上一条记录的分类残留（防跨记录残留）。
        SupportsDocMode = payload.DomainKey is "FamilyApplication" or "EconomicReview";
        if (!SupportsDocMode && OutputCategoryIndex != 0)
            OutputCategoryIndex = 0; // 触发一次重备（SelectedRecord 已就位，幂等）

        PrintNavigationData.OutputCategories = SupportsDocMode && OutputCategoryIndex == 1
            ? ArchiveCategoryResolver.DocumentOperationCategories
            : null;
        // 其余文书模式字段逐条显式赋值（含置 null），与 OutputCategories 同一口径防残留
        PrintNavigationData.OperationOverride = null;
        PrintNavigationData.TemplateFilter = null;
        PrintNavigationData.PrefilterTemplateNames = null;
    }

    /// <summary>
    /// 动态管理记录的文书清单准备：用低收入人口域字段构建（全档字段，含渐退审批表分项句）
    /// 填充 PrintNavigationData 与 Output，使动态域不离开本域即可输出变动文书。
    /// 默认输出分类「变动文书」在选中记录时设置（见 PrepareSelectedRecordAsync）；此处尊重当前 OutputCategoryIndex。
    /// 准备失败静默降级为 Warn 日志——留痕补打是该域主功能，不被文书面板打断。
    /// </summary>
    private async Task PrepareDynamicDocsAsync(ReprintArchiveItem record)
    {
        try
        {
            var provider = _providers.FirstOrDefault(p => p.DomainKey == SocialAssistanceReprintProvider.DomainKeyConst);
            if (provider == null)
            {
                _logger.Warn("动态域文书清单：未注册低收入人口域能力，无法构建文书");
                ShowDynamicDocs = false;
                return;
            }

            var prepare = await provider.PrepareAsync(record.BusinessId, CancellationToken);
            if (prepare.IsFailure || prepare.Value == null)
            {
                _logger.Warn($"动态域文书清单准备失败: BusinessId={record.BusinessId}, {prepare.Message}");
                ShowDynamicDocs = false;
                return;
            }

            _logger.LogBusiness("统一补打中心·动态域文书数据构建完成",
                ("BusinessId", record.BusinessId.ToString()),
                ("Applicant", DataMasker.MaskName(prepare.Value.Name)));

            ApplyArchivePayloadToNav(prepare.Value, record);
            await Output.InitializeAsync();
            ShowDynamicDocs = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "动态域文书清单准备异常");
            ShowDynamicDocs = false;
        }
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
        PrintNavigationData.OutputCategories = null;
        ShowDynamicDocs = false; // 切换记录/分支后文书清单需重新准备
    }

}
