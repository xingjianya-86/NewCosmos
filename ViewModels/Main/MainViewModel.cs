using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using System.Collections.ObjectModel;
using System.Windows.Input;
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
    private readonly ISessionLifecycleService _sessionLifecycle = null!;
    private readonly IElderlyApplicationService _elderlyApplicationService = null!;
    private readonly ICollegeStudentService _collegeStudentService = null!;
    private readonly Services.Domain.ArchiveManagement.IDeathStatementService _deathStatementService = null!;
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

    #region 首页仪表盘卡片分组（模型驱动：无权限卡片不入列、其余自动补位，空组整组隐藏）

    /// <summary>首页模块分组（救助业务/管理工具/娱乐天地/快捷链接），权限加载后整体重建</summary>
    public ObservableCollection<ModuleGroup> DashboardGroups { get; } = new();

    /// <summary>
    /// 按卡片目录重建首页模块分组：无权限卡片跳过，组内卡片按
    /// 行=序号/3、列=序号%3 向前压实，避免 Grid 固定格位留下空洞；
    /// 整组无卡时不进 <see cref="DashboardGroups"/>，分组标题随之隐藏。
    /// </summary>
    private void RebuildDashboard()
    {
        ModuleCardItem Card(string title, string subtitle, string description,
            string iconGlyph, string iconBgColor, string iconColor,
            string buttonText, ICommand command, object? parameter = null) => new()
        {
            Title = title,
            Subtitle = subtitle,
            Description = description,
            IconGlyph = iconGlyph,
            IconBgColor = iconBgColor,
            IconColor = iconColor,
            ButtonText = buttonText,
            Command = command,
            CommandParameter = parameter
        };

        // 卡片目录：元组顺序 = 组内展示顺序，数组顺序 = 分组展示顺序。
        // 图标色值与 Resources\Styles\Colors.xaml 的 Module* 色板保持一致（模型无法引用 XAML 资源）。
        var catalog = new (string Title, List<(ModuleCardItem Card, bool Visible)> Items)[]
        {
            (ModuleCategoryConstants.BUSINESS, new()
            {
                (Card("低收入人口救助帮扶", "认定审核确认 · 动态管理",
                    "覆盖救助申请、保障对象动态管理、临时救助、月报表与渐退期管理",
                    "\uE765", "#EBF5FF", "#ab3227", "进入模块", NavigateCommand, "SocialAssistance"), CanAccessSocialAssistance),
                (Card("高龄津贴发放", "申请登记 · 停发管理 · 类别复核",
                    "覆盖高龄津贴申请登记、停发办理、类别复核与月度报表",
                    "\uE7B8", "#F5F3FF", "#5a4a72", "进入模块", NavigateCommand, "Elderly"), CanAccessElderlyBenefit),
                (Card("家庭经济状况核对", "经济核对 · 报告审核",
                    "覆盖快速核对、报告上传月度审核与核对月报表",
                    "\uE721", "#FFFBEB", "#96681c", "进入模块", NavigateCommand, "AssetVerification"), CanAccessAssetVerification),
                (Card("救助档案管理", "历史档案检索查阅",
                    "现存低收入人口历史档案查询",
                    "\uE8F1", "#ECFDF5", "#2e4738", "进入模块", NavigateCommand, "Archive"), CanAccessArchiveQuery),
                // 死亡情况说明：紧随救助档案管理，入口在中间业务模块（侧栏不放）
                (Card("死亡情况说明", "死亡申报 · 身份聚合 · 文书出具",
                    "覆盖村内死亡申报、在库身份聚合、数据提醒与情况说明出具",
                    "\uE7BA", "#F1F0FB", "#6D28D9", "进入模块", NavigateCommand, "DeathStatement"), CanAccessArchiveQuery),
                // 后补追缴：独立模块卡（权限 RECOVERY_VIEW，与侧栏同源门控），紧随死亡情况说明
                (Card("后补追缴", "停止人员追缴 · 手工录入",
                    "覆盖低保、刚性支出、临时救助与高龄停止人员的追缴办理与台账",
                    "\uE8D1", "#FFF3E0", "#7a5315", "进入模块", NavigateCommand, "Recovery"), CanAccessRecovery),
                // 照片补传：历史档案照片按人补传（入口跟随档案查看权，与死亡情况说明同口径）
                (Card("照片补传", "按人检索 · 连续采集 · 推送打印",
                    "跨低收入/高龄/临时救助检索人员已传照片数，勾选后连续采集证件照并可推送打印",
                    "", "#ECFDF5", "#2e4738", "进入模块", NavigateCommand, "PhotoBackfill"), CanAccessArchiveQuery)
            }),
            (ModuleCategoryConstants.TOOLS, new()
            {
                (Card("数据中心", "备份恢复 · 数据导入导出",
                    "数据库管理、数据字典、标准配置、数据导入与区域数据维护",
                    "\uE88E", "#F8FAFC", "#6d6354", "进入模块", NavigateCommand, "Database"), CanAccessDatabaseManagement),
                (Card("用户与权限管理", "账户 · 角色 · 权限配置",
                    "系统用户、角色权限与经办机构信息维护（经办机构亦可从左侧菜单进入）",
                    "\uE77B", "#FDF2F8", "#7f231b", "进入模块", NavigateCommand, "User"), CanAccessUserManagement),
                (Card("值班管理", "月度值班表 · 班务调整",
                    "值班表轮转生成、请假/串班/代班调整、节假日维护与政务值班表导出",
                    "\uE8C0", "#ECFDF5", "#2e4738", "进入模块", NavigateCommand, "Duty"), CanAccessDuty),
                (Card("历史补打中心", "按人检索 · 批量补打",
                    "跨域按人检索低保/临时救助/普惠高龄/资产核查/动态管理档案，支持留痕原样补打与按月批量打印",
                    "\uE749", "#F0F9FF", "#6d6354", "进入模块", NavigateCommand, "ReprintCenter"), CanAccessReprint)
            }),
            (ModuleCategoryConstants.ENTERTAINMENT, new()
            {
                (Card("彩票助手", "机选 · 智能预测",
                    "双色球/大乐透历史数据、号码统计分析与智能预测",
                    "\uE734", "#FFF7ED", "#EA580C", "进入模块", NavigateCommand, "Lottery"), CanAccessLottery)
            }),
            (ModuleCategoryConstants.LINKS, new()
            {
                (Card("作者官网", "软件更新 · 通告",
                    "用于软件更新以及通告",
                    "\uE774", "#EBF5FF", "#ab3227", "访问网站", OpenWebsite1Command), true),
                (Card("黑龙江民政官网", "黑龙江省民政厅",
                    "访问黑龙江省民政厅网上办事大厅",
                    "\uE774", "#ECFDF5", "#2e4738", "访问网站", OpenWebsite2Command), true)
            })
        };

        DashboardGroups.Clear();
        foreach (var (title, items) in catalog)
        {
            var cards = items.Where(x => x.Visible).Select(x => x.Card).ToList();
            if (cards.Count == 0) continue;

            for (var i = 0; i < cards.Count; i++)
            {
                cards[i].Row = i / 3;
                cards[i].Col = i % 3;
            }

            DashboardGroups.Add(new ModuleGroup { Title = title, Cards = cards });
        }
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

    /// <summary>顶栏显示名（优先真实姓名，无姓名时回退登录账号）</summary>
    public string CurrentUserDisplayName =>
        string.IsNullOrWhiteSpace(UserFullName) ? CurrentUserName : UserFullName;

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

    #region 死亡申报联动提醒

    /// <summary>本范围未办死亡联动待办数（口径见 DeathStatementService.GetLinkageTaskCountAsync，任务表实时判定）</summary>
    [ObservableProperty]
    private int _deathLinkagePendingCount;

    /// <summary>是否显示死亡申报联动提醒横幅</summary>
    [ObservableProperty]
    private bool _showDeathLinkageReminder;

    /// <summary>
    /// 当前用户是否具备任一联动手续承接权限（高龄停发/档案变更/大学生/追缴）。
    /// true → 横幅引导「去办理」；false（如村级：只申报不办理）→ 横幅改「待镇级经办处理」。
    /// </summary>
    [ObservableProperty]
    private bool _deathLinkageCanHandle;

    /// <summary>横幅文案（按承接权限分流：有权限引导办理，无权限告知已入队待镇级处理）</summary>
    public string DeathLinkageBannerText => !DeathLinkageCanHandle
        ? $"已收到 {DeathLinkagePendingCount} 项死亡申报关联手续（高龄停发 / 档案变更等），本级无办理权限，待镇级经办处理"
        : $"有 {DeathLinkagePendingCount} 项死亡申报关联手续待办（高龄停发 / 档案变更等），请前往「死亡情况说明」办理";

    partial void OnDeathLinkagePendingCountChanged(int value) => OnPropertyChanged(nameof(DeathLinkageBannerText));
    partial void OnDeathLinkageCanHandleChanged(bool value) => OnPropertyChanged(nameof(DeathLinkageBannerText));

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
        NewCosmos.Services.System.IClientVersionService clientVersionService,
        ISessionLifecycleService sessionLifecycle,
        Services.Domain.ArchiveManagement.IDeathStatementService deathStatementService)
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
        _clientVersionService = clientVersionService;
        _sessionLifecycle = sessionLifecycle;
        _deathStatementService = deathStatementService;

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
                LoadCollegeStudentReminderAsync(),
                LoadDeathLinkageReminderAsync());
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
        //
        // 未安装时【降级继续】而非中断：旧实现此处 return 会连带跳过权限/统计/字典加载，
        // 导致整页空主页（侧栏卡片全隐藏、本会话无法恢复）。缺 Office 只影响打印/PDF，
        // 打印执行时自有明确报错，不应拖垮主页初始化。
        //
        // 2026-11 并行化：Office 探测（100~500ms 注册表/COM）与下方权限播种互不依赖，
        // 探测提前入后台跑，权限播种完再取结果弹窗——主页初始化总时长省一个探测段。
        var officeTask = Task.Run(() => OfficeProviderDetector.IsOfficeInstalled());

        // 公文字体安装检查（与 Office 检测同款并行/非阻断口径）：扫描随包 公文字体\ 目录、
        // 解析族名与系统已装字体比对（SkiaSharp 枚举，纯读注册表/字体表，放线程池避免卡 UI）。
        // 缺失只影响公文文书打印/导出版式，其余功能正常——按降级继续处理，绝不拖垮主页初始化。
        var fontTask = Task.Run(FontInstallChecker.GetMissingFonts);
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

