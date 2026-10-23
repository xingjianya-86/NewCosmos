using NewCosmos.Services.Domain.UserManagement;

namespace NewCosmos.Services.Core;

/// <summary>会话被服务端终止时的事件负载</summary>
public sealed class SessionEndedInfo
{
    public Guid SessionId { get; init; }
    public SessionValidationStatus Status { get; init; }
    public string? EndReason { get; init; }
}

/// <summary>
/// 会话心跳：登录/恢复后启动，周期性刷新 last_seen_at 并探测会话是否仍有效。
/// 网络失败只记日志、不踢人；只有数据库明确返回非 active 才触发 SessionEnded。
/// </summary>
public interface ISessionHeartbeatService
{
    /// <summary>会话被顶号/强制下线/过期时触发（后台线程，处理方需自行切主线程）</summary>
    event Func<SessionEndedInfo, Task>? SessionEnded;

    bool IsRunning { get; }

    Task StartAsync(Guid sessionId, CancellationToken ct = default);

    Task StopAsync();
}
