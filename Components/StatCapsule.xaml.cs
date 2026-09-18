namespace NewCosmos.Components;

/// <summary>
/// 模块首页统计胶囊：数值 + 标签，半透明白底嵌于域色渐变头部。
/// 替代三个 HomePage 中 12 枚逐字同构的手写统计块。
/// </summary>
public partial class StatCapsule : ContentView
{
    public static readonly BindableProperty ValueProperty =
        BindableProperty.Create(nameof(Value), typeof(string), typeof(StatCapsule), string.Empty,
            propertyChanged: (b, o, n) => { if (b is StatCapsule v) v.ValueLabel.Text = n as string; });

    public static readonly BindableProperty CaptionProperty =
        BindableProperty.Create(nameof(Caption), typeof(string), typeof(StatCapsule), string.Empty,
            propertyChanged: (b, o, n) => { if (b is StatCapsule v) v.CaptionLabel.Text = n as string; });

    /// <summary>统计值（通常绑定 *Text 属性，失败降级显示 "—"）</summary>
    public string Value
    {
        get => (string)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>统计项名称（含单位）</summary>
    public string Caption
    {
        get => (string)GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    public static readonly BindableProperty IsLightProperty =
        BindableProperty.Create(nameof(IsLight), typeof(bool), typeof(StatCapsule), false,
            propertyChanged: OnIsLightChanged);

    /// <summary>浅色变体：白底页面统计栏用（InfoSoft 底 + PrimaryDark 数值）；默认深色 Hero 半透明白底</summary>
    public bool IsLight
    {
        get => (bool)GetValue(IsLightProperty);
        set => SetValue(IsLightProperty, value);
    }

    public StatCapsule()
    {
        InitializeComponent();
    }

    private static void OnIsLightChanged(BindableObject b, object o, object n)
    {
        if (b is not StatCapsule v) return;

        if ((bool)n)
        {
            v.HostBorder.BackgroundColor = ResolveColor("InfoSoft", Color.FromArgb("#E8F3FE"));
            v.ValueLabel.TextColor = ResolveColor("PrimaryDark", Color.FromArgb("#0D47A1"));
            v.CaptionLabel.TextColor = ResolveColor("TextSecondary", Colors.Gray);
        }
        else
        {
            v.HostBorder.BackgroundColor = Color.FromArgb("#26FFFFFF");
            v.ValueLabel.TextColor = ResolveColor("White", Colors.White);
            v.CaptionLabel.TextColor = Color.FromArgb("#CCFFFFFF");
        }
    }

    private static Color ResolveColor(string key, Color fallback) =>
        Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c ? c : fallback;
}
