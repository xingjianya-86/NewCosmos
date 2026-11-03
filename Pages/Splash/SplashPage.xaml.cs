using NewCosmos.Services.Core;

namespace NewCosmos.Pages.Splash;

/// <summary>
/// 启动动画页：加载 Resources\Splash 的 Web 动画，动画结束（或被跳过）后打开 <see cref="ISplashGate"/>。
/// 页面本身不做任何会话/数据库判断——那些仍在 App.RestoreSessionAndNavigateAsync 后台并行执行，
/// 两边都就绪后由 App 切换真实页面。
/// </summary>
public partial class SplashPage : ContentPage
{
    private const string BridgeSchemePrefix = "newcosmos-splash://";

    private readonly ISplashGate _gate;
    private readonly ISplashAssetsService _assets;
    private readonly ILoggerService _logger;

    private bool _started;
    private bool _completed;
    private CancellationTokenSource? _bootWatchdog;

    /// <summary>脚本加载回执超时：超过该时长仍无 boot 回执，判定 WebView/虚拟主机链路异常，直接放行</summary>
    private static readonly TimeSpan BootTimeout = TimeSpan.FromSeconds(12);

    public SplashPage(ISplashGate gate, ISplashAssetsService assets, ILoggerService logger)
    {
        InitializeComponent();
        _gate = gate;
        _assets = assets;
        _logger = logger;

        // 双入口保险：OnHandlerChanged 时平台控件可能尚未进可视树，
        // Loaded 一定在控件就绪后触发；StartAsync 自带幂等保护。
        Loaded += (_, _) => _ = StartAsync();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        if (_started)
            return;

        _started = true;

        try
        {
            await _assets.PrepareAsync();
            var url = _assets.StartUrl;
            SplashWebView.Navigating += OnWebNavigating;

#if WINDOWS
            if (SplashWebView.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 webView2)
            {
                // 虚拟主机必须先于导航建立，动画里的 fetch('audio/*.mp3') 才能工作。
                // WebView2 默认要求"用户手势"才允许发声：启动动画无人点击 → 台词被静音。
                // 这里给启动动画单独创建一个 WebView2 环境并放行自动播放。
                if (webView2.CoreWebView2 == null)
                {
                    try
                    {
                        var envOptions = new Microsoft.Web.WebView2.Core.CoreWebView2EnvironmentOptions
                        {
                            AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required"
                        };
                        var userDataFolder = Path.Combine(FileSystem.AppDataDirectory, "WebView2Splash");
                        var environment = await Microsoft.Web.WebView2.Core.CoreWebView2Environment
                            .CreateWithOptionsAsync(null, userDataFolder, envOptions);
                        await webView2.EnsureCoreWebView2Async(environment);
                    }
                    catch (Exception ex)
                    {
                        // 环境创建失败不阻断启动：动画照播，只是可能没有声音
                        _logger.Warn("[SplashPage] 自动播放环境创建失败，回退默认环境: " + ex.Message);
                        await webView2.EnsureCoreWebView2Async();
                    }
                }
                else
                {
                    await webView2.EnsureCoreWebView2Async();
                    _logger.Warn("[SplashPage] WebView2 已提前初始化，无法追加自动播放参数（可能无声音）");
                }

                webView2.DefaultBackgroundColor = Microsoft.UI.Colors.Black;

                var core = webView2.CoreWebView2;
                if (core == null)
                {
                    _logger.Warn("[SplashPage] WebView2 初始化失败（CoreWebView2 为空），直接放行");
                    Complete();
                    return;
                }

                core.SetVirtualHostNameToFolderMapping(
                    ISplashAssetsService.VirtualHost,
                    _assets.WindowsFolder,
                    Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.Allow);
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.WebMessageReceived += OnWindowsWebMessage;
            }
#elif ANDROID
            if (SplashWebView.Handler?.PlatformView is global::Android.Webkit.WebView androidWebView)
            {
                var settings = androidWebView.Settings;
                if (settings != null)
                {
                    settings.JavaScriptEnabled = true;
                    settings.DomStorageEnabled = true;
                    // 开屏即播台词：WebView 默认要求用户手势才允许发声
                    settings.MediaPlaybackRequiresUserGesture = false;
                    settings.BuiltInZoomControls = false;
                    settings.SetSupportZoom(false);
                }

                // 包一层客户端：虚拟主机走 assets，其余交还 MAUI 原有客户端（保留 Navigating/Navigated 事件）
                var inner = androidWebView.WebViewClient;
                androidWebView.SetWebViewClient(
                    new Platforms.Android.SplashWebViewClient(_assets, OnBridgeMessage, inner));
            }
#endif
            SplashWebView.Source = url;
            _logger.Info("[SplashPage] 启动动画加载: " + url);

            // 看门狗：页面/脚本链路异常时 12 秒内收不到 boot 回执就直接放行
            // （仅 Windows：WebView2 有 postMessage 通道；Android 由浏览器客户端拦截 + 闸门超时兜底）
#if WINDOWS
            _bootWatchdog = new CancellationTokenSource();
            _ = WatchBootAsync(_bootWatchdog.Token);
#endif
        }
        catch (Exception ex)
        {
            // 动画加载失败绝不阻塞启动：记日志后直接放行
            _logger.LogError(ex, "[SplashPage] 启动动画加载失败，直接放行");
            Complete();
        }
    }

