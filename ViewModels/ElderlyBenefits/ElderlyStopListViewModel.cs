using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.ElderlyBenefits;

/// <summary>
/// 普惠高龄停发列表（支持搜索当前库 + 导入库，选择后进入停发登记）
/// </summary>
public partial class ElderlyStopListViewModel : PagedSearchViewModelBase
{
    private readonly IElderlyApplicationService _applicationService = null!;
    private readonly ILoggerService _logger = null!;
    private readonly IServiceProvider _serviceProvider = null!;
    private readonly IDialogService _dialogService = null!;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    /// <summary>默认在享人员列表</summary>
    [ObservableProperty]
    private ObservableCollection<ElderlyApplication> _applications = new();

    /// <summary>搜索结果 - 当前库记录</summary>
    [ObservableProperty]
    private ObservableCollection<ElderlyApplication> _currentSearchResults = new();

    /// <summary>搜索结果 - 导入库记录</summary>
    [ObservableProperty]
    private ObservableCollection<ElderlyImportedSearchItem> _importedSearchResults = new();

    /// <summary>是否有搜索结果（控制搜索结果列表可见性）</summary>
    [ObservableProperty]
    private bool _hasSearchResults;

    /// <summary>是否有默认列表（控制默认列表可见性）</summary>
    [ObservableProperty]
    private bool _hasDefaultList = true;

    public ElderlyStopListViewModel(
        IElderlyApplicationService applicationService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        IDialogService dialogService,
        Services.Domain.Reporting.IStatisticsService statisticsService)
    {
        _applicationService = applicationService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _dialogService = dialogService;
        _statisticsService = statisticsService;
    }

    private readonly Services.Domain.Reporting.IStatisticsService _statisticsService = null!;

    #region 页头统计栏

    /// <summary>统计加载失败占位</summary>
    private const string StatNA = "—";

    /// <summary>在享领取总人数</summary>
    [ObservableProperty]
    private string _activeCountText = StatNA;

    /// <summary>本月停发人次</summary>
    [ObservableProperty]
    private string _monthlyStoppedText = StatNA;

    /// <summary>年累计停发人次</summary>
    [ObservableProperty]
    private string _yearStoppedText = StatNA;

