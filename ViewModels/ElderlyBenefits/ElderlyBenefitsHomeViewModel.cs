using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.Domain.Reporting;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;

namespace NewCosmos.ViewModels.ElderlyBenefits;

public partial class ElderlyBenefitsHomeViewModel : ModuleHomeViewModelBase
{
    private readonly INewPermissionService _permissionService;
    private readonly IStatisticsService _statisticsService;
    private readonly IElderlyApplicationService _elderlyApplicationService;

    #region 权限

    [ObservableProperty]
    private bool _canAccessElderlyBenefit;

    /// <summary>子功能卡置灰透明度（无权限 0.5）</summary>
    public double ElderlyOpacity => CanAccessElderlyBenefit ? 1.0 : 0.5;

    partial void OnCanAccessElderlyBenefitChanged(bool value) => OnPropertyChanged(nameof(ElderlyOpacity));

    #endregion

    #region 模块统计（失败降级显示 StatNA）

    /// <summary>在享领取人数（status='Confirmed'）</summary>
    [ObservableProperty]
    private string _activeCountText = StatNA;

    /// <summary>本月新增登记数（自然月口径）</summary>
    [ObservableProperty]
    private string _monthlyNewText = StatNA;

    /// <summary>待新增人数（满80、无在享登记且历史名册无记录）</summary>
    [ObservableProperty]
    private string _pendingNewText = StatNA;

    /// <summary>需停旧增新人数（正式表 Confirmed 在享或历史名册有记录）</summary>
    [ObservableProperty]
    private string _pendingTransferText = StatNA;

    #endregion

    public ElderlyBenefitsHomeViewModel(
        IServiceProvider serviceProvider,
        IDialogService dialogService,
        ILoggerService logger,
        IWindowTitleService windowTitleService,
        INewPermissionService permissionService,
        IStatisticsService statisticsService,
        IElderlyApplicationService elderlyApplicationService)
        : base(serviceProvider, dialogService, logger, windowTitleService)
    {
        _permissionService = permissionService;
        _statisticsService = statisticsService;
        _elderlyApplicationService = elderlyApplicationService;
        Title = "高龄津贴发放";
    }

    protected override async Task LoadDataAsync()
    {
        var userId = App.CurrentUserId ?? 0;
        if (userId <= 0) return;

        try
        {
            await Task.WhenAll(
                LoadPermissionsAsync(userId),
                LoadStatsAsync());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "高龄津贴发放首页数据加载失败");
        }
    }

    private async Task LoadPermissionsAsync(int userId)
    {
        try
        {
            var permissions = await _permissionService.CheckPermissionsAsync(
                userId, new[] { PermissionCodes.ELDERLY_VIEW });

            CanAccessElderlyBenefit = permissions.TryGetValue(PermissionCodes.ELDERLY_VIEW, out var granted) && granted;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "模块权限加载失败");
        }
    }

    private async Task LoadStatsAsync()
    {
        // 在享/月增来自 StatisticsService，待办两分类来自 ElderlyApplicationService；任一失败仅降级对应项
        var statsTask = SafeLoadAsync(
            () => _statisticsService.GetElderlyStatsAsync(),
            s =>
            {
                ActiveCountText = s.ActiveCount.ToString("N0");
                MonthlyNewText = s.MonthlyNew.ToString("N0");
            },
            "高龄津贴模块统计加载失败");

        var pendingTask = SafeLoadAsync(
            () => _elderlyApplicationService.GetPendingElderlyCountsAsync(),
            p =>
            {
                PendingNewText = p.PendingNew.ToString("N0");
                PendingTransferText = p.PendingTransfer.ToString("N0");
            },
            "高龄待办统计加载失败");

        await Task.WhenAll(statsTask, pendingTask);
    }

    // SafeLoadAsync 已上提至 ModuleHomeViewModelBase 基类

    #region 子功能导航命令

    [RelayCommand]
    private Task OpenApplicationListAsync() =>
        NavigateToFeatureAsync<Pages.ElderlyBenefits.ElderlyApplicationListPage>(
            CanAccessElderlyBenefit, "高龄津贴申请登记", "高龄津贴申请登记");

    [RelayCommand]
    private Task OpenStopListAsync() =>
        NavigateToFeatureAsync<Pages.ElderlyBenefits.ElderlyStopListPage>(
            CanAccessElderlyBenefit, "停发办理", "停发办理");

    [RelayCommand]
    private Task OpenMonthlyReportAsync() =>
        NavigateToFeatureAsync<Pages.ElderlyBenefits.ElderlyReportPage>(
            CanAccessElderlyBenefit, "高龄津贴月报表", "高龄津贴月报表");

    [RelayCommand]
    private Task OpenReviewListAsync() =>
        NavigateToFeatureAsync<Pages.ElderlyBenefits.ElderlyReviewListPage>(
            CanAccessElderlyBenefit, "高龄津贴复核", "高龄津贴复核");

    [RelayCommand]
    private Task NavigateToElderlyPendingAsync() =>
        NavigateToFeatureAsync<Pages.ElderlyBenefits.ElderlyPendingListPage>(
            CanAccessElderlyBenefit, "高龄津贴下月待办", "高龄津贴下月待办");

    [RelayCommand]
    private Task OpenRecoveryAsync() =>
        NavigateToFeatureAsync<Pages.Recovery.RecoverySearchPage>(
            true, "后补追缴", "后补追缴");

    // 返回上一页：使用基类 GoBackCommand（含栈守卫与窗口标题恢复）

    #endregion
}
