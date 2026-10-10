using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using NewCosmos.Constants;
using NewCosmos.Models.Options;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Navigation;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.Domain.Reporting;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.AssetVerification;
using System.Runtime.InteropServices;
using NewCosmos.Services.Utilities;

namespace NewCosmos.ViewModels.Main;

public partial class MainViewModel : ViewModelBase
{
    private readonly ILoggerService _logger = null!;
    private readonly ISystemService _systemService = null!;
    private readonly IServiceProvider _serviceProvider = null!;
    private readonly IDialogService _dialogService = null!;
    private readonly INewPermissionService _permissionService = null!;
    private readonly IStatisticsService _statisticsService = null!;
    private readonly IConfigService _configService = null!;
    private readonly IUpdateService _updateService = null!;
    private readonly IAppUpdateCoordinator _updateCoordinator = null!;
    private readonly NewCosmos.Services.System.IClientVersionService _clientVersionService = null!;
    private readonly ISchemaService _schemaService = null!;
    private readonly IDictCacheService _dictCacheService = null!;
    private readonly IGracePeriodService _gracePeriodService = null!;
    private readonly IWindowTitleService _windowTitleService = null!;
    private readonly INavigationService _navigationService = null!;
    private readonly IElderlyApplicationService _elderlyApplicationService = null!;
    private readonly ICollegeStudentService _collegeStudentService = null!;
    private readonly AppOptions _appOptions = null!;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    #endregion

    public string AppVersion => _appOptions.Version;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    #region Schema警告横幅

    [ObservableProperty]
    private bool _showSchemaWarning;

    [ObservableProperty]
    private string _schemaWarningMessage = string.Empty;

    [ObservableProperty]
    private SchemaStatus _schemaStatus = new();

    #endregion

    #region 侧边栏导航状态

    /// <summary>侧边栏选中项；首页是唯一就地切换项，跳转型 TAB 返回主页时由 code-behind 复位为 Home</summary>
    [ObservableProperty]
    private string _selectedNavigation = "Home";

    public bool IsHomeSelected => SelectedNavigation == "Home";

    #endregion

    #region 权限控制属性（控制首页导航与卡片可见性）

    [ObservableProperty]
    private bool _canAccessSocialAssistance;

    [ObservableProperty]
    private bool _canAccessSocialAssistanceAdd;

    [ObservableProperty]
    private bool _canAccessSocialAssistanceReport;

    [ObservableProperty]
    private bool _canAccessSocialAssistanceEdit;

    [ObservableProperty]
    private bool _canAccessTemporaryAssistance;

    [ObservableProperty]
    private bool _canAccessElderlyBenefit;

    [ObservableProperty]
    private bool _canAccessAssetVerification;

    [ObservableProperty]
    private bool _canAccessMonthlyAudit;

    [ObservableProperty]
    private bool _canAccessArchiveQuery;

    [ObservableProperty]
    private bool _canAccessDatabaseManagement;

    [ObservableProperty]
    private bool _canAccessUserManagement;

    [ObservableProperty]
    private bool _canAccessOrganizationManagement;

    [ObservableProperty]
    private bool _canAccessChange;

    /// <summary>值班管理入口（DUTY_VIEW）</summary>
    [ObservableProperty]
    private bool _canAccessDuty;

    /// <summary>后补追缴入口（RECOVERY_VIEW）</summary>
    [ObservableProperty]
    private bool _canAccessRecovery;

    /// <summary>文书模板管理入口（TEMPLATE_MANAGE，侧边栏 Settings）</summary>
    [ObservableProperty]
    private bool _canAccessTemplateManagement;

    /// <summary>彩票助手入口（LOTTERY_ACCESS）</summary>
    [ObservableProperty]
    private bool _canAccessLottery;

    /// <summary>历史补打中心入口（任一业务域查看/办理权限即可，页面内无域级门控）</summary>
    [ObservableProperty]
    private bool _canAccessReprint;

    #region 首页仪表盘卡片动态排位（无权限卡片隐藏后其余卡片自动补位）

    public int SocialAssistanceRow { get; private set; }
    public int SocialAssistanceCol { get; private set; }
    public int ElderlyBenefitRow { get; private set; }
    public int ElderlyBenefitCol { get; private set; }
    public int AssetVerificationRow { get; private set; }
    public int AssetVerificationCol { get; private set; }
    public int ArchiveQueryRow { get; private set; }
    public int ArchiveQueryCol { get; private set; }
    public int DatabaseManagementRow { get; private set; }
    public int DatabaseManagementCol { get; private set; }
    public int UserManagementRow { get; private set; }
    public int UserManagementCol { get; private set; }
    public int LotteryRow { get; private set; }
    public int LotteryCol { get; private set; }
    public int ReprintCenterRow { get; private set; }
    public int ReprintCenterCol { get; private set; }
    public int Website1Row { get; private set; }
    public int Website1Col { get; private set; }
    public int Website2Row { get; private set; }
    public int Website2Col { get; private set; }
    public int DutyRow { get; private set; }
    public int DutyCol { get; private set; }

