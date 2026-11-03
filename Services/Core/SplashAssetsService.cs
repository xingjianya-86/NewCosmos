using System.Text;
using System.Text.Json;
using NewCosmos.Models.Options;

namespace NewCosmos.Services.Core;

/// <summary>
/// 启动动画资源服务（单例）：组装 Web 资产 + 本次启动配置。
/// 虚拟主机加载方案见 <see cref="ISplashAssetsService"/> 说明。
/// </summary>
public class SplashAssetsService : BaseService, ISplashAssetsService
{
    private const string ConfigFileName = "config.js";

    private readonly AppOptions _appOptions;
    private readonly PreferencesOptions _preferences;

    public SplashAssetsService(
        AppOptions appOptions,
        PreferencesOptions preferences,
        ILoggerService logger)
        : base(logger)
    {
        _appOptions = appOptions;
        _preferences = preferences;
    }

    protected override string ServiceName => "SplashAssetsService";

    public string StartUrl => $"https://{ISplashAssetsService.VirtualHost}/splash.html";

#if WINDOWS
    public string WindowsFolder => Path.Combine(FileSystem.AppDataDirectory, "Splash");
#endif

    public Task PrepareAsync(CancellationToken ct = default)
    {
#if WINDOWS
        // Windows：WebView2 虚拟主机只能映射目录，把构建输出里的资产同步到 AppData 可写目录，
        // 再写入本次启动的 config.js（程序装在 Program Files 时输出目录只读）。
        return Task.Run(() => PrepareWindowsFolder(), ct);
#else
        return Task.CompletedTask;
#endif
    }

