using Android.App;
using Android.Content;
using Android.Content.PM;
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

    /// <summary>将 SAF 文件夹选择结果回填给 <see cref="AndroidFolderPickerService"/>。</summary>
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (AndroidFolderPickerService.HandleActivityResult(requestCode, resultCode, data))
            return;

        base.OnActivityResult(requestCode, resultCode, data);
    }
}
