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

    private static void LogCrashException(Exception ex, string source)
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

            var windowTitleService = Services.GetRequiredService<IWindowTitleService>();

            // 尝试恢复持久化会话（SecureStorage 同步阻塞读取，避开 UI 同步上下文）
            ISessionStore? sessionStore = null;
            UserSession? session = null;
            try
            {
                sessionStore = Services.GetRequiredService<ISessionStore>();
                session = Task.Run(() => sessionStore.LoadAsync()).GetAwaiter().GetResult();
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
                    var userResult = Task.Run(() => userService.GetByIdAsync(session.UserId)).GetAwaiter().GetResult();
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
                        var mainWindow = new Window(new NavigationPage(mainPage))
                        {
                            Title = appOptions.WindowTitle
                        };
                        if (DeviceInfo.Platform != DevicePlatform.Android)
                        {
                            mainWindow.Width = 1920;
                            mainWindow.Height = 1080;
                            mainWindow.X = 0;
                            mainWindow.Y = 0;
                        }
                        Serilog.Log.Information("[APP] CreateWindow 结束（会话恢复）");
                        return mainWindow;
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
            var navigationPage = new NavigationPage(loginPage);
            var loginWindow = new Window(navigationPage) { Title = $"登录 - {appOptions.WindowTitle}" };

            // 设置窗口全屏
            loginWindow.Width = 1920;
            loginWindow.Height = 1080;
            loginWindow.X = 0;
            loginWindow.Y = 0;

            Serilog.Log.Information("[APP] CreateWindow 结束");
            return loginWindow;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "[APP] CreateWindow 初始化失败");
            return CreateErrorWindow($"应用程序初始化失败:\n{ex.Message}\n\n堆栈跟踪:\n{ex.StackTrace}");
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
