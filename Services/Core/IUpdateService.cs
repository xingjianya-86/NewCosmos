using NewCosmos.Models.Options;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Core;

/// <summary>在线更新信息（清单解析并验签后）</summary>
public sealed record UpdateInfo(
    string LatestVersion,
    string MinSupportedVersion,
    bool ForceByManifest,
    string Notes,
    string PackageUrl,
    long PackageSize,
    string PackageSha256,
    DateTime? PublishedAt);

/// <summary>更新检查结果</summary>
public sealed record UpdateCheckResult(bool UpdateAvailable, bool ForceUpdate, UpdateInfo? Info);

/// <summary>
/// 在线更新服务：清单验签 → 版本比较 → 下载校验 → 静默安装。
/// 更新开关与地址来自 config/update.ini（文件缺失=关闭）。
/// </summary>
public interface IUpdateService
{
    /// <summary>当前更新配置</summary>
    UpdateOptions Options { get; }

    /// <summary>当前运行版本（config/app.ini 的 Version）</summary>
    string CurrentVersion { get; }

    /// <summary>检查更新（多源按序回退；验签失败不采用该源）</summary>
    Task<Result<UpdateCheckResult>> CheckAsync(bool manual, CancellationToken ct = default);

    /// <summary>下载安装包到本地缓存并校验 SHA-256，返回安装包路径</summary>
    Task<Result<string>> DownloadAsync(UpdateInfo info, Action<double>? onProgress, CancellationToken ct = default);

    /// <summary>静默启动安装器（调用方随后退出应用；安装器 /UPDATE=1 会重新拉起新版）</summary>
    Result LaunchInstaller(string installerPath, string version);

    /// <summary>已跳过的版本（非强制更新可跳过）</summary>
    string? GetSkippedVersion();

    /// <summary>记录跳过的版本</summary>
    void SetSkippedVersion(string version);
}
