using NewCosmos.Models.Options;
using NewCosmos.ViewModels.Auth;

namespace NewCosmos.Pages.Auth;

/// <summary>
/// 登录页（暖白「技术档案」版）：只做视觉与轻动画，登录逻辑全部在 LoginViewModel。
/// 视觉语言与启动动画同源：暖白底板、四角定位标记、等宽读数、双语状态字、环形准星。
/// </summary>
public partial class LoginPage : ContentPage
{
    private const string ReticleSpinName = "LoginReticleSpin";
    private const string InnerRingSpinName = "LoginInnerRingSpin";
    private const string ScanlineName = "LoginScanline";
    private const string ButtonPulseName = "LoginButtonPulse";

    private static readonly Color AccentColor = Color.FromArgb("#1A73E8");
    private static readonly Color HairColor = Color.FromArgb("#290A0A0A");

    private readonly LoginViewModel _viewModel;
    private readonly string _version;

    private IDispatcherTimer? _clockTimer;

    public LoginPage(LoginViewModel viewModel, AppOptions appOptions)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        _version = string.IsNullOrWhiteSpace(appOptions.Version) ? "1.0" : appOptions.Version;

        // 输入焦点语言：左侧强调条 + 细边框点亮
        HookFieldFocus(UsernameEntry, UserAccent, UserFieldBorder);
        HookFieldFocus(PasswordEntry, PassAccent, PassFieldBorder);
        HookFieldFocus(UsernameEntryPhone, UserAccentPhone, UserFieldBorderPhone);
        HookFieldFocus(PasswordEntryPhone, PassAccentPhone, PassFieldBorderPhone);

        UpdateClockText();
    }

    private void HookFieldFocus(Entry entry, BoxView accent, Border border)
    {
        entry.Focused += (_, _) => SetFieldActive(accent, border, true);
        entry.Unfocused += (_, _) => SetFieldActive(accent, border, false);

#if WINDOWS
        // WinUI TextBox 自带边框与聚焦描边，会与外层档案框叠成"双层框"
        StripWindowsEntryChrome(entry);
        entry.HandlerChanged += (_, _) => StripWindowsEntryChrome(entry);
        entry.Loaded += (_, _) => StripWindowsEntryChrome(entry);
#endif
    }

#if WINDOWS
    /// <summary>
    /// 去掉 WinUI Entry 自带的边框/背景/聚焦描边，只保留档案面板自己的外框。
    /// 注意：聚焦描边由主题资源（TextControlBorderBrushFocused / …ThicknessFocused）驱动，
    /// 只改 BorderThickness 会被可视状态覆盖，必须同时覆盖资源键。
    /// </summary>
    private static void StripWindowsEntryChrome(Entry entry)
    {
        if (entry.Handler?.PlatformView is not Microsoft.UI.Xaml.Controls.TextBox textBox)
            return;

        textBox.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
        textBox.Background = null;
        textBox.Padding = new Microsoft.UI.Xaml.Thickness(2, 0, 2, 0);
        textBox.FocusVisualPrimaryThickness = new Microsoft.UI.Xaml.Thickness(0);
        textBox.FocusVisualSecondaryThickness = new Microsoft.UI.Xaml.Thickness(0);

        var transparent = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        textBox.Resources["TextControlBorderBrush"] = transparent;
        textBox.Resources["TextControlBorderBrushFocused"] = transparent;
        textBox.Resources["TextControlBorderThemeThickness"] = new Microsoft.UI.Xaml.Thickness(0);
        textBox.Resources["TextControlBorderThemeThicknessFocused"] = new Microsoft.UI.Xaml.Thickness(0);
    }
