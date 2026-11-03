namespace NewCosmos.Components;

/// <summary>
/// 日期时刻输入组件：外观统一的圆角值框 + 点击弹出 <see cref="DateTimePickerPopupView"/> 选择。
/// 可绑定属性：<see cref="Title"/>（标签）、<see cref="IsRequired"/>（红色 * 必填标记）、
/// <see cref="Value"/>（DateTime?，未选为 null——必填校验因此真实生效）、<see cref="Placeholder"/>、
/// <see cref="MaximumDate"/>（默认当前时刻，禁止选未来）、<see cref="ShowTime"/>（纯日期场景置 false）。
/// 存在意义：替代表单里平铺的原生 DatePicker/TimePicker（观感割裂、TimePicker 恒有值导致必填校验空转）。
/// </summary>
public partial class DateTimeEntryView : ContentView
{
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(DateTimeEntryView), string.Empty, propertyChanged: OnVisualChanged);

    public static readonly BindableProperty IsRequiredProperty = BindableProperty.Create(
        nameof(IsRequired), typeof(bool), typeof(DateTimeEntryView), false, propertyChanged: OnVisualChanged);

    public static readonly BindableProperty ValueProperty = BindableProperty.Create(
        nameof(Value), typeof(DateTime?), typeof(DateTimeEntryView), default(DateTime?),
        BindingMode.TwoWay, propertyChanged: OnVisualChanged);

    public static readonly BindableProperty PlaceholderProperty = BindableProperty.Create(
        nameof(Placeholder), typeof(string), typeof(DateTimeEntryView), "请选择日期与时间", propertyChanged: OnVisualChanged);

    public static readonly BindableProperty MaximumDateProperty = BindableProperty.Create(
        nameof(MaximumDate), typeof(DateTime?), typeof(DateTimeEntryView), default(DateTime?));

    public static readonly BindableProperty ShowTimeProperty = BindableProperty.Create(
        nameof(ShowTime), typeof(bool), typeof(DateTimeEntryView), true, propertyChanged: OnVisualChanged);

    /// <summary>标签文本</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>是否必填（显示红色 *；校验本身由宿主 ViewModel 负责）</summary>
    public bool IsRequired
    {
        get => (bool)GetValue(IsRequiredProperty);
        set => SetValue(IsRequiredProperty, value);
    }

    /// <summary>当前值（null = 未选择）</summary>
    public DateTime? Value
    {
        get => (DateTime?)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>未选择时的占位文本</summary>
    public string Placeholder
    {
        get => (string)GetValue(PlaceholderProperty);
        set => SetValue(PlaceholderProperty, value);
    }

    /// <summary>可选上限（默认当前时刻，禁止选未来）</summary>
    public DateTime? MaximumDate
    {
        get => (DateTime?)GetValue(MaximumDateProperty);
        set => SetValue(MaximumDateProperty, value);
    }

    /// <summary>是否包含时间（false = 纯日期）</summary>
    public bool ShowTime
    {
        get => (bool)GetValue(ShowTimeProperty);
        set => SetValue(ShowTimeProperty, value);
    }

    public DateTimeEntryView()
    {
        InitializeComponent();
        RefreshVisual();
    }

    private static void OnVisualChanged(BindableObject bindable, object oldValue, object newValue)
        => ((DateTimeEntryView)bindable).RefreshVisual();

    private void RefreshVisual()
    {
        if (TitleLabel == null)
            return;

        TitleLabel.Text = Title ?? string.Empty;
        RequiredMarkLabel.IsVisible = IsRequired;

        if (Value is { } value)
        {
            ValueLabel.Text = ShowTime ? value.ToString("yyyy-MM-dd HH:mm") : value.ToString("yyyy-MM-dd");
            ValueLabel.TextColor = ResolveColor("TextPrimary", ValueLabel.TextColor);
        }
        else
        {
            ValueLabel.Text = Placeholder ?? "请选择日期与时间";
            ValueLabel.TextColor = ResolveColor("Gray500", ValueLabel.TextColor);
        }
    }

    /// <summary>取全局颜色资源（取不到时保持现值，模式同 StatCapsule）</summary>
    private static Color ResolveColor(string key, Color fallback)
        => Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c ? c : fallback;

    private async void OnFieldTapped(object? sender, EventArgs e)
    {
        var picked = await DateTimePickerPopupView.ShowAsync(Value, MaximumDate, ShowTime, Title);
        if (picked != null)
            Value = picked;
    }
}
