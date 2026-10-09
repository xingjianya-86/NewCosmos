using Microsoft.Maui;
using NewCosmos.Helpers;
using NewCosmos.Models.Options;
using NewCosmos.Models.Session;
using NewCosmos.Services.Core;
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
            Serilog.Log.Information("[APP] 应用配置加载完成，版本: {Version}, B线结算日: {SettleDay}",
                appOptions.Version, BusinessCycleHelper.SettleDay);

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
            var window = new Window(CreateStartupTransitionalPage()) { Title = appOptions.WindowTitle };
            if (DeviceInfo.Platform != DevicePlatform.Android)
            {
                window.Width = 1920;
                window.Height = 1080;
                window.X = 0;
                window.Y = 0;
            }

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

    private static ContentPage CreateStartupTransitionalPage() => new()
    {
        BackgroundColor = Colors.White,
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
    /// 后台恢复会话并切换到主页面或登录页；失败一律落登录页，异常记日志+错误页兜底，绝不吞掉。
    /// </summary>
    private static async Task RestoreSessionAndNavigateAsync(Window window, AppOptions appOptions, IWindowTitleService windowTitleService)
    {
        try
        {
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

            if (session != null)
            {
                // 校验用户仍存在且启用；失败则清会话回登录页
                try
                {
                    var userService = Services.GetRequiredService<Services.Domain.UserManagement.IUserService>();
                    var userResult = await userService.GetByIdAsync(session.UserId);
                    if (userResult.IsSuccess && userResult.Value != null && userResult.Value.IsActive)
                    {
                        ApplySession(session);
                        Serilog.Log.Information("[APP] 会话已恢复: UserId={UserId}", session.UserId);

                        Page mainPage;
                        if (DeviceInfo.Platform == DevicePlatform.Android)
                        {
                            var mobileType = typeof(Pages.Main.MainPage).Assembly.GetType("NewCosmos.Pages.Mobile.MobileMainPage");
                            mainPage = mobileType != null
                                ? (Services.GetService(mobileType) as Page) ?? Services.GetRequiredService<Pages.Main.MainPage>()
                                : Services.GetRequiredService<Pages.Main.MainPage>();
                        }
                        else
                        {
                            mainPage = Services.GetRequiredService<Pages.Main.MainPage>();
                        }

                        windowTitleService.Register(mainPage);
                        window.Page = new NavigationPage(mainPage);
                        window.Title = appOptions.WindowTitle;
                        Serilog.Log.Information("[APP] 启动完成（会话恢复）");
                        return;
                    }

                    Serilog.Log.Warning("[APP] 会话用户无效，清除并转登录页");
                    ClearSession();
                    if (sessionStore != null)
                        _ = sessionStore.ClearAsync();
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "[APP] 会话校验失败，转登录页");
                    ClearSession();
                }
            }

            var loginPage = Services.GetRequiredService<LoginPage>();
            // root 页也注册标题跟随：pop/系统返回回登录页时窗口标题按其 Page.Title 恢复
            windowTitleService.Register(loginPage);
            window.Page = new NavigationPage(loginPage);
            window.Title = $"登录 - {appOptions.WindowTitle}";
            Serilog.Log.Information("[APP] 启动完成（登录页）");
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "[APP] 后台会话恢复异常");
            window.Page = CreateErrorWindow($"启动初始化失败:\n{ex.Message}").Page;
        }
    }

    private static Window CreateErrorWindow(string message)
    {
        // 错误详情必须可见：Serilog 异步 sink 在进程退出时会丢失未落盘的日志，
        // 若此页不显示 message，启动失败的根因将无从排查（曾因此无法定位初始化异常）
        var errorPage = new ContentPage
        {
            BackgroundColor = Colors.White,
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
                        new Label { Text = "启动失败", FontSize = 24, FontAttributes = FontAttributes.Bold, TextColor = Colors.Red, HorizontalTextAlignment = TextAlignment.Center },
                        new Label { Text = "应用程序出现问题，无法启动...", FontSize = 18, HorizontalTextAlignment = TextAlignment.Center },
                        new Label { Text = message, FontSize = 13, TextColor = Colors.DarkRed }
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