    /// <summary>
    /// 按固定顺序重排仪表盘卡片：无权限（不可见）卡片跳过，其余卡片按
    /// 行=序号/3、列=序号%3 向前压实，避免 Grid 固定格位留下空洞。
    /// </summary>
    private void UpdateDashboardLayout()
    {
        var i = 0;
        (int Row, int Col) NextPos()
        {
            var p = (i / 3, i % 3);
            i++;
            return p;
        }

        void Place(bool visible, Action<int, int> assign, string rowProp, string colProp)
        {
            if (!visible) return;
            var p = NextPos();
            assign(p.Row, p.Col);
            OnPropertyChanged(rowProp);
            OnPropertyChanged(colProp);
        }

        Place(CanAccessSocialAssistance, (r, c) => { SocialAssistanceRow = r; SocialAssistanceCol = c; }, nameof(SocialAssistanceRow), nameof(SocialAssistanceCol));
        Place(CanAccessElderlyBenefit, (r, c) => { ElderlyBenefitRow = r; ElderlyBenefitCol = c; }, nameof(ElderlyBenefitRow), nameof(ElderlyBenefitCol));
        Place(CanAccessAssetVerification, (r, c) => { AssetVerificationRow = r; AssetVerificationCol = c; }, nameof(AssetVerificationRow), nameof(AssetVerificationCol));
        Place(CanAccessArchiveQuery, (r, c) => { ArchiveQueryRow = r; ArchiveQueryCol = c; }, nameof(ArchiveQueryRow), nameof(ArchiveQueryCol));
        Place(CanAccessDatabaseManagement, (r, c) => { DatabaseManagementRow = r; DatabaseManagementCol = c; }, nameof(DatabaseManagementRow), nameof(DatabaseManagementCol));
        Place(CanAccessUserManagement, (r, c) => { UserManagementRow = r; UserManagementCol = c; }, nameof(UserManagementRow), nameof(UserManagementCol));
        Place(CanAccessReprint, (r, c) => { ReprintCenterRow = r; ReprintCenterCol = c; }, nameof(ReprintCenterRow), nameof(ReprintCenterCol));
        Place(CanAccessLottery, (r, c) => { LotteryRow = r; LotteryCol = c; }, nameof(LotteryRow), nameof(LotteryCol));
        Place(true, (r, c) => { Website1Row = r; Website1Col = c; }, nameof(Website1Row), nameof(Website1Col));
        Place(true, (r, c) => { Website2Row = r; Website2Col = c; }, nameof(Website2Row), nameof(Website2Col));
        Place(CanAccessDuty, (r, c) => { DutyRow = r; DutyCol = c; }, nameof(DutyRow), nameof(DutyCol));
    }

    #endregion

    #endregion

    #region 用户信息属性
    [ObservableProperty]
    private int _currentUserId;

    [ObservableProperty]
    private string _currentUserName = "用户";

    [ObservableProperty]
    private string _userFullName = string.Empty;

    [ObservableProperty]
    private string _userPhone = string.Empty;

    /// <summary>顶栏时间显示格式（刷新粒度为分钟，故不显示秒）</summary>
    private const string DateTimeDisplayFormat = "yyyy-MM-dd HH:mm";

    /// <summary>顶栏时间刷新定时器（每分钟一跳；Dispose 时停止）</summary>
    private IDispatcherTimer? _clockTimer;

    /// <summary>登录后定时更新检查定时器（间隔来自 update.ini；Dispose 时停止）</summary>
    private IDispatcherTimer? _updateTimer;
    private bool _updateCheckRunning;

        /// <summary>一次初始化守卫：主页每次 OnAppearing 都会调用 InitializeAsync，但重活只需首屏跑一次</summary>
        private bool _initialized;

        /// <summary>权限成功加载标记：失败时保留 false，下次 OnAppearing 重试（AGENTS §6）</summary>
        private bool _permissionsLoaded;

    [ObservableProperty]
    private string _currentDateString = DateTime.Now.ToString(DateTimeDisplayFormat);

    #endregion

    #region 系统状态属性
    [ObservableProperty]
    private string _computerName = Environment.MachineName;

    [ObservableProperty]
    private string _osVersion = RuntimeInformation.OSDescription;

    [ObservableProperty]
    private bool _isDatabaseConnected;

    [ObservableProperty]
    private string _databaseStatusMessage = "未检测";

    #endregion

    #region 统计数据属性
    /// <summary>本月总新增（B线周期内新入保，月报"新增救助明细"口径）</summary>
    [ObservableProperty]
    private int _monthlyNewAdditions;

    /// <summary>本月总退出（B线周期内停保退出，月报"停保汇总"口径）</summary>
    [ObservableProperty]
    private int _monthlyExits;

    /// <summary>新申请资产核查（B线周期内已申请未出授权报告，status=0）</summary>
    [ObservableProperty]
    private int _newAssetChecks;

    /// <summary>本月新增高龄老人数（自然月受理登记，普惠高龄月报"新增明细"口径）</summary>
    [ObservableProperty]
    private int _monthlyNewElderly;

    #endregion

    #region 渐退期到期提醒

    /// <summary>渐退期进行中/已到期户数</summary>
    [ObservableProperty]
    private int _gracePeriodExpiringCount;

    /// <summary>是否显示渐退期到期提醒横幅</summary>
    [ObservableProperty]
    private bool _showGracePeriodWarning;