    private void OnWebNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Url) || !e.Url.StartsWith(BridgeSchemePrefix, StringComparison.OrdinalIgnoreCase))
            return;

        e.Cancel = true;
        HandleBridgeMessage(e.Url);
    }

#if WINDOWS
    /// <summary>WebView2 消息通道：与自定义协议双保险（bridge.js 两路同时发，Complete 幂等）</summary>
    private void OnWindowsWebMessage(object? sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs e)
    {
        string text;
        try
        {
            text = e.TryGetWebMessageAsString();
        }
        catch
        {
            return;
        }

        if (text.StartsWith("newcosmos-splash:boot", StringComparison.OrdinalIgnoreCase))
            HandleBridgeMessage("newcosmos-splash://boot");
        else if (text.StartsWith("newcosmos-splash:audio:", StringComparison.OrdinalIgnoreCase))
            _logger.Info("[SplashPage] 音频上下文状态: " + text["newcosmos-splash:audio:".Length..]);
        else if (text.StartsWith("newcosmos-splash:done", StringComparison.OrdinalIgnoreCase))
            HandleBridgeMessage("newcosmos-splash://done");
        else if (text.StartsWith("newcosmos-splash:error:", StringComparison.OrdinalIgnoreCase))
            HandleBridgeMessage("newcosmos-splash://error?msg=" + Uri.EscapeDataString(text["newcosmos-splash:error:".Length..]));
    }
#endif

    /// <summary>Android 拦截器在非 UI 线程回调，统一切回主线程再处理</summary>
    private void OnBridgeMessage(string url)
    {
        if (MainThread.IsMainThread)
            HandleBridgeMessage(url);
        else
            MainThread.BeginInvokeOnMainThread(() => HandleBridgeMessage(url));
    }

    private void HandleBridgeMessage(string url)
    {
        // newcosmos-splash://boot       → 脚本已加载回执：撤掉启动看门狗
        // newcosmos-splash://done       → 动画播完 / 被跳过
        // newcosmos-splash://error?msg= → 脚本异常（bridge.js 上报），同样放行
        if (url.Contains("://boot", StringComparison.OrdinalIgnoreCase))
        {
            CancelBootWatchdog();
            return;
        }

        if (url.Contains("://error", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Warn("[SplashPage] 启动动画脚本异常: " + ExtractMessage(url));
        }
        else
        {
            _logger.Info("[SplashPage] 启动动画结束，放行启动闸门");
        }

        Complete();
    }

    private async Task WatchBootAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(BootTimeout, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (_completed)
            return;

        _logger.Warn("[SplashPage] 启动动画 12 秒内未收到脚本加载回执，判定加载失败，直接放行");
        Complete();
    }

    private void CancelBootWatchdog()
    {
        try
        {
            _bootWatchdog?.Cancel();
        }
        catch
        {
            // 已释放/已取消均无关紧要
        }

        _bootWatchdog?.Dispose();
        _bootWatchdog = null;
    }

    private static string ExtractMessage(string url)
    {
        const string marker = "msg=";
        var index = url.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
            return url;

        var raw = url[(index + marker.Length)..];
        try
        {
            return Uri.UnescapeDataString(raw);
        }
        catch
        {
            return raw;
        }
    }

    private void Complete()
    {
        if (_completed)
            return;

        _completed = true;
        CancelBootWatchdog();
        _gate.Complete();
    }
}
