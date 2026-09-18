using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.Reporting;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;

namespace NewCosmos.ViewModels.AssetVerification;

public partial class AssetVerificationHomeViewModel : ModuleHomeViewModelBase
{
    private readonly INewPermissionService _permissionService;
    private readonly IStatisticsService _statisticsService;

    #region 权限

    [ObservableProperty]
    private bool _canAccessAssetVerification;

    /// <summary>月度审核入口（ASSET_MAUDIT）</summary>
    [ObservableProperty]
    private bool _canAccessMonthlyAudit;

    /// <summary>月度报表入口（ASSET_REPORT）</summary>
    [ObservableProperty]
    private bool _canAccessMonthlyReport;

    /// <summary>子功能卡置灰透明度（无权限 0.5）</summary>
    public double AssetVerificationOpacity => CanAccessAssetVerification ? 1.0 : 0.5;

    partial void OnCanAccessAssetVerificationChanged(bool value) => OnPropertyChanged(nameof(AssetVerificationOpacity));

    #endregion

    #region 模块统计（失败降级显示 StatNA）

    /// <summary>待上传报告数（status='0'，不限时间）</summary>
    [ObservableProperty]
    private string _pendingReportCountText = StatNA;

    /// <summary>本月已完成核对数（status='1' 且 updated_at∈当月）</summary>
    [ObservableProperty]
    private string _monthlyCompletedText = StatNA;

    /// <summary>本月新增申请数</summary>
    [ObservableProperty]
    private string _monthlyNewText = StatNA;

    /// <summary>全年累计申请数</summary>
    [ObservableProperty]
    private string _yearTotalText = StatNA;

    #endregion

    public AssetVerificationHomeViewModel(
        IServiceProvider serviceProvider,
        IDialogService dialogService,
        ILoggerService logger,
        IWindowTitleService windowTitleService,
        INewPermissionService permissionService,
        IStatisticsService statisticsService)
        : base(serviceProvider, dialogService, logger, windowTitleService)
    {
        _permissionService = permissionService;
        _statisticsService = statisticsService;
        Title = "家庭经济状况核对";
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
            Logger.LogError(ex, "家庭经济状况核对首页数据加载失败");
        }
    }

    private async Task LoadPermissionsAsync(int userId)
    {
        try
        {
            var permissions = await _permissionService.CheckPermissionsAsync(
                userId, new[] { PermissionCodes.ASSET_VIEW, PermissionCodes.ASSET_MAUDIT, PermissionCodes.ASSET_REPORT });

            CanAccessAssetVerification = permissions.TryGetValue(PermissionCodes.ASSET_VIEW, out var granted) && granted;
            CanAccessMonthlyAudit = permissions.TryGetValue(PermissionCodes.ASSET_MAUDIT, out var mauditGranted) && mauditGranted;
            CanAccessMonthlyReport = permissions.TryGetValue(PermissionCodes.ASSET_REPORT, out var reportGranted) && reportGranted;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "模块权限加载失败");
        }
    }

    private async Task LoadStatsAsync()
    {
        try
        {
            var result = await _statisticsService.GetAssetVerificationStatsAsync();
            if (result.IsSuccess && result.Value != null)
            {
                PendingReportCountText = result.Value.PendingReportCount.ToString("N0");
                MonthlyCompletedText = result.Value.MonthlyCompleted.ToString("N0");
                MonthlyNewText = result.Value.MonthlyNew.ToString("N0");
                YearTotalText = result.Value.YearTotal.ToString("N0");
            }
            else
            {
                Logger.Warn($"核对模块统计加载失败: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "核对模块统计加载异常");
        }
    }

    #region 子功能导航命令

    [RelayCommand]
    private Task OpenQuickVerificationAsync() =>
        NavigateToFeatureAsync<Pages.AssetVerification.QuickAssetVerificationPage>(
            CanAccessAssetVerification, "快速经济核对", "快速经济核对");

    [RelayCommand]
    private Task OpenMonthlyAuditAsync() =>
        NavigateToFeatureAsync<Pages.AssetVerification.MonthlyAssetAuditPage>(
            CanAccessMonthlyAudit, "月度审核", "月度审核");

    [RelayCommand]
    private Task OpenMonthlyReportAsync() =>
        NavigateToFeatureAsync<Pages.AssetVerification.MonthlyReportPage>(
            CanAccessMonthlyReport, "核对月报表", "核对月报表");

    // 返回上一页：使用基类 GoBackCommand（含栈守卫与窗口标题恢复）

    #endregion
}
