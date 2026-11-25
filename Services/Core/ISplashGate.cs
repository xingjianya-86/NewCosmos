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

    /// <summary>
    /// 设置本次启动的分流去向（<see cref="StartupIntent.Pending"/> 不是定论，忽略；
    /// 首次定论生效，后续调用为 no-op）。由 App 的会话恢复各出口在 await 闸门之前调用。
    /// </summary>
    void SetIntent(StartupIntent intent);

    /// <summary>
    /// 等待分流去向定论（已定则立即返回）。动画片头底板就位后经 splash-intent 问路，
    /// SplashPage 用本任务的答复回给页面；从不定论时调用方自行限时兜底。
    /// </summary>
    Task<StartupIntent> WhenIntentDecided { get; }
}
