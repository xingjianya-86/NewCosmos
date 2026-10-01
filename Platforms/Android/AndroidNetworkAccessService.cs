using NewCosmos.Models.Results;

namespace NewCosmos.Services.System;

/// <summary>
/// Android 网络接入服务 stub - ZeroTier 管理由用户通过 Android ZeroTier App 完成
/// </summary>
public class AndroidNetworkAccessService : INetworkAccessService
{
    public Task<Result<ZeroTierStatus>> GetStatusAsync(CancellationToken ct = default)
    {
        return Task.FromResult(Result<ZeroTierStatus>.Failure("ZT_NOT_SUPPORTED", "ZeroTier 管理请使用 Android 版 ZeroTier One App"));
    }

    public Task<Result> InstallZeroTierAsync(CancellationToken ct = default)
    {
        return Task.FromResult(Result.Failure("ZT_NOT_SUPPORTED", "Android 请通过 Google Play 安装 ZeroTier One"));
    }

    public Task<Result> EnsureCliAccessAsync(CancellationToken ct = default)
    {
        return Task.FromResult(Result.Failure("ZT_NOT_SUPPORTED", "Android 不支持 ZeroTier CLI 操作"));
    }

    public Task<Result> JoinNetworkAsync(string networkId, string? moonId, CancellationToken ct = default)
    {
        return Task.FromResult(Result.Failure("ZT_NOT_SUPPORTED", "Android 请通过 ZeroTier One App 加入网络"));
    }

    public Task<Result> LeaveNetworkAsync(string networkId, CancellationToken ct = default)
    {
        return Task.FromResult(Result.Failure("ZT_NOT_SUPPORTED", "Android 请通过 ZeroTier One App 退出网络"));
    }

    public Task<Result> OpenAuthorizationPageAsync(string url, CancellationToken ct = default)
    {
        return Task.FromResult(Result.Failure("ZT_NOT_SUPPORTED", "Android 请通过 ZeroTier One App 管理授权"));
    }
}