#endif

    private static void SetFieldActive(BoxView accent, Border border, bool active)
    {
        accent.Color = active ? AccentColor : Colors.Transparent;
        border.Stroke = new SolidColorBrush(active ? AccentColor : HairColor);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();

        _clockTimer?.Stop();
        _clockTimer = null;
        this.AbortAnimation(ReticleSpinName);
        this.AbortAnimation(InnerRingSpinName);
        this.AbortAnimation(ScanlineName);
        this.AbortAnimation(ButtonPulseName);
        // 安卓系统栏形态已由全局 AndroidSystemBarsService 在窗口创建时统一接管，
        // 登录页不再单独改/恢复状态栏（旧实现离开时恢复系统栏会与全局沉浸式打架，造成切页闪烁）
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // 安卓系统栏形态由全局 AndroidSystemBarsService 统一套用，此处无需再处理

        StartClock();
        PlayEntrance();
        StartAmbientAnimation();

        // 在页面加载时初始化记住设置
        await _viewModel.LoadRememberSettingsCommand.ExecuteAsync(null);

        // 登录窗前静默检查更新（强制更新在 VM 内拦截；失败/断网仅记日志不阻断登录）
        _ = _viewModel.CheckForUpdatesOnStartupAsync();
    }

    /// <summary>进页动画：品牌块左→右擦出、状态区与授权面板淡入上浮（一次性）</summary>
    private void PlayEntrance()
    {
        PlayEntranceFor(BrandBlock, fromX: -18, fromY: 0, delayMs: 0);
        PlayEntranceFor(BrandBlockPhone, fromX: -18, fromY: 0, delayMs: 0);
        PlayEntranceFor(StatusBlock, fromX: 0, fromY: 0, delayMs: 90);
        PlayEntranceFor(FormPanel, fromX: 0, fromY: 16, delayMs: 160);
        PlayEntranceFor(FormPanelPhone, fromX: 0, fromY: 16, delayMs: 160);
    }

    private static async void PlayEntranceFor(VisualElement element, double fromX, double fromY, int delayMs)
    {
        try
        {
            element.Opacity = 0;
            element.TranslationX = fromX;
            element.TranslationY = fromY;

            if (delayMs > 0)
                await Task.Delay(delayMs);

            await Task.WhenAll(
                element.FadeToAsync(1, 480, Easing.CubicOut),
                element.TranslateToAsync(0, 0, 520, Easing.CubicOut));
        }
        catch
        {
            // 页面提前离开时动画可能被打断：直接落到终态，绝不留半透明
            element.Opacity = 1;
            element.TranslationX = 0;
            element.TranslationY = 0;
        }
    }

    /// <summary>常驻动画：准星匀速自转 + 授权按钮脉冲（离开页面时 Abort）</summary>
    private void StartAmbientAnimation()
    {
        var spin = new Animation(v =>
        {
            ReticleRing.Rotation = v;
            ReticleRingPhone.Rotation = v;
        }, 0, 360);
        spin.Commit(this, ReticleSpinName, rate: 16, length: 18000, easing: Easing.Linear, finished: null, repeat: () => true);

        // 内环反向旋转，形成"对转"的机械感
        var innerSpin = new Animation(v => ReticleRingInner.Rotation = v, 360, 0);
        innerSpin.Commit(this, InnerRingSpinName, rate: 16, length: 26000, easing: Easing.Linear, finished: null, repeat: () => true);

        // 扫描光带：面板内部（560 - 2×36 内边距 = 488）自左向右缓慢掠过
        var scan = new Animation(v => LeftScanline.TranslationX = v, -90, 488);
        scan.Commit(this, ScanlineName, rate: 16, length: 6200, easing: Easing.Linear, finished: null, repeat: () => true);

        var pulse = new Animation();
        pulse.Add(0, 0.5, new Animation(v => SetButtonOpacity(v), 1, 0.93, Easing.SinIn));
        pulse.Add(0.5, 1, new Animation(v => SetButtonOpacity(v), 0.93, 1, Easing.SinOut));
        pulse.Commit(this, ButtonPulseName, rate: 16, length: 2600, easing: Easing.Linear, finished: null, repeat: () => true);
    }

    private void SetButtonOpacity(double value)
    {
        LoginButton.Opacity = value;
        LoginButtonPhone.Opacity = value;
    }

    private void StartClock()
    {
        if (_clockTimer == null)
        {
            _clockTimer = Dispatcher.CreateTimer();
            _clockTimer.Interval = TimeSpan.FromSeconds(1);
            _clockTimer.Tick += (_, _) => UpdateClockText();
        }

        UpdateClockText();
        _clockTimer.Start();
    }

    /// <summary>右上角技术读数：版本 + 端类型 + 实时时钟（热路径，仅更新文本）</summary>
    private void UpdateClockText()
    {
        var mode = DeviceInfo.Platform == DevicePlatform.Android ? "MOBILE" : "DESKTOP";
        var text = $"REV {_version} · {mode} {DateTime.Now:HH:mm:ss}";
        ClockLabel.Text = text;
        ClockLabelPhone.Text = text;
    }
}
