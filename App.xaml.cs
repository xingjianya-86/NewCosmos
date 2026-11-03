using Microsoft.Maui;
using NewCosmos.Helpers;
using NewCosmos.Models.Options;
using NewCosmos.Models.Session;
using NewCosmos.Services.Core;
using NewCosmos.Services.Platform;
using NewCosmos.Navigation;
using NewCosmos.Pages.Auth;
using System.Threading;

namespace NewCosmos;

public partial class App : Application
{
    private static Mutex _singleInstanceMutex = null!;
    private static readonly string MutexName = "NewCosmos_SingleInstance_Mutex";
    private static bool _isFirstInstance;

    public static IServiceProvider Services { get; set; } = null!;
    public static int? CurrentUserId { get; private set; }
    public static Guid? CurrentSessionId { get; private set; }
    public static string CurrentUserName { get; private set; } = string.Empty;
    public static string CurrentUserFullName { get; private set; } = string.Empty;
    public static string CurrentUserPhone { get; private set; } = string.Empty;
    public static int? CurrentUserOrganizationId { get; private set; }

    // 组织地址信息（登录时一次性填充）
    public static string? CurrentUserOrgName { get; private set; }
    public static string? CurrentUserCityName { get; private set; }
    public static string? CurrentUserCountyName { get; private set; }
    public static string? CurrentUserTownName { get; private set; }
    public static string? CurrentUserVillageName { get; private set; }
    public static string? CurrentUserOrgAddress { get; private set; }

    public static void SetCurrentUserId(int userId) => CurrentUserId = userId;

    /// <summary>记录当前数据库会话令牌（心跳/会话闸门/登出使用）</summary>
    public static void SetCurrentSessionId(Guid sessionId) => CurrentSessionId = sessionId;

    public static void SetCurrentUser(string userName, string fullName, string phone, int? organizationId)
    {
        CurrentUserName = userName;
        CurrentUserFullName = fullName;
        CurrentUserPhone = phone;
        CurrentUserOrganizationId = organizationId;
    }

    public static void SetCurrentUserAddress(string? orgName, string? cityName, string? countyName,
        string? townName, string? villageName, string? orgAddress)
    {
        CurrentUserOrgName = orgName;
        CurrentUserCityName = cityName;
        CurrentUserCountyName = countyName;
        CurrentUserTownName = townName;
        CurrentUserVillageName = villageName;
        CurrentUserOrgAddress = orgAddress;
    }

    public App(IServiceProvider services)
    {
        InitializeComponent();
        Services = services;
        CheckSingleInstance();
        SetupGlobalExceptionHandling();
    }

    private void CheckSingleInstance()
    {
        try
        {
            _singleInstanceMutex = new Mutex(true, MutexName, out _isFirstInstance);
            if (!_isFirstInstance)
            {
                Serilog.Log.Information("[APP] 应用程序已存在，尝试激活现有窗口");
                _singleInstanceMutex.Dispose();
                _singleInstanceMutex = null;
#if WINDOWS
                var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
                var processes = System.Diagnostics.Process.GetProcessesByName(currentProcess.ProcessName);
                foreach (var process in processes)
                {
                    if (process.Id != currentProcess.Id)
                    {
                        NativeMethods.SetForegroundWindow(process.MainWindowHandle);
                        break;
                    }
                }
#endif
                Environment.Exit(0);
            }
            Serilog.Log.Information("[APP] 首次实例通过");
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[APP] 单例检查失败，继续运行");
            _isFirstInstance = true;
        }
    }

