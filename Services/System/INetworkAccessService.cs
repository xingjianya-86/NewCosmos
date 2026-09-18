using NewCosmos.Models.Results;

namespace NewCosmos.Services.System;

/// <summary>
/// 网络接入服务（ZeroTier）：检测/安装/加入网络/Moon 加速/状态查询。
/// 仅用于 Windows 客户端；授权仍在服务器网页端完成。
/// </summary>
public interface INetworkAccessService
{
    /// <summary>检测 ZeroTier 是否安装并返回节点/网络状态</summary>
    Task<Result<ZeroTierStatus>> GetStatusAsync(CancellationToken ct = default);

    /// <summary>安装随包分发的 ZeroTier（需管理员提权）</summary>
    Task<Result> InstallZeroTierAsync(CancellationToken ct = default);

    /// <summary>
    /// 确保本机 CLI 可读取 ZeroTier 管理令牌（authtoken.secret，默认仅管理员可读）：
    /// 需要时以管理员提权（UAC）授予当前用户读权限；已可读时直接成功。
    /// </summary>
    Task<Result> EnsureCliAccessAsync(CancellationToken ct = default);

    /// <summary>加入网络；moonId 非空时同时 orbit（私有化加速）</summary>
    Task<Result> JoinNetworkAsync(string networkId, string? moonId, CancellationToken ct = default);

    /// <summary>退出网络</summary>
    Task<Result> LeaveNetworkAsync(string networkId, CancellationToken ct = default);

    /// <summary>打开网页授权地址</summary>
    Task<Result> OpenAuthorizationPageAsync(string url, CancellationToken ct = default);
}

/// <summary>ZeroTier 运行状态</summary>
public class ZeroTierStatus
{
    public bool Installed { get; set; }
    public string NodeId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public bool Online { get; set; }
    public string CliPath { get; set; } = string.Empty;
    public List<ZeroTierNetworkState> Networks { get; set; } = new();

    /// <summary>CLI 无法读取管理令牌（authtoken.secret）→ 需要在"接入"时提权授权一次</summary>
    public bool TokenAccessDenied { get; set; }

    /// <summary>本机是否已加入指定网络</summary>
    public bool IsJoined(string networkId) =>
        Networks.Any(n => string.Equals(n.Nwid, networkId, StringComparison.OrdinalIgnoreCase));
}

/// <summary>单个 ZeroTier 网络状态</summary>
public class ZeroTierNetworkState
{
    public string Nwid { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>OK / ACCESS_DENIED / NOT_FOUND / REQUESTING_CONFIGURATION ...</summary>
    public string Status { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Device { get; set; } = string.Empty;
    public List<string> AssignedAddresses { get; set; } = new();

    public bool IsOk => string.Equals(Status, "OK", StringComparison.OrdinalIgnoreCase);
    public bool IsAuthorized => !string.Equals(Status, "ACCESS_DENIED", StringComparison.OrdinalIgnoreCase);
}
