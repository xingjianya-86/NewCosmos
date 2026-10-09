using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Options;
using NewCosmos.Services.Core;
using NewCosmos.Services.System;
using NewCosmos.ViewModels.Base;

namespace NewCosmos.ViewModels.Config;

/// <summary>
/// 网络接入向导 ViewModel：选择公共/私有化服务器，安装并接入 ZeroTier，网页授权。
/// </summary>
public partial class NetworkAccessViewModel : ViewModelBase
{
    private readonly INetworkAccessService _networkAccessService;
    private readonly IConfigService _configService;
    private readonly IDialogService _dialogService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerService _logger;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    #region 模式
    public List<string> ModeOptions { get; } = new()
    {
        "私有化服务器（自建控制器）",
        "公共服务器（ZeroTier Central）"
    };

    [ObservableProperty]
    private int _selectedModeIndex;

    public bool IsPublic => SelectedModeIndex == 1;
    public bool IsPrivate => SelectedModeIndex == 0;
    public bool IsPrivateModeVisible => IsPrivate;
    public bool IsPublicModeVisible => IsPublic;

    partial void OnSelectedModeIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsPublic));
        OnPropertyChanged(nameof(IsPrivate));
        OnPropertyChanged(nameof(IsPrivateModeVisible));
        OnPropertyChanged(nameof(IsPublicModeVisible));
        UpdateStatusText();
    }
    #endregion

    #region 配置字段
    [ObservableProperty] private string _publicNetworkId = string.Empty;
    [ObservableProperty] private string _privateNetworkId = string.Empty;
    [ObservableProperty] private string _moonId = string.Empty;
    [ObservableProperty] private string _dbHost = string.Empty;
    [ObservableProperty] private string _dbPortText = "5432";
    [ObservableProperty] private string _webControllerUrl = string.Empty;
    #endregion

    #region 状态
    [ObservableProperty] private bool _installed;
    [ObservableProperty] private string _nodeId = "—";
    [ObservableProperty] private string _version = string.Empty;
    [ObservableProperty] private bool _online;
    [ObservableProperty] private bool _joined;
    [ObservableProperty] private string _networkStatusText = "未检测";

    /// <summary>ZeroTier 管理令牌未授权（首次接入需一次管理员授权）</summary>
    [ObservableProperty] private bool _tokenAccessDenied;

    /// <summary>ZeroTier 分配到的本机 IP（未授权时为空）</summary>
    [ObservableProperty] private string _networkIpText = "—";

    /// <summary>已接入的网络是否已授权（网络状态 OK）</summary>
    [ObservableProperty] private bool _networkAuthorized;

    /// <summary>当前 database.ini 的连接地址（host:port）</summary>
    [ObservableProperty] private string _currentDbEndpointText = "—";
    #endregion

    public string EffectiveNetworkId => IsPublic ? PublicNetworkId?.Trim() ?? "" : PrivateNetworkId?.Trim() ?? "";

    public NetworkAccessViewModel(
        INetworkAccessService networkAccessService,
        IConfigService configService,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider)
    {
        _networkAccessService = networkAccessService;
        _configService = configService;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        Title = "网络接入";

        var opt = _configService.GetNetworkOptions();
        SelectedModeIndex = opt.IsPublic ? 1 : 0;
        PublicNetworkId = opt.PublicNetworkId;
        PrivateNetworkId = opt.PrivateNetworkId;
        MoonId = opt.MoonId;
        DbHost = opt.DbHost;
        DbPortText = opt.DbPort.ToString();
        WebControllerUrl = opt.WebControllerUrl;
        CurrentDbEndpointText = ReadCurrentDbEndpointText();
    }

    private string ReadCurrentDbEndpointText()
    {
        try
        {
            var db = _configService.GetDatabaseOptions();
            return $"{db.Host}:{db.Port}";
        }
        catch
        {
            return "—";
        }
    }

    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        await RefreshStatusAsync();
    }

    private void UpdateStatusText()
    {
        if (!Installed)
        {
            NetworkStatusText = "未检测到 ZeroTier（可点击“安装 ZeroTier”）";
            return;
        }
        if (TokenAccessDenied)
        {
            NetworkStatusText = "ZeroTier 权限未授权：点击“接入网络”将请求一次管理员授权（UAC）";
            return;
        }
        if (string.IsNullOrEmpty(NodeId))
        {
            NetworkStatusText = "ZeroTier 已安装，但未获取到节点信息";
            return;
        }
        if (Joined)
        {
            NetworkStatusText = NetworkAuthorized
                ? (Online
                    ? $"已接入网络 · 已授权（IP {NetworkIpText}，节点 {NodeId}）"
                    : $"已接入网络 · 已授权但离线（IP {NetworkIpText}，节点 {NodeId}）")
                : $"已接入网络 · 等待服务器授权（请在 {WebControllerUrl} 勾选授权本机节点）";
            return;
        }
        NetworkStatusText = $"未接入目标网络（节点 {NodeId}，请在服务器网页端授权）";
    }

    [RelayCommand]
    private async Task RefreshStatusAsync()
    {
        await ExecuteAsync(async () =>
        {
            var result = await _networkAccessService.GetStatusAsync(CancellationToken);
            if (result.IsFailure || result.Value == null)
                return result;

            var s = result.Value;
            Installed = s.Installed;
            NodeId = string.IsNullOrEmpty(s.NodeId) ? "—" : s.NodeId;
            Version = s.Version;
            Online = s.Online;
            TokenAccessDenied = s.TokenAccessDenied;
            var net = s.Networks.FirstOrDefault(n =>
                string.Equals(n.Nwid, EffectiveNetworkId, StringComparison.OrdinalIgnoreCase));
            NetworkIpText = net?.AssignedAddresses.FirstOrDefault() ?? "—";
            NetworkAuthorized = net?.IsOk == true;
            Joined = !string.IsNullOrEmpty(EffectiveNetworkId) && s.IsJoined(EffectiveNetworkId);
            UpdateStatusText();
            return result;
        }, "检测网络状态...");
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
#if ANDROID
        var confirm = await _dialogService.DisplayAlertAsync("安装 ZeroTier",
            "将跳转应用商店安装 ZeroTier One（Android 上组网由该 App 承担）。\n\n" +
            "安装完成后回到本页点击“检测”，再执行接入。是否继续？", "继续", "取消");
#else
        var confirm = await _dialogService.DisplayAlertAsync("安装 ZeroTier",
            "将安装随包分发的 ZeroTier，并授予本机接入权限（需要管理员权限，会弹出 UAC）。是否继续？", "安装", "取消");
#endif
        if (!confirm) return;

        await ExecuteAsync(async () =>
        {
            var result = await _networkAccessService.InstallZeroTierAsync(CancellationToken);
            if (result.IsFailure)
            {
                await _dialogService.DisplayAlertAsync("安装失败", result.Message, "确定");
                return result;
            }
            await _dialogService.ShowSnackBarAsync("ZeroTier 安装完成");
            return result;
        }, "安装 ZeroTier...");

        await RefreshStatusAsync();
    }

    [RelayCommand]
    private async Task JoinAsync()
    {
        var networkId = EffectiveNetworkId;
        if (string.IsNullOrWhiteSpace(networkId))
        {
            await _dialogService.DisplayAlertAsync("提示",
                IsPublic ? "请填写公共服务器（ZeroTier Central）网络ID" : "私有化网络ID未配置", "确定");
            return;
        }

        // 管理令牌未授权：首次接入提权授予当前用户读权限（一次 UAC，之后免授权）
        if (TokenAccessDenied)
        {
            var confirmAccess = await _dialogService.DisplayAlertAsync("需要一次管理员授权",
                "首次接入需要读取 ZeroTier 管理令牌（默认仅管理员可读），将请求一次管理员授权（UAC）。是否继续？",
                "继续", "取消");
            if (!confirmAccess) return;
        }

        var synced = false;
        await ExecuteAsync(async () =>
        {
            var result = await _networkAccessService.JoinNetworkAsync(
                networkId, IsPublic ? null : MoonId, CancellationToken);
            if (result.IsFailure)
            {
#if ANDROID
                // Android 上加入网络由 ZeroTier One App 承担：这里返回的是"已跳转去操作"的指引，不是失败
                await _dialogService.DisplayAlertAsync("请在 ZeroTier One 中完成接入", result.Message, "确定");
#else
                await _dialogService.DisplayAlertAsync("接入失败", result.Message, "确定");
#endif
                return result;
            }

            SaveCurrent();

            // 静默同步数据库地址：私有化模式下将 database.ini 切到 network.ini 的 ZeroTier 地址
            synced = TrySyncDatabaseEndpoint();

            await _dialogService.DisplayAlertAsync("已提交接入",
                $"已加入网络 {networkId}。\n\n请在服务器网页端授权本机节点：\n" +
                $"节点ID：{NodeId}\n授权地址：{WebControllerUrl}" +
                (synced ? $"\n\n数据库地址已自动更新为 {DbHost}:{DbPortText}。" : ""), "确定");
            return result;
        }, "接入网络...");

        await RefreshStatusAsync();

        if (synced)
        {
            await _dialogService.DisplayAlertAsync("需要重启应用",
                $"数据库地址已更新为 {DbHost}:{DbPortText}。\n\n点击“确定”立即重启应用使配置生效。", "确定");
            AppRestartHelper.Restart();
            Application.Current?.Quit();
        }
    }

    [RelayCommand]
    private async Task LeaveAsync()
    {
        var networkId = EffectiveNetworkId;
        if (string.IsNullOrWhiteSpace(networkId)) return;

        var confirm = await _dialogService.DisplayAlertAsync("退出网络",
            $"确定退出网络 {networkId} 吗？", "退出", "取消");
        if (!confirm) return;

        await ExecuteAsync(async () =>
        {
            var result = await _networkAccessService.LeaveNetworkAsync(networkId, CancellationToken);
            if (result.IsFailure)
            {
                await _dialogService.DisplayAlertAsync("退出失败", result.Message, "确定");
                return result;
            }
            await _dialogService.ShowSnackBarAsync("已退出网络");
            return result;
        }, "退出网络...");

        await RefreshStatusAsync();
    }

    [RelayCommand]
    private async Task OpenWebAsync()
    {
        var result = await _networkAccessService.OpenAuthorizationPageAsync(WebControllerUrl, CancellationToken);
        if (result.IsFailure)
            await _dialogService.DisplayAlertAsync("提示", result.Message, "确定");
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        SaveCurrent();
        await _dialogService.ShowSnackBarAsync("网络配置已保存到 config\\network.ini");
    }

    [RelayCommand]
    private async Task SyncDbEndpointAsync()
    {
        if (!IsPrivate)
        {
            await _dialogService.DisplayAlertAsync("提示", "仅私有化服务器模式需要同步数据库地址", "确定");
            return;
        }

        SaveCurrent();
        if (!TrySyncDatabaseEndpoint())
        {
            await _dialogService.ShowSnackBarAsync("数据库地址无需变更");
            return;
        }

        var restart = await _dialogService.DisplayAlertAsync("需要重启应用",
            $"数据库地址已更新为 {DbHost}:{DbPortText}。\n\n点击“确定”立即重启应用使配置生效。", "确定", "稍后");
        if (restart)
        {
            AppRestartHelper.Restart();
            Application.Current?.Quit();
        }
    }

    /// <summary>
    /// 私有化模式下把 database.ini 的主机/端口同步为 network.ini 的 ZeroTier 地址；有变更返回 true
    /// </summary>
    private bool TrySyncDatabaseEndpoint()
    {
        if (!IsPrivate) return false;

        var host = DbHost?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(host)) return false;
        var port = int.TryParse(DbPortText, out var p) && p > 0 ? p : 5432;

        try
        {
            var db = _configService.GetDatabaseOptions();
            if (string.Equals(db.Host?.Trim(), host, StringComparison.OrdinalIgnoreCase) && db.Port == port)
                return false;

            _configService.UpdateDatabaseEndpoint(host, port);
            CurrentDbEndpointText = $"{host}:{port}";
            _logger.Info($"已自动同步数据库地址: {host}:{port}（原 {db.Host}:{db.Port}）");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "自动同步数据库地址失败");
            return false;
        }
    }

    private void SaveCurrent()
    {
        var current = _configService.GetNetworkOptions();
        current.Mode = IsPublic ? NetworkOptions.ModePublic : NetworkOptions.ModePrivate;
        current.PublicNetworkId = PublicNetworkId?.Trim() ?? string.Empty;
        current.PrivateNetworkId = PrivateNetworkId?.Trim() ?? string.Empty;
        current.MoonId = MoonId?.Trim() ?? string.Empty;
        current.DbHost = DbHost?.Trim() ?? string.Empty;
        current.DbPort = int.TryParse(DbPortText, out var p) ? p : 5432;
        current.WebControllerUrl = WebControllerUrl?.Trim() ?? string.Empty;
        _configService.SaveNetworkOptions(current);
    }
}