#if WINDOWS
        var officeInstalled = await officeTask;
        if (!officeInstalled)
        {
            _logger.Warn("Microsoft Office 未安装：打印/PDF 导出不可用，主页按降级模式继续初始化");
            await _dialogService.DisplayAlertAsync(
                "缺少必要组件",
                "系统未检测到 Microsoft Office，打印与 PDF 文档导出功能将不可用。\n\n" +
                "其余功能可正常使用。如需打印/导出，请安装 Microsoft Office 365 后重启应用。",
                "确定");
        }
        else
        {
            _logger.Info("Office 检测通过");
        }

        // 公文字体检查结果（权限播种后取，与 Office 弹窗同位；内部已捕获异常，失败=无缺失）
        var missingFonts = await fontTask;
        if (missingFonts.Count == 0)
        {
            _logger.Info("公文字体检查通过");
        }
        else
        {
            var fontNames = string.Join("、", missingFonts.Select(f => f.FileName));
            _logger.Warn($"公文字体缺失 {missingFonts.Count} 个：{fontNames}");
            var install = await _dialogService.DisplayAlertAsync(
                "公文字体缺失",
                $"系统未安装以下公文排版字体：\n{fontNames}\n\n" +
                "公文文书打印/导出的版式将不正确（字体被替换）。" +
                "可一键安装到当前用户字体（免管理员，安装后请重启应用与 Word）。",
                "一键安装",
                "暂不");
            if (install)
            {
                // 安装含文件复制与注册表写入，放线程池；结果回 UI 线程弹窗
                var installResult = await Task.Run(() => FontInstallChecker.Install(missingFonts));
                if (installResult.IsSuccess)
                {
                    _logger.Info($"公文字体一键安装成功 {missingFonts.Count} 个（需重启应用与 Word 生效）");
                    await _dialogService.DisplayAlertAsync(
                        "安装完成",
                        $"已安装 {missingFonts.Count} 个公文字体。\n\n请重启应用与 Word 后生效。",
                        "确定");
                }
                else
                {
                    _logger.Warn($"公文字体一键安装未全部成功：{installResult.Message}");
                    await _dialogService.DisplayAlertAsync(
                        "安装未完成",
                        installResult.Message + "\n\n可重启应用后重试，或手动安装 公文字体 目录内的字体。",
                        "确定");
                }
            }
        }
