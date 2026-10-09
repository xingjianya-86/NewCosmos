using System.Net.NetworkInformation;
using System.Net.Sockets;
using NewCosmos.Models.Options;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;

namespace NewCosmos.Services.System;

/// <summary>
/// Android 网络接入服务。
/// ZeroTier 在 Android 上由官方 ZeroTier One App（VpnService）承载，没有 CLI、也不能静默加入网络；
/// 因此本服务做**真实探测**而非空实现：
///   ① ZeroTier One App 是否已安装（需 AndroidManifest 的 &lt;queries&gt; 声明，否则包可见性被系统屏蔽）
///   ② ZeroTier TUN 网卡是否存在（接口名形如 zt&lt;节点ID&gt;，为 ZeroTier 在 Linux/Android 上的固定命名）
///   ③ 隧道建立后，对配置的数据库地址做 TCP 可达性探测，区分"已接入可用"与"已接入待授权"
/// 加入 / 退出 / 授权一律跳转 ZeroTier One App 或网页端完成，本服务不代劳。
/// </summary>
public class AndroidNetworkAccessService : BaseService, INetworkAccessService
{
    protected override string ServiceName => "AndroidNetworkAccessService";

    private const string ZeroTierPackage = "net.zerotier.one";

    private readonly IConfigService _config;

    public AndroidNetworkAccessService(ILoggerService logger, IConfigService config) : base(logger)
    {
        _config = config;
    }

    public async Task<Result<ZeroTierStatus>> GetStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var status = new ZeroTierStatus();

            var pkg = QueryZeroTierPackage();
            status.Installed = pkg.Installed;
            status.Version = pkg.Version;

            var iface = FindZeroTierInterface();
            if (iface != null)
            {
                status.Online = true;
                // TUN 接口名 = "zt" + 节点ID（10 位十六进制）
                var name = iface.Name;
                if (name.StartsWith("zt", StringComparison.OrdinalIgnoreCase) && name.Length > 2)
                    status.NodeId = name[2..];
            }

            // 隧道真的起来了才合成网络条目：否则 UI 会误判为"已接入"
            if (iface != null && TryGetTargetNetworkId(out var networkId, out var netOptions))
            {
                var localIp = GetInterfaceAddress(iface);
                if (!string.IsNullOrEmpty(localIp))
                    status.Networks.Add(new ZeroTierNetworkState
                    {
                        Nwid = networkId,
                        Name = "ZeroTier One App 管理",
                        Type = "PRIVATE",
                        Device = iface.Name,
                        AssignedAddresses = new List<string> { localIp },
                        // 隧道通 + 数据库端口通 = 已授权可用；只通隧道 = 待授权（或服务器未就绪）
                        Status = await ProbeDatabaseAsync(netOptions, ct) ? "OK" : "REQUESTING_CONFIGURATION",
                    });
            }