    private void SetupGlobalExceptionHandling()
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var ex = (Exception)args.ExceptionObject;
            LogCrashException(ex, "AppDomain.UnhandledException");
        };

        TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            LogCrashException(args.Exception, "TaskScheduler.UnobservedTaskException");
            args.SetObserved();
        };
    }

    internal static void LogCrashException(Exception ex, string source)
    {
        // 直接写文件——Serilog 异步 sink 在进程退出时丢失未落盘日志
        try
        {
            var crashLog = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}]\n{ex}\n\n";
            var dir = Path.Combine(FileSystem.AppDataDirectory, "..", "crash");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, $"crash_{DateTime.Now:yyyyMMdd}.log"), crashLog);
        }
        catch { }

        try
        {
            if (Services != null)
            {
                var logger = Services.GetService<ILoggerService>();
                if (logger != null)
                {
                    logger.LogError(ex, $"未捕获异常 [{source}]");
                    return;
                }
            }
        }
        catch
        {
        }

        Console.WriteLine($"[CRITICAL] [{source}] {ex}");
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        Serilog.Log.Information("[APP] CreateWindow 开始");

        try
        {
            if (Services == null)
            {
                Serilog.Log.Error("[APP] Services 为 null");
                return CreateErrorWindow("应用程序服务未初始化，无法启动");
            }

            // 全局 SnackBar 宿主：订阅 IDialogService.SnackBarRequested，一次性接线
            Components.GlobalSnackBarHost.Attach(Services.GetRequiredService<IDialogService>());

            var configService = Services.GetRequiredService<IConfigService>();
            if (configService == null)
            {
                Serilog.Log.Error("[APP] ConfigService 为 null");
                return CreateErrorWindow("配置服务未初始化，无法启动");
            }

            if (!configService.CheckConfigFilesExist())
            {
                Serilog.Log.Warning("[APP] 配置文件缺失，启动配置向导");
                var wizardPage = Services.GetRequiredService<Pages.Config.ConfigWizardPage>();
                var wizardWindow = new Window(new NavigationPage(wizardPage)) { Title = "配置向导" };
                return wizardWindow;
            }

            var appOptions = configService.GetAppOptions();
            // B 线统计周期结算日注入（默认 15；上级业务截止 20 号时 app.ini 配 20）
            Helpers.BusinessCycleHelper.Configure(appOptions.BCycleSettleDay);
            // 保障金变更登记阈值注入（默认 10 元/月；app.ini [Change] FundChangeThreshold 可配）
            Helpers.FundChangeThresholdHelper.Configure(appOptions.FundChangeThreshold);
            Serilog.Log.Information("[APP] 应用配置加载完成，版本: {Version}, B线结算日: {SettleDay}, 保障金登记阈值: {Threshold}",
                appOptions.Version, BusinessCycleHelper.SettleDay, FundChangeThresholdHelper.Threshold);

            // 文档输出根统一（config/document_output.yaml）：注入输出根与预览临时目录；
            // 历史"输出"目录一次性迁移与临时文件清理放后台，不阻断启动
            var documentOutputOptions = configService.GetDocumentOutputOptions();
            Helpers.OutputPathHelper.Configure(documentOutputOptions.BaseDirectory, documentOutputOptions.TempSubdirectory);
            _ = Task.Run(async () =>
            {
                try
                {
                    var outputMigrator = Services.GetRequiredService<Services.Core.IOutputRootMigrationService>();
                    outputMigrator.CleanupTempDirectory();
                    await outputMigrator.MigrateLegacyOutputAsync();
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "[APP] 输出根迁移/预览临时目录清理失败");
                }
            });

            var windowTitleService = Services.GetRequiredService<IWindowTitleService>();

            // 会话恢复改真异步：原实现在 UI 线程用 GetAwaiter().GetResult() 同步阻塞
            // SecureStorage 读取与 DB 用户校验。现立即返回窗口（过渡页），后台恢复完成后再换页。
            // 启动首屏：启用动画时用 SplashPage，与下面的后台会话恢复并行；
            // 会话恢复的所有出口都先 await ISplashGate，两边就绪即切真实页面。
            var splashGate = Services.GetRequiredService<ISplashGate>();
            var window = new Window(CreateStartupPage(splashGate)) { Title = appOptions.WindowTitle };
