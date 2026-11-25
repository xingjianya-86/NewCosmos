using NewCosmos.Models.Options;

namespace NewCosmos.Services.Core;

/// <summary>
/// 启动画面闸门实现（单例）。
/// 用 TaskCompletionSource 表达"动画结束"这一个一次性状态，不持有每次操作的可变实例数据，
/// 并发调用 Complete() 安全；未启用启动画面或超时兜底时立即放行。
/// </summary>
public sealed class SplashGate : ISplashGate
{
    private readonly TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<StartupIntent> _intentTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public SplashGate(AppOptions options, ILoggerService logger)
    {
        IsEnabled = options.SplashEnabled;

        if (!IsEnabled)
        {
            _tcs.TrySetResult();
            return;
        }

        // 看门狗：脚本加载失败 / WebView 初始化异常时，动画事件永远不会来；
        // 到点直接放行，宁可少看一段动画，也不能把用户留在启动画面。
        var timeoutSeconds = Math.Clamp(options.SplashTimeoutSeconds,
            Constants.SplashConstants.MinTimeoutSeconds, Constants.SplashConstants.MaxTimeoutSeconds);
        _ = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)).ContinueWith(_ =>
        {
            if (_tcs.TrySetResult())
                logger.Warn($"[SplashGate] 启动动画 {timeoutSeconds} 秒内未结束，已强制放行（脚本或 WebView 异常）");
        }, TaskScheduler.Default);
    }

    public bool IsEnabled { get; }

    public Task WhenCompleted => _tcs.Task;

    public void Complete() => _tcs.TrySetResult();

    public Task<StartupIntent> WhenIntentDecided => _intentTcs.Task;

    /// <summary>首次定论生效（会话恢复的各出口只会有一个到达；重试再入为 no-op，不影响已放行的动画）</summary>
    public void SetIntent(StartupIntent intent)
    {
        if (intent == StartupIntent.Pending)
            return;

        _intentTcs.TrySetResult(intent);
    }
}