    /// <summary>横幅文案（预警天数引用 GracePeriodConstants，禁止 XAML 写死 30）</summary>
    public string GracePeriodExpiringBannerText =>
        $"有 {GracePeriodExpiringCount} 户渐退期将在 {GracePeriodConstants.EXPIRING_WARNING_DAYS} 天内到期或已到期，请及时前往「低收入人口救助帮扶」处理";

    partial void OnGracePeriodExpiringCountChanged(int value) => OnPropertyChanged(nameof(GracePeriodExpiringBannerText));

    #endregion

    #region 普惠高龄待办提醒

    /// <summary>待新增人数（满80、无在享登记且历史名册无记录）</summary>
    [ObservableProperty]
    private int _elderlyPendingNewCount;

    /// <summary>待停旧增新人数（正式表 Confirmed 在享或历史名册有记录）</summary>
    [ObservableProperty]
    private int _elderlyPendingTransferCount;

    /// <summary>是否显示普惠高龄待办提醒横幅</summary>
    [ObservableProperty]
    private bool _showElderlyReminder;

    /// <summary>横幅文案（合并两类计数）</summary>
    public string ElderlyReminderText =>
        $"有 {ElderlyPendingNewCount + ElderlyPendingTransferCount} 位老人的高龄津贴需于下月办理变更" +
        $"（{ElderlyPendingNewCount} 人待新增 / {ElderlyPendingTransferCount} 人需先停发后新增），请及时前往「高龄津贴发放」处理";

    #endregion

    #region 大学生毕业提醒

    /// <summary>今年毕业的在读大学生人数</summary>
    [ObservableProperty]
    private int _collegeStudentGraduatingCount;

    /// <summary>是否显示大学生毕业提醒横幅</summary>
    [ObservableProperty]
    private bool _showCollegeStudentReminder;

    /// <summary>横幅文案（毕业后最长择业期半年，超出应退出低保）</summary>
    public string CollegeStudentReminderText =>
        CollegeStudentConstants.BuildGraduationBannerText(CollegeStudentGraduatingCount);

    partial void OnCollegeStudentGraduatingCountChanged(int value) => OnPropertyChanged(nameof(CollegeStudentReminderText));

    #endregion

    public MainViewModel(
        ILoggerService logger,
        ISystemService systemService,
        IServiceProvider serviceProvider,
        IDialogService dialogService,
        INewPermissionService permissionService,
        IStatisticsService statisticsService,
        IConfigService configService,
        ISchemaService schemaService,
        IDictCacheService dictCacheService,
        IGracePeriodService gracePeriodService,
        IElderlyApplicationService elderlyApplicationService,
        ICollegeStudentService collegeStudentService,
        IWindowTitleService windowTitleService,
        IUpdateService updateService,
        IAppUpdateCoordinator updateCoordinator,
        INavigationService navigationService,
        NewCosmos.Services.System.IClientVersionService clientVersionService)
    {
        _logger = logger;
        _systemService = systemService;
        _serviceProvider = serviceProvider;
        _dialogService = dialogService;
        _permissionService = permissionService;
        _statisticsService = statisticsService;
        _configService = configService;
        _schemaService = schemaService;
        _dictCacheService = dictCacheService;
        _gracePeriodService = gracePeriodService;
        _elderlyApplicationService = elderlyApplicationService;
        _collegeStudentService = collegeStudentService;
        _windowTitleService = windowTitleService;
        _updateService = updateService;
        _updateCoordinator = updateCoordinator;
        _navigationService = navigationService;
        _clientVersionService = clientVersionService;

        _appOptions = _configService.GetAppOptions();
        Title = _appOptions.WindowTitle;

        _logger.Info($"主视图模型初始化完成");
    }

    private async Task NavigateToAsync<T>(string pageTitle = null) where T : Page
    {
        // 页面标题写入 Page.Title（push 后窗口标题由 NavigationPage 事件按栈顶 Title 驱动）
        if (string.IsNullOrEmpty(pageTitle))
            await NavigateToPageAsync<T>();
        else
            await NavigateToPageAsync<T>(pageTitle);
    }

