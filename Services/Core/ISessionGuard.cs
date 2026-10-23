namespace NewCosmos.Services.Core;

/// <summary>
/// 会话闸门：心跳判定被顶号/强制下线后置为吊销，数据库入口据此快速失败，
/// 避免"已被踢但还没轮到下一次心跳"期间继续写业务数据。
/// 说明：直连数据库的客户端可被改造绕过，本闸门是减少窗口与防误操作，不是安全边界。
/// </summary>
public interface ISessionGuard
{
    /// <summary>当前会话令牌（未登录为 null）</summary>
    Guid? SessionId { get; }

    /// <summary>是否已建立会话（登录成功/恢复成功后为 true）</summary>
    bool IsArmed { get; }

    /// <summary>是否应拦截数据库操作（已吊销且未处于豁免上下文）</summary>
    bool IsBlocked { get; }

    /// <summary>登录成功/会话恢复成功后启用</summary>
    void Arm(Guid sessionId);

    /// <summary>登出/退出时解除（不再拦截）</summary>
    void Disarm();

    /// <summary>
    /// 标记会话已失效（被顶号/强制下线/过期）。
    /// grace 为写操作宽限期：宽限期内允许保存未完成表单，到期后数据库入口开始拦截。
    /// </summary>
    void Revoke(TimeSpan? grace = null);

    /// <summary>
    /// 豁免当前异步流的拦截（心跳、登录裁决、登出吊销等会话自身 SQL 必须走此豁免）。
    /// </summary>
    IDisposable Bypass();
}
