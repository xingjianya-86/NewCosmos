namespace NewCosmos.Models.Options;

/// <summary>
/// 在线更新配置（config/update.ini）。
/// 文件缺失或 Enabled=false 时更新功能关闭，不影响应用启动。
/// </summary>
public class UpdateOptions
{
    /// <summary>是否启用在线更新检查</summary>
    public bool Enabled { get; set; }

    /// <summary>更新清单地址（分号分隔，按顺序回退尝试）</summary>
    public List<string> ManifestUrls { get; set; } = new();

    /// <summary>
    /// Android 专用更新清单地址（分号分隔）。
    /// 留空时由 ManifestUrls 推导（把 "/stable/" 替换为 "/android/"）。
    /// Android 清单与 Windows 清单同 schema、同签名，仅 package.url 指向 .apk。
    /// </summary>
    public List<string> AndroidManifestUrls { get; set; } = new();

    /// <summary>启动时（登录窗前）是否静默检查更新</summary>
    public bool CheckOnStartup { get; set; } = true;

    /// <summary>登录后定时检查间隔（分钟，最小 30）</summary>
    public int CheckIntervalMinutes { get; set; } = 240;

    /// <summary>检查/下载的 HTTP 超时（秒）</summary>
    public int HttpTimeoutSeconds { get; set; } = 30;

    /// <summary>安装包下载超时（秒）</summary>
    public int DownloadTimeoutSeconds { get; set; } = 900;

    public void Normalize()
    {
        if (CheckIntervalMinutes < 30) CheckIntervalMinutes = 30;
        if (HttpTimeoutSeconds < 5) HttpTimeoutSeconds = 5;
        if (DownloadTimeoutSeconds < 60) DownloadTimeoutSeconds = 60;
        ManifestUrls = ManifestUrls
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        AndroidManifestUrls = AndroidManifestUrls
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => u.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
