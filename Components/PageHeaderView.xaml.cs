using System.Windows.Input;

namespace NewCosmos.Components;

/// <summary>
/// 全库统一页面页头：返回钮 + 图标 + 标题（单行/双行/带尾注）+ 右侧动作插槽。
/// 归一 28 页手写页头的三种 Padding、两档 Shadow、两种返回实现与 MDL2/Fluent 字体混用。
/// 主题色统一取自 Colors.xaml 资源字典（本组件不在代码中定义新颜色）。
/// </summary>
[ContentProperty(nameof(HeaderActions))]
public partial class PageHeaderView : ContentView
{
    public static readonly BindableProperty GlyphProperty =
        BindableProperty.Create(nameof(Glyph), typeof(string), typeof(PageHeaderView), string.Empty,
            propertyChanged: OnLayoutChanged);

    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(PageHeaderView), string.Empty,
            propertyChanged: OnLayoutChanged);

    public static readonly BindableProperty SubtitleProperty =
        BindableProperty.Create(nameof(Subtitle), typeof(string), typeof(PageHeaderView), string.Empty,
            propertyChanged: OnLayoutChanged);

    /// <summary>单行模式下显示在标题右侧的灰色小字（如"快速申请 · 信息录入 · 提交核对"）</summary>
    public static readonly BindableProperty TrailingTextProperty =
        BindableProperty.Create(nameof(TrailingText), typeof(string), typeof(PageHeaderView), string.Empty,
            propertyChanged: OnLayoutChanged);

    public static readonly BindableProperty IsDarkProperty =
        BindableProperty.Create(nameof(IsDark), typeof(bool), typeof(PageHeaderView), false,
            propertyChanged: OnThemeChanged);

    public static readonly BindableProperty IconColorProperty =
        BindableProperty.Create(nameof(IconColor), typeof(Color), typeof(PageHeaderView),
            null, propertyChanged: OnThemeChanged);

    public static readonly BindableProperty BackCommandProperty =
        BindableProperty.Create(nameof(BackCommand), typeof(ICommand), typeof(PageHeaderView),
            null, propertyChanged: OnBackChanged);

    public static readonly BindableProperty BackTextProperty =
        BindableProperty.Create(nameof(BackText), typeof(string), typeof(PageHeaderView), string.Empty,
            propertyChanged: OnBackChanged);

    /// <summary>右侧动作区内容（View；通过 ContentProperty 直接写在元素内）</summary>
    public View? HeaderActions { get; set; }

    /// <summary>Segoe Fluent Icons 图标字符；空则不显示图标列</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>页头标题（支持字面量或 Binding）</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>副标题；非空时标题切双行垂排</summary>
    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    /// <summary>标题右侧灰色小字（仅单行模式生效，Subtitle 优先）</summary>
    public string TrailingText
    {
        get => (string)GetValue(TrailingTextProperty);
        set => SetValue(TrailingTextProperty, value);
    }

    /// <summary>true = 深色页头（Primary 底白字）；false = 白底（默认）</summary>
    public bool IsDark
    {
        get => (bool)GetValue(IsDarkProperty);
        set => SetValue(IsDarkProperty, value);
    }

    /// <summary>图标颜色；缺省跟随主题（白底 Primary / 深色 White），可指定 ModuleUser 等特例色</summary>
    public Color? IconColor
    {
        get => (Color?)GetValue(IconColorProperty);
        set => SetValue(IconColorProperty, value);
    }

    /// <summary>返回命令；null 时隐藏返回钮（无返回页面）</summary>
    public ICommand? BackCommand
    {
        get => (ICommand?)GetValue(BackCommandProperty);
        set => SetValue(BackCommandProperty, value);
    }

    /// <summary>返回钮附加文字（如"返回工作台"）；空则纯图标</summary>
    public string BackText
    {
        get => (string)GetValue(BackTextProperty);
        set => SetValue(BackTextProperty, value);
    }

    public PageHeaderView()
    {
        InitializeComponent();
        ApplyTheme();
        ApplyBack();
        ApplyLayout();
    }

    private static void OnLayoutChanged(BindableObject b, object o, object n)
    {
        if (b is PageHeaderView v) v.ApplyLayout();
    }

    private static void OnThemeChanged(BindableObject b, object o, object n)
    {
        if (b is PageHeaderView v) v.ApplyTheme();
    }

    private static void OnBackChanged(BindableObject b, object o, object n)
    {
        if (b is PageHeaderView v) v.ApplyBack();
    }

    /// <summary>从合并资源字典取颜色（单点来源 Colors.xaml）</summary>
    private static Color? Res(string key) =>
        Application.Current?.Resources.TryGetValue(key, out var v) == true && v is Color c ? c : null;

    private void ApplyLayout()
    {
        var hasGlyph = !string.IsNullOrEmpty(Glyph);
        GlyphLabel.Text = Glyph;
        GlyphLabel.IsVisible = hasGlyph;

        var hasSubtitle = !string.IsNullOrEmpty(Subtitle);
        DualRowLayout.IsVisible = hasSubtitle;

        // 有图标时文字区占 Column=1；无图标时占第 0 列
        Grid.SetColumn(TitleLabel, hasGlyph ? 1 : 0);
        Grid.SetColumn(TrailingLabel, hasGlyph ? 1 : 0);

        if (hasSubtitle)
        {
            DualTitleLabel.Text = Title;
            SubTitleLabel.Text = Subtitle;
            TitleLabel.IsVisible = false;
            TrailingLabel.IsVisible = false;
        }
        else
        {
            TitleLabel.Text = Title;
            TitleLabel.IsVisible = true;

            var hasTrailing = !string.IsNullOrEmpty(TrailingText);
            TrailingLabel.Text = TrailingText;
            TrailingLabel.IsVisible = hasTrailing;
        }
    }

    private void ApplyTheme()
    {
        if (IsDark)
        {
            SetIfNotNull(Res("Primary"), c => HostBorder.BackgroundColor = c);
            HostBorder.Shadow = MakeShadow(Res("PrimaryDark"), new Point(0, 4), 8, 0.3f);

            var iconWhite = IconColor ?? Res("White") ?? Colors.White;
            GlyphLabel.TextColor = iconWhite;
            TitleLabel.TextColor = Res("White") ?? Colors.White;
            DualTitleLabel.TextColor = Res("White") ?? Colors.White;
            SubTitleLabel.TextColor = Res("PrimarySoft") ?? Colors.White;
            TrailingLabel.TextColor = Res("PrimarySoft") ?? Colors.White;
            BackBtn.IconColor = Res("White") ?? Colors.White;
        }
        else
        {
            SetIfNotNull(Res("White"), c => HostBorder.BackgroundColor = c);
            HostBorder.Shadow = MakeShadow(Res("Black"), new Point(0, 2), 4, 0.05f);

            GlyphLabel.TextColor = IconColor ?? Res("Primary") ?? Colors.DodgerBlue;
            TitleLabel.TextColor = Res("TextPrimary") ?? Colors.Black;
            DualTitleLabel.TextColor = Res("TextPrimary") ?? Colors.Black;
            SubTitleLabel.TextColor = Res("TextSecondary") ?? Colors.Gray;
            TrailingLabel.TextColor = Res("TextTertiary") ?? Colors.Gray;
            BackBtn.IconColor = IconColor ?? Res("Primary") ?? Colors.DodgerBlue;
        }
    }

    private static void SetIfNotNull(Color? c, Action<Color> setter)
    {
        if (c != null) setter(c);
    }

    private static Shadow MakeShadow(Color? brush, Point offset, float radius, float opacity) => new()
    {
        Brush = new SolidColorBrush(brush ?? Colors.Black),
        Offset = offset,
        Radius = radius,
        Opacity = opacity
    };

    private void ApplyBack()
    {
        BackBtn.Command = BackCommand;
        BackBtn.Text = BackText;
        BackBtn.IsVisible = BackCommand != null;
    }
}
