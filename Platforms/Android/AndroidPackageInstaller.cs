using Android.Content;
using Android.OS;
using Android.Provider;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;

namespace NewCosmos.Services.Platform;

/// <summary>
/// Android 更新包安装：经 FileProvider 触发系统 APK 安装 Intent。
/// 需 AndroidManifest 声明 REQUEST_INSTALL_PACKAGES 权限与 fileprovider。
/// </summary>
public sealed class AndroidPackageInstaller : IAppPackageInstaller
{
    private readonly ILoggerService _logger;

    public AndroidPackageInstaller(ILoggerService logger)
    {
        _logger = logger;
    }

    public bool IsSupported => true;

    public Result InstallPackage(string filePath, string version)
    {
        try
        {
            var context = global::Android.App.Application.Context;
            var file = new Java.IO.File(filePath);
            if (!file.Exists())
                return Result.Failure(ErrorCodes.FILE_NOT_FOUND, "更新包不存在");

            // Android 8.0+ 需显式授予"安装未知来源应用"权限
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O
                && context.PackageManager != null
                && !context.PackageManager.CanRequestPackageInstalls())
            {
                var settings = new Intent(Settings.ActionManageUnknownAppSources);
                settings.SetData(Android.Net.Uri.Parse("package:" + context.PackageName));
                settings.AddFlags(ActivityFlags.NewTask);
                context.StartActivity(settings);
                return Result.Failure(ErrorCodes.PERMISSION_DENIED,
                    "请先允许本应用安装未知来源应用，然后重新点击更新");
            }

            var authority = context.PackageName + ".fileprovider";
            var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(context, authority, file);

            var intent = new Intent(Intent.ActionView);
            intent.SetDataAndType(uri, "application/vnd.android.package-archive");
            intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
            context.StartActivity(intent);

            _logger.LogBusiness("已触发 APK 安装", ("File", filePath), ("Version", version));
            return Result.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "触发 APK 安装失败");
            return Result.FromException(ex);
        }
    }
}
