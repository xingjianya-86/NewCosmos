using NewCosmos.Models;
using NewCosmos.Models.Options;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Core;

/// <summary>
/// 在线更新流程协调器实现（唯一流程实现，Windows/Android 共用）。
/// </summary>
public class AppUpdateCoordinator : IAppUpdateCoordinator
{
    private readonly IUpdateService _updateService;
    private readonly IDialogService _dialogService;
    private readonly ILoadingProgressService _progressService;
    private readonly ILoggerService _logger;
    private readonly ILoadingProgressRunner _progressRunner;

    public AppUpdateCoordinator(
        IUpdateService updateService,
        IDialogService dialogService,
        ILoadingProgressService progressService,
        ILoggerService logger,
        ILoadingProgressRunner progressRunner)
    {
        _updateService = updateService;
        _dialogService = dialogService;
        _progressService = progressService;
        _logger = logger;
        _progressRunner = progressRunner;
    }

    public async Task<bool> CheckAndPromptAsync(bool manual, CancellationToken ct = default)
    {
        try
        {
            if (!_updateService.Options.Enabled)
            {
                if (manual)
                    await _dialogService.DisplayAlertAsync("检查更新", "在线更新未启用（config/update.ini）", "确定");
                return false;
            }

            var check = await _updateService.CheckAsync(manual, ct);
            if (check.IsFailure)
            {
                _logger.Warn($"更新检查失败: {check.Message}");
                if (manual)
                    await _dialogService.DisplayAlertAsync("检查更新", $"无法连接更新服务器：{check.Message}", "确定");
                return false;
            }

            var result = check.Value;
            if (!result.UpdateAvailable || result.Info == null)
            {
                if (manual)
                    await _dialogService.DisplayAlertAsync("检查更新", $"当前已是最新版本（{_updateService.CurrentVersion}）", "确定");
                return false;
            }

            var info = result.Info;
            if (!result.ForceUpdate && !manual
                && string.Equals(_updateService.GetSkippedVersion(), info.LatestVersion, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var message = BuildUpdateMessage(info);
            string? choice;
            if (result.ForceUpdate)
            {
                var go = await _dialogService.DisplayAlertAsync("必须更新",
                    message + "\n\n本次为强制更新，请尽快完成升级。", "立即更新", "稍后");
                choice = go ? "立即更新" : "稍后";
            }
            else
            {
                choice = await _dialogService.DisplayActionSheetAsync(
                    $"发现新版本 {info.LatestVersion}", "稍后", null, "立即更新", "跳过此版本");
            }

            if (choice == "跳过此版本")
            {
                _updateService.SetSkippedVersion(info.LatestVersion);
                return false;
            }
            if (choice != "立即更新") return false;

            return await DownloadAndInstallAsync(info, ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "检查更新异常");
            if (manual)
                await _dialogService.DisplayAlertAsync("检查更新", $"检查失败：{ex.Message}", "确定");
            return false;
        }
    }

    private string BuildUpdateMessage(UpdateInfo info)
    {
        var sizeText = info.PackageSize > 0 ? $"{info.PackageSize / 1024d / 1024d:F0} MB" : "未知大小";
        var notes = string.IsNullOrWhiteSpace(info.Notes) ? string.Empty : $"\n\n更新说明：\n{info.Notes}";
        return $"当前版本：{_updateService.CurrentVersion}\n最新版本：{info.LatestVersion}（{sizeText}）{notes}";
    }

    private async Task<bool> DownloadAndInstallAsync(UpdateInfo info, CancellationToken ct)
    {
        var completed = await _progressRunner.RunAsync(async innerCt =>
        {
            _progressService.UpdateProgress(0, "正在下载更新包...");
            var download = await _updateService.DownloadAsync(info, p =>
                _progressService.UpdateProgress(
                    (int)Math.Clamp(p / 100 * LoadingSteps.Total, 0, LoadingSteps.Total),
                    $"正在下载更新包... {p:F0}%"), innerCt);
            if (download.IsFailure)
                throw new Exception(download.Message);

            _progressService.UpdateProgress(LoadingSteps.Total, "正在启动更新程序...");
            var launch = _updateService.LaunchInstaller(download.Value, info.LatestVersion);
            if (launch.IsFailure)
                throw new Exception(launch.Message);
        });

        if (!completed)
        {
            await _dialogService.DisplayAlertAsync("更新",
                "更新未完成，可稍后重试或联系管理员手动安装。", "确定");
        }
        return completed;
    }
}
