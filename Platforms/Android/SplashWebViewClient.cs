using Android.Webkit;
using NewCosmos.Services.Core;

namespace NewCosmos.Platforms.Android;

/// <summary>
/// 启动动画 Android WebView 客户端：
/// 1) 把虚拟主机 https://splash.newcosmos.local/* 映射到 APK assets\splash\（config.js 与背景图由内存提供），
///    这样动画里的 fetch('audio/*.mp3') 走 https 同源，不受 file:// 限制；
/// 2) 拦截 newcosmos-splash:// 桥协议，交给页面放行启动闸门；
/// 3) 其余请求/导航原样交还给 MAUI 自己的 WebViewClient，保留 Navigating/Navigated 事件。
/// </summary>
public sealed class SplashWebViewClient : WebViewClient
{
    private const string BridgeSchemePrefix = "newcosmos-splash://";
    private const string AssetRoot = "splash/";

    private readonly ISplashAssetsService _assets;
    private readonly Action<string> _onBridge;
    private readonly WebViewClient? _inner;

    public SplashWebViewClient(ISplashAssetsService assets, Action<string> onBridge, WebViewClient? inner)
    {
        _assets = assets;
        _onBridge = onBridge;
        _inner = inner;
    }

    public override bool ShouldOverrideUrlLoading(global::Android.Webkit.WebView? view, IWebResourceRequest? request)
    {
        var url = request?.Url?.ToString();
        if (!string.IsNullOrEmpty(url) && url.StartsWith(BridgeSchemePrefix, StringComparison.OrdinalIgnoreCase))
        {
            _onBridge(url);
            return true;
        }

        return _inner?.ShouldOverrideUrlLoading(view, request) ?? false;
    }

    public override WebResourceResponse? ShouldInterceptRequest(global::Android.Webkit.WebView? view, IWebResourceRequest? request)
    {
        var uri = request?.Url;
        if (uri != null && string.Equals(uri.Host, ISplashAssetsService.VirtualHost, StringComparison.OrdinalIgnoreCase))
        {
            var path = uri.Path?.TrimStart('/') ?? string.Empty;
            var bytes = _assets.TryReadVirtualFile(path);

            if (bytes == null)
                return new WebResourceResponse("text/plain", "utf-8", new MemoryStream(Array.Empty<byte>()));

            var headers = new Dictionary<string, string> { ["Cache-Control"] = "no-store" };
            return new WebResourceResponse(GetMimeType(path), null, 200, "OK", headers, new MemoryStream(bytes));
        }

        return _inner?.ShouldInterceptRequest(view, request);
    }

    private static string GetMimeType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".html" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".mp3" => "audio/mpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }
}