#endif

        // 权限必须先加载完成（UI 按钮启用状态依赖权限门控）
        await LoadPermissionsAsync();

        // 其余启动阶段互不依赖，并发执行；各阶段内部均已捕获异常，单个失败不影响其他阶段
        await Task.WhenAll(
            LoadSystemStatusAsync(),
            LoadStatisticsAsync(),
            LoadGracePeriodReminderAsync(),
            LoadElderlyReminderAsync(),
            LoadCollegeStudentReminderAsync(),
            LoadDeathLinkageReminderAsync(),
            CheckSchemaStatusAsync(),
            WarmupDictCacheAsync());

        // 高龄复核自检（大学生提醒同款机制）：后台扫描「已获救助身份但高龄金额未升级」人员
        // 与「当月+回溯3个月满90周岁」人员并写入待复核队列，完成后刷新高龄待办横幅计数
        // （入队成功 → Transfer 计数上升 → 横幅亮起）。
        // 不阻塞启动；SafeFireAndForget 统一捕获记录日志。
        SafeFireAndForget(async () =>
        {
            var scan = await _elderlyApplicationService.ScanAndEnqueueIdentityUpgradesAsync();
            var age90 = await _elderlyApplicationService.ScanAndEnqueueAge90Async();
            if (scan.IsFailure)
            {
                _logger.Warn($"高龄身份升级自检失败: {scan.Message}");
            }
            else if (scan.Value > 0)
            {
                _logger.Info($"高龄身份升级自检入队 {scan.Value} 人");
            }
            if (age90.IsFailure)
            {
                _logger.Warn($"满90周岁自扫描失败: {age90.Message}");
            }
            else if (age90.Value > 0)
            {
                _logger.Info($"满90周岁自扫描入队 {age90.Value} 人");
            }
            if ((scan.IsSuccess && scan.Value > 0) || (age90.IsSuccess && age90.Value > 0))
            {
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
        OnPropertyChanged(nameof(CurrentUserDisplayName));

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

            // 无权限卡片不入列、其余卡片自动补位（整组无卡则分组隐藏）
            RebuildDashboard();

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

    /// <summary>
    /// 加载死亡申报联动提醒（失败容忍：查询失败仅记日志、不显示横幅，
    /// 账号未绑定村社区时服务返回 Success(0) 同样不亮横幅）。
    /// 口径 = 任务表实时未办数（先按范围幂等补齐再计数）；文案按承接权限分流。
    /// </summary>
    private async Task LoadDeathLinkageReminderAsync()
    {
        try
        {
            // 承接权限分流：任一域办理权限（高龄停发/档案变更/大学生/追缴）→ 引导办理；
            // 全无（典型如村级：只申报不办理）→ 告知已入队待镇级经办处理
            var handleCodes = DeathLinkageConstants.AllDomains
                .Select(DeathLinkageConstants.GetRequiredPermission)
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct()
                .ToArray();
            var userId = App.CurrentUserId ?? 0;
            var perms = userId > 0
                ? await _permissionService.CheckPermissionsAsync(userId, handleCodes)
                : null;
            DeathLinkageCanHandle = perms != null && handleCodes.Count(c => perms.TryGetValue(c, out var granted) && granted) > 0;

            var scopeResult = await _deathStatementService.ResolveScopeAsync(CancellationToken);
            if (scopeResult.IsFailure)
            {
                ShowDeathLinkageReminder = false;
                _logger.Warn($"死亡申报联动计数跳过（范围解析失败）: {scopeResult.Message}");
                return;
            }

            var result = await _deathStatementService.GetLinkageTaskCountAsync(scopeResult.Value, CancellationToken);
            if (result.IsSuccess)
            {
                DeathLinkagePendingCount = result.Value;
                ShowDeathLinkageReminder = result.Value > 0;
                _logger.Info($"死亡申报联动提醒加载完成: 待办 {DeathLinkagePendingCount} 项，可承接={DeathLinkageCanHandle}");
            }
            else
            {
                ShowDeathLinkageReminder = false;
                _logger.Warn($"死亡申报联动计数查询失败: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            ShowDeathLinkageReminder = false;
            _logger.LogError(ex, "死亡申报联动提醒加载异常");
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

                // 死亡情况说明：入口跟随档案查看权（Android 由 ResolvePage 解析 MobileDeathStatementPage）
                case "DeathStatement":
                    if (!CanAccessArchiveQuery) { await ShowNoPermissionAlertAsync(); return; }
                    await NavigateToAsync<Pages.ArchiveManagement.DeathStatementPage>("死亡情况说明");
                    break;

                // 更新日志：当前版本 / 检查更新 / 升级历史（本机轨迹 + 服务端全量），无权限门槛
                case "UpdateLog":
                    await NavigateToAsync<Pages.Config.UpdateLogPage>("更新日志");
                    break;

                // 照片补传：入口跟随档案查看权（与死亡情况说明同口径，不新增权限种子）
                case "PhotoBackfill":
                    if (!CanAccessArchiveQuery) { await ShowNoPermissionAlertAsync(); return; }
                    await NavigateToAsync<Pages.Mobile.MobilePhotoBackfillPage>("照片补传");
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

                case "Templates": // 侧栏文书模板管理入口（历史键名"Settings"与目标不符且与顶栏系统设置撞名，已改名）
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
        // 入口门控（2026-10-31 立码 COLLEGE_MANAGE：入口与承接同权，村级不再可达）
        var userId = App.CurrentUserId ?? 0;
        if (userId <= 0 ||
            !await _permissionService.HasPermissionAsync(userId, PermissionCodes.COLLEGE_MANAGE, CancellationToken))
        {
            _logger.LogSecurity("大学生管理入口被拦截", ("UserId", userId), ("Reason", "NoPermission"));
            await ShowNoPermissionAlertAsync();
            return;
        }

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

    /// <summary>
    /// 导航到死亡情况说明页面
    /// 由首页死亡申报联动提醒横幅点击触发
    /// </summary>
    [RelayCommand]
    private async Task NavigateToDeathStatementAsync()
    {
        _logger.LogBusiness("导航到死亡情况说明", ("Module", "DeathStatement"));
        try
        {
            await NavigateToPageAsync<Pages.ArchiveManagement.DeathStatementPage>("死亡情况说明");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导航到死亡情况说明失败");
            await _dialogService.DisplayAlertAsync("错误", $"打开死亡情况说明页面失败: {ex.Message}", "确定");
        }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        _logger.LogSecurity("用户登出", ("UserId", CurrentUserId));
        var confirm = await _dialogService.DisplayAlertAsync("确认", "确定要退出登录吗", "确定", "取消");
        if (confirm)
        {
            // 统一收尾：吊销数据库会话 → 停止心跳/解除闸门 → 清本地缓存 → 回登录页（root 由 NavigationService 注册标题）
            await _sessionLifecycle.LogoutAsync();
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
