using NewCosmos.ViewModels.Auth;

namespace NewCosmos.Pages.Auth;

public partial class LoginPage : ContentPage
{
    private readonly LoginViewModel _viewModel;
#if ANDROID
    private Android.Graphics.Color? _prevStatusBarColor;
    private bool _edgeToEdgeApplied;
#endif

    public LoginPage(LoginViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        // 永劫回归特效每删除一个人员 → 顺序切换一张背景图（仅 Windows）
        if (DeviceInfo.Platform == DevicePlatform.WinUI)
            EternalRegressionEffect.PersonDeleted += OnEternalPersonDeleted;
    }

    private void OnEternalPersonDeleted(object? sender, EventArgs e)
    {
        _viewModel.ChangeBackgroundCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();
        EternalRegressionEffect.PersonDeleted -= OnEternalPersonDeleted;
#if ANDROID
        // 离开登录页恢复默认状态栏，避免透明状态栏泄漏到主界面
        RestoreStatusBar();
#endif
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
#if ANDROID
        // 登录页全屏延伸：背景图盖满全屏（含状态栏区域），状态栏透明+浅色图标
        ApplyFullScreenStatusBar();
#endif

        // 在页面加载时初始化记住设置
        await _viewModel.LoadRememberSettingsCommand.ExecuteAsync(null);

        // 登录窗前静默检查更新（强制更新在 VM 内拦截；失败/断网仅记日志不阻断登录）
        _ = _viewModel.CheckForUpdatesOnStartupAsync();
    }

#if ANDROID
    /// <summary>android.view.WindowInsetsController.APPEARANCE_LIGHT_STATUS_BARS（深色状态栏图标位）。</summary>
    private const int AppearanceLightStatusBars = 1;

    /// <summary>
    /// 登录页全屏延伸：状态栏透明 + 窗口内容延伸到系统栏底下（仅登录页生效）。
    /// MAUI 主题默认 opt-out edge-to-edge（状态栏不透明且内容避让 → 顶部空白条），
    /// 此处仅在登录页手动开启，离开时恢复，不影响其他页面。
    /// </summary>
    private void ApplyFullScreenStatusBar()
    {
        var window = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window;
        if (window == null || _edgeToEdgeApplied)
            return;

        _prevStatusBarColor = new Android.Graphics.Color(window.StatusBarColor);

        // CA1422: SetDecorFitsSystemWindows/SetStatusBarColor 在 API 35+ 标记过时，
        // 但本调用点正是为 API 30–34 生效而设；API 35+ 系统强制 edge-to-edge，此处为无害 no-op。
#pragma warning disable CA1422
        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.R)
        {
            window.SetDecorFitsSystemWindows(false);
            // 背景多为深色照片 → 用浅色（白）状态栏图标（清除 light-status-bars 位）
            window.InsetsController?.SetSystemBarsAppearance(0, AppearanceLightStatusBars);
        }
        else
        {
#pragma warning disable CS0618
            window.DecorView.SystemUiVisibility = (Android.Views.StatusBarVisibility)(
                Android.Views.SystemUiFlags.LayoutStable | Android.Views.SystemUiFlags.LayoutFullscreen);
#pragma warning restore CS0618
        }

        window.SetStatusBarColor(Android.Graphics.Color.Transparent);
#pragma warning restore CA1422
        _edgeToEdgeApplied = true;
    }

    /// <summary>恢复系统默认状态栏（不透明原色 + 深色图标 + 内容避让）。</summary>
    private void RestoreStatusBar()
    {
        if (!_edgeToEdgeApplied)
            return;

        var window = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.Window;
        if (window == null)
            return;

        // CA1422: 同 ApplyFullScreenStatusBar——为 API 30–34 生效而设，35+ 为无害 no-op
#pragma warning disable CA1422
        if (Android.OS.Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.R)
        {
            window.SetDecorFitsSystemWindows(true);
            // 恢复深色状态栏图标（浅色主题默认）
            window.InsetsController?.SetSystemBarsAppearance(AppearanceLightStatusBars, AppearanceLightStatusBars);
        }
        else
        {
#pragma warning disable CS0618
            window.DecorView.SystemUiVisibility = Android.Views.StatusBarVisibility.Visible;
#pragma warning restore CS0618
        }

        if (_prevStatusBarColor.HasValue)
            window.SetStatusBarColor(_prevStatusBarColor.Value);
#pragma warning restore CA1422

        _edgeToEdgeApplied = false;
    }
#endif
}