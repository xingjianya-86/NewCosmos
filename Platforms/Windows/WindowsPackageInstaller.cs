using System.Diagnostics;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Platform;

namespace NewCosmos.Platforms.Windows;

/// <summary>
/// Windows 更新包安装：启动 Inno 安装器静默升级（/UPDATE=1 安装完成后自动重启新版）。
/// </summary>
public sealed class WindowsPackageInstaller : IAppPackageInstaller
{
    private readonly ILoggerService _logger;

    public WindowsPackageInstaller(ILoggerService logger)
    {
        _logger = logger;
    }

    public bool IsSupported => true;

    public Result InstallPackage(string filePath, string version)
    {
        try
        {
            var logPath = Path.Combine(Path.GetTempPath(), $"newcosmos_update_{version}.log");
            var psi = new ProcessStartInfo
            {
                FileName = filePath,
                Arguments = $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE=1 /LOG=\"{logPath}\"",
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(filePath) ?? Path.GetTempPath()
            };

            var process = Process.Start(psi);
            if (process == null)
                return Result.Failure(ErrorCodes.NETWORK_ERROR, "无法启动更新安装程序");

            _logger.LogBusiness("已启动更新安装程序，应用即将退出",
                ("Installer", filePath), ("Version", version), ("Pid", process.Id));
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动更新安装程序失败");
            return Result.FromException(ex);
        }
    }
}