    public async Task InitializeAsync()
    {
        // 刷新顶栏时间并确保时钟定时器已启动（每次返回主页都要，轻量且幂等）
        StartClockTimer();

        // 重活（Office 检测、权限、统计、Schema、字典预热）只在首次执行
        if (_initialized)
        {
            // 首次权限加载失败时不锁死：每次 OnAppearing 重试至成功（失败不得清零 CanAccess*）
            if (!_permissionsLoaded)
                await LoadPermissionsAsync();

            // 回首页即刷新三条提醒横幅（渐退到期/高龄待办/大学生毕业）：原仅首屏加载是半开环，
            // 会话内办理停保/复核/渐退后横幅计数残留至重启；三条均为轻量计数查询、内部失败容忍，与权限重试同型
            await Task.WhenAll(
                LoadGracePeriodReminderAsync(),
                LoadElderlyReminderAsync(),
                LoadCollegeStudentReminderAsync());
            return;
        }
        _initialized = true;

        // 客户端版本台账上报（失败不阻断；县局据此查看未升级机器）
        _ = ReportClientVersionAsync();

        // 登录后定时检查更新（间隔来自 update.ini；仅发现新版本时提示）
        StartUpdateTimer();

        _logger.Info($"主页初始化开始");

#if WINDOWS
        // 检测 Microsoft Office 是否安装（PDF 导出必需）
        // COM/注册表探测为同步阻塞操作，放到线程池执行，避免卡住 UI 线程；
        // 结果进程内缓存一次，后续返回主页不再重复探测。
        // Android 端无 Office，跳过此检测（手机端打印走推送打印队列）。
        var officeInstalled = await Task.Run(() => OfficeProviderDetector.IsOfficeInstalled());
        if (!officeInstalled)
        {
            _logger.Error("Microsoft Office 未安装");
            await _dialogService.DisplayAlertAsync(
                "缺少必要组件",
                "系统未检测到 Microsoft Office，\nPDF 文档导出功能需要 Microsoft Office 支持。\n\n请安装 Microsoft Office 365 后重试。",
                "确定");
            return;
        }

        _logger.Info("Office 检测通过");
#endif

        InitializeUserInfo();

        // 新权限码随版本发布自动补种（幂等逐条比对种子清单，已有库也能补齐）；
        // 失败仅降级记日志，不阻塞启动（权限检查仍可用旧权限集合）
        try
        {
            await _permissionService.InitializeDefaultPermissionsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "默认权限同步失败（降级继续启动）");
        }

        // 权限必须先加载完成（UI 按钮启用状态依赖权限门控）
        await LoadPermissionsAsync();

        // 其余启动阶段互不依赖，并发执行；各阶段内部均已捕获异常，单个失败不影响其他阶段
        await Task.WhenAll(
            LoadSystemStatusAsync(),
            LoadStatisticsAsync(),
            LoadGracePeriodReminderAsync(),
            LoadElderlyReminderAsync(),
            LoadCollegeStudentReminderAsync(),
            CheckSchemaStatusAsync(),
            WarmupDictCacheAsync());

        // 高龄身份升级自检（大学生提醒同款机制）：后台扫描"已获救助身份但高龄金额未升级"人员
        // 并写入待复核队列，完成后刷新高龄待办横幅计数（入队成功 → Transfer 计数上升 → 横幅亮起）。
        // 不阻塞启动；SafeFireAndForget 统一捕获记录日志。
        SafeFireAndForget(async () =>
        {
            var scan = await _elderlyApplicationService.ScanAndEnqueueIdentityUpgradesAsync();
            if (scan.IsFailure)
            {
                _logger.Warn($"高龄身份升级自检失败: {scan.Message}");
                return;
            }
            if (scan.Value > 0)
            {
                _logger.Info($"高龄身份升级自检入队 {scan.Value} 人，刷新待办横幅");
                await LoadElderlyReminderAsync();
            }
        });

