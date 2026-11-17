using NewCosmos.Constants;
using NewCosmos.Services.Core;

namespace NewCosmos.Components;

/// <summary>
/// 动画 WebView 宿主控件（启动动画 SplashPage 与序列登录 LoginPage 覆盖层共用）。
/// 从 <c>Pages\Splash\SplashPage.xaml.cs</c> 的平台初始化逻辑抽取（2026-11-15）：
/// <list type="bullet">
/// <item>Windows：WebView2 独立环境（放开自动播放策略）+ 虚拟主机映射 + postMessage 桥；</item>
/// <item>Android：<see cref="Platforms.Android.SplashWebViewClient"/> 拦截虚拟主机资产与自定义协议；</item>
/// <item>双端统一：<c>newcosmos-splash://</c> 自定义协议导航一律取消并解析为 (kind, payload)；</item>
/// <item>脚本加载回执看门狗（<see cref="SplashConstants.WebBootTimeoutSeconds"/>，仅 Windows 有 postMessage 通道），
/// 超时或初始化异常经 <see cref="HostFaulted"/> 上抛，由页面决定放行 / 回落。</item>
/// </list>
/// 页面侧订阅 <see cref="BridgeReceived"/>（boot/done/error/audio/login-submit/login-ready/login-skip…）
/// 与 <see cref="HostFaulted"/>；调用 <see cref="EvalAsync"/> 向页面派发 host→web 消息。
/// </summary>
public class AnimationWebViewView : ContentView
{
    /// <summary>桥协议自定义 scheme（bridge.js ping 与 C# Navigating 拦截共用）</summary>
    public const string BridgeSchemePrefix = "newcosmos-splash://";

    private const string PostMessagePrefix = "newcosmos-splash:";

    private readonly WebView _webView;
    private readonly ISplashAssetsService _assets;
    private readonly ILoggerService _logger;

    private bool _started;
    private CancellationTokenSource? _bootWatchdog;

    /// <summary>脚本要加载的页面 URL（虚拟主机 login.html / splash.html），须在 Loaded 前设置</summary>
    public string? StartUrl { get; set; }

    /// <summary>
    /// 平台控件底色：Windows 写 WebView2.DefaultBackgroundColor，Android/共享层写 WebView.BackgroundColor。
    /// 序列登录传透明——加载期间透出下方经典登录页，回落时无闪切；启动动画传黑。
    /// </summary>
    public Color? PlatformBackgroundColor { get; set; }

    /// <summary>web → host：桥接消息（kind=boot/done/error/audio/login-submit/login-ready/login-skip…，payload 为 JSON 或原文）</summary>
    public event Action<string, string>? BridgeReceived;

    /// <summary>宿主链路故障（WebView2 初始化失败 / 脚本回执超时 / 加载异常），页面据此放行或回落</summary>
    public event Action<string>? HostFaulted;

    public AnimationWebViewView(ISplashAssetsService assets, ILoggerService logger)
    {
        _assets = assets;
        _logger = logger;

        _webView = new WebView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            BackgroundColor = PlatformBackgroundColor ?? Colors.Black
        };
        Content = _webView;
        _webView.Navigating += OnWebNavigating;

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

            if (PlatformBackgroundColor is { } bg)
                _webView.BackgroundColor = bg;

#if WINDOWS
            if (_webView.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 webView2)
            {
                // 虚拟主机必须先于导航建立，动画里的 fetch('audio/*.mp3') 才能工作。
                // WebView2 默认要求"用户手势"才允许发声：启动动画无人点击 → 台词被静音。
                // 这里给动画单独创建一个 WebView2 环境并放行自动播放。
                // 底色尽量早设：CoreWebView2 就绪前控件按默认白底绘制，早设缩小闪白窗口。
                if (PlatformBackgroundColor is { } earlyColor)
                    webView2.DefaultBackgroundColor = ToWinUiColor(earlyColor);

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
                        // 环境创建失败不阻断：动画照播，只是可能没有声音；再失败才走 HostFaulted
                        _logger.Warn("[AnimationWebView] 自动播放环境创建失败，回退默认环境: " + ex.Message);
                        await webView2.EnsureCoreWebView2Async();
                    }
                }
                else
                {
                    await webView2.EnsureCoreWebView2Async();
                    _logger.Warn("[AnimationWebView] WebView2 已提前初始化，无法追加自动播放参数（可能无声音）");
                }

                if (PlatformBackgroundColor is { } color)
                    webView2.DefaultBackgroundColor = ToWinUiColor(color);

                var core = webView2.CoreWebView2;
                if (core == null)
                {
                    _logger.Warn("[AnimationWebView] WebView2 初始化失败（CoreWebView2 为空）");
                    RaiseFault("WebView2 初始化失败（CoreWebView2 为空）");
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
            if (_webView.Handler?.PlatformView is global::Android.Webkit.WebView androidWebView)
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

                if (PlatformBackgroundColor is { } androidColor)
                {
                    var c = androidColor;
                    androidWebView.SetBackgroundColor(new global::Android.Graphics.Color(
                        (int)(c.Red * 255), (int)(c.Green * 255), (int)(c.Blue * 255), (int)(c.Alpha * 255)));
                }

                // 包一层客户端：虚拟主机走 assets，其余交还 MAUI 原有客户端（保留 Navigating/Navigated 事件）
                var inner = androidWebView.WebViewClient;
                androidWebView.SetWebViewClient(
                    new Platforms.Android.SplashWebViewClient(_assets, OnSchemeUrl, inner));
            }
