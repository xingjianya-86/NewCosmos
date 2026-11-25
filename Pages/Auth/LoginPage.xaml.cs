using System.Text.Json;
using NewCosmos.Components;
using NewCosmos.Constants;
using NewCosmos.Models.Options;
using NewCosmos.Services.Core;
using NewCosmos.ViewModels.Auth;

namespace NewCosmos.Pages.Auth;

/// <summary>
/// 登录页（暖白「技术档案」版）：只做视觉与轻动画，登录逻辑全部在 LoginViewModel。
/// 视觉语言与启动动画同源：暖白底板、四角定位标记、等宽读数、双语状态字、环形准星。
///
/// 序列登录（app.ini LoginStyle=sequence，默认）：本页额外显示 <see cref="SequenceHost"/>
/// 覆盖层，整页 WebView 播放五步验证序列（login.html），第 2 步嵌真实账号密码表单，
/// 认证结果驱动后续步骤与终幕；任何故障（WebView 初始化失败 / 脚本回执超时 / 脚本异常 /
/// 用户点「切换经典登录」）都只是隐藏覆盖层——经典 XAML 本来就在底下渲染，回落无闪切。
/// </summary>
public partial class LoginPage : ContentPage
{
    private const string ReticleSpinName = "LoginReticleSpin";
    private const string InnerRingSpinName = "LoginInnerRingSpin";
    private const string ScanlineName = "LoginScanline";
    private const string ButtonPulseName = "LoginButtonPulse";

    private static readonly Color AccentColor = Color.FromArgb("#1A73E8");
    private static readonly Color HairColor = Color.FromArgb("#290A0A0A");

    private static readonly JsonSerializerOptions SubmitJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly LoginViewModel _viewModel;
    private readonly string _version;
    private readonly ISplashAssetsService _assets;
    private readonly ILoggerService _logger;
    private readonly bool _sequenceMode;

    private AnimationWebViewView? _sequenceView;
    private Task? _rememberSettingsTask;
    private bool _fellBack;          // 已回落经典登录页
    private bool _authInFlight;      // 序列认证请求在途（防并发提交）
    private string? _lastSubmitId;   // 桥接双路（postMessage + 自定义协议）去重
    private bool _navigated;         // 认证成功后导航幂等
    private CancellationTokenSource? _doneWatchdogCts;

    private IDispatcherTimer? _clockTimer;

    public LoginPage(LoginViewModel viewModel, AppOptions appOptions,
        ISplashAssetsService assets, ILoggerService logger)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        _version = string.IsNullOrWhiteSpace(appOptions.Version) ? "1.0" : appOptions.Version;
        _assets = assets;
        _logger = logger;
        _sequenceMode = appOptions.IsSequenceLogin;

        // 输入焦点语言：左侧强调条 + 细边框点亮
        HookFieldFocus(UsernameEntry, UserAccent, UserFieldBorder);
        HookFieldFocus(PasswordEntry, PassAccent, PassFieldBorder);
        HookFieldFocus(UsernameEntryPhone, UserAccentPhone, UserFieldBorderPhone);
        HookFieldFocus(PasswordEntryPhone, PassAccentPhone, PassFieldBorderPhone);

        UpdateClockText();

        if (_sequenceMode)
        {
            // 序列模式：预加载记住设置（web 表单 prefill 依赖），并挂覆盖层
            _ = EnsureRememberSettingsAsync();
            InitSequenceHost();
        }
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

    /* ══════════════════ 序列登录（LoginStyle=sequence）══════════════════ */

    /// <summary>挂载序列登录覆盖层：透明底加载（透出下方经典版式），故障即隐藏回落</summary>
    private void InitSequenceHost()
    {
        _sequenceView = new AnimationWebViewView(_assets, _logger)
        {
            StartUrl = _assets.LoginUrl,
            // 加载期间透出下方经典登录页；web 底板（app.css）与本页 Plate 同源，观感连续
            PlatformBackgroundColor = Colors.Transparent
        };
        _sequenceView.BridgeReceived += OnSequenceBridge;
        _sequenceView.HostFaulted += OnSequenceFault;
        SequenceHost.Children.Add(_sequenceView);
        SequenceHost.IsVisible = true;
    }

