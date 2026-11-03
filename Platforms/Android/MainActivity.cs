using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using NewCosmos.Services.Platform;

namespace NewCosmos;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation |
                           ConfigChanges.UiMode | ConfigChanges.ScreenLayout |
                           ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
    }

    /// <summary>
    /// 锁定系统字体缩放（fontScale 强制 1.0）：手机端字号由应用内 OnPlatform 分档决定，
    /// 不随系统「字体大小」设置整体放大（否则 18 号正文在大字体档下会再乘 1.15~2 倍导致界面拥挤）。
    /// 需在 AttachBaseContext 用 CreateConfigurationContext 生效，见 docs 与 AGENTS §1。
    /// </summary>
    protected override void AttachBaseContext(Context? context)
    {
        if (context?.Resources?.Configuration is { } baseConfig)
        {
            var config = new Configuration(baseConfig);
            config.FontScale = 1f;
            context = context.CreateConfigurationContext(config);
        }
        base.AttachBaseContext(context);
    }

    /// <summary>将 SAF 文件夹选择结果回填给 <see cref="AndroidFolderPickerService"/>。</summary>
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (AndroidFolderPickerService.HandleActivityResult(requestCode, resultCode, data))
            return;

        base.OnActivityResult(requestCode, resultCode, data);
    }
}
