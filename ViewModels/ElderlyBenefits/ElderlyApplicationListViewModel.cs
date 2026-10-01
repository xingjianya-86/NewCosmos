using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.ElderlyBenefits;

/// <summary>
/// 普惠高龄业务申请列表 ViewModel（工作流风格：3 个状态 Tab + 计数徽章 + 卡片列表）
/// </summary>
public partial class ElderlyApplicationListViewModel : PagedSearchViewModelBase
{
    private readonly IElderlyApplicationService _applicationService = null!;
    private readonly ILoggerService _logger = null!;
    private readonly IServiceProvider _serviceProvider = null!;

    private int _loadVersion;
    private CancellationTokenSource? _tabLoadCts;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    #region Tab 状态

    [ObservableProperty]
    private int _selectedTabIndex = 1;

    public bool IsTab0Selected => SelectedTabIndex == 0;
    public bool IsTab1Selected => SelectedTabIndex == 1;
    public bool IsTab2Selected => SelectedTabIndex == 2;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsTab0Selected));
        OnPropertyChanged(nameof(IsTab1Selected));
        OnPropertyChanged(nameof(IsTab2Selected));

        PageIndex = 1;
        SearchText = string.Empty;
        // 已确认 Tab 每页显示最新 10 条；草稿/停发保持 20 条/页
        PageSize = value == 1 ? 10 : 20;

        var version = ++_loadVersion;
        _tabLoadCts?.Cancel();
        _tabLoadCts = new CancellationTokenSource();

        SafeFireAndForget(async () => await LoadDataAsync(_tabLoadCts.Token, version));
    }

    #endregion

    #region 数据集合

    [ObservableProperty]
    private ObservableCollection<ElderlyApplication> _draftApps = new();

    [ObservableProperty]
    private ObservableCollection<ElderlyApplication> _confirmedApps = new();

    [ObservableProperty]
    private ObservableCollection<ElderlyApplication> _stoppedApps = new();

    #endregion

    #region 计数徽章

    [ObservableProperty]
    private int _draftCount;

    [ObservableProperty]
    private int _confirmedCount;

    [ObservableProperty]
    private int _stoppedCount;

    #endregion

    public ElderlyApplicationListViewModel(
        IElderlyApplicationService applicationService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        Services.Domain.Reporting.IStatisticsService statisticsService,
        Services.Domain.Printing.IPrintJobFactory printJobFactory)
    {
        _applicationService = applicationService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _statisticsService = statisticsService;
        _printJobFactory = printJobFactory;
        Title = "高龄津贴申请登记";
    }

    private readonly Services.Domain.Reporting.IStatisticsService _statisticsService = null!;
    private readonly Services.Domain.Printing.IPrintJobFactory _printJobFactory = null!;

    #region 页头统计栏（跨状态全局视角；草稿/确认/停发分项已在 Tab 徽章展示）

    /// <summary>统计加载失败占位</summary>
    private const string StatNA = "—";

    /// <summary>在享领取人数</summary>
    [ObservableProperty]
    private string _activeCountText = StatNA;

    /// <summary>本月新增登记数</summary>
    [ObservableProperty]
    private string _monthlyNewText = StatNA;

    private async Task LoadHeaderStatsAsync()
    {
        try
        {
            var result = await _statisticsService.GetElderlyStatsAsync();
            if (result.IsSuccess && result.Value != null)
            {
                ActiveCountText = result.Value.ActiveCount.ToString("N0");
                MonthlyNewText = result.Value.MonthlyNew.ToString("N0");
            }
            else
            {
                _logger.Warn($"高龄统计加载失败: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "高龄统计加载异常");
        }
    }

    #endregion

    public override async Task OnAppearingAsync()
    {
        try
        {
            await LoadAllCountsAsync();
            await LoadDataAsync();
            _ = LoadHeaderStatsAsync();
        }
        catch (Exception)
        {
            ErrorMessage = "页面加载失败，请重试";
        }
    }

    public override void OnDisappearing()
    {
        _tabLoadCts?.Cancel();
        _tabLoadCts?.Dispose();
        _tabLoadCts = null;
        base.OnDisappearing();
    }

    [RelayCommand]
    private void SwitchTab(string? tabIndex)
    {
        if (int.TryParse(tabIndex, out var idx))
        {
            SelectedTabIndex = idx;
        }
    }

    protected override async Task LoadDataAsync()
    {
        await ExecuteAsync(async ct =>
        {
            await LoadCurrentTabDataAsync(ct, _loadVersion);
        }, "加载数据...");
    }

    private async Task LoadDataAsync(CancellationToken ct, int version)
    {
        await ExecuteAsync(async () =>
        {
            await LoadCurrentTabDataAsync(ct, version);
            return Result.Success();
        }, "加载数据...");
    }

    private async Task LoadCurrentTabDataAsync(CancellationToken ct, int version)
    {
        var status = SelectedTabIndex switch
        {
            0 => ElderlyBenefitConstants.StatusDraft,
            1 => ElderlyBenefitConstants.StatusConfirmed,
            _ => ElderlyBenefitConstants.StatusStopped
        };

        var result = await _applicationService.GetPagedAsync(SearchText, status, PageIndex, PageSize, ct);
        if (result.IsFailure || result.Value == null)
        {
            _logger.LogError(new Exception(result.Message ?? "未知错误"), "加载普惠高龄登记列表失败");
            return;
        }

        if (version != _loadVersion) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (version != _loadVersion) return;

            // 填充段复用基类模板半段；版本守卫在本方法内保留（防切 Tab 后旧响应覆盖）
            var target = SelectedTabIndex switch
            {
                0 => DraftApps,
                1 => ConfirmedApps,
                _ => StoppedApps
            };
            FillPagedPage(target, result.Value);
        });
    }

    private async Task LoadAllCountsAsync()
    {
        try
        {
            var draftResult = await _applicationService.GetPagedAsync(string.Empty, ElderlyBenefitConstants.StatusDraft, 1, 1);
            if (draftResult.IsSuccess && draftResult.Value != null)
                DraftCount = draftResult.Value.TotalCount;

            var confirmedResult = await _applicationService.GetPagedAsync(string.Empty, ElderlyBenefitConstants.StatusConfirmed, 1, 1);
            if (confirmedResult.IsSuccess && confirmedResult.Value != null)
                ConfirmedCount = confirmedResult.Value.TotalCount;

            var stoppedResult = await _applicationService.GetPagedAsync(string.Empty, ElderlyBenefitConstants.StatusStopped, 1, 1);
            if (stoppedResult.IsSuccess && stoppedResult.Value != null)
                StoppedCount = stoppedResult.Value.TotalCount;
        }
        catch (Exception)
        {
            // 计数加载失败不影响主列表
        }
    }

    [RelayCommand]
    private async Task CreateNewAsync()
    {
        _logger.LogBusiness("创建普惠高龄登记");
        await NavigateToPageAsync<Pages.ElderlyBenefits.ElderlyApplicationFormPage, ElderlyFormPageParameter>(new ElderlyFormPageParameter(FormOperationMode.Create));
    }

    [RelayCommand]
    private async Task ViewDetailAsync(ElderlyApplication? app)
    {
        if (app == null) return;

        _logger.LogBusiness("查看普惠高龄登记", ("ApplicationId", app.Id));
        await NavigateToPageAsync<Pages.ElderlyBenefits.ElderlyApplicationFormPage, ElderlyFormPageParameter>(new ElderlyFormPageParameter(FormOperationMode.View, app.Id));
    }

    [RelayCommand]
    private async Task EditAsync(ElderlyApplication? app)
    {
        if (app == null) return;

        _logger.LogBusiness("编辑普惠高龄登记", ("ApplicationId", app.Id));
        await NavigateToPageAsync<Pages.ElderlyBenefits.ElderlyApplicationFormPage, ElderlyFormPageParameter>(new ElderlyFormPageParameter(FormOperationMode.Edit, app.Id));
    }

    [RelayCommand]
    private async Task ConfirmAsync(ElderlyApplication? app)
    {
        if (app == null) return;

        var dialogService = _serviceProvider.GetRequiredService<IDialogService>();
        var confirm = await dialogService.DisplayAlertAsync("确认生效",
            $"确定要确认 [{app.ApplicationNo}] {app.Name} 的高龄补贴登记生效吗？",
            "确认", "取消");
        if (!confirm) return;

        await ExecuteAsync(async () =>
        {
            var result = await _applicationService.ConfirmAsync(app.Id, App.CurrentUserName, CancellationToken);
            if (result.IsSuccess)
            {
                _logger.LogBusiness("普惠高龄登记确认成功", ("ApplicationId", app.Id));
                await LoadAllCountsAsync();
                await LoadDataAsync();
            }
            return result;
        }, "确认登记生效...");
    }

    [RelayCommand]
    private async Task StopAsync(ElderlyApplication? app)
    {
        if (app == null) return;

        _logger.LogBusiness("进入普惠高龄停发", ("ApplicationId", app.Id));
        await NavigateToPageAsync<Pages.ElderlyBenefits.ElderlyStopPage, Models.Entities.ElderlyApplication>(app);
    }

    [RelayCommand]
    private async Task DeleteAsync(ElderlyApplication? app)
    {
        if (app == null) return;

        var dialogService = _serviceProvider.GetRequiredService<IDialogService>();
        var confirm = await dialogService.DisplayAlertAsync("确认删除",
            $"确定要删除登记 [{app.ApplicationNo}] {app.Name} 吗？\n此操作不可恢复。",
            "删除", "取消");
        if (!confirm) return;

        var result = await _applicationService.DeleteAsync(app.Id);
        if (result.IsSuccess)
        {
            var deletedId = app.Id;
            var target = SelectedTabIndex switch
            {
                0 => DraftApps,
                1 => ConfirmedApps,
                _ => StoppedApps
            };
            target.Remove(app);
            _logger.LogBusiness("高龄津贴登记删除成功", ("ApplicationId", deletedId));
        }
    }

    /// <summary>手机端推送打印：在享登记表（classification=新增）。</summary>
    [RelayCommand]
    private Task PushPrintAsync(ElderlyApplication? app) => PushPrintCoreAsync(app, "新增");

    /// <summary>手机端推送打印：取消备案表（classification=Stop）。</summary>
    [RelayCommand]
    private Task PushPrintStopAsync(ElderlyApplication? app) => PushPrintCoreAsync(app, "Stop");

    private async Task PushPrintCoreAsync(ElderlyApplication? app, string classification)
    {
        if (app == null) return;

        await ExecuteAsync(async () =>
        {
            var result = await _printJobFactory.EnqueueElderlyAsync(app.Id, classification, ct: CancellationToken);
            var dialogService = _serviceProvider.GetRequiredService<IDialogService>();
            if (result.IsFailure)
            {
                await dialogService.DisplayAlertAsync("推送失败", result.Message ?? "推送打印失败", "确定");
                return result;
            }

            _logger.LogBusiness("高龄推送打印入队",
                ("ApplicationId", (object)app.Id),
                ("Classification", classification),
                ("JobNo", result.Value?.JobNo ?? string.Empty));
            await dialogService.DisplayAlertAsync("已推送打印",
                $"打印任务 {result.Value?.JobNo} 已推送，将在电脑端自动打印。", "确定");
            return Result.Success();
        }, "推送打印...");
    }

    // 返回上一页：使用基类 GoBackCommand（含栈守卫与窗口标题恢复）
}