    private void OnSequenceBridge(string kind, string payload)
    {
        if (_fellBack)
            return;

        switch (kind.ToLowerInvariant())
        {
            case "login-ready":
                // 页面脚本就绪：推送记住的账号/密码到 web 表单（不进 config.js、不入日志）
                _ = PushPrefillAsync();
                break;

            case "login-submit":
                // 凭据只在进程内传递：本分支绝不记录 payload
                _ = HandleSubmitAsync(payload);
                break;

            case "done":
                _ = HandleDoneAsync();
                break;

            case "login-skip":
                // 用户点「切换经典登录 ▸」/ Esc：隐藏覆盖层即回落（经典版式本就在下方）
                FallBackToClassic("用户切换到经典登录");
                break;

            case "error":
                _logger.Warn("[LoginPage] 序列登录脚本异常: " + payload);
                if (_viewModel.LastSignInSucceeded)
                    _ = NavigateAfterAuthAsync();   // 已认证成功：脚本异常也不拦路，直接进主页
                else
                    FallBackToClassic("脚本异常: " + payload);
                break;

            case "boot":
                break; // 脚本回执看门狗由 AnimationWebViewView 自行撤销

            case "audio":
                _logger.Info("[LoginPage] 音频上下文状态: " + payload);
                break;
        }
    }

    /// <summary>宿主链路故障（WebView2 初始化失败 / boot 回执超时 / 加载异常）→ 回落经典页</summary>
    private void OnSequenceFault(string reason)
    {
        if (_viewModel.LastSignInSucceeded)
        {
            // 认证已成功：故障只影响动画，直接进主页
            _ = NavigateAfterAuthAsync();
            return;
        }

        FallBackToClassic(reason);
    }

    /// <summary>把记住的账号/密码推给 web 表单（rememberPass=false 时密码留空）</summary>
    private async Task PushPrefillAsync()
    {
        try
        {
            await EnsureRememberSettingsAsync();

            var rememberPass = _viewModel.RememberPassword && !string.IsNullOrEmpty(_viewModel.Password);
            var json = JsonSerializer.Serialize(new
            {
                user = _viewModel.Username ?? string.Empty,
                pass = rememberPass ? _viewModel.Password : string.Empty,
                remember = _viewModel.RememberMe,
                rememberPass = rememberPass
            });

            if (_sequenceView != null)
                await _sequenceView.EvalAsync(
                    "window.__NC_BRIDGE__ && window.__NC_BRIDGE__.dispatch('prefill', " + json + ");");
        }
        catch (Exception ex)
        {
            _logger.Warn("[LoginPage] 推送登录预填失败: " + ex.Message);
        }
    }