        _windowTitleService.SetPageTitle(null);
    }

    private async Task WarmupDictCacheAsync()
    {
        // 预热字典缓存：首次全量加载；后续仅当距上次刷新超过 TTL 才重载（多机字典变更同步节拍）
        try
        {
            await _dictCacheService.EnsureFreshAsync();
            _logger.Info($"字典缓存预热完成");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "字典缓存预热失败");
        }
    }

    private void InitializeUserInfo()
    {
        // 未登录态兜底为 0（无效用户），使 LoadPermissionsAsync 的 <=0 守卫生效，
        // 禁止静默落到 1 号账号——历史上该兜底会让未登录用户获得管理员权限视图
        CurrentUserId = App.CurrentUserId ?? 0;
        CurrentUserName = App.CurrentUserName ?? "用户";
        UserFullName = App.CurrentUserFullName ?? string.Empty;
        UserPhone = App.CurrentUserPhone ?? string.Empty;

        _logger.Info($"用户信息初始化完成, UserId={CurrentUserId}");
    }

    /// <summary>
    /// 立即刷新顶栏时间，并启动每分钟刷新一次的时钟定时器。
    /// InitializeAsync 每次 OnAppearing 都会执行，此方法保持幂等（定时器只创建一次）。
    /// </summary>
    private void StartClockTimer()
    {
        CurrentDateString = DateTime.Now.ToString(DateTimeDisplayFormat);

        if (_clockTimer != null)
            return;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null)
            return;

        _clockTimer = dispatcher.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromMinutes(1);
        _clockTimer.Tick += (_, _) => CurrentDateString = DateTime.Now.ToString(DateTimeDisplayFormat);
        _clockTimer.Start();
    }

    /// <summary>释放资源时停止时钟定时器（基类 Dispose 已改为 virtual）</summary>
    public override void Dispose()
    {
        _clockTimer?.Stop();
        _clockTimer = null;
        _updateTimer?.Stop();
        _updateTimer = null;
        base.Dispose();
    }

    #region 在线更新

    private async Task ReportClientVersionAsync()
    {
        try
        {
            var result = await _clientVersionService.ReportAsync(_appOptions.Version);
            if (result.IsFailure)
                _logger.Warn($"客户端版本上报失败: {result.Message}");
        }
        catch (Exception ex)
        {
            _logger.Warn($"客户端版本上报异常: {ex.Message}");
        }
    }

    /// <summary>登录后定时更新检查（幂等；间隔来自 update.ini）</summary>
    private void StartUpdateTimer()
    {
        if (_updateTimer != null) return;

        var options = _updateService.Options;
        if (!options.Enabled || options.CheckIntervalMinutes <= 0) return;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        _updateTimer = dispatcher.CreateTimer();
        _updateTimer.Interval = TimeSpan.FromMinutes(options.CheckIntervalMinutes);
        _updateTimer.Tick += async (_, _) =>
        {
            // 重入保护：一次检查耗时超过间隔时不再叠加并行任务；
            // 异常必须捕获——async void Tick 抛出会变成未观察任务异常
            if (_updateCheckRunning) return;
            _updateCheckRunning = true;
            try
            {
                await CheckForUpdatesInternalAsync(manual: false);
            }
            catch (Exception ex)
            {
                _logger.Warn($"定时更新检查失败: {ex.Message}");
            }
            finally
            {
                _updateCheckRunning = false;
            }
        };
        _updateTimer.Start();
        _logger.Info($"更新定时检查已启动: 间隔 {options.CheckIntervalMinutes} 分钟");
    }

    /// <summary>手动检查更新（主界面按钮）</summary>
    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        try
        {
            await CheckForUpdatesInternalAsync(manual: true);
        }
        catch (Exception ex)
        {
            // RelayCommand 的 Task 异常无人观察，必须就地捕获
            _logger.Warn($"手动更新检查失败: {ex.Message}");
        }
    }

    private async Task CheckForUpdatesInternalAsync(bool manual)
    {
        var launched = await _updateCoordinator.CheckAndPromptAsync(manual);
        // Windows 需退出应用以便安装器覆盖升级；Android 由系统安装器接管，无需退出
        if (launched && OperatingSystem.IsWindows())
        {
            Application.Current?.Quit();
        }
    }

    #endregion

    private async Task CheckSchemaStatusAsync()
    {
        try
        {
            var result = await _schemaService.GetSchemaStatusAsync();
            if (result.IsSuccess && result.Value != null)
            {
                SchemaStatus = result.Value;

                if (!result.Value.IsInitialized || result.Value.MissingTables.Count > 0)
                {
                    ShowSchemaWarning = true;
                    SchemaWarningMessage = result.Value.MissingTables.Count > 0
                        ? $"检测到数据库结构未初始化（缺少 {result.Value.MissingTables.Count} 个表），请联系管理员"
                        : "检测到数据库结构需要初始化，请联系管理员";
                    _logger.Warn($"数据库结构缺失告警");
                }
                else
                {
                    ShowSchemaWarning = false;
                    _logger.Info($"Schema 状态检查通过");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"Schema 状态检查失败: {ex.Message}");
        }
    }

    private async Task LoadPermissionsAsync()
    {
        if (CurrentUserId <= 0) return;

        try
        {
            var userId = CurrentUserId;

            // 一次批量查询取代逐码串行往返（首页门控 + 模块子功能）
            var codes = new[]
            {
                PermissionCodes.INCOME_VIEW,
                PermissionCodes.INCOME_CREATE,
                PermissionCodes.INCOME_EDIT,
                PermissionCodes.REPORT_VIEW,
                PermissionCodes.TEMP_CREATE,
                PermissionCodes.ASSET_VIEW,
                PermissionCodes.ASSET_MAUDIT,
                PermissionCodes.ARCHIVE_VIEW,
                PermissionCodes.DB_CENTER_ACCESS,
                PermissionCodes.RECOVERY_VIEW,
                PermissionCodes.TEMPLATE_MANAGE,
                PermissionCodes.LOTTERY_ACCESS,
                PermissionCodes.USER_VIEW,
                PermissionCodes.ORG_VIEW,
                PermissionCodes.CHANGE_VIEW,
                PermissionCodes.ELDERLY_VIEW,
                PermissionCodes.DUTY_VIEW
            };
            var permissions = await _permissionService.CheckPermissionsAsync(userId, codes);

            bool Has(string code) => permissions.TryGetValue(code, out var granted) && granted;

            CanAccessSocialAssistance = Has(PermissionCodes.INCOME_VIEW);
            CanAccessSocialAssistanceAdd = Has(PermissionCodes.INCOME_CREATE);
            CanAccessSocialAssistanceReport = Has(PermissionCodes.REPORT_VIEW);
            CanAccessSocialAssistanceEdit = Has(PermissionCodes.INCOME_EDIT);
            CanAccessTemporaryAssistance = Has(PermissionCodes.TEMP_CREATE);
            CanAccessElderlyBenefit = Has(PermissionCodes.ELDERLY_VIEW);
            CanAccessAssetVerification = Has(PermissionCodes.ASSET_VIEW);
            CanAccessMonthlyAudit = Has(PermissionCodes.ASSET_MAUDIT);
            CanAccessArchiveQuery = Has(PermissionCodes.ARCHIVE_VIEW);
            CanAccessDatabaseManagement = Has(PermissionCodes.DB_CENTER_ACCESS);
            CanAccessUserManagement = Has(PermissionCodes.USER_VIEW);
            CanAccessOrganizationManagement = Has(PermissionCodes.ORG_VIEW);
            CanAccessChange = Has(PermissionCodes.CHANGE_VIEW);
            CanAccessDuty = Has(PermissionCodes.DUTY_VIEW);
            CanAccessRecovery = Has(PermissionCodes.RECOVERY_VIEW);
            CanAccessTemplateManagement = Has(PermissionCodes.TEMPLATE_MANAGE);
            CanAccessLottery = Has(PermissionCodes.LOTTERY_ACCESS);
            CanAccessReprint = Has(PermissionCodes.INCOME_VIEW) || Has(PermissionCodes.TEMP_CREATE)
                || Has(PermissionCodes.ELDERLY_VIEW) || Has(PermissionCodes.ASSET_VIEW)
                || Has(PermissionCodes.CHANGE_VIEW);

            // 无权限卡片隐藏后其余卡片自动补位
            UpdateDashboardLayout();

            _permissionsLoaded = true;
            _logger.Info($"权限加载完成: SocialAssistance={CanAccessSocialAssistance}");
        }
        catch (Exception ex)
        {
            // 失败不清零、不关窗：保留上次状态，下次 OnAppearing 重试（AGENTS §6）
            _logger.LogError(ex, "权限加载失败（保留上次状态，稍后重试）");
            await _dialogService.DisplayAlertAsync("提示", "权限加载失败，正在重试", "确定");
        }
    }

    private async Task LoadStatisticsAsync()
    {
        try
        {
            var result = await _statisticsService.GetDashboardStatisticsAsync();

            if (result.IsSuccess)
            {
                var stats = result.Value;
                MonthlyNewAdditions = stats.MonthlyNewAdditions;
                MonthlyExits = stats.MonthlyExits;
                NewAssetChecks = stats.NewAssetChecks;
                MonthlyNewElderly = stats.MonthlyNewElderly;

                _logger.Info($"统计数据加载完成: 新增={MonthlyNewAdditions}, 退出={MonthlyExits}, 资产核查={NewAssetChecks}, 高龄={MonthlyNewElderly}");
            }
            else
            {
                MonthlyNewAdditions = 0;
                MonthlyExits = 0;
                NewAssetChecks = 0;
                MonthlyNewElderly = 0;

                _logger.Warn($"统计数据加载失败");
            }
        }
        catch (Exception ex)
        {
            MonthlyNewAdditions = 0;
            MonthlyExits = 0;
            NewAssetChecks = 0;
            MonthlyNewElderly = 0;

            _logger.LogError(ex, "统计数据加载异常");
        }
    }

    /// <summary>
    /// 加载渐退期到期提醒（失败容忍：查询失败仅记日志、不显示横幅）
    /// </summary>
    private async Task LoadGracePeriodReminderAsync()
    {
        try
        {
            var result = await _gracePeriodService.GetWarningCountAsync(GracePeriodConstants.EXPIRING_WARNING_DAYS);
            if (result.IsSuccess)
            {
                GracePeriodExpiringCount = result.Value;
                ShowGracePeriodWarning = result.Value > 0;
                _logger.Info($"渐退期到期提醒加载完成: {GracePeriodExpiringCount} 户");
            }
            else
            {
                ShowGracePeriodWarning = false;
                _logger.Warn($"渐退期到期统计查询失败: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            ShowGracePeriodWarning = false;
            _logger.LogError(ex, "渐退期到期提醒加载异常");
        }
    }

    /// <summary>
    /// 加载普惠高龄待办提醒（失败容忍：查询失败仅记日志、不显示横幅）
    /// </summary>
    private async Task LoadElderlyReminderAsync()
    {
        try
        {
            var result = await _elderlyApplicationService.GetPendingElderlyCountsAsync();
            if (result.IsSuccess && result.Value != null)
            {
                ElderlyPendingNewCount = result.Value.PendingNew;
                ElderlyPendingTransferCount = result.Value.PendingTransfer;
                ShowElderlyReminder = result.Value.Total > 0;
                OnPropertyChanged(nameof(ElderlyReminderText));
                _logger.Info($"普惠高龄待办提醒加载完成: 待新增 {result.Value.PendingNew} 人 / 待停旧增新 {result.Value.PendingTransfer} 人");
            }
            else
            {
                ShowElderlyReminder = false;
                _logger.Warn($"普惠高龄待办统计查询失败: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            ShowElderlyReminder = false;
            _logger.LogError(ex, "普惠高龄待办提醒加载异常");
        }
    }

    private async Task LoadCollegeStudentReminderAsync()
    {
        try
        {
            var result = await _collegeStudentService.GetGraduatingStudentsAsync(DateTime.Today.Year);
            if (result.IsSuccess)
            {
                CollegeStudentGraduatingCount = result.Value.Count;
                ShowCollegeStudentReminder = result.Value.Count > 0;
                _logger.Info($"大学生毕业提醒加载完成: 今年毕业 {result.Value.Count} 人");
            }
            else
            {
                ShowCollegeStudentReminder = false;
                _logger.Warn($"大学生毕业统计查询失败: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            ShowCollegeStudentReminder = false;
            _logger.LogError(ex, "大学生毕业提醒加载异常");
        }
    }

    #region 侧边栏导航命令

    /// <summary>
    /// 侧边栏/仪表盘卡片统一导航入口。
    /// 首页为唯一就地切换项；其余 TAB 一律权限校验后 PushAsync 到真实功能页（模块首页或管理页），
    /// 不再维护右侧多区域视图——占位页与假界面已全部移除。
    /// </summary>
    [RelayCommand]
    private async Task NavigateAsync(string target)
    {
        try
        {
            _logger.LogBusiness($"导航到 {target}", ("Target", target));

            switch (target)
            {
                case "Home":
                    SelectedNavigation = "Home";
                    _windowTitleService.SetPageTitle(null);
                    break;

                case "SocialAssistance":
                    if (!CanAccessSocialAssistance) { await ShowNoPermissionAlertAsync(); return; }
                    await NavigateToAsync<Pages.SocialAssistance.SocialAssistanceHomePage>("低收入人口救助帮扶");
                    break;

                case "Elderly":
                    if (!CanAccessElderlyBenefit) { await ShowNoPermissionAlertAsync(); return; }
                    await NavigateToAsync<Pages.ElderlyBenefits.ElderlyBenefitsHomePage>("高龄津贴发放");
                    break;

                case "AssetVerification":
                    if (!CanAccessAssetVerification) { await ShowNoPermissionAlertAsync(); return; }
                    await NavigateToAsync<Pages.AssetVerification.AssetVerificationHomePage>("家庭经济状况核对");
                    break;

                case "Archive":
                    if (!CanAccessArchiveQuery) { await ShowNoPermissionAlertAsync(); return; }
                    await NavigateToAsync<Pages.ArchiveManagement.ArchiveQueryPage>("救助档案管理");
                    break;

                case "Database":
                    if (!CanAccessDatabaseManagement) { await ShowNoPermissionAlertAsync(); return; }
                    await NavigateToAsync<Pages.DatabaseManagement.DatabaseManagementPage>("数据中心");
                    break;

                case "User":
                    if (!CanAccessUserManagement) { await ShowNoPermissionAlertAsync(); return; }
                    await NavigateToAsync<Pages.UserManagement.UserManagementPage>("用户与权限管理");
                    break;

                case "Organization":
                    if (!CanAccessOrganizationManagement) { await ShowNoPermissionAlertAsync(); return; }
                    await NavigateToAsync<Pages.UserManagement.OrganizationPage>("经办机构管理");
                    break;

                case "Settings":
                    if (!CanAccessTemplateManagement) { await ShowNoPermissionAlertAsync(); return; }
                    await NavigateToAsync<Pages.ArchiveManagement.TemplateManagementPage>("文书模板管理");
                    break;

                case "Recovery":
                    if (!CanAccessRecovery) { await ShowNoPermissionAlertAsync(); return; }
                    await NavigateToAsync<Pages.Recovery.RecoverySearchPage>("后补追缴");
                    break;

                case "Lottery":
                    if (!CanAccessLottery) { await ShowNoPermissionAlertAsync(); return; }
                    await NavigateToAsync<Pages.Lottery.LotteryHomePage>("彩票助手");
                    break;

                case "ReprintCenter":
                    if (!CanAccessReprint) { await ShowNoPermissionAlertAsync(); return; }
                    await NavigateToAsync<Pages.Reprint.UnifiedReprintPage>("历史补打中心");
                    break;

                case "Duty":
                    if (!CanAccessDuty) { await ShowNoPermissionAlertAsync(); return; }
                    await NavigateToAsync<Pages.DutyManagement.DutySchedulePage>("值班管理");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "页面导航失败");
            await _dialogService.DisplayAlertAsync("错误", $"页面导航失败: {ex.Message}", "确定");
        }
    }

    /// <summary>
    /// 系统设置（文档输出位置等本机配置）——主页顶栏设置按钮入口
    /// </summary>
    [RelayCommand]
    private Task OpenSettingsAsync() => NavigateToAsync<Pages.Config.SettingsPage>("系统设置");

    /// <summary>
    /// 打开官方网站
    /// </summary>
    [RelayCommand]
    private async Task OpenWebsite1Async()
    {
        try
        {
            _logger.LogBusiness("打开官方网站", ("Url", "https://sua17.cn"));
            await Launcher.OpenAsync(new Uri("https://sua17.cn"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "打开官方网站失败");
            await _dialogService.DisplayAlertAsync("提示", "无法打开官方网站，请检查网络或浏览器", "确定");
        }
    }

    /// <summary>
    /// 打开黑龙江民政官方网站
    /// </summary>
    [RelayCommand]
    private async Task OpenWebsite2Async()
    {
        try
        {
            _logger.LogBusiness("打开黑龙江民政官方网站", ("Url", "https://zwfw.mzt.hlj.gov.cn/casserver/login"));
            await Launcher.OpenAsync(new Uri("https://zwfw.mzt.hlj.gov.cn/casserver/login"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "打开黑龙江民政官方网站失败");
            await _dialogService.DisplayAlertAsync("提示", "无法打开黑龙江民政官方网站，请检查网络或浏览器", "确定");
        }
    }

    #endregion

    #region 提醒横幅导航命令

    /// <summary>
    /// 导航到渐退期管理页面
    /// 由首页渐退期提醒横幅点击触发
    /// </summary>
    [RelayCommand]
    private async Task NavigateToGracePeriodExpiringAsync()
    {
        try
        {
            _logger.LogBusiness("导航到渐退期管理", ("Module", "GracePeriodExpiring"));

            await NavigateToPageAsync<Pages.SocialAssistance.GracePeriodExpiringListPage>("渐退期管理");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导航到渐退期管理失败");
            await _dialogService.DisplayAlertAsync("错误", $"打开渐退期管理页面失败: {ex.Message}", "确定");
        }
    }

    /// <summary>
    /// 导航到普惠高龄下月待办列表页
    /// 由首页普惠高龄待办提醒横幅点击触发
    /// </summary>
    [RelayCommand]
    private async Task NavigateToElderlyPendingAsync()
    {
        _logger.LogBusiness("导航到普惠高龄待办", ("Module", "ElderlyPending"));
        try
        {
            await NavigateToPageAsync<Pages.ElderlyBenefits.ElderlyPendingListPage>("高龄津贴下月待办");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导航到普惠高龄待办失败");
            await _dialogService.DisplayAlertAsync("错误", $"打开高龄津贴下月待办页面失败: {ex.Message}", "确定");
        }
    }

    /// <summary>
    /// 导航到大学生管理页面
    /// 由首页大学生毕业提醒横幅点击触发
    /// </summary>
    [RelayCommand]
    private async Task NavigateToCollegeStudentAsync()
    {
        _logger.LogBusiness("导航到大学生管理", ("Module", "CollegeStudent"));
        try
        {
            await NavigateToPageAsync<Pages.SocialAssistance.CollegeStudentManagementPage>("大学生管理");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导航到大学生管理失败");
            await _dialogService.DisplayAlertAsync("错误", $"打开大学生管理页面失败: {ex.Message}", "确定");
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        _logger.LogSecurity("用户登出", ("UserId", CurrentUserId));
        var confirm = await _dialogService.DisplayAlertAsync("确认", "确定要退出登录吗", "确定", "取消");
        if (confirm)
        {
            App.ClearSession();
            try
            {
                await _serviceProvider.GetRequiredService<ISessionStore>().ClearAsync();
            }
            catch (Exception ex)
            {
                _logger.Warn($"清除会话存储失败: {ex.Message}");
            }
            // 回到登录页（镜像 App.CreateWindow 的启动结构）。
            // 历史上解析 AppShell 会抛 InvalidOperationException（它从未注册进 DI）；AppShell 已删除，现只经 NavigationService 导航。
            // 登出后导航栈整体替换，root 登录页同样注册标题跟随（SetRootAsync 内部完成）
            await _navigationService.SetRootAsync(NavigationKeys.Login);
            _windowTitleService.SetPageTitle(null);
        }
    }

    private async Task ShowNoPermissionAlertAsync()
    {
        try
        {
            await _dialogService.DisplayAlertAsync("权限不足", "您没有此功能的访问权限", "确定");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "显示权限不足提示失败");
        }
    }

    #endregion

    #region 系统功能

    [RelayCommand]
    private async Task TestDatabaseConnectionAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusMessage = "正在测试数据库连接...";

        try
        {
            var result = await _systemService.TestDatabaseConnectionAsync();

            if (result.IsSuccess)
            {
                IsDatabaseConnected = true;
                DatabaseStatusMessage = "已连接";
                StatusMessage = "数据库连接正常";
                _logger.Info($"数据库连接测试完成");
                await _dialogService.DisplayAlertAsync("成功", "数据库连接正常", "确定");
            }
            else
            {
                IsDatabaseConnected = false;
                DatabaseStatusMessage = "未连接";
                StatusMessage = $"连接失败: {result.Message}";
                _logger.Warn($"数据库连接测试失败，请检查");
                await _dialogService.DisplayAlertAsync("失败", result.Message ?? "连接失败", "确定");
            }
        }
        catch (Exception ex)
        {
            IsDatabaseConnected = false;
            DatabaseStatusMessage = "错误";
            StatusMessage = $"系统错误: {ex.Message}";
            _logger.LogError(ex, "数据库连接测试异常");
            await _dialogService.DisplayAlertAsync("错误", UserFriendlyMessages.Get(ErrorCodes.DB_CONNECTION_FAILED), "确定");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task LoadSystemStatusAsync()
    {
        IsBusy = true;
        StatusMessage = "正在获取系统状态...";

        try
        {
            var result = await _systemService.GetSystemStatusAsync();

            if (result.IsSuccess)
            {
                var status = result.Value;
                IsDatabaseConnected = status.IsDatabaseConnected;
                DatabaseStatusMessage = status.IsDatabaseConnected ? "已连接" : "未连接";
                StatusMessage = $"数据库状态: {DatabaseStatusMessage} | 活跃用户: {status.ActiveUserCount} | 响应时间: {status.ResponseTimeMs:F0}ms";

                _logger.Info($"系统状态加载完成");
            }
            else
            {
                StatusMessage = $"获取状态失败 {result.Message}";
                DatabaseStatusMessage = "检测失败";
                _logger.Warn($"系统状态获取失败");
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"系统错误: {ex.Message}";
            DatabaseStatusMessage = "错误";
            _logger.LogError(ex, "系统状态加载异常");
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion
}
