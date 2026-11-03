namespace NewCosmos.Services.Core;

/// <summary>
/// 启动动画资源服务：把 Resources\Splash 下的 Web 资产与本次启动的配置
/// （身份、品牌文案、配色、音效开关）组装成可加载的页面。
/// <para>
/// 加载方式统一为虚拟主机 <c>https://splash.newcosmos.local/</c>，不用 file://：
/// 动画运行时用 fetch 读取 mp3 再用 WebAudio 解码，file:// 下 fetch 会被浏览器拒绝。
/// Windows 用 WebView2 的 SetVirtualHostNameToFolderMapping 映射到 AppData 目录；
/// Android 用 WebViewClient.ShouldInterceptRequest 直接返回 APK assets 与内存生成的配置。
/// </para>
/// </summary>
public interface ISplashAssetsService
{
    /// <summary>虚拟主机名（两端共用）</summary>
    const string VirtualHost = "splash.newcosmos.local";

    /// <summary>启动页 URL</summary>
    string StartUrl { get; }

    /// <summary>准备本次启动的资源（Windows 复制资产到 AppData 并写 config.js）</summary>
    Task PrepareAsync(CancellationToken ct = default);

#if WINDOWS
    /// <summary>Windows：虚拟主机映射的本地目录</summary>
    string WindowsFolder { get; }
#endif

#if ANDROID
    /// <summary>Android：按相对路径读取虚拟主机文件（assets / 内存 config.js / AppData 背景图）</summary>
    byte[]? TryReadVirtualFile(string relativePath);
#endif
}