#if WINDOWS
            // 窗口贴合主显示器工作区（排除任务栏）：写死 1920×1080@（0,0）会盖到任务栏区域，
            // 底部状态栏整条被任务栏压住（2026-11-03 实测"检查更新"按钮落在任务栏之下）。
            var density = DeviceDisplay.Current.MainDisplayInfo.Density;
            var workArea = Microsoft.UI.Windowing.DisplayArea.Primary?.WorkArea;
            if (density > 0 && workArea != null)
            {
                window.X = workArea.Value.X / density;
                window.Y = workArea.Value.Y / density;
                window.Width = workArea.Value.Width / density;
                window.Height = workArea.Value.Height / density;
            }
            else
            {
                window.Width = 1920;
                window.Height = 1080;
                window.X = 0;
                window.Y = 0;
            }
#endif

            // 系统栏形态（安卓沉浸式全屏/EdgeToEdge；Windows 空实现）与 Windows 自定义标题栏
            ApplyPlatformChrome(window);

            _ = RestoreSessionAndNavigateAsync(window, appOptions, windowTitleService);

            Serilog.Log.Information("[APP] CreateWindow 结束（会话恢复转后台）");
            return window;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "[APP] CreateWindow 初始化失败");
            return CreateErrorWindow($"应用程序初始化失败:\n{ex.Message}\n\n堆栈跟踪:\n{ex.StackTrace}");
        }
    }

    /// <summary>
    /// 平台外观收口：
    ///  - 安卓：套用系统栏形态（沉浸式全屏 / EdgeToEdge，见 ISystemBarsService）
    ///  - Windows：自定义标题栏（纸墨风改色 + 拖拽热区）
    /// 两端都从 DI 解析，失败不阻断启动（外观问题不能影响业务可用性）。
    /// </summary>
    private static void ApplyPlatformChrome(Window window)
    {
        try
        {
            Services.GetService<ISystemBarsService>()?.Apply();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[APP] 系统栏形态应用失败");
        }

#if WINDOWS
        try
        {
            // 自定义标题栏（纸墨风改色）。
            // 时机很关键：CreateWindow 时窗口 Handler 尚未就绪，直接 Attach 会提前 return，
            // 导致首帧渲染后才改标题栏 → 布局跳变（用户看到的"首次启动闪屏"之一）。
            // 故订阅 HandlerChanged：Handler 就绪时恰好应用一次；已就绪则立即应用。
            var titleBarService = Services.GetService<Platforms.Windows.WindowsTitleBarService>();
            if (titleBarService != null)
            {
                if (window.Handler != null)
                {
                    titleBarService.Attach(window);
                }
                else
                {
                    void OnHandlerChanged(object? s, EventArgs e)
                    {
                        window.HandlerChanged -= OnHandlerChanged;
                        try { titleBarService.Attach(window); }
                        catch (Exception hx) { Serilog.Log.Warning(hx, "[APP] Windows 标题栏延迟应用失败"); }
                    }
                    window.HandlerChanged += OnHandlerChanged;
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[APP] Windows 自定义标题栏应用失败");
        }
#endif
    }

    /// <summary>
    /// 启动首屏：SplashEnabled=1 时用启动动画页，否则沿用白底"正在启动…"过渡页。
    /// 动画页创建失败时回落过渡页并立即放行闸门，绝不阻塞启动。
    /// </summary>
    private static Page CreateStartupPage(ISplashGate splashGate)
    {
        if (!splashGate.IsEnabled)
            return CreateStartupTransitionalPage();

        try
        {
            return Services.GetRequiredService<Pages.Splash.SplashPage>();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "[APP] 启动动画页创建失败，回落过渡页");
            splashGate.Complete();
            return CreateStartupTransitionalPage();
        }
    }

    /// <summary>
    /// 从合并资源字典取色；资源不可用时回退到暖纸底。
    /// 用于代码构造的根页面（启动过渡/连接重试/错误页）——它们必须与全局 Background 令牌同色，
    /// 否则启动链路 黑(Splash) → 白(过渡) → 纸(令牌) 每跳一次就是一次闪屏。
    /// </summary>
    private static Color PaperBackground() =>
        Application.Current?.Resources.TryGetValue("Background", out var v) == true && v is Color c
            ? c
            : Color.FromArgb("#f2ece1");

    private static ContentPage CreateStartupTransitionalPage() => new()
    {
        BackgroundColor = PaperBackground(),
        Content = new VerticalStackLayout
        {
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            Spacing = 16,
            Children =
            {
                new ActivityIndicator { IsRunning = true, Color = Colors.SteelBlue, HorizontalOptions = LayoutOptions.Center },
                new Label { Text = "正在启动…", FontSize = 14, TextColor = Colors.Gray, HorizontalTextAlignment = TextAlignment.Center }
            }
        }
    };

    /// <summary>
    /// 后台恢复会话并切换页面：
    /// 本地会话 → 用户仍启用 → 数据库会话仍 active 且设备匹配 → 恢复并启动心跳；
    /// 网络/数据库不可用时保留本地会话并显示"重试/重新登录"，绝不再把抖动当失效直接清会话。
    /// </summary>
    private static async Task RestoreSessionAndNavigateAsync(Window window, AppOptions appOptions, IWindowTitleService windowTitleService)
    {
        try
        {
            var splashGate = Services.GetRequiredService<ISplashGate>();
            ISessionStore? sessionStore = null;
            UserSession? session = null;
            try
            {
                sessionStore = Services.GetRequiredService<ISessionStore>();
                session = await sessionStore.LoadAsync();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "[APP] 读取会话失败，转登录页");
            }

            // SessionId 为空 = 升级前缓存，无法向数据库校验，直接要求重新登录
            if (session != null && session.SessionId != Guid.Empty)
            {
                // 1) 用户仍存在且启用
                Services.Domain.UserManagement.User? user = null;
                try
                {
                    var userService = Services.GetRequiredService<Services.Domain.UserManagement.IUserService>();
                    var userResult = await userService.GetByIdAsync(session.UserId);
                    if (userResult.IsFailure)
                    {
                        if (IsConnectionFailure(userResult))
                        {
                            Serilog.Log.Warning("[APP] 启动校验无法连接数据库，保留本地会话并提示重试");
                            await ShowConnectionRetryPageAsync(window, appOptions, windowTitleService, splashGate);
                            return;
                        }

                        Serilog.Log.Warning("[APP] 用户校验失败，清除本地会话: {Message}", userResult.Message);
                    }
                    else
                    {
                        user = userResult.Value;
                    }
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "[APP] 启动校验无法连接数据库，保留本地会话并提示重试");
                    await ShowConnectionRetryPageAsync(window, appOptions, windowTitleService, splashGate);
                    return;
                }

                // 2) 数据库会话仍有效且属于本设备
                if (user is { IsActive: true })
                {
                    Services.Domain.UserManagement.SessionValidationResult? validation = null;
                    try
                    {
                        var sessionService = Services.GetRequiredService<Services.Domain.UserManagement.IUserSessionService>();
                        var validationResult = await sessionService.ValidateAsync(session.SessionId, session.DeviceId);
                        if (validationResult.IsFailure)
                        {
                            if (IsConnectionFailure(validationResult))
                            {
                                Serilog.Log.Warning("[APP] 会话校验无法连接数据库，保留本地会话并提示重试");
                                await ShowConnectionRetryPageAsync(window, appOptions, windowTitleService, splashGate);
                                return;
                            }

                            Serilog.Log.Warning("[APP] 会话校验失败，转登录页: {Message}", validationResult.Message);
                        }
                        else
                        {
                            validation = validationResult.Value;
                        }
                    }
                    catch (Exception ex)
                    {
                        Serilog.Log.Warning(ex, "[APP] 会话校验无法连接数据库，保留本地会话并提示重试");
                        await ShowConnectionRetryPageAsync(window, appOptions, windowTitleService, splashGate);
                        return;
                    }

                    if (validation is { IsActive: true })
                    {
                        ApplySession(session);
                        SetCurrentSessionId(session.SessionId);
                        try
                        {
                            await Services.GetRequiredService<ISessionLifecycleService>().StartAsync(session.SessionId);
                        }
                        catch (Exception ex)
                        {
                            // 心跳启动失败不阻断进入首页：下一次启动校验仍会兜底
                            Serilog.Log.Warning(ex, "[APP] 会话心跳启动失败");
                        }

                        await splashGate.WhenCompleted;
                        SwitchToMainPage(window, appOptions, windowTitleService);
                        Serilog.Log.Information("[APP] 会话已恢复: UserId={UserId}", session.UserId);
                        return;
                    }

                    Serilog.Log.Warning("[APP] 会话已失效（{Status}/{Reason}），清除并转登录页",
                        validation?.Status, validation?.EndReason);
                }
                else
                {
                    Serilog.Log.Warning("[APP] 会话用户无效或已停用，清除并转登录页");
                }

                ClearSession();
                if (sessionStore != null)
                    _ = sessionStore.ClearAsync();
            }

            await splashGate.WhenCompleted;
            SwitchToLoginPage(window, appOptions, windowTitleService);
            Serilog.Log.Information("[APP] 启动完成（登录页）");
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "[APP] 后台会话恢复异常");
            window.Page = CreateErrorWindow($"启动初始化失败:\n{ex.Message}").Page;
        }
    }

    /// <summary>连接级失败（网络/超时）：保留本地会话并允许重试，不能当成会话失效</summary>
    private static bool IsConnectionFailure(Models.Results.Result result) =>
        result.ErrorCode is Constants.ErrorCodes.DB_CONNECTION_FAILED
            or Constants.ErrorCodes.DB_TIMEOUT
            or Constants.ErrorCodes.NETWORK_ERROR;

    private static void SwitchToMainPage(Window window, AppOptions appOptions, IWindowTitleService windowTitleService)
    {
        // Android 解析 MobileMainPage 的回退规则单点实现于 PageResolver（与 ViewModelBase/NavigationService 同源）
        var mainPage = PageResolver.Resolve(Services, typeof(Pages.Main.MainPage));

        windowTitleService.Register(mainPage);
        window.Page = new NavigationPage(mainPage);
        window.Title = appOptions.WindowTitle;
    }

    private static void SwitchToLoginPage(Window window, AppOptions appOptions, IWindowTitleService windowTitleService)
    {
        var loginPage = (Page)PageResolver.Resolve(Services, typeof(LoginPage));
        // root 页也注册标题跟随：pop/系统返回回登录页时窗口标题按其 Page.Title 恢复
        windowTitleService.Register(loginPage);
        window.Page = new NavigationPage(loginPage);
        window.Title = $"登录 - {appOptions.WindowTitle}";
    }

    /// <summary>
    /// 启动时数据库不可用：保留本地会话，提供重试与重新登录两条出口。
    /// （历史实现会在这里直接清会话，导致弱网用户每次启动都被迫重新登录）
    /// 注意：先等启动动画播完/跳过再展示，避免动画盖住重试按钮。
    /// </summary>
    private static async Task ShowConnectionRetryPageAsync(Window window, AppOptions appOptions,
        IWindowTitleService windowTitleService, ISplashGate splashGate)
    {
        await splashGate.WhenCompleted;

        var retryButton = new Button { Text = "重试", HorizontalOptions = LayoutOptions.Fill };
        var reloginButton = new Button
        {
            Text = "重新登录",
            HorizontalOptions = LayoutOptions.Fill,
            BackgroundColor = Colors.Transparent,
            TextColor = Colors.Gray
        };

        var page = new ContentPage
        {
            BackgroundColor = PaperBackground(),
            Content = new ScrollView
            {
                Content = new VerticalStackLayout
                {
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Center,
                    Spacing = 16,
                    Padding = new Thickness(40),
                    WidthRequest = 360,
                    Children =
                    {
                        new Label { Text = "暂时无法连接服务器", FontSize = DeviceInfo.Platform == DevicePlatform.Android ? 18 : 20, FontAttributes = FontAttributes.Bold, HorizontalTextAlignment = TextAlignment.Center },
                        new Label
                        {
                            Text = "登录状态已保留。请检查网络（ZeroTier / 4G）后点击重试，无需重新输入密码。",
                            FontSize = DeviceInfo.Platform == DevicePlatform.Android ? 12 : 13,
                            TextColor = Colors.Gray,
                            HorizontalTextAlignment = TextAlignment.Center
                        },
                        retryButton,
                        reloginButton
                    }
                }
            }
        };

        retryButton.Clicked += (_, _) =>
        {
            retryButton.IsEnabled = false;
            _ = RestoreSessionAndNavigateAsync(window, appOptions, windowTitleService);
        };

        reloginButton.Clicked += async (_, _) =>
        {
            reloginButton.IsEnabled = false;
            try
            {
                await Services.GetRequiredService<ISessionStore>().ClearAsync();
            }
            catch { }
            ClearSession();
            SwitchToLoginPage(window, appOptions, windowTitleService);
        };

        windowTitleService.Register(page);
        window.Page = new NavigationPage(page);
        window.Title = $"连接中断 - {appOptions.WindowTitle}";
    }

    private static Window CreateErrorWindow(string message)
    {
        // 错误详情必须可见：Serilog 异步 sink 在进程退出时会丢失未落盘的日志，
        // 若此页不显示 message，启动失败的根因将无从排查（曾因此无法定位初始化异常）
        var errorPage = new ContentPage
        {
            BackgroundColor = PaperBackground(),
            Content = new ScrollView
            {
                Content = new VerticalStackLayout
                {
                    VerticalOptions = LayoutOptions.Center,
                    HorizontalOptions = LayoutOptions.Center,
                    Spacing = 20,
                    Padding = new Thickness(40),
                    Children =
                    {
                        new Label { Text = "启动失败", FontSize = DeviceInfo.Platform == DevicePlatform.Android ? 20 : 24, FontAttributes = FontAttributes.Bold, TextColor = Colors.Red, HorizontalTextAlignment = TextAlignment.Center },
                        new Label { Text = "应用程序出现问题，无法启动...", FontSize = DeviceInfo.Platform == DevicePlatform.Android ? 16 : 18, HorizontalTextAlignment = TextAlignment.Center },
                        new Label { Text = message, FontSize = DeviceInfo.Platform == DevicePlatform.Android ? 12 : 13, TextColor = Colors.DarkRed }
                    }
                }
            }
        };
        return new Window(errorPage);
    }

    public static void ClearCurrentUserId()
    {
        CurrentUserId = 0;
    }

    /// <summary>将持久化会话回填到静态上下文（不含密码）。</summary>
    public static void ApplySession(UserSession session)
    {
        SetCurrentUserId(session.UserId);
        SetCurrentUser(session.UserName, session.FullName, session.Phone, session.OrganizationId);
        SetCurrentUserAddress(session.OrgName, session.CityName, session.CountyName,
            session.TownName, session.VillageName, session.OrgAddress);
    }

    /// <summary>清空静态会话上下文（登出/失效）。持久化清除由调用方处理 ISessionStore.ClearAsync。</summary>
    public static void ClearSession()
    {
        ClearCurrentUserId();
        CurrentSessionId = null;
        CurrentUserName = string.Empty;
        CurrentUserFullName = string.Empty;
        CurrentUserPhone = string.Empty;
        CurrentUserOrganizationId = null;
        CurrentUserOrgName = null;
        CurrentUserCityName = null;
        CurrentUserCountyName = null;
        CurrentUserTownName = null;
        CurrentUserVillageName = null;
        CurrentUserOrgAddress = null;
    }
}

internal static class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hWnd);
}
