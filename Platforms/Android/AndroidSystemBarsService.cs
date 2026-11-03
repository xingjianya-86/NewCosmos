#if ANDROID
using Android.App;
using Android.Views;
using NewCosmos.Services.Core;
using NewCosmos.Services.Platform;

namespace NewCosmos.Platforms.Android;

/// <summary>
/// 安卓系统栏形态控制（纸墨档案风全屏）。
/// 两种模式（config\app.ini [Android] FullscreenMode）：
///  - Immersive（默认）：隐藏状态栏 + 导航栏，内容顶到屏幕最上沿；边缘下滑临时呼出。
///  - EdgeToEdge：状态栏透明、内容延伸到底下，但时钟/电量仍可见。
/// 实现源自 LoginPage 已实测的 edge-to-edge 写法，此处抽为全 App 复用的单点服务。
/// </summary>
public sealed class AndroidSystemBarsService : ISystemBarsService
{
    // android.view.WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS（深色状态栏图标位）
    private const int AppearanceLightStatusBars = 1;
    // BEHAVIOR_SHOW_TRANSIENT_BARS_BY_SWIPE：系统栏隐藏后，边缘下滑临时呼出并自动消失
    private const int BehaviorShowTransientBarsBySwipe = 1;

    private readonly IConfigService _config;
    private bool _applied;

    public AndroidSystemBarsService(IConfigService config)
    {
        _config = config;
    }

    private static global::Android.Views.Window? CurrentWindow =>
        Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window;

    public void Apply()
    {
        var window = CurrentWindow;
        if (window == null)
            return;

        // 读取配置：Immersive（默认隐藏）/ EdgeToEdge（透明可见）
        bool immersive;
        try
        {
            immersive = _config.GetAppOptions().AndroidImmersiveFullscreen;
        }
        catch
        {
            immersive = true; // 配置读取失败时按默认沉浸式处理，不阻断启动
        }

#pragma warning disable CA1422 // SetDecorFitsSystemWindows/SetStatusBarColor 在 API 35+ 标记过时；
                               // 本调用点正是为 API 30–34 生效而设，API 35+ 系统强制 edge-to-edge，此处为无害 no-op。
        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.R)
        {
            var controller = window.InsetsController;
            if (controller != null)
            {
                if (immersive)
                {
                    // 隐藏状态栏 + 导航栏，下滑临时呼出
                    var bars = WindowInsets.Type.StatusBars() | WindowInsets.Type.NavigationBars();
                    controller.Hide(bars);
                    controller.SystemBarsBehavior = BehaviorShowTransientBarsBySwipe;
                    // 全屏沉浸式下不置 light-status-bars（无状态栏可着色）
                    controller.SetSystemBarsAppearance(0, AppearanceLightStatusBars);
                }
                else
                {
                    // EdgeToEdge：内容延伸，保留系统栏；暖纸底 → 深色图标
                    window.SetDecorFitsSystemWindows(false);
                    controller.Show(WindowInsets.Type.StatusBars() | WindowInsets.Type.NavigationBars());
                    controller.SetSystemBarsAppearance(AppearanceLightStatusBars, AppearanceLightStatusBars);
                }
            }

            window.SetStatusBarColor(global::Android.Graphics.Color.Transparent);
            window.SetNavigationBarColor(global::Android.Graphics.Color.Transparent);
        }
        else
        {
#pragma warning disable CS0618 // SystemUiVisibility 在 API 30 以下使用
            var flags = SystemUiFlags.LayoutStable | SystemUiFlags.LayoutFullscreen | SystemUiFlags.LayoutHideNavigation;
            if (immersive)
            {
                flags |= SystemUiFlags.Fullscreen | SystemUiFlags.HideNavigation | SystemUiFlags.ImmersiveSticky;
            }
            else
            {
                flags |= SystemUiFlags.LightStatusBar;
            }
            window.DecorView.SystemUiVisibility = (StatusBarVisibility)flags;
#pragma warning restore CS0618
        }
#pragma warning restore CA1422

        _applied = true;
    }

    public void ShowTransient()
    {
        if (!_applied)
            return;

        var window = CurrentWindow;
        window?.InsetsController?.Show(
            WindowInsets.Type.StatusBars() | WindowInsets.Type.NavigationBars());
    }
}
#endif
