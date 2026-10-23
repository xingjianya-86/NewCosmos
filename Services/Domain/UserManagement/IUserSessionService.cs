using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.UserManagement;

/// <summary>会话校验结论</summary>
public enum SessionValidationStatus
{
    Active,
    Replaced,
    Kicked,
    LoggedOut,
    Expired,
    NotFound
}

/// <summary>会话校验结果（含结束原因，用于给用户可读提示）</summary>
public sealed class SessionValidationResult
{
    public SessionValidationStatus Status { get; init; }
    public string? EndReason { get; init; }
    public DateTime ExpiresAt { get; init; }
    public bool IsActive => Status == SessionValidationStatus.Active;
}

/// <summary>登录创建会话结果</summary>
public sealed class SessionCreationResult
{
    public Guid SessionId { get; init; }
    public DateTime ExpiresAt { get; init; }
    public int ReplacedCount { get; init; }
}

/// <summary>心跳结果：IsActive=false 时 Status/EndReason 说明原因</summary>
public sealed class SessionTouchResult
{
    public bool IsActive { get; init; }
    public SessionValidationStatus Status { get; init; }
    public string? EndReason { get; init; }
}

/// <summary>
/// 会话策略（nc_config_system_standards category='session'，未配置时用代码默认值）。
/// 不可变快照，整对象替换；Singleton 服务不原地修改。
/// </summary>
public sealed class SessionPolicy
{
    /// <summary>1=按端配额，2=严格单会话，3=多端并存</summary>
    public int Mode { get; init; } = Constants.SessionConstants.DefaultPolicy;
    public int MaxDesktop { get; init; } = Constants.SessionConstants.DefaultMaxDesktop;
    public int MaxMobile { get; init; } = Constants.SessionConstants.DefaultMaxMobile;
    public int HeartbeatDesktopSeconds { get; init; } = Constants.SessionConstants.DefaultHeartbeatDesktopSeconds;
    public int HeartbeatMobileSeconds { get; init; } = Constants.SessionConstants.DefaultHeartbeatMobileSeconds;
    public int OnlineWindowSeconds { get; init; } = Constants.SessionConstants.DefaultOnlineWindowSeconds;
    public int AbsoluteDays { get; init; } = Constants.SessionConstants.DefaultAbsoluteDays;
    public int GraceSeconds { get; init; } = Constants.SessionConstants.DefaultGraceSeconds;

    public static SessionPolicy CreateDefault() => new();
}

/// <summary>
/// 登录会话服务（数据库权威）：登录裁决、会话校验、心跳保活、登出/强制下线、在线会话查询。
/// </summary>
public interface IUserSessionService
{
    /// <summary>读取会话策略（进程内缓存 60 秒）</summary>
    Task<SessionPolicy> GetPolicyAsync(CancellationToken ct = default);

    /// <summary>
    /// 登录成功后创建会话：事务内按策略顶掉旧会话（同端配额/单会话/多端），
    /// 使用 advisory lock 串行化同一账号的并发登录。
    /// </summary>
    Task<Result<SessionCreationResult>> CreateAsync(
        int userId,
        string deviceId,
        string deviceKind,
        string deviceName,
        string appVersion,
        string? clientIp,
        CancellationToken ct = default);

    /// <summary>启动/恢复时校验会话是否仍为 active 且设备标识匹配（查询失败按 Result 失败上抛，不误判为失效）</summary>
    Task<Result<SessionValidationResult>> ValidateAsync(Guid sessionId, string deviceId, CancellationToken ct = default);

    /// <summary>心跳：刷新 last_seen_at，并返回会话是否仍有效</summary>
    Task<Result<SessionTouchResult>> TouchAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>结束单个会话（登出/管理员强制下线）</summary>
    Task<Result> RevokeAsync(Guid sessionId, string reason, int? byUserId = null, CancellationToken ct = default);

    /// <summary>结束某用户的全部活动会话（改密/禁用/权限变更）</summary>
    Task<Result<int>> RevokeAllForUserAsync(int userId, string reason, int? byUserId = null,
        Guid? exceptSessionId = null, CancellationToken ct = default);

    /// <summary>
    /// 结束活动会话（管理员批量下线）；organizationIds 非空时只影响该组织边界内的会话，
    /// exceptSessionId 用于保留操作者自身会话。
    /// </summary>
    Task<Result<int>> RevokeAllActiveAsync(string reason, int? byUserId = null,
        Guid? exceptSessionId = null, IReadOnlyCollection<int>? organizationIds = null,
        CancellationToken ct = default);

    /// <summary>在线会话列表（仅 active 且未过期；organizationIds 非空时按组织边界过滤）</summary>
    Task<Result<List<UserSessionRecord>>> ListActiveAsync(IReadOnlyCollection<int>? organizationIds = null,
        CancellationToken ct = default);
}
