using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using NewCosmos.Constants;
using NewCosmos.Models.Options;
using NewCosmos.Models.Session;
using NewCosmos.Services.Core;
using NewCosmos.Navigation;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.System;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.Auth;

public partial class LoginViewModel : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IUserService _userService;
    private readonly IBackgroundLoaderService _backgroundLoader;
    private readonly IConfigService _configService;
    private readonly ILoggerService _logger;
    private readonly IDialogService _dialogService;
    private readonly IWindowTitleService _windowTitleService;
    private readonly IUpdateService _updateService;
    private readonly INetworkAccessService _networkAccessService;
    private readonly INavigationService _navigationService;
    private readonly PreferencesOptions _preferencesOptions;

    [ObservableProperty]
    private string _username = string.Empty;

    [ObservableProperty]
    private string _password = string.Empty;

    [ObservableProperty]
    private string _currentBackgroundImage = string.Empty;

    [ObservableProperty]
    private bool _rememberMe = true;

    [ObservableProperty]
    private bool _rememberPassword;

    public ObservableCollection<string> BackgroundImages { get; } = new();

    public LoginViewModel(
        IServiceProvider serviceProvider,
        IUserService userService, 
        IBackgroundLoaderService backgroundLoader,
        IConfigService configService,
        ILoggerService logger,
        IDialogService dialogService,
        IWindowTitleService windowTitleService,
        IUpdateService updateService,
        INetworkAccessService networkAccessService,
        INavigationService navigationService)
    {
        _serviceProvider = serviceProvider;
        _userService = userService;
        _backgroundLoader = backgroundLoader;
        _configService = configService;
        _logger = logger;
        _dialogService = dialogService;
        _windowTitleService = windowTitleService;
        _updateService = updateService;
        _networkAccessService = networkAccessService;
        _navigationService = navigationService;
        _preferencesOptions = _configService.GetPreferencesOptions();

        LoadBackgroundImages();
        SelectRandomBackground();
        // 移除构造函数中的异步调用，改为页面加载时调用
    }

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    /// <summary>
    /// 登录窗前静默检查更新：强制更新必须完成才能继续；更新源不可达时提示后放行（避免断网锁死业务）。
    /// 失败仅记日志，不影响登录。
    /// </summary>
    public async Task CheckForUpdatesOnStartupAsync()
    {
        try
        {
            var options = _configService.GetUpdateOptions();
            if (!options.Enabled || !options.CheckOnStartup) return;

            var check = await _updateService.CheckAsync(manual: false);
            if (check.IsFailure)
            {
                _logger.Warn($"启动更新检查失败（放行）: {check.Message}");
                return;
            }

            var result = check.Value;
            var info = result.Info;
            if (!result.UpdateAvailable || info == null) return;

            // 非强制版本：用户可跳过当前版本
            if (!result.ForceUpdate
                && string.Equals(_updateService.GetSkippedVersion(), info.LatestVersion, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var message = BuildUpdateMessage(info);
            if (result.ForceUpdate)
            {
                var proceedForce = await _dialogService.DisplayAlertAsync(
                    "必须更新",
                    message + "\n\n本次为强制更新，完成后才能继续使用系统。",
                    "立即更新", "退出程序");
                if (!proceedForce)
                {
                    _logger.LogSecurity("用户放弃强制更新，退出程序",
                        ("CurrentVersion", _updateService.CurrentVersion), ("LatestVersion", info.LatestVersion));
                    Application.Current?.Quit();
                    return;
                }
            }
            else
            {
                var choice = await _dialogService.DisplayActionSheetAsync("发现新版本", "稍后", null, "立即更新", "跳过此版本");
                if (choice == "跳过此版本")
                {
                    _updateService.SetSkippedVersion(info.LatestVersion);
                    return;
                }
                if (choice != "立即更新") return;
            }

            await DownloadAndInstallAsync(info);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动更新检查异常（放行）");
        }
    }

    private string BuildUpdateMessage(UpdateInfo info)
    {
        var sizeText = info.PackageSize > 0 ? $"{info.PackageSize / 1024d / 1024d:F0} MB" : "未知大小";
        var notes = string.IsNullOrWhiteSpace(info.Notes) ? string.Empty : $"\n\n更新说明：\n{info.Notes}";
        return $"当前版本：{_updateService.CurrentVersion}\n最新版本：{info.LatestVersion}（{sizeText}）{notes}";
    }

    private async Task DownloadAndInstallAsync(UpdateInfo info)
    {
        try
        {
            IsBusy = true;
            LoadingMessage = "正在下载更新包...";

            var download = await _updateService.DownloadAsync(info,
                p => LoadingMessage = $"正在下载更新包... {p:F0}%");
            if (download.IsFailure)
            {
                await _dialogService.DisplayAlertAsync("更新失败",
                    $"下载失败：{download.Message}\n\n可继续使用当前版本，或联系管理员手动安装。", "确定");
                return;
            }

            LoadingMessage = "正在启动更新程序...";
            var launch = _updateService.LaunchInstaller(download.Value, info.LatestVersion);
            if (launch.IsFailure)
            {
                await _dialogService.DisplayAlertAsync("更新失败", $"无法启动安装程序：{launch.Message}", "确定");
                return;
            }

            // 安装器接管：/UPDATE=1 静默升级完成后自动重启新版
            Application.Current?.Quit();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadBackgroundImages()
    {
        var backgrounds = _backgroundLoader.GetBackgroundImages();
        
        BackgroundImages.Clear();
        foreach (var bg in backgrounds)
        {
            BackgroundImages.Add(bg);
        }
        
        _logger.Info("已加载背景图片列表");
    }

    private void SelectRandomBackground()
    {
        try
        {
            var uiOptions = _configService.GetUIOptions();
            
            var lastUserBg = _backgroundLoader.GetLastUserBackground();
            if (!string.IsNullOrEmpty(lastUserBg))
            {
                CurrentBackgroundImage = lastUserBg;
                _logger.Info("已应用用户上次选择的背景");
                return;
            }
            
            var backgrounds = _backgroundLoader.GetBackgroundImages();
            
            if (backgrounds.Count == 0)
            {
                CurrentBackgroundImage = _backgroundLoader.GetDefaultBackgroundImage();
                _logger.Warn("背景图片列表为空，使用默认背景");
                return;
            }
            
            if (uiOptions.UseRandomBackground)
            {
                var random = new Random();
                CurrentBackgroundImage = backgrounds[random.Next(backgrounds.Count)];
                _logger.Info("已随机选择背景图片");
            }
            else
            {
                CurrentBackgroundImage = _backgroundLoader.GetDefaultBackgroundImage();
                _logger.Info("已使用默认背景图片");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "选择背景图片失败");
        }
    }

    [RelayCommand]
    private void ChangeBackground()
    {
        try
        {
            var backgrounds = _backgroundLoader.GetBackgroundImages();
            
            if (backgrounds.Count == 0)
                return;
            
            var currentIndex = backgrounds.IndexOf(CurrentBackgroundImage);
            
            if (currentIndex == -1)
                currentIndex = 0;
            
            var nextIndex = (currentIndex + 1) % backgrounds.Count;
            CurrentBackgroundImage = backgrounds[nextIndex];
            
            _backgroundLoader.SaveUserBackground(CurrentBackgroundImage);
            
            _logger.Info("已切换背景图片");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "切换背景图片失败");
        }
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "用户名和密码不能为空";
            return;
        }

        IsBusy = true;
        ErrorMessage = string.Empty;

        try
        {
            var result = await _userService.AuthenticateAsync(Username, Password);

            if (result.IsSuccess)
            {
                _logger.LogSecurity("登录成功", ("UserId", result.Value.Id), ("UserName", Username));

                await SaveRememberSettingsAsync();

                App.SetCurrentUserId(result.Value.Id);
                App.SetCurrentUser(
                    result.Value.Username ?? Username,
                    result.Value.FullName ?? result.Value.Username ?? Username,
                    result.Value.Phone ?? string.Empty,
                    result.Value.OrganizationId);

                // 加载组织地址信息到 App 静态缓存
                if (result.Value.OrganizationId.HasValue)
                {
                    try
                    {
                        var orgService = _serviceProvider.GetRequiredService<IOrganizationService>();
                        var orgResult = await orgService.GetByIdAsync(result.Value.OrganizationId.Value);
                        if (orgResult.IsSuccess && orgResult.Value != null)
                        {
                            var org = orgResult.Value;
                            App.SetCurrentUserAddress(
                                org.Name, org.CityName, org.CountyName,
                                org.TownName, org.VillageName, org.Address);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "加载组织地址信息失败");
                    }
                }

                // 持久化会话（不含密码，7 天有效期），进程重启后可免登录恢复
                try
                {
                    await _serviceProvider.GetRequiredService<ISessionStore>().SaveAsync(new UserSession
                    {
                        UserId = result.Value.Id,
                        UserName = result.Value.Username ?? Username,
                        FullName = result.Value.FullName ?? result.Value.Username ?? Username,
                        Phone = result.Value.Phone ?? string.Empty,
                        OrganizationId = result.Value.OrganizationId,
                        OrgName = App.CurrentUserOrgName,
                        CityName = App.CurrentUserCityName,
                        CountyName = App.CurrentUserCountyName,
                        TownName = App.CurrentUserTownName,
                        VillageName = App.CurrentUserVillageName,
                        OrgAddress = App.CurrentUserOrgAddress,
                        LoginAtUtc = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    _logger.Warn($"保存登录会话失败（不影响本次登录）: {ex.Message}");
                }

                if (Helpers.WindowNavigator.CurrentPage != null)
                {
                    // 设置导航根：桌面 MainPage / Android MobileMainPage 回退在 NavigationService 注册表内处理；
                    // 主首页是 NavigationPage 的 root（不经 push），必须显式注册标题跟随，
                    // 否则从模块页点系统返回按钮回主首页时标题残留（实测 BUG）
                    await _navigationService.SetRootAsync(NavigationKeys.Main);

                    _windowTitleService.SetLoginTitle();
                }
            }
            else
            {
                ErrorMessage = result.Message ?? "登录失败";
                _logger.LogSecurity("登录失败", ("UserName", Username), ("Reason", ErrorMessage));

                // 连接类失败：自动诊断 ZeroTier 地址不一致/未授权等原因并提示修复
                if (result.ErrorCode == ErrorCodes.DB_CONNECTION_FAILED
                    || result.ErrorCode == ErrorCodes.NETWORK_ERROR)
                {
                    await HandleConnectionFailureAsync();
                }
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = "系统错误，请稍后重试";
            _logger.LogError(ex, "登录失败");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 连接失败自动诊断：识别"数据库地址与 ZeroTier 私有网络不一致 / 未接入 / 未授权"等原因，
    /// 地址不一致时提供一键切换并重启；其余情况给出针对性指引。
    /// </summary>
    private async Task HandleConnectionFailureAsync()
    {
        try
        {
            var network = _configService.GetNetworkOptions();

            var dbHost = "—";
            var dbPort = 0;
            try
            {
                var db = _configService.GetDatabaseOptions();
                dbHost = db.Host;
                dbPort = db.Port;
            }
            catch { /* 配置缺失时保持占位 */ }

            var statusResult = await _networkAccessService.GetStatusAsync();
            var status = statusResult.IsSuccess ? statusResult.Value : null;
            var netState = status?.Networks.FirstOrDefault(n =>
                string.Equals(n.Nwid, network.EffectiveNetworkId, StringComparison.OrdinalIgnoreCase));
            var joined = netState != null;
            var authorized = netState?.IsOk == true;
            var assignedIp = netState?.AssignedAddresses.FirstOrDefault() ?? "—";

            // 数据库地址是否属于 ZeroTier 私有网络（不再写死 "10.110." 网段——自建控制器可任意规划）：
            // ① 与 network.ini 配置的私有化数据库地址一致；
            // ② 命中本机已获分配的任一 ZeroTier 地址；
            // ③ 私有化模式已配置网络ID，且地址是回环以外的内网地址（该部署形态下基本即 ZT 段）。
            var isZeroTierAddress =
                IsConfiguredZeroTierHost(network, dbHost)
                || IsAssignedZeroTierAddress(status, dbHost)
                || (network.IsPublic == false
                    && !string.IsNullOrWhiteSpace(network.EffectiveNetworkId)
                    && IsPrivateNonLoopbackAddress(dbHost));

            // 1) 私有化模式、已接入但 database.ini 地址与网络地址不一致 → 一键切换并重启
            if (!network.IsPublic && joined
                && !string.IsNullOrWhiteSpace(network.DbHost)
                && !string.Equals(dbHost?.Trim(), network.DbHost.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                var fix = await _dialogService.DisplayAlertAsync("数据库地址不一致",
                    $"当前数据库地址：{dbHost}:{dbPort}\n" +
                    $"私有化网络地址：{network.DbHost}:{network.DbPort}\n\n" +
                    $"点击“确定”自动切换为 {network.DbHost}:{network.DbPort} 并重启应用。",
                    "确定", "取消");
                if (fix)
                {
                    _configService.UpdateDatabaseEndpoint(network.DbHost.Trim(), network.DbPort);
                    Helpers.AppRestartHelper.Restart();
                    Application.Current?.Quit();
                }
                return;
            }

            // 2) 已接入但未授权（无分配 IP / 网络状态非 OK）
            if (!network.IsPublic && joined && !authorized)
            {
                await _dialogService.DisplayAlertAsync("等待服务器授权",
                    $"ZeroTier 已接入（节点 IP：{assignedIp}），但尚未获得服务器授权，数据网络不通。\n\n" +
                    $"请登录 {network.WebControllerUrl} 授权本机节点后重试。", "确定");
                return;
            }

            // 3) 地址是 ZeroTier 地址但未接入 → 引导去网络接入页
            if (!network.IsPublic && !joined && isZeroTierAddress)
            {
                await _dialogService.DisplayAlertAsync("数据库连接失败",
                    "当前数据库地址是 ZeroTier 私有网络地址，但本机尚未接入该网络。\n\n" +
                    "请到“配置向导 → 网络接入”完成接入并授权。", "确定");
                return;
            }

            await _dialogService.DisplayAlertAsync("数据库连接失败",
                $"无法连接数据库 {dbHost}:{dbPort}。\n\n请检查服务器运行状态与网络连接后重试。", "确定");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "数据库连接失败自动诊断异常");
        }
    }

    /// <summary>数据库地址是否就是 network.ini 中配置的私有化 ZeroTier 地址</summary>
    private static bool IsConfiguredZeroTierHost(NetworkOptions network, string? dbHost) =>
        !string.IsNullOrWhiteSpace(network.DbHost)
        && !string.IsNullOrWhiteSpace(dbHost)
        && string.Equals(dbHost.Trim(), network.DbHost.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>数据库地址是否落在本机已获 ZeroTier 分配的任一地址上</summary>
    private static bool IsAssignedZeroTierAddress(ZeroTierStatus? status, string? dbHost)
    {
        if (string.IsNullOrWhiteSpace(dbHost) || status?.Networks == null) return false;
        var target = dbHost.Trim();
        return status.Networks
            .Where(n => n.AssignedAddresses != null)
            .SelectMany(n => n.AssignedAddresses)
            .Any(a => string.Equals(a?.Trim(), target, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>回环以外的内网地址（10./172.16-31./192.168.）——私有化部署下基本即 ZeroTier 段</summary>
    private static bool IsPrivateNonLoopbackAddress(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        var h = host.Trim();
        if (h.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || h.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || h.Equals("::1", StringComparison.OrdinalIgnoreCase)
            || h.Equals("0.0.0.0", StringComparison.OrdinalIgnoreCase))
            return false;

        var parts = h.Split('.');
        if (parts.Length != 4) return false;
        if (!byte.TryParse(parts[0], out var first) || !byte.TryParse(parts[1], out var second)) return false;

        return first == 10
            || (first == 192 && second == 168)
            || (first == 172 && second >= 16 && second <= 31);
    }

    [RelayCommand]
    private async Task LoadRememberSettingsAsync()
    {
        try
        {
            RememberMe = Preferences.Get(_preferencesOptions.RememberMeKey, true);

            if (RememberMe)
            {
                Username = Preferences.Get(_preferencesOptions.RememberedUsernameKey, string.Empty);
                RememberPassword = Preferences.Get(_preferencesOptions.RememberPasswordKey, false);
                if (RememberPassword)
                {
                    try
                    {
                        var savedPassword = await SecureStorage.GetAsync("remembered_password");
                        if (!string.IsNullOrEmpty(savedPassword))
                            Password = savedPassword;
                    }
                    catch { }
                }
            }

            _logger.Info($"加载记住设置: RememberMe={RememberMe}");
        }
        catch (Exception ex)
        {
            _logger.Warn($"加载记住设置失败: {ex.Message}");
        }
    }

    private async Task SaveRememberSettingsAsync()
    {
        try
        {
            Preferences.Set(_preferencesOptions.RememberMeKey, RememberMe);

            if (RememberMe)
            {
                Preferences.Set(_preferencesOptions.RememberedUsernameKey, Username);
                Preferences.Set(_preferencesOptions.RememberPasswordKey, RememberPassword);
                if (RememberPassword && !string.IsNullOrEmpty(Password))
                    await SecureStorage.SetAsync("remembered_password", Password);
                else
                    SecureStorage.Remove("remembered_password");
            }
            else
            {
                Preferences.Remove(_preferencesOptions.RememberedUsernameKey);
                Preferences.Remove(_preferencesOptions.RememberPasswordKey);
                SecureStorage.Remove("remembered_password");
            }

            _logger.Info("已保存记住设置");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "保存记住设置失败");
        }
    }

    [RelayCommand]
    private void SwitchBackground()
    {
        ChangeBackground();
    }
}