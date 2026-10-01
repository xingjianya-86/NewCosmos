using System.Windows.Input;

namespace NewCosmos.Components;

public enum SnackBarType
{
    Success,
    Info,
    Warning,
    Error
}

public partial class SnackBarView : ContentView
{
    public static readonly BindableProperty MessageProperty =
        BindableProperty.Create(nameof(Message), typeof(string), typeof(SnackBarView), string.Empty);

    public static readonly BindableProperty TypeProperty =
        BindableProperty.Create(nameof(Type), typeof(SnackBarType), typeof(SnackBarView), SnackBarType.Success, propertyChanged: OnTypeChanged);

    public static readonly BindableProperty DurationProperty =
        BindableProperty.Create(nameof(Duration), typeof(int), typeof(SnackBarView), 3000);

    public static readonly BindableProperty CloseCommandProperty =
        BindableProperty.Create(nameof(CloseCommand), typeof(ICommand), typeof(SnackBarView));

    private CancellationTokenSource _hideCts = null!;
    private bool _isAnimating;

    public string Message
    {
        get => (string)GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    public SnackBarType Type
    {
        get => (SnackBarType)GetValue(TypeProperty);
        set => SetValue(TypeProperty, value);
    }

    public int Duration
    {
        get => (int)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    public ICommand CloseCommand
    {
        get => (ICommand)GetValue(CloseCommandProperty);
        set => SetValue(CloseCommandProperty, value);
    }

    public SnackBarView()
    {
        InitializeComponent();
        CloseCommand = new Command(Hide);
        this.TranslationY = 100;
        this.Opacity = 0;
    }

    private static void OnTypeChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is SnackBarView view && newValue is SnackBarType type)
        {
            view.UpdateStyle(type);
        }
    }

    private void UpdateStyle(SnackBarType type)
    {
        // Android 无 Segoe 字体，使用 emoji/通用字符，避免显示为方块
        var useSegoe = DeviceInfo.Platform != DevicePlatform.Android;
        var (bgColor, icon) = type switch
        {
            SnackBarType.Success => ("#10B981", useSegoe ? "\uE73E" : "✓"),
            SnackBarType.Info => ("#3B82F6", useSegoe ? "\uE946" : "ℹ"),
            SnackBarType.Warning => ("#F59E0B", useSegoe ? "\uE7BA" : "⚠"),
            SnackBarType.Error => ("#EF4444", useSegoe ? "\uE783" : "✕"),
            _ => ("#10B981", useSegoe ? "\uE73E" : "✓")
        };

        if (SnackBarFrame != null)
        {
            SnackBarFrame.BackgroundColor = Color.FromArgb(bgColor);
        }
        if (IconLabel != null)
        {
            IconLabel.Text = icon;
            IconLabel.FontFamily = useSegoe ? "Segoe Fluent Icons" : null;
        }
    }

    public async Task ShowAsync()
    {
        if (_isAnimating) return;
        _isAnimating = true;

        UpdateStyle(Type);
        IsVisible = true;

        await Task.WhenAll(
            this.TranslateToAsync(0, 0, 250, Easing.CubicOut),
            this.FadeToAsync(1, 250)
        );

        _isAnimating = false;

        _hideCts?.Cancel();
        _hideCts?.Dispose();
        _hideCts = new CancellationTokenSource();

        try
        {
            await Task.Delay(Duration, _hideCts.Token);
            await HideAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task HideAsync()
    {
        if (_isAnimating || !IsVisible) return;
        _isAnimating = true;

        await Task.WhenAll(
            this.TranslateToAsync(0, 100, 250, Easing.CubicIn),
            this.FadeToAsync(0, 250)
        );

        IsVisible = false;
        this.TranslationY = 100;
        this.Opacity = 0;
        _isAnimating = false;
    }

    private void Hide()
    {
        _hideCts.Cancel();
        MainThread.BeginInvokeOnMainThread(async () => await HideAsync());
    }

    protected override void OnPropertyChanged(string propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        if (propertyName == IsVisibleProperty.PropertyName)
        {
            if (IsVisible)
            {
                MainThread.BeginInvokeOnMainThread(async () => await ShowAsync());
            }
        }
    }
}
