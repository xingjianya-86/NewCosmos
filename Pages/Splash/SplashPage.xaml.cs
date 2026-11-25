using NewCosmos.Components;
using NewCosmos.Services.Core;

namespace NewCosmos.Pages.Splash;

/// <summary>
/// 启动动画页：平台宿主（WebView2 环境 / 虚拟主机 / 桥接 / 看门狗）在
/// <see cref="AnimationWebViewView"/>（与序列登录共用），本页只把动画事件接到
/// 启动闸门 <see cref="ISplashGate"/>。
/// 页面本身不做任何会话/数据库判断——那些仍在 App.RestoreSessionAndNavigateAsync
/// 后台并行执行，两边都就绪后由 App 切换真实页面。
/// </summary>
public partial class SplashPage : ContentPage
{
    private readonly ISplashGate _gate;
    private readonly ILoggerService _logger;
    private readonly AnimationWebViewView _host;

    private bool _completed;
    private bool _intentHandled;   // splash-intent 双路桥（postMessage + 自定义协议）各到一次，只应答一次

    public SplashPage(ISplashGate gate, ISplashAssetsService assets, ILoggerService logger)
    {
        InitializeComponent();
        _gate = gate;
        _logger = logger;

        _host = new AnimationWebViewView(assets, logger)
        {
            StartUrl = assets.StartUrl,
            PlatformBackgroundColor = Colors.Black
        };
        _host.BridgeReceived += OnBridgeReceived;
        _host.HostFaulted += OnHostFaulted;
        HostPanel.Children.Add(_host);
    }

    /// <summary>
    /// 桥接事件：
    /// boot   → 脚本加载回执（看门狗由 AnimationWebViewView 自行撤销）；
    /// done   → 动画播完 / 被跳过 → 放行启动闸门；
    /// error  → 脚本异常（bridge.js 上报），记日志后同样放行，绝不把用户留在黑幕里；
    /// audio  → 音频上下文诊断，仅记日志；
    /// splash-intent → 片头底板就位后的分流问路：等会话恢复定论后答复 full/skip（见 ReplyIntentAsync）。
    /// （done/error 双路上报会各到一次，Complete 幂等。）
    /// </summary>
    private void OnBridgeReceived(string kind, string payload)
    {
        switch (kind.ToLowerInvariant())
        {
            case "boot":
                break;

            case "error":
                _logger.Warn("[SplashPage] 启动动画脚本异常: " + payload);
                Complete();
                break;

            case "audio":
                _logger.Info("[SplashPage] 音频上下文状态: " + payload);
                break;

            case "splash-intent":
                if (!_intentHandled)
                {
                    _intentHandled = true;
                    _ = ReplyIntentAsync();
                }
                break;

            case "done":
                _logger.Info("[SplashPage] 启动动画结束，放行启动闸门");
                Complete();
                break;

            default:
                _logger.Info($"[SplashPage] 动画桥消息: {kind}");
                break;
        }
    }

    /// <summary>
    /// 分流问路应答：等会话恢复在闸门上定去向（Pending 时限时等 20 秒），
    /// full = 会话有效继续全片；skip = 需要登录/重试页，片头收尾交接。
    /// 超时未定论按 full 兜底（与既往"每次启动播全片"行为一致，绝不卡住动画）。
    /// </summary>
    private async Task ReplyIntentAsync()
    {
        try
        {
            StartupIntent intent;
            try
            {
                intent = await _gate.WhenIntentDecided.WaitAsync(TimeSpan.FromSeconds(20));
            }
            catch (TimeoutException)
            {
                intent = StartupIntent.Full;
                _logger.Warn("[SplashPage] 启动去向 20 秒内未定论，按全片继续");
            }

            var mode = intent == StartupIntent.Full ? "full" : "skip";
            _logger.Info("[SplashPage] 启动分流答复: " + mode);
            await _host.EvalAsync(
                "window.__NC_BRIDGE__ && window.__NC_BRIDGE__.dispatch('splash-intent-result', '" + mode + "');");
        }
        catch (Exception ex)
        {
            _logger.Warn("[SplashPage] 启动分流答复失败: " + ex.Message);
        }
    }

    /// <summary>宿主链路故障（WebView2 初始化失败 / boot 回执超时 / 加载异常）→ 照旧放行</summary>
    private void OnHostFaulted(string reason)
    {
        _logger.Warn("[SplashPage] 动画宿主故障，直接放行: " + reason);
        Complete();
    }

    private void Complete()
    {
        if (_completed)
            return;

        _completed = true;
        _gate.Complete();
    }
}