    /// <summary>处理 web 表单提交：写入 VM 属性 → 执行认证 → 把结果派回页面（auth-result）</summary>
    private async Task HandleSubmitAsync(string payload)
    {
        if (_sequenceView == null)
            return;

        LoginSubmitPayload? submit;
        try
        {
            submit = JsonSerializer.Deserialize<LoginSubmitPayload>(payload, SubmitJsonOptions);
        }
        catch (Exception ex)
        {
            // 载荷可能含凭据：只记录异常类型与长度，不记录内容。
            Serilog.Log.Warning("[LoginPage] 登录提交载荷解析失败，已丢弃。Type={Type}, Length={Length}",
                ex.GetType().Name, payload?.Length ?? 0);
            return;
        }

        if (submit is null || string.IsNullOrEmpty(submit.Id))
            return;

        // 双路桥接（postMessage + 自定义协议）按提交号去重；在途认证不接受新提交
        if (string.Equals(submit.Id, _lastSubmitId, StringComparison.Ordinal) || _authInFlight)
            return;

        _lastSubmitId = submit.Id;
        _authInFlight = true;

        try
        {
            _viewModel.DeferNavigation = true;
            _viewModel.Username = submit.User ?? string.Empty;
            _viewModel.Password = submit.Pass ?? string.Empty;
            _viewModel.RememberMe = submit.Remember ?? true;
            _viewModel.RememberPassword = submit.RememberPass ?? false;

            await _viewModel.LoginCommand.ExecuteAsync(null);

            var ok = _viewModel.LastSignInSucceeded;
            var json = JsonSerializer.Serialize(new
            {
                id = submit.Id,
                ok,
                msg = ok ? string.Empty : _viewModel.ErrorMessage
            });

            if (_sequenceView != null)
                await _sequenceView.EvalAsync(
                    "window.__NC_BRIDGE__ && window.__NC_BRIDGE__.dispatch('auth-result', " + json + ");");

            if (ok)
            {
                if (_fellBack)
                {
                    // 认证在途时已回落经典页：动画已无关，认证成功直接进主页
                    _ = NavigateAfterAuthAsync();
                }
                else
                {
                    StartDoneWatchdog();
                }
            }
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "[LoginPage] 登录提交处理异常");
            // 必须回执失败结果，否则 web 端会永久等待 auth-result。
            try
            {
                var failJson = JsonSerializer.Serialize(new
                {
                    id = submit.Id,
                    ok = false,
                    msg = "登录处理异常，请重试"
                });
                if (_sequenceView != null)
                    await _sequenceView.EvalAsync(
                        "window.__NC_BRIDGE__ && window.__NC_BRIDGE__.dispatch('auth-result', " + failJson + ");");
            }
            catch (Exception notifyEx)
            {
                Serilog.Log.Warning(notifyEx, "[LoginPage] 回执登录失败结果失败");
            }
        }
        finally
        {
            _authInFlight = false;
        }
    }

    /// <summary>动画终幕完成（done）→ 认证成功才进主页；否则防御性回落</summary>
    private async Task HandleDoneAsync()
    {
        if (_navigated)
            return;

        if (_viewModel.LastSignInSucceeded)
            await NavigateAfterAuthAsync();
        else
            FallBackToClassic("终幕到达但认证未成功");
    }

    /// <summary>认证成功后进主页（done 回执 / 终幕看门狗 / 故障时已认证，三处共用，幂等）</summary>
    private async Task NavigateAfterAuthAsync()
    {
        if (_navigated)
            return;

        _navigated = true;
        CancelDoneWatchdog();

        try
        {
            _viewModel.DeferNavigation = false;
            await _viewModel.CompleteNavigationAsync();
            _logger.Info("[LoginPage] 序列登录认证成功，已进入主页");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LoginPage] 序列登录导航失败");
            _navigated = false;
            FallBackToClassic("导航失败: " + ex.Message);
        }
    }

    /// <summary>认证成功后等终幕的兜底：到点不再等动画，直接进主页</summary>
    private void StartDoneWatchdog()
    {
        CancelDoneWatchdog();
        _doneWatchdogCts = new CancellationTokenSource();
        var token = _doneWatchdogCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(SplashConstants.LoginDoneTimeoutSeconds), token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            _logger.Warn("[LoginPage] 认证成功后终幕未在兜底时长内完成，直接进主页");
            await MainThread.InvokeOnMainThreadAsync(() => NavigateAfterAuthAsync());
        }, token);
    }

    private void CancelDoneWatchdog()
    {
        try
        {
            _doneWatchdogCts?.Cancel();
        }
        catch
        {
            // 已释放/已取消均无关紧要
        }

        _doneWatchdogCts?.Dispose();
        _doneWatchdogCts = null;
    }

    /// <summary>
    /// 回落经典登录页：隐藏覆盖层（经典 XAML 本就在底下渲染，无闪切）。
    /// 关键复位：DeferNavigation=false，否则经典表单登录会卡住不导航。
    /// </summary>
    private void FallBackToClassic(string reason)
    {
        if (_fellBack)
            return;

        _fellBack = true;
        _logger.Warn("[LoginPage] 序列登录回落经典登录页: " + reason);
        CancelDoneWatchdog();
        _viewModel.DeferNavigation = false;

        if (_sequenceView != null)
        {
            _sequenceView.BridgeReceived -= OnSequenceBridge;
            _sequenceView.HostFaulted -= OnSequenceFault;
            SequenceHost.Children.Remove(_sequenceView);
            _sequenceView = null;
        }

        SequenceHost.IsVisible = false;
    }

    /// <summary>记住设置只加载一次（序列 prefill 与经典 OnAppearing 共用；页面瞬时重建，无跨会话陈旧问题）</summary>
    private Task EnsureRememberSettingsAsync() =>
        _rememberSettingsTask ??= _viewModel.LoadRememberSettingsCommand.ExecuteAsync(null);

    /* ════════════════════════ 生命周期与动效 ═══════════════════════ */

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();

        _clockTimer?.Stop();
        _clockTimer = null;
        CancelDoneWatchdog();
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

        // 在页面加载时初始化记住设置（序列模式下构造时已预热，此处幂等）
        await EnsureRememberSettingsAsync();

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

    /// <summary>web → host 登录提交载荷（login.js 发送；字段名小写，忽略大小写反序列化）</summary>
    private sealed class LoginSubmitPayload
    {
        public string? Id { get; set; }
        public string? User { get; set; }
        public string? Pass { get; set; }
        public bool? Remember { get; set; }
        public bool? RememberPass { get; set; }
    }
}