    /// <summary>上次登录用户名（登录页"记住账号"），用于终幕欢迎语</summary>
    private string? ResolveRememberedUsername()
    {
        try
        {
            var username = Preferences.Get(_preferences.RememberedUsernameKey, string.Empty);
            return string.IsNullOrWhiteSpace(username) ? null : username.Trim();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>生成 config.js：动画运行时的全部配置（品牌文案、身份、配色、音效开关）</summary>
    private string BuildConfigScript()
    {
        // 版本号取 app.ini（AppInfo.VersionString 在未打包 WinUI3 下是 0.0.0.0）
        var version = string.IsNullOrWhiteSpace(_appOptions.Version) ? "1.0" : _appOptions.Version;
        var username = ResolveRememberedUsername();
        var identity = username ?? "操作员";
        var welcomeText = username != null
            ? $"欢迎回来 · {username}"
            : "欢迎使用 · 社会救助管理系统";
        var platform = DeviceInfo.Platform == DevicePlatform.Android ? "MOBILE" : "DESKTOP";

        var config = new Dictionary<string, object?>
        {
            // 播放控制
            ["enabled"] = true,
            ["mode"] = "always",                     // 每次启动都播；app.ini SplashEnabled 控制总开关
            ["theme"] = "light",                     // 暖白"技术档案"底板（参考片原色）
            ["speed"] = Math.Clamp(_appOptions.SplashSpeed, 0.35, 3),
            ["requireInteraction"] = _appOptions.SplashRequireInteraction,
            ["allowSkip"] = true,

            // 音频：sound=界面音效，voice=真声台词，二者由 SplashSound 统一控制
            ["sound"] = _appOptions.SplashSound,
            ["soundVolume"] = 0.5,
            ["voice"] = _appOptions.SplashSound,
            ["voiceSource"] = "asset",               // 用内置 mp3，不退回系统 TTS
            ["voiceCue"] = true,
            ["voiceLead"] = 240,
            ["voiceRate"] = 0.88,
            ["voicePitch"] = 0.85,
            ["voiceVolume"] = 0.9,

            // 身份资料（第二步身份卡 / 终幕欢迎语）
            ["identity"] = identity,
            ["accessLevel"] = "3",
            ["welcomeText"] = welcomeText,

            // 品牌文案（全量本系统化，不含任何第三方品牌）
            ["brand1"] = "NEWCOSMOS",
            ["brand2"] = "SOCIAL ASSISTANCE MANAGEMENT",
            ["brand3a"] = "帝皇权杖",
            ["brand3b"] = "δ-me13",
            ["orgZh"] = "民政社会救助",
            ["orgEn"] = "NEWCOSMOS",
            ["orgSub"] = "SOCIAL ASSISTANCE SYSTEM",
            ["logoLabel"] = "N E W C O S M O S",
            ["welcomeLabel"] = "N E W C O S M O S · 社会救助",
            ["welcomeBox"] = "帝皇权杖δ-me13",
            ["footer"] = "© 2026 帝皇权杖δ-me13 社会救助管理系统",
            ["metaLine"] = $"REV {version} · {platform}",

            // 配色：强调色取系统主题 Primary (#1a73e8)
            ["accent"] = "#1a73e8",
            ["glitchA"] = "#ff3b30",
            ["glitchB"] = "#2971b8"
        };

        var json = JsonSerializer.Serialize(config);
        return "window.__DSH_STARTUP__ = " + json + ";\n" +
               "window.__DSH_STARTUP_SCRIPT_URL__ = 'splash.js';\n";
    }

#if WINDOWS
    /// <summary>
    /// 把构建输出 splash\ 下的静态资产同步到 AppData\Splash\，并写入 config.js 与揭幕背景图。
    /// 用 App 版本号 + splash.js 时间戳做戳记：升级替换资产后自动重新复制。
    /// </summary>
    private void PrepareWindowsFolder()
    {
        var sourceDir = Path.Combine(AppContext.BaseDirectory, "splash");
        var targetDir = WindowsFolder;

        if (!Directory.Exists(sourceDir))
            throw new DirectoryNotFoundException($"启动动画资产目录不存在: {sourceDir}");

        Directory.CreateDirectory(targetDir);

        var stampPath = Path.Combine(targetDir, ".assets.stamp");
        var jsPath = Path.Combine(sourceDir, "splash.js");
        var stamp = $"{AppInfo.Current.VersionString}|{(File.Exists(jsPath) ? File.GetLastWriteTimeUtc(jsPath).Ticks : 0)}";

        var needCopy = !File.Exists(Path.Combine(targetDir, "splash.html"))
                       || !File.Exists(stampPath)
                       || !string.Equals(File.ReadAllText(stampPath), stamp, StringComparison.Ordinal);

        if (needCopy)
        {
            CopyDirectory(sourceDir, targetDir);
            File.WriteAllText(stampPath, stamp);
            LogInfo($"启动动画资产已同步到 {targetDir}");
        }

        Directory.CreateDirectory(Path.Combine(targetDir, "img"));
        File.WriteAllText(Path.Combine(targetDir, ConfigFileName), BuildConfigScript(), new UTF8Encoding(false));
    }

    private static void CopyDirectory(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var name = Path.GetFileName(file);
            if (name.Equals(ConfigFileName, StringComparison.OrdinalIgnoreCase))
                continue; // config.js 每次启动重新生成
            File.Copy(file, Path.Combine(targetDir, name), overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(dir, Path.Combine(targetDir, Path.GetFileName(dir)));
        }
    }
#endif

#if ANDROID
    /// <summary>
    /// Android：ShouldInterceptRequest 按相对路径返回内容。
    /// config.js 每次内存生成（不落盘），其余走 APK assets\splash\。
    /// </summary>
    public byte[]? TryReadVirtualFile(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        var path = relativePath.TrimStart('/');

        if (path.Equals(ConfigFileName, StringComparison.OrdinalIgnoreCase))
            return Encoding.UTF8.GetBytes(BuildConfigScript());

        try
        {
            using var stream = Android.App.Application.Context.Assets?.Open("splash/" + path);
            if (stream == null)
                return null;

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }
        catch (Exception ex)
        {
            LogWarn($"读取启动动画资产失败: {path} — {ex.Message}");
            return null;
        }
    }
#endif
}