    private async Task LoadHeaderStatsAsync()
    {
        try
        {
            var result = await _statisticsService.GetElderlyStopStatsAsync();
            var activeResult = await _statisticsService.GetElderlyStatsAsync();

            MonthlyStoppedText = result.IsSuccess && result.Value != null
                ? result.Value.MonthlyStopped.ToString("N0") : StatNA;
            YearStoppedText = result.IsSuccess && result.Value != null
                ? result.Value.YearStopped.ToString("N0") : StatNA;
            ActiveCountText = activeResult.IsSuccess && activeResult.Value != null
                ? activeResult.Value.ActiveCount.ToString("N0") : StatNA;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "停发页头统计加载失败");
        }
    }

    #endregion

    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        await LoadDataAsync();
        _ = LoadHeaderStatsAsync();
    }

    protected override Task LoadDataAsync()
        => LoadPageAsync(
            ct => _applicationService.GetPagedAsync(
                SearchText, ElderlyBenefitConstants.StatusConfirmed, PageIndex, PageSize, ct),
            Applications,
            null,
            "加载在享人员...");

    /// <summary>
    /// 搜索：当前库 + 导入库双源合并
    /// </summary>
    [RelayCommand]
    private async Task SearchImportedAsync()
    {
        var keyword = SearchText?.Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            CurrentSearchResults.Clear();
            ImportedSearchResults.Clear();
            HasSearchResults = false;
            HasDefaultList = true;
            await LoadDataAsync();
            return;
        }

        await ExecuteAsync(async ct =>
        {
            CurrentSearchResults.Clear();
            ImportedSearchResults.Clear();
            HasDefaultList = false;

            _logger.LogBusiness("普惠高龄停发搜索", ("Keyword", keyword));

            // 1. 搜索当前库（已确认/在享）
            var currentResult = await _applicationService.GetPagedAsync(
                keyword, ElderlyBenefitConstants.StatusConfirmed, 1, 20, ct);

            if (currentResult.IsSuccess && currentResult.Value != null)
            {
                foreach (var app in currentResult.Value.Items)
                {
                    CurrentSearchResults.Add(app);
                }
            }
            else if (currentResult.IsFailure)
            {
                _logger.Error($"当前库搜索失败: {currentResult.Message}");
            }

            // 2. 搜索导入库
            var importedResult = await _applicationService.SearchImportedLibraryAsync(keyword, ct);
            if (importedResult.IsSuccess && importedResult.Value != null)
            {
                foreach (var item in importedResult.Value)
                {
                    ImportedSearchResults.Add(item);
                }
            }
            else if (importedResult.IsFailure)
            {
                _logger.Error($"导入库搜索失败: {importedResult.Message}");
                await _dialogService.DisplayAlertAsync("提示",
                    $"历史导入库搜索失败：{importedResult.Message}\n当前仅显示当前库结果。", "确定");
            }

            var totalCount = CurrentSearchResults.Count + ImportedSearchResults.Count;
            HasSearchResults = totalCount > 0;
            TotalCount = totalCount;

            if (!HasSearchResults)
            {
                await _dialogService.DisplayAlertAsync("提示", "未找到匹配的记录，请更换关键词重试", "确定");
            }
        }, "搜索...");
    }

    /// <summary>
    /// 从导入库建档后进入申请表单页（补全信息后停发）
    /// 先检查该身份证是否已在当前库建档：
    /// 已建档 → 打开档案补全（保存后自动进入停发登记）；未建档 → 先迁移建档再补全。
    /// </summary>
    [RelayCommand]
    private async Task StopFromImportedAsync(ElderlyImportedSearchItem? item)
    {
        if (item == null) return;

        _logger.LogBusiness("停发-导入库点击",
            ("HistoryId", item.Id),
            ("IdCard", DataMasker.MaskIdCard(item.IdCard)));

        await ExecuteAsync(async ct =>
        {
            // 1. 检查当前库是否已有该身份证的记录
            var existsResult = await _applicationService.CheckIdCardExistsAsync(item.IdCard, ct: ct);
            if (existsResult.IsFailure)
                _logger.Warn($"停发-导入库: 建档检查失败按未建档处理: {existsResult.ErrorCode} {existsResult.Message}");
            var alreadyExists = existsResult.IsSuccess && existsResult.Value;

            if (alreadyExists)
            {
                // 已建档：查询档案记录，进入表单补全（保存后自动跳停发）
                var loadResult = await _applicationService.GetPagedAsync(item.IdCard, null, 1, 1, ct);
                if (!loadResult.IsSuccess || loadResult.Value == null || loadResult.Value.Items.Count == 0)
                {
                    _logger.Error($"停发-导入库: 已建档但加载档案失败 idcard={DataMasker.MaskIdCard(item.IdCard)}: {loadResult.ErrorCode} {loadResult.Message}");
                    await _dialogService.DisplayAlertAsync("提示",
                        $"[{item.Name}] 已在当前库建档，但未能加载档案记录，请刷新后重试。", "确定");
                    return;
                }
                var app = loadResult.Value.Items.First();

                var confirm = await _dialogService.DisplayAlertAsync("补全档案",
                    $"[{item.Name}] {item.IdCard} 已在当前库建档（编号 {app.ApplicationNo}）。\n是否打开档案补全信息？保存后将自动进入停发登记。",
                    "去补全", "取消");

                if (!confirm) return;

                _logger.LogBusiness("停发-导入库: 已建档转表单补全",
                    ("ApplicationId", app.Id),
                    ("ApplicationNo", app.ApplicationNo));

                // 导航到申请表单页（编辑模式），保存后自动跳转停发页
                await NavigateToPageAsync<Pages.ElderlyBenefits.ElderlyApplicationFormPage, ElderlyFormPageParameter>(new ElderlyFormPageParameter(FormOperationMode.Edit, app.Id, NavigateToStopAfterSave: true));
            }
            else
            {
                // 未建档：建档后补全信息
                var confirm = await _dialogService.DisplayAlertAsync("确认建档",
                    $"[{item.Name}] {item.IdCard} 为导入库记录，尚未建档。\n是否立即建档并补全信息后停发？",
                    "建档并补全", "取消");

                if (!confirm) return;

                var migrateResult = await _applicationService.MigrateFromHistoryAsync(
                    item.Id, App.CurrentUserName, ct);

                if (migrateResult.IsFailure)
                {
                    await _dialogService.DisplayAlertAsync("建档失败", migrateResult.Message, "确定");
                    return;
                }

                var newAppId = migrateResult.Value;
                _logger.LogBusiness("从导入库建档完成，进入表单补全",
                    ("HistoryId", item.Id),
                    ("NewApplicationId", newAppId));

                // 导航到申请表单页（编辑模式），保存后自动跳转停发页
                await NavigateToPageAsync<Pages.ElderlyBenefits.ElderlyApplicationFormPage, ElderlyFormPageParameter>(new ElderlyFormPageParameter(FormOperationMode.Edit, newAppId, NavigateToStopAfterSave: true));
            }
        }, "处理导入库记录...");
    }

    /// <summary>
    /// 进入停发登记页
    /// </summary>
    [RelayCommand]
    private async Task StopAsync(ElderlyApplication? app)
    {
        if (app == null) return;

        _logger.LogBusiness("进入普惠高龄停发", ("ApplicationId", app.Id));
        await NavigateToPageAsync<Pages.ElderlyBenefits.ElderlyStopPage, Models.Entities.ElderlyApplication>(app);
    }

    // 返回上一页：使用基类 GoBackCommand（含栈守卫与窗口标题恢复）
}
