#if ANDROID
using Android.Views;
using NewCosmos.Services.Platform;

namespace NewCosmos.Platforms.Android;

/// <summary>
/// 安卓屏幕常亮：当前 Activity 窗口加 <see cref="WindowManagerFlags.KeepScreenOn"/>。
/// 不只清一次全量 flag——只清本服务加的那一位，避免误伤他人设置的窗口标志。
/// 必须在 UI 线程操作窗口（调用方可能在后台线程释放租约）。
/// </summary>
public sealed class ScreenAwakeService : ScreenAwakeServiceBase
{
    private const string LogPrefix = "[ScreenAwake]";

    protected override void SetAwakeCore(bool awake)
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        var window = activity?.Window;
        if (activity == null || window == null)
            return;

        activity.RunOnUiThread(() =>
        {
            try
            {
                if (awake)
                    window.AddFlags(WindowManagerFlags.KeepScreenOn);
                else
                    window.ClearFlags(WindowManagerFlags.KeepScreenOn);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"{LogPrefix} 设置常亮失败: {ex.Message}");
            }
        });
    }
}
#endif
