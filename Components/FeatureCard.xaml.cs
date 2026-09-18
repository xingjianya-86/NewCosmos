using System.Windows.Input;

namespace NewCosmos.Components;

/// <summary>
/// 模块首页子功能导航卡（替代三张 HomePage 中 15 张逐字同构的手写卡片）。
/// 差异全部收敛为六个绑定属性；悬停高亮由 FeatureCard 样式统一承载。
/// </summary>
public partial class FeatureCard : ContentView
{
    public static readonly BindableProperty GlyphProperty =
        BindableProperty.Create(nameof(Glyph), typeof(string), typeof(FeatureCard), string.Empty,
            propertyChanged: OnGlyphChanged);

    public static readonly BindableProperty IconBackgroundColorProperty =
        BindableProperty.Create(nameof(IconBackgroundColor), typeof(Color), typeof(FeatureCard),
            null, propertyChanged: OnVisualChanged);

    public static readonly BindableProperty IconColorProperty =
        BindableProperty.Create(nameof(IconColor), typeof(Color), typeof(FeatureCard),
            null, propertyChanged: OnVisualChanged);

    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(FeatureCard), string.Empty,
            propertyChanged: OnTitleChanged);

    public static readonly BindableProperty DescriptionProperty =
        BindableProperty.Create(nameof(Description), typeof(string), typeof(FeatureCard), string.Empty,
            propertyChanged: OnDescriptionChanged);

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(FeatureCard));

    /// <summary>Segoe Fluent Icons 字形代码（如 &#xE7C1; 对应的字符）</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>图标座底色（各域 Soft 色，如 #EBF5FF）</summary>
    public Color? IconBackgroundColor
    {
        get => (Color?)GetValue(IconBackgroundColorProperty);
        set => SetValue(IconBackgroundColorProperty, value);
    }

    /// <summary>图标颜色（各域主色，如 ModuleSocialAssistance）</summary>
    public Color? IconColor
    {
        get => (Color?)GetValue(IconColorProperty);
        set => SetValue(IconColorProperty, value);
    }

    /// <summary>功能名称</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>一句话说明</summary>
    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    /// <summary>整卡点击命令</summary>
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public FeatureCard()
    {
        InitializeComponent();
    }

    private static void OnGlyphChanged(BindableObject b, object o, object n)
    {
        if (b is FeatureCard v) v.GlyphLabel.Text = n as string;
    }

    private static void OnVisualChanged(BindableObject b, object o, object n)
    {
        if (b is FeatureCard v)
        {
            if (v.IconBackgroundColor != null) v.IconHost.BackgroundColor = v.IconBackgroundColor;
            if (v.IconColor != null) v.GlyphLabel.TextColor = v.IconColor;
        }
    }

    private static void OnTitleChanged(BindableObject b, object o, object n)
    {
        if (b is FeatureCard v) v.TitleLabel.Text = n as string;
    }

    private static void OnDescriptionChanged(BindableObject b, object o, object n)
    {
        if (b is FeatureCard v) v.DescLabel.Text = n as string;
    }
}
