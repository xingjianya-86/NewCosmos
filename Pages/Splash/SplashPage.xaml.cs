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

    private bool _completed;

    public SplashPage(ISplashGate gate, ISplashAssetsService assets, ILoggerService logger)
    {
        InitializeComponent();
        _gate = gate;
        _logger = logger;

        var host = new AnimationWebViewView(assets, logger)
        {
            StartUrl = assets.StartUrl,
            PlatformBackgroundColor = Colors.Black
        };
        host.BridgeReceived += OnBridgeReceived;
        host.HostFaulted += OnHostFaulted;
        HostPanel.Children.Add(host);
    }

    /// <summary>
    /// 桥接事件：
    /// boot   → 脚本加载回执（看门狗由 AnimationWebViewView 自行撤销）；
    /// done   → 动画播完 / 被跳过 → 放行启动闸门；
    /// error  → 脚本异常（bridge.js 上报），记日志后同样放行，绝不把用户留在黑幕里；
    /// audio  → 音频上下文诊断，仅记日志。
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

            case "done":
                _logger.Info("[SplashPage] 启动动画结束，放行启动闸门");
                Complete();
                break;

            default:
                _logger.Info($"[SplashPage] 动画桥消息: {kind}");
                break;
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
