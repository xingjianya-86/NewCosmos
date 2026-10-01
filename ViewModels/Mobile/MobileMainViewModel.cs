using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Components;
using NewCosmos.Services.Core;
using NewCosmos.Navigation;
using NewCosmos.Services.Domain.Reporting;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.Mobile;

public partial class MobileMainViewModel : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerService _logger;
    private readonly INewPermissionService _permissionService;
    private readonly IConfigService _configService;
    private readonly IDialogService _dialogService;
    private readonly IWindowTitleService _windowTitleService;
    private readonly IStatisticsService _statisticsService;
    private readonly IAppUpdateCoordinator _updateCoordinator;
    private readonly ISessionStore _sessionStore;
    private readonly INavigationService _navigationService;

    private bool _permissionsLoading;
    private int _permissionRetryCount;
    private bool _hasAnyModulePermission;
    private bool _redirectingToLogin;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    public string AppVersion => _configService.GetAppOptions().Version;
    public string CurrentUserName => App.CurrentUserName ?? "用户";

    #region Tab 状态

    [ObservableProperty] private bool _isWorkspaceSelected = true;
    [ObservableProperty] private bool _isVerifySelected;
    [ObservableProperty] private bool _isArchiveSelected;
    [ObservableProperty] private bool _isProfileSelected;

    #endregion

    #region 权限

    [ObservableProperty] private bool _canAccessSocialAssistance;
    [ObservableProperty] private bool _canAccessElderlyBenefit;
    [ObservableProperty] private bool _canAccessAssetVerification;
    [ObservableProperty] private bool _canAccessMonthlyAudit;
    [ObservableProperty] private bool _canAccessArchiveQuery;
    [ObservableProperty] private bool _canAccessDatabaseManagement;
    [ObservableProperty] private bool _canAccessUserManagement;
    [ObservableProperty] private bool _canAccessLottery;
    [ObservableProperty] private bool _canAccessDuty;
    [ObservableProperty] private bool _canAccessReprint;
    [ObservableProperty] private bool _canAccessRecovery;
    [ObservableProperty] private bool _canAccessChange;
    [ObservableProperty] private bool _canAccessTempRelief;
    [ObservableProperty] private bool _canAccessSocialAssistanceReport;
    [ObservableProperty] private bool _canAccessAssetMonthlyReport;

    #endregion

    #region 核对模块统计

    [ObservableProperty] private string _pendingReportCount = "—";
    [ObservableProperty] private string _monthlyCompletedCount = "—";
    [ObservableProperty] private string _monthlyNewCount = "—";
    [ObservableProperty] private string _yearTotalCount = "—";

    #endregion

    #region 内容区（卡片数据，视图在 MobileMainPage.xaml 模板渲染）

    public ObservableCollection<ModuleCardViewModel> WorkspaceCards { get; } = new();
    public ObservableCollection<ModuleCardViewModel> VerifyFeatureCards { get; } = new();
    public ObservableCollection<ModuleCardViewModel> ArchiveCards { get; } = new();

    public bool HasNoWorkspaceCards => WorkspaceCards.Count == 0;
    public bool HasNoArchiveCards => ArchiveCards.Count == 0;

    #endregion

    public MobileMainViewModel(
        IServiceProvider serviceProvider,
        ILoggerService logger,
        INewPermissionService permissionService,
        IConfigService configService,
        IDialogService dialogService,
        IWindowTitleService windowTitleService,
        IStatisticsService statisticsService,
        IAppUpdateCoordinator updateCoordinator,
        ISessionStore sessionStore,
        INavigationService navigationService)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _permissionService = permissionService;
        _configService = configService;
        _dialogService = dialogService;
        _windowTitleService = windowTitleService;
        _statisticsService = statisticsService;
        _updateCoordinator = updateCoordinator;
        _sessionStore = sessionStore;
        _navigationService = navigationService;

        // 卡片数据在构造期构建一次；权限到位后 RebuildCards 重建（权限未加载时全部隐藏，与原实现一致）
        RebuildCards();
    }

    public async Task InitializeAsync()
    {
        try
        {
            var userId = App.CurrentUserId ?? 0;
            if (userId <= 0)
            {
                var restored = await TryRestoreSessionAsync();
                if (!restored)
                {
                    await GoToLoginAsync("会话已失效，请重新登录");
                    return;
                }
            }

            await LoadPermissionsAsync();
            _ = LoadStatsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "手机端首页初始化失败");
        }
    }

    private async Task<bool> TryRestoreSessionAsync()
    {
        try
        {
            var session = await _sessionStore.LoadAsync();
            if (session == null || session.UserId <= 0)
                return false;

            App.ApplySession(session);
            _logger.Info($"会话已恢复: UserId={session.UserId}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.Warn($"恢复会话失败: {ex.Message}");
            return false;
        }
    }

    private async Task GoToLoginAsync(string? toast = null)
    {
        if (_redirectingToLogin) return;
        _redirectingToLogin = true;

        if (!string.IsNullOrEmpty(toast))
            await ShowSnackBarAsync(toast!, SnackBarType.Warning);

        App.ClearSession();
        try { await _sessionStore.ClearAsync(); } catch { /* 已尽力清除 */ }

        await _navigationService.SetRootAsync(NavigationKeys.Login);
        _windowTitleService.SetPageTitle(null);
        _redirectingToLogin = false;
    }

    #region SnackBar

    [ObservableProperty] private string _snackBarMessage = string.Empty;
    [ObservableProperty] private SnackBarType _snackBarType = SnackBarType.Warning;
    [ObservableProperty] private bool _isSnackBarVisible;

    private async Task ShowSnackBarAsync(string message, SnackBarType type = SnackBarType.Warning)
    {
        SnackBarMessage = message;
        SnackBarType = type;
        IsSnackBarVisible = false;
        IsSnackBarVisible = true;
        await Task.Delay(4000);
        IsSnackBarVisible = false;
    }

    #endregion

    #region 权限加载

    private async Task LoadPermissionsAsync()
    {
        if (_permissionsLoading) return;
        var userId = App.CurrentUserId ?? 0;
        if (userId <= 0)
        {
            await GoToLoginAsync("会话已失效，请重新登录");
            return;
        }

        _permissionsLoading = true;
        try
        {
            var codes = new[]
            {
                PermissionCodes.INCOME_VIEW,
                PermissionCodes.ELDERLY_VIEW,
                PermissionCodes.ASSET_VIEW,
                PermissionCodes.ASSET_MAUDIT,
                PermissionCodes.ARCHIVE_VIEW,
                PermissionCodes.DB_CENTER_ACCESS,
                PermissionCodes.USER_VIEW,
                PermissionCodes.LOTTERY_ACCESS,
                PermissionCodes.DUTY_VIEW,
                PermissionCodes.RECOVERY_VIEW,
                PermissionCodes.CHANGE_VIEW,
                PermissionCodes.TEMP_CREATE,
                PermissionCodes.REPORT_VIEW,
                PermissionCodes.ASSET_REPORT,
            };
            var result = await _permissionService.CheckPermissionsAsync(userId, codes);
            if (result != null)
            {
                bool Has(string code) => result.TryGetValue(code, out var v) && v;
                CanAccessSocialAssistance = Has(PermissionCodes.INCOME_VIEW);
                CanAccessElderlyBenefit = Has(PermissionCodes.ELDERLY_VIEW);
                CanAccessAssetVerification = Has(PermissionCodes.ASSET_VIEW);
                CanAccessMonthlyAudit = Has(PermissionCodes.ASSET_MAUDIT);
                CanAccessArchiveQuery = Has(PermissionCodes.ARCHIVE_VIEW);
                CanAccessDatabaseManagement = Has(PermissionCodes.DB_CENTER_ACCESS);
                CanAccessUserManagement = Has(PermissionCodes.USER_VIEW);
                CanAccessLottery = Has(PermissionCodes.LOTTERY_ACCESS);
                CanAccessDuty = Has(PermissionCodes.DUTY_VIEW);
                CanAccessRecovery = Has(PermissionCodes.RECOVERY_VIEW);
                CanAccessChange = Has(PermissionCodes.CHANGE_VIEW);
                CanAccessTempRelief = Has(PermissionCodes.TEMP_CREATE);
                CanAccessSocialAssistanceReport = Has(PermissionCodes.REPORT_VIEW);
                CanAccessAssetMonthlyReport = Has(PermissionCodes.ASSET_REPORT);
                CanAccessReprint = Has(PermissionCodes.INCOME_VIEW) || Has(PermissionCodes.ELDERLY_VIEW)
                    || Has(PermissionCodes.ASSET_VIEW) || Has(PermissionCodes.ARCHIVE_VIEW);

                _hasAnyModulePermission = CanAccessSocialAssistance || CanAccessElderlyBenefit
                    || CanAccessAssetVerification || CanAccessMonthlyAudit || CanAccessArchiveQuery
                    || CanAccessDatabaseManagement || CanAccessUserManagement || CanAccessLottery
                    || CanAccessDuty || CanAccessRecovery || CanAccessChange || CanAccessTempRelief
                    || CanAccessSocialAssistanceReport || CanAccessAssetMonthlyReport;
                _permissionRetryCount = 0;

                // 权限到位后重建内容区：构造期构建时权限尚未加载，工作台卡片会全部隐藏
                RebuildCards();
                OnPropertyChanged(nameof(HasNoWorkspaceCards));
                OnPropertyChanged(nameof(HasNoArchiveCards));
            }
        }
        catch (Exception ex)
        {
            // 失败不清零：保留上次 CanAccess*（有旧状态则菜单不消失）
            _logger.LogError(ex, "加载权限失败（保留上次状态）");
            if (_hasAnyModulePermission)
            {
                await ShowSnackBarAsync("权限加载失败，正在重试", SnackBarType.Warning);
            }
            else
            {
                await ShowSnackBarAsync("权限加载失败，请检查网络后重试", SnackBarType.Error);
            }
            _ = RetryPermissionsLaterAsync();
        }
        finally
        {
            _permissionsLoading = false;
        }
    }

    private async Task RetryPermissionsLaterAsync()
    {
        // 指数退避：2s、4s、8s…上限 30s，最多自动重试 5 次
        if (_permissionRetryCount >= 5) return;
        _permissionRetryCount++;
        var delayMs = Math.Min(30_000, 2_000 * (1 << (_permissionRetryCount - 1)));
        await Task.Delay(delayMs);
        if (_redirectingToLogin) return;
        await LoadPermissionsAsync();
    }

    #endregion

    #region 统计加载

    private async Task LoadStatsAsync()
    {
        try
        {
            var result = await _statisticsService.GetAssetVerificationStatsAsync();
            if (result.IsSuccess && result.Value != null)
            {
                PendingReportCount = result.Value.PendingReportCount.ToString("N0");
                MonthlyCompletedCount = result.Value.MonthlyCompleted.ToString("N0");
                MonthlyNewCount = result.Value.MonthlyNew.ToString("N0");
                YearTotalCount = result.Value.YearTotal.ToString("N0");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "核对模块统计加载异常");
        }
    }

    #endregion

    #region 卡片数据构建（视图在 MobileMainPage.xaml 模板渲染）

    private void RebuildCards()
    {
        WorkspaceCards.Clear();
        AddWorkspaceCard("📝", "低收入人口救助帮扶", "申请建档 · 草稿续办 · 档案编辑", "#EBF5FF", CanAccessSocialAssistance, NavigateToSocialAssistanceAsync);
        AddWorkspaceCard("🎓", "大学生管理", "在校大学生救助帮扶管理", "#EBF5FF", CanAccessSocialAssistance, NavigateToCollegeStudentAsync);
        AddWorkspaceCard("📊", "救助月报表", "救助统计月度报表", "#EFF6FF", CanAccessSocialAssistanceReport, NavigateToSocialAssistanceMonthlyReportAsync);
        AddWorkspaceCard("👴", "高龄津贴发放", "申请登记 · 停发 · 复核", "#F5F3FF", CanAccessElderlyBenefit, NavigateToElderlyBenefitsAsync);
        AddWorkspaceCard("🔄", "保障对象动态管理", "户主变更 · 成员增减 · 死亡减员", "#ECFDF5", CanAccessChange, NavigateToChangeManagementAsync);
        AddWorkspaceCard("🆘", "临时救助", "急难型 · 支出型救助办理", "#FFF7ED", CanAccessTempRelief, NavigateToTempReliefAsync);
        AddWorkspaceCard("⏳", "渐退期管理", "进行中与已到期的渐退户", "#EFF6FF", CanAccessSocialAssistance, NavigateToGracePeriodAsync);
        AddWorkspaceCard("📁", "救助档案管理", "历史档案检索查阅", "#ECFDF5", CanAccessArchiveQuery, NavigateToArchiveQueryAsync);
        AddWorkspaceCard("📅", "值班管理", "按月查看值班表", "#F5F3FF", CanAccessDuty, NavigateToDutyScheduleAsync);
        AddWorkspaceCard("🖨️", "打印任务", "查看推送打印任务状态", "#EEF2FF", true, NavigateToPrintJobsAsync);
        AddWorkspaceCard("💰", "后补追缴", "停止人员追缴 · 手工录入", "#ECFDF5", CanAccessRecovery, NavigateToRecoveryAsync);
        AddWorkspaceCard("🔍", "家庭经济状况核对", "经济核对 · 报告审核", "#FFF7ED", CanAccessAssetVerification, NavigateToAssetVerificationAsync);

        VerifyFeatureCards.Clear();
        AddFeatureCard("⚡", "快速经济核对", "发起单户家庭经济状况核对申请", "#FFF7ED", OpenQuickVerificationAsync);
        AddFeatureCard("📋", "月度审核", "核对报告上传、复核与批量处理", "#FFF7ED", OpenMonthlyAuditAsync);
        if (CanAccessAssetMonthlyReport)
            AddFeatureCard("📊", "核对月报表", "资产核查月度报表统计", "#FFF7ED", OpenAssetMonthlyReportAsync);

        ArchiveCards.Clear();
        AddArchiveCard("📁", "救助档案管理", "历史档案检索查阅", "#ECFDF5", CanAccessArchiveQuery, NavigateToMobileArchiveAsync);
    }

    private void AddWorkspaceCard(string icon, string title, string subtitle, string bgColor, bool accessible, Func<Task> onTap)
    {
        if (!accessible) return;
        WorkspaceCards.Add(new ModuleCardViewModel
        {
            Icon = icon, Title = title, Subtitle = subtitle, BgColor = bgColor,
            Command = new AsyncRelayCommand(onTap)
        });
    }

    private void AddFeatureCard(string icon, string title, string subtitle, string bgColor, Func<Task> onTap)
    {
        VerifyFeatureCards.Add(new ModuleCardViewModel
        {
            Icon = icon, Title = title, Subtitle = subtitle, BgColor = bgColor,
            ShowArrow = true, Command = new AsyncRelayCommand(onTap)
        });
    }

    private void AddArchiveCard(string icon, string title, string subtitle, string bgColor, bool accessible, Func<Task> onTap)
    {
        if (!accessible) return;
        ArchiveCards.Add(new ModuleCardViewModel
        {
            Icon = icon, Title = title, Subtitle = subtitle, BgColor = bgColor,
            Command = new AsyncRelayCommand(onTap)
        });
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        var confirm = await _dialogService.DisplayAlertAsync("确认", "确定要退出登录吗", "确定", "取消");
        if (!confirm) return;

        _logger.LogSecurity("用户登出", ("UserId", App.CurrentUserId ?? 0));
        App.ClearSession();
        try { await _sessionStore.ClearAsync(); } catch { /* 已尽力清除 */ }
        await _navigationService.SetRootAsync(NavigationKeys.Login);
        _windowTitleService.SetPageTitle(null);
    }

    /// <summary>检查更新（共用在线更新流程；Android 下载 APK 并触发系统安装）。</summary>
    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        await _updateCoordinator.CheckAndPromptAsync(manual: true);
    }

    #endregion
    #region Tab 选择

    [RelayCommand]
    private void SelectWorkspace()
    {
        IsWorkspaceSelected = true; IsVerifySelected = false; IsArchiveSelected = false; IsProfileSelected = false;
    }

    [RelayCommand]
    private void SelectVerify()
    {
        IsWorkspaceSelected = false; IsVerifySelected = true; IsArchiveSelected = false; IsProfileSelected = false;
    }

    [RelayCommand]
    private void SelectArchive()
    {
        IsWorkspaceSelected = false; IsVerifySelected = false; IsArchiveSelected = true; IsProfileSelected = false;
    }

    [RelayCommand]
    private void SelectProfile()
    {
        IsWorkspaceSelected = false; IsVerifySelected = false; IsArchiveSelected = false; IsProfileSelected = true;
    }

    #endregion

    #region 导航命令

    private async Task NavigateToAssetVerificationAsync()
    {
        await NavigateToPageAsync<Pages.AssetVerification.AssetVerificationHomePage>("家庭经济状况核对");
    }

    private async Task NavigateToSocialAssistanceAsync()
    {
        await NavigateToPageAsync<Pages.SocialAssistance.ApplicationWorkflowPage>("业务申请工作流");
    }

    private async Task NavigateToElderlyBenefitsAsync()
    {
        await NavigateToPageAsync<Pages.ElderlyBenefits.ElderlyBenefitsHomePage>("高龄津贴发放");
    }

    private async Task NavigateToChangeManagementAsync()
    {
        await NavigateToPageAsync<Pages.ChangeManagement.ChangePage>("保障对象动态管理");
    }

    private async Task NavigateToTempReliefAsync()
    {
        await NavigateToPageAsync<Pages.TempRelief.TempReliefListPage>("临时救助");
    }

    private async Task NavigateToGracePeriodAsync()
    {
        await NavigateToPageAsync<Pages.SocialAssistance.GracePeriodExpiringListPage>("渐退期管理");
    }

    private async Task NavigateToArchiveQueryAsync()
    {
        await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveQueryPage>("救助档案管理");
    }

    private async Task NavigateToMobileArchiveAsync()
    {
        await NavigateToPageAsync<Pages.Mobile.MobileArchiveQueryPage>("救助档案管理");
    }

    private async Task NavigateToCollegeStudentAsync()
    {
        await NavigateToPageAsync<Pages.SocialAssistance.CollegeStudentManagementPage>("大学生管理");
    }

    private async Task NavigateToSocialAssistanceMonthlyReportAsync()
    {
        await NavigateToPageAsync<Pages.Reporting.MonthlyReportPage>("救助月报表");
    }

    private async Task NavigateToDutyScheduleAsync()
    {
        await NavigateToPageAsync<Pages.DutyManagement.DutySchedulePage>("值班管理");
    }

    private async Task NavigateToPrintJobsAsync()
    {
        await NavigateToPageAsync<Pages.Mobile.MobilePrintJobsPage>("打印任务");
    }

    private async Task NavigateToRecoveryAsync()
    {
        await NavigateToPageAsync<Pages.Recovery.RecoverySearchPage>("后补追缴");
    }

    private async Task OpenQuickVerificationAsync()
    {
        if (!CanAccessAssetVerification)
        {
            await _dialogService.DisplayAlertAsync("权限不足", "您没有快速核对的访问权限", "确定");
            return;
        }
        await NavigateToPageAsync<Pages.AssetVerification.QuickAssetVerificationPage>("快速经济核对");
    }

    private async Task OpenMonthlyAuditAsync()
    {
        if (!CanAccessMonthlyAudit)
        {
            await _dialogService.DisplayAlertAsync("权限不足", "您没有月度审核的访问权限", "确定");
            return;
        }
        await NavigateToPageAsync<Pages.AssetVerification.MonthlyAssetAuditPage>("月度审核");
    }

    private async Task OpenAssetMonthlyReportAsync()
    {
        if (!CanAccessAssetMonthlyReport)
        {
            await _dialogService.DisplayAlertAsync("权限不足", "您没有核对月报表的访问权限", "确定");
            return;
        }
        await NavigateToPageAsync<Pages.AssetVerification.MonthlyReportPage>("核对月报表");
    }

    private async Task ShowPcOnlyAsync()
    {
        await _dialogService.DisplayAlertAsync("提示", "此功能请使用电脑端操作", "确定");
    }

    #endregion
}
