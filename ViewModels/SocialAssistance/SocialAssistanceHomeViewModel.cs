using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Navigation;
using NewCosmos.Services.Domain.Reporting;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;

namespace NewCosmos.ViewModels.SocialAssistance;

public partial class SocialAssistanceHomeViewModel : ModuleHomeViewModelBase
{
    private readonly INewPermissionService _permissionService;
    private readonly IStatisticsService _statisticsService;
    private readonly IGracePeriodService _gracePeriodService;

    #region 权限

    [ObservableProperty]
    private bool _canAccessSocialAssistance;

    [ObservableProperty]
    private bool _canAccessSocialAssistanceReport;

    [ObservableProperty]
    private bool _canAccessTemporaryAssistance;

    [ObservableProperty]
    private bool _canAccessChange;

    /// <summary>各子功能卡置灰透明度（无权限 0.5）</summary>
    public double SocialAssistanceOpacity => CanAccessSocialAssistance ? 1.0 : 0.5;
    public double ReportOpacity => CanAccessSocialAssistanceReport ? 1.0 : 0.5;
    public double TempReliefOpacity => CanAccessTemporaryAssistance ? 1.0 : 0.5;
    public double ChangeOpacity => CanAccessChange ? 1.0 : 0.5;

    partial void OnCanAccessSocialAssistanceChanged(bool value) => OnPropertyChanged(nameof(SocialAssistanceOpacity));
    partial void OnCanAccessSocialAssistanceReportChanged(bool value) => OnPropertyChanged(nameof(ReportOpacity));
    partial void OnCanAccessTemporaryAssistanceChanged(bool value) => OnPropertyChanged(nameof(TempReliefOpacity));
    partial void OnCanAccessChangeChanged(bool value) => OnPropertyChanged(nameof(ChangeOpacity));

    #endregion

    #region 模块统计（失败降级显示 StatNA）

    /// <summary>在享保障对象数（status=ApplicationStatusCodes.APPROVED）</summary>
    [ObservableProperty]
    private string _activeCountText = StatNA;

    /// <summary>本月总新增（B 线周期口径）</summary>
    [ObservableProperty]
    private string _monthlyNewAdditionsText = StatNA;

    /// <summary>本月总退出（B 线周期口径）</summary>
    [ObservableProperty]
    private string _monthlyExitsText = StatNA;

    /// <summary>渐退期进行中/已到期户数</summary>
    [ObservableProperty]
    private string _gracePeriodExpiringText = StatNA;

    #endregion

    public SocialAssistanceHomeViewModel(
        IServiceProvider serviceProvider,
        IDialogService dialogService,
        ILoggerService logger,
        IWindowTitleService windowTitleService,
        INewPermissionService permissionService,
        IStatisticsService statisticsService,
        IGracePeriodService gracePeriodService)
        : base(serviceProvider, dialogService, logger, windowTitleService)
    {
        _permissionService = permissionService;
        _statisticsService = statisticsService;
        _gracePeriodService = gracePeriodService;
        Title = "低收入人口救助帮扶";
    }

    protected override async Task LoadDataAsync()
    {
        var userId = App.CurrentUserId ?? 0;
        if (userId <= 0) return;

        try
        {
            // 权限与统计互不依赖，并发执行；各自内部捕获异常，单侧失败不影响另一侧
            await Task.WhenAll(
                LoadPermissionsAsync(userId),
                LoadStatsAsync());
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "低收入人口救助帮扶首页数据加载失败");
        }
    }

    private async Task LoadPermissionsAsync(int userId)
    {
        try
        {
            var codes = new[]
            {
                PermissionCodes.INCOME_VIEW,
                PermissionCodes.REPORT_VIEW,
                PermissionCodes.TEMP_CREATE,
                PermissionCodes.CHANGE_VIEW
            };
            var permissions = await _permissionService.CheckPermissionsAsync(userId, codes);

            bool Has(string code) => permissions.TryGetValue(code, out var granted) && granted;

            CanAccessSocialAssistance = Has(PermissionCodes.INCOME_VIEW);
            CanAccessSocialAssistanceReport = Has(PermissionCodes.REPORT_VIEW);
            CanAccessTemporaryAssistance = Has(PermissionCodes.TEMP_CREATE);
            CanAccessChange = Has(PermissionCodes.CHANGE_VIEW);
            MarkPermissionsLoaded();
        }
        catch (Exception ex)
        {
            // 失败不清零：保留上次状态，基类允许下次 OnAppearing 重试
            Logger.LogError(ex, "模块权限加载失败（保留上次状态，稍后重试）");
        }
    }

    private async Task LoadStatsAsync()
    {
        // 救助统计与渐退期统计来自两个服务，任一失败仅降级对应项为 "—"
        var statsTask = SafeLoadAsync(
            () => _statisticsService.GetSocialAssistanceStatsAsync(),
            s =>
            {
                ActiveCountText = s.ActiveCount.ToString("N0");
                MonthlyNewAdditionsText = s.MonthlyNewAdditions.ToString("N0");
                MonthlyExitsText = s.MonthlyExits.ToString("N0");
            },
            "救助模块统计加载失败");

        var graceTask = SafeLoadAsync(
            () => _gracePeriodService.GetExpiringCountAsync(GracePeriodConstants.EXPIRING_WARNING_DAYS),
            count => GracePeriodExpiringText = count.ToString("N0"),
            "渐退期临期统计加载失败");

        await Task.WhenAll(statsTask, graceTask);
    }

    // SafeLoadAsync 已上提至 ModuleHomeViewModelBase 基类

    #region 子功能导航命令

    [RelayCommand]
    private Task OpenApplicationWorkflowAsync() =>
        NavigateToFeatureAsync<Pages.SocialAssistance.ApplicationWorkflowPage>(
            CanAccessSocialAssistance, "业务申请工作流", "业务申请工作流");

    [RelayCommand]
    private Task OpenChangeManagementAsync() =>
        NavigateToFeatureAsync<Pages.ChangeManagement.ChangePage>(
            CanAccessChange, "保障对象动态管理", "保障对象动态管理");

    [RelayCommand]
    private Task OpenTempReliefAsync() =>
        NavigateToFeatureAsync<Pages.TempRelief.TempReliefListPage>(
            CanAccessTemporaryAssistance, "临时救助", "临时救助");

    [RelayCommand]
    private Task OpenMonthlyReportAsync() =>
        NavigateToFeatureAsync<Pages.Reporting.MonthlyReportPage>(
            CanAccessSocialAssistanceReport, "救助月报表", "救助月报表");

    [RelayCommand]
    private Task OpenGracePeriodExpiringAsync() =>
        NavigateToFeatureAsync<Pages.SocialAssistance.GracePeriodExpiringListPage>(
            true, "渐退期管理", "渐退期管理");

    [RelayCommand]
    private Task OpenRecoveryAsync() =>
        NavigateToFeatureAsync<Pages.Recovery.RecoverySearchPage>(
            true, "后补追缴", "后补追缴");

    [RelayCommand]
    private Task OpenCollegeStudentAsync() =>
        NavigateToFeatureAsync<Pages.SocialAssistance.CollegeStudentManagementPage>(
            CanAccessSocialAssistance, "大学生管理", "大学生管理");

    // 返回上一页：使用基类 GoBackCommand（含栈守卫与窗口标题恢复）

    #endregion
}
