namespace NewCosmos.Components;

/// <summary>
/// 日期时刻选择弹窗：日期（≤ 当前时间）+ 时间 + 今天/昨天/此刻快捷芯片。
/// 用法：直接 <see cref="ShowAsync"/> 以模态方式弹出并等待选择结果（模式与 SignaturePopupView 一致）。
/// 存在意义：原生 DatePicker/TimePicker 直接平铺在表单里时观感割裂、必填语义虚假（TimePicker 恒有值）；
/// 本弹窗把「选一个时刻」收敛为一次显式确认，未确认则 Value 保持 null。
/// </summary>
public partial class DateTimePickerPopupView : ContentView
{
    private readonly TaskCompletionSource<DateTime?> _tcs = new();
    private DateTime? _maximum;
    private bool _showTime = true;

    public DateTimePickerPopupView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 初始化选择区当前值与上限。
    /// </summary>
    /// <param name="current">当前值（null 时默认今天此刻）</param>
    /// <param name="maximum">日期上限（含当日；null 时取当前时刻）</param>
    /// <param name="showTime">是否显示时间选择（纯日期场景置 false）</param>
    /// <param name="title">弹窗标题</param>
    public void Initialize(DateTime? current, DateTime? maximum, bool showTime, string? title)
    {
        _maximum = maximum ?? DateTime.Now;
        _showTime = showTime;

        if (!string.IsNullOrWhiteSpace(title))
            TitleLabel.Text = title;

        var seed = current ?? DateTime.Now;
        // 超上限时回落到上限，避免 DatePicker 初始化抛异常
        if (_maximum is { } max && seed > max)
            seed = max;

        DatePickerControl.Date = seed.Date;
        TimePickerControl.Time = seed.TimeOfDay;
        TimeSection.IsVisible = showTime;
        NowChip.IsVisible = showTime;
        SubtitleLabel.Text = showTime ? "日期与时间不能晚于当前时刻" : "日期不能晚于当前日期";
        ErrorLabel.IsVisible = false;
    }

    private void OnTodayClicked(object? sender, EventArgs e) => ApplyQuickPick(DateTime.Today);

    private void OnYesterdayClicked(object? sender, EventArgs e) => ApplyQuickPick(DateTime.Today.AddDays(-1));

    private void OnNowClicked(object? sender, EventArgs e)
    {
        var now = DateTime.Now;
        ApplyQuickPick(now.Date);
        TimePickerControl.Time = new TimeSpan(now.Hour, now.Minute, 0);
        ErrorLabel.IsVisible = false;
    }

    private void ApplyQuickPick(DateTime date)
    {
        if (_maximum is { } max && date > max.Date)
            date = max.Date;
        DatePickerControl.Date = date;
        ErrorLabel.IsVisible = false;
    }

    private void OnCancelClicked(object? sender, EventArgs e)
    {
        _tcs.TrySetResult(null);
        _ = CloseAsync();
    }

    private void OnConfirmClicked(object? sender, EventArgs e)
    {
        // DatePicker.Date 在 .NET 10 MAUI 为 DateTime?（理论上总有值，防御性回退今天）
        var pickedDate = (DatePickerControl.Date ?? DateTime.Today).Date;
        var combined = _showTime ? pickedDate + TimePickerControl.Time : pickedDate;

        if (_maximum is { } max && combined > max)
        {
            ErrorLabel.Text = _showTime ? "所选日期时间晚于当前时刻，请调整" : "所选日期晚于当前日期，请调整";
            ErrorLabel.IsVisible = true;
            return;
        }

        _tcs.TrySetResult(combined);
        _ = CloseAsync();
    }

    private async Task CloseAsync()
    {
        var navigation = Helpers.WindowNavigator.CurrentPage?.Navigation;
        if (navigation != null && navigation.ModalStack.Count > 0)
            await navigation.PopModalAsync(false);
    }

    /// <summary>
    /// 以模态方式弹出日期时刻选择弹窗，等待用户确认或取消。
    /// </summary>
    /// <param name="current">当前值（null 时默认今天此刻）</param>
    /// <param name="maximum">日期时刻上限（null 时取当前时刻）</param>
    /// <param name="showTime">是否显示时间选择</param>
    /// <param name="title">弹窗标题</param>
    /// <returns>用户确认返回所选时刻；取消返回 null</returns>
    public static async Task<DateTime?> ShowAsync(DateTime? current, DateTime? maximum, bool showTime, string? title)
    {
        var navigation = Helpers.WindowNavigator.CurrentPage?.Navigation;
        if (navigation == null)
            return null;

        var popup = new DateTimePickerPopupView();
        popup.Initialize(current, maximum, showTime, title);

        var page = new ContentPage
        {
            BackgroundColor = Color.FromArgb("#80000000"),
            Content = new Grid
            {
                Padding = new Thickness(24),
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center,
                Children = { popup }
            }
        };

        await navigation.PushModalAsync(page);
        return await popup._tcs.Task;
    }
}