#endif
            if (string.IsNullOrEmpty(StartUrl))
            {
                RaiseFault("StartUrl 未设置");
                return;
            }

            _webView.Source = StartUrl;
            _logger.Info("[AnimationWebView] 动画加载: " + StartUrl);

            // 看门狗：页面/脚本链路异常时到时长仍无 boot 回执就判加载失败（仅 Windows：
            // WebView2 有 postMessage 通道；Android 由页面级兜底与宿主看门狗兜底）
#if WINDOWS
            _bootWatchdog = new CancellationTokenSource();
            _ = WatchBootAsync(_bootWatchdog.Token);
#endif
        }
        catch (Exception ex)
        {
            // 动画加载失败绝不阻塞：记日志后交由页面放行/回落
            _logger.LogError(ex, "[AnimationWebView] 动画加载失败");
            RaiseFault("动画加载失败: " + ex.Message);
        }
    }

    /// <summary>向页面执行 JS（host → web 派发，如 auth-result / prefill）</summary>
    public async Task EvalAsync(string script)
    {
        try
        {
#if WINDOWS
            if (_webView.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.WebView2 webView2
                && webView2.CoreWebView2 != null)
            {
                await webView2.CoreWebView2.ExecuteScriptAsync(script);
            }
#elif ANDROID
            if (_webView.Handler?.PlatformView is global::Android.Webkit.WebView androidWebView)
            {
                androidWebView.EvaluateJavascript(script, null);
            }
#endif
        }
        catch (Exception ex)
        {
            _logger.Warn("[AnimationWebView] EvalJs 失败: " + ex.Message);
        }
    }

    /* ── 桥接解析 ─────────────────────────────────────────────────────────── */

    private void OnWebNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Url) || !e.Url.StartsWith(BridgeSchemePrefix, StringComparison.OrdinalIgnoreCase))
            return;

        e.Cancel = true;
        OnSchemeUrl(e.Url);
    }

#if WINDOWS
    /// <summary>WebView2 消息通道：与自定义协议双保险（bridge.js 两路同时发，页面侧按 id 去重）</summary>
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

        var (kind, payload) = ParsePostMessage(text);
        if (kind.Length > 0)
            Raise(kind, payload);
    }
#endif

    /// <summary>自定义协议导航（Windows Navigating / Android WebViewClient 回调）</summary>
    private void OnSchemeUrl(string url)
    {
        var (kind, payload) = ParseSchemeUrl(url);
        if (kind.Length > 0)
            Raise(kind, payload);
    }

    /// <summary>Android 拦截器可能在非 UI 线程回调，统一切回主线程再派发</summary>
    private void Raise(string kind, string payload)
    {
        void fire()
        {
            if (string.Equals(kind, "boot", StringComparison.OrdinalIgnoreCase))
                CancelBootWatchdog();
            BridgeReceived?.Invoke(kind, payload);
        }

        if (MainThread.IsMainThread)
            fire();
        else
            MainThread.BeginInvokeOnMainThread(fire);
    }

    private void RaiseFault(string reason)
    {
        void fire() => HostFaulted?.Invoke(reason);

        if (MainThread.IsMainThread)
            fire();
        else
            MainThread.BeginInvokeOnMainThread(fire);
    }

    /// <summary>newcosmos-splash://kind?msg=… / ?payload=… → (kind, payload)</summary>
    private static (string Kind, string Payload) ParseSchemeUrl(string url)
    {
        var rest = url[BridgeSchemePrefix.Length..];
        var qIndex = rest.IndexOf('?');
        var kind = qIndex >= 0 ? rest[..qIndex] : rest;
        var query = qIndex >= 0 ? rest[(qIndex + 1)..] : string.Empty;

        var payload = ExtractQueryParam(query, "payload");
        if (payload.Length == 0)
            payload = ExtractQueryParam(query, "msg");
        return (kind, payload);
    }

    /// <summary>WebView2 postMessage：newcosmos-splash:kind[:payload]（payload 可含冒号，只切第一段）</summary>
    private static (string Kind, string Payload) ParsePostMessage(string text)
    {
        if (!text.StartsWith(PostMessagePrefix, StringComparison.OrdinalIgnoreCase))
            return (string.Empty, string.Empty);

        var rest = text[PostMessagePrefix.Length..];
        var colon = rest.IndexOf(':');
        return colon < 0 ? (rest, string.Empty) : (rest[..colon], rest[(colon + 1)..]);
    }

    private static string ExtractQueryParam(string query, string name)
    {
        foreach (var pair in query.Split('&'))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0 || !string.Equals(pair[..eq], name, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                return Uri.UnescapeDataString(pair[(eq + 1)..]);
            }
            catch
            {
                return pair[(eq + 1)..];
            }
        }

        return string.Empty;
    }

    /* ── 看门狗 ───────────────────────────────────────────────────────────── */

    private async Task WatchBootAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(SplashConstants.WebBootTimeoutSeconds), token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _logger.Warn($"[AnimationWebView] {SplashConstants.WebBootTimeoutSeconds} 秒内未收到脚本加载回执，判定加载失败");
        RaiseFault("脚本加载回执超时");
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

#if WINDOWS
    /// <summary>MAUI Color(float 0–1) → Windows.UI.Color(byte)；FromArgb 参数序为 (a,r,g,b)</summary>
    private static global::Windows.UI.Color ToWinUiColor(Color c) =>
        global::Windows.UI.Color.FromArgb(
            (byte)Math.Round(c.Alpha * 255), (byte)Math.Round(c.Red * 255),
            (byte)Math.Round(c.Green * 255), (byte)Math.Round(c.Blue * 255));
#endif
}
