namespace NewCosmos.Services.Core;

/// <summary>
/// 会话生命周期：登录后启动心跳、登出/被顶号后统一收尾（吊销数据库会话 → 清本地缓存 → 回登录页）。
/// UI 收尾统一在主线程执行；桌面被顶号给宽限秒数保存，移动端短提示后直接退出。
/// </summary>
public interface ISessionLifecycleService
{
    /// <summary>是否正在执行被顶号退出流程（防重入）</summary>
    bool IsEnding { get; }

    /// <summary>登录成功 / 会话恢复成功后调用</summary>
    Task StartAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>仅停止心跳并解除闸门（不导航、不清本地，用于跳登录页前的清理）</summary>
    Task StopAsync();

    /// <summary>主动登出：吊销数据库会话 + 清本地 + 导航登录页</summary>
    Task LogoutAsync(CancellationToken ct = default);
}
