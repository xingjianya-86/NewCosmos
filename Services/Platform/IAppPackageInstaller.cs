using NewCosmos.Models.Results;

namespace NewCosmos.Services.Platform;

/// <summary>
/// 安装已下载的更新包（Windows=启动 Inno 安装器；Android=触发 APK 安装 Intent）。
/// </summary>
public interface IAppPackageInstaller
{
    /// <summary>当前平台是否支持自动安装更新包</summary>
    bool IsSupported { get; }

    /// <summary>安装更新包（调用方随后可能退出应用）。</summary>
    Result InstallPackage(string filePath, string version);
}