            LogDebug($"ZeroTier 探测: 安装={status.Installed} 在线={status.Online} 节点={status.NodeId} 网络数={status.Networks.Count}");
            return Result.Success(status);
        }
        catch (Exception ex)
        {
            LogException(ex, "ZeroTier 状态探测失败");
            return Result.FromException<ZeroTierStatus>(ex);
        }
    }

    public Task<Result> InstallZeroTierAsync(CancellationToken ct = default)
    {
        // Android 不允许静默安装第三方 APK：跳转应用商店，由用户自行安装
        var opened = OpenStoreOrWeb(ZeroTierPackage);
        return Task.FromResult(opened
            ? Result.Failure("ZT_INSTALL_MANUAL",
                "已打开 ZeroTier One 下载页面。安装完成后回到本页点击“检测”，再执行接入。")
            : Result.Failure("ZT_INSTALL_FAILED",
                "无法打开应用商店，请在应用市场手动搜索安装 ZeroTier One。"));
    }

    public Task<Result> EnsureCliAccessAsync(CancellationToken ct = default)
    {
        // Android 无 zerotier-cli，也就不存在"管理令牌读权限"问题：直接成功，避免调用方被误拦
        return Task.FromResult(Result.Success());
    }

    public Task<Result> JoinNetworkAsync(string networkId, string? moonId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(networkId))
            return Task.FromResult(Result.Failure("ZT_NO_NETWORK_ID", "未配置 ZeroTier 网络ID"));

        var launched = LaunchZeroTierApp();
        return Task.FromResult(launched
            ? Result.Failure("ZT_JOIN_MANUAL",
                $"已打开 ZeroTier One。请在其中加入网络 {networkId} 并开启连接，" +
                "然后回到服务器网页端授权本机节点，最后返回本页点击“检测”。")
            : Result.Failure("ZT_JOIN_MANUAL",
                $"未安装 ZeroTier One，无法加入网络 {networkId}。请先安装后在其中加入该网络。"));
    }

    public Task<Result> LeaveNetworkAsync(string networkId, CancellationToken ct = default)
    {
        var launched = LaunchZeroTierApp();
        return Task.FromResult(launched
            ? Result.Failure("ZT_LEAVE_MANUAL",
                $"已打开 ZeroTier One，请在其中断开网络 {networkId}。")
            : Result.Failure("ZT_LEAVE_MANUAL",
                $"未安装 ZeroTier One，无法断开网络 {networkId}。"));
    }

    public async Task<Result> OpenAuthorizationPageAsync(string url, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return Result.Failure("ZT_NO_AUTH_URL", "未配置网页授权地址");

        try
        {
            var ok = await Microsoft.Maui.ApplicationModel.Launcher.Default.OpenAsync(url);
            if (!ok)
                return Result.Failure("ZT_OPEN_URL_FAILED", "无法打开授权页面，请检查浏览器是否可用");
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "打开 ZeroTier 授权页面失败");
            return Result.FromException(ex);
        }
    }

    #region 探测实现

    /// <summary>读取 ZeroTier One 包信息（Android 11+ 需 Manifest &lt;queries&gt;，否则一律查不到）</summary>
    private (bool Installed, string Version) QueryZeroTierPackage()
    {
        try
        {
            var context = Android.App.Application.Context;
            var pm = context.PackageManager;
            if (pm == null) return (false, string.Empty);

            var info = pm.GetPackageInfo(ZeroTierPackage, 0);
            if (info == null) return (false, string.Empty);
            return (true, info.VersionName ?? string.Empty);
        }
        catch (Java.Lang.Exception)
        {
            // NameNotFoundException：未安装（在无 <queries> 时也会走到这里）
            return (false, string.Empty);
        }
        catch (Exception ex)
        {
            LogWarn($"查询 ZeroTier One 包信息失败: {ex.Message}");
            return (false, string.Empty);
        }
    }

    /// <summary>查找 ZeroTier TUN 网卡（接口名 zt*，且处于 Up 状态）</summary>
    private static NetworkInterface? FindZeroTierInterface()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n =>
                    n.Name.StartsWith("zt", StringComparison.OrdinalIgnoreCase)
                    && n.OperationalStatus == OperationalStatus.Up);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>取 TUN 网卡上的第一个 IPv4 地址（即本机在 ZeroTier 网络中的 IP）</summary>
    private static string GetInterfaceAddress(NetworkInterface iface)
    {
        try
        {
            return iface.GetIPProperties().UnicastAddresses
                .Select(a => a.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private bool TryGetTargetNetworkId(out string networkId, out NetworkOptions netOptions)
    {
        netOptions = _config.GetNetworkOptions();
        networkId = netOptions.EffectiveNetworkId;
        return !string.IsNullOrWhiteSpace(networkId);
    }

    /// <summary>对配置的数据库地址做一次 TCP 可达性探测（隧道已建立时才有意义）</summary>
    private async Task<bool> ProbeDatabaseAsync(NetworkOptions net, CancellationToken ct)
    {
        var host = net.DbHost?.Trim();
        if (string.IsNullOrWhiteSpace(host) || host == "127.0.0.1" || host == "localhost")
            return false;

        var port = net.DbPort > 0 ? net.DbPort : 5432;
        try
        {
            using var client = new TcpClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            await client.ConnectAsync(host, port, timeout.Token);
            LogInfo($"数据库可达性探测成功: {host}:{port}");
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogDebug($"数据库可达性探测失败: {host}:{port} - {ex.Message}");
            return false;
        }
    }

    /// <summary>启动 ZeroTier One App；未安装返回 false</summary>
    private bool LaunchZeroTierApp()
    {
        try
        {
            var context = Android.App.Application.Context;
            var intent = context.PackageManager?.GetLaunchIntentForPackage(ZeroTierPackage);
            if (intent == null) return false;
            intent.AddFlags(Android.Content.ActivityFlags.NewTask);
            context.StartActivity(intent);
            return true;
        }
        catch (Exception ex)
        {
            LogWarn($"启动 ZeroTier One 失败: {ex.Message}");
            return false;
        }
    }

    /// <summary>打开应用商店详情页，失败时回退浏览器</summary>
    private bool OpenStoreOrWeb(string packageName)
    {
        var market = $"market://details?id={packageName}";
        var web = $"https://play.google.com/store/apps/details?id={packageName}";
        try
        {
            var context = Android.App.Application.Context;
            var pm = context.PackageManager;
            var intent = new Android.Content.Intent(Android.Content.Intent.ActionView,
                Android.Net.Uri.Parse(market));
            intent.AddFlags(Android.Content.ActivityFlags.NewTask);
            if (pm != null && intent.ResolveActivity(pm) != null)
            {
                context.StartActivity(intent);
                return true;
            }
        }
        catch (Exception ex)
        {
            LogWarn($"打开应用商店失败，改用浏览器: {ex.Message}");
        }

        try
        {
            var context = Android.App.Application.Context;
            var intent = new Android.Content.Intent(Android.Content.Intent.ActionView,
                Android.Net.Uri.Parse(web));
            intent.AddFlags(Android.Content.ActivityFlags.NewTask);
            context.StartActivity(intent);
            return true;
        }
        catch (Exception ex)
        {
            LogError($"打开 ZeroTier One 下载页失败: {ex.Message}");
            return false;
        }
    }

    #endregion
}
