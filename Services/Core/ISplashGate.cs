namespace NewCosmos.Services.Core;

/// <summary>
/// 启动画面闸门：动画播完 / 被跳过 / 超时兜底后放行。
/// App 的会话恢复出口统一 await 本闸门，实现"启动动画与后台会话恢复并行，
/// 两边都就绪即切页"——绝不因为动画而阻塞登录校验，也绝不因为校验快而掐断动画。
/// </summary>
public interface ISplashGate
{
    /// <summary>本次启动是否播放启动动画（app.ini SplashEnabled）</summary>
    bool IsEnabled { get; }

    /// <summary>闸门打开时完成；未启用启动画面时立即完成</summary>
    Task WhenCompleted { get; }

    /// <summary>放行动画（播完、跳过、脚本异常、超时兜底都会调用；幂等）</summary>
    void Complete();
}
