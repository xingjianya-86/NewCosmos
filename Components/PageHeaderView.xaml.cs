using System.Windows.Input;

namespace NewCosmos.Components;

/// <summary>
/// 全库统一页面页头（纸墨档案风 · 报头样式）：
/// 朱砂竖条 + 返回钮 + 图标 + 宋体标题 + 英文等宽小字 + 右侧动作插槽 + 底部双线。
/// 属性面兼容旧版（Glyph/Title/Subtitle/TrailingText/IsDark/IconColor/BackCommand/BackText/HeaderActions），
/// 新增 Kicker（英文小字）与 AccentColor（竖条色）。
/// 颜色一律取自 Colors.xaml 资源字典，本组件不定义新颜色。
/// ⚠ 故意【不加】[ContentProperty]：曾用 ContentProperty(HeaderActions)，SourceGen 会把
///   组件自身 XAML 的根元素也解析进 HeaderActions（Content 变 null → 页头 0 高度不显示，
///   且根元素经 ActionsPresenter 形成自包含循环 → 布局全为 -1）。
///   所有调用处一律显式写 &lt;PageHeaderView.HeaderActions&gt; / &lt;PageHeaderView.Stats&gt;，
///   不需要默认内容属性。
/// </summary>
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

    /// <summary>英文等宽小字（DM Mono 大写宽字距，报头 kicker），如 "Death Registry"</summary>
    public static readonly BindableProperty KickerProperty =
        BindableProperty.Create(nameof(Kicker), typeof(string), typeof(PageHeaderView), string.Empty,
            propertyChanged: OnLayoutChanged);

    /// <summary>标题右侧灰色小字（如"快速申请 · 信息录入 · 提交核对"）</summary>
    public static readonly BindableProperty TrailingTextProperty =
        BindableProperty.Create(nameof(TrailingText), typeof(string), typeof(PageHeaderView), string.Empty,
            propertyChanged: OnLayoutChanged);

    /// <summary>兼容旧版深色页头：true = 墨底反白（纸墨风下用墨黑而非 Primary）</summary>
    public static readonly BindableProperty IsDarkProperty =
        BindableProperty.Create(nameof(IsDark), typeof(bool), typeof(PageHeaderView), false,
            propertyChanged: OnThemeChanged);

    public static readonly BindableProperty IconColorProperty =
        BindableProperty.Create(nameof(IconColor), typeof(Color), typeof(PageHeaderView),
            null, propertyChanged: OnThemeChanged);

    /// <summary>左侧竖条/强调色；缺省朱砂 Seal</summary>
    public static readonly BindableProperty AccentColorProperty =
        BindableProperty.Create(nameof(AccentColor), typeof(Color), typeof(PageHeaderView),
            null, propertyChanged: OnThemeChanged);

    public static readonly BindableProperty BackCommandProperty =
        BindableProperty.Create(nameof(BackCommand), typeof(ICommand), typeof(PageHeaderView),
            null, propertyChanged: OnBackChanged);

    public static readonly BindableProperty BackTextProperty =
        BindableProperty.Create(nameof(BackText), typeof(string), typeof(PageHeaderView), string.Empty,
            propertyChanged: OnBackChanged);

    public static readonly BindableProperty HeaderActionsProperty =
        BindableProperty.Create(nameof(HeaderActions), typeof(View), typeof(PageHeaderView),
            null, propertyChanged: OnHeaderActionsChanged);

    /// <summary>
    /// 右侧动作区内容（View）：须用显式属性元素 &lt;PageHeaderView.HeaderActions&gt; 设置。
    /// 注意必须是 BindableProperty 且由 propertyChanged 直接赋给 ActionsPresenter.Content——
    /// XAML 的 {x:Reference Root} 绑定在 SourceGen 下对 ContentPresenter 失效（Content 恒 null）。
    /// </summary>
    public View? HeaderActions
    {
        get => (View?)GetValue(HeaderActionsProperty);
        set => SetValue(HeaderActionsProperty, value);
    }

    public static readonly BindableProperty StatsProperty =
        BindableProperty.Create(nameof(Stats), typeof(View), typeof(PageHeaderView),
            null, propertyChanged: OnStatsChanged);

    /// <summary>
    /// 统计插槽（模块主页的统计胶囊行）：位于标题行下方、底部双线之上，朱砂竖条贯穿其左侧。
    /// 为 null 时该行隐藏（行高归零），既有未使用该插槽的页面布局完全不受影响。
    /// </summary>
    public View? Stats
    {
        get => (View?)GetValue(StatsProperty);
        set => SetValue(StatsProperty, value);
    }

    private static void OnStatsChanged(BindableObject b, object o, object n)
    {
        if (b is PageHeaderView v)
        {
            // 同 HeaderActions：代码后置直接赋值，不用 XAML Binding（SourceGen 下 x:Reference 绑定失效）
            v.StatsPresenter.Content = n as View;
            v.StatsHost.IsVisible = n != null;
            v.SyncBindingContext(n as BindableObject);
        }
    }

    private static void OnHeaderActionsChanged(BindableObject b, object o, object n)
    {
        if (b is PageHeaderView v)
        {
            v.ActionsPresenter.Content = n as View;
            v.SyncBindingContext(n as BindableObject);
        }
    }

    /// <summary>
    /// 把组件的 BindingContext 显式传给插槽内容。
    /// 插槽（Stats/HeaderActions）在调用页 XAML 里是【属性元素】，赋值时尚不在可视树，
    /// BindingContext 为 null；经 ContentPresenter 放入后 MAUI 不会重新继承 →
    /// 内部绑定（如 StatCapsule.Value="{Binding ActiveCountText}"）求值 null → 数值空白。
    /// </summary>
    private void SyncBindingContext(BindableObject? slot)
    {
        if (slot != null && slot.BindingContext == null)
            slot.BindingContext = BindingContext;
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        // 组件 BC 就绪/变化时同步两个插槽（覆盖"插槽先于 BC 设置"的时序）
        SyncBindingContext(Stats as BindableObject);
        SyncBindingContext(HeaderActions as BindableObject);
    }

    /// <summary>图标字符；空则不显示图标列</summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }

    /// <summary>页头标题（宋体大字，支持字面量或 Binding）</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>副标题（暖墨小字，标题下方一行）</summary>
    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    /// <summary>英文等宽 kicker（标题上方一行）</summary>
    public string Kicker
    {
        get => (string)GetValue(KickerProperty);
        set => SetValue(KickerProperty, value);
    }

    /// <summary>标题右侧灰色小字</summary>
    public string TrailingText
    {
        get => (string)GetValue(TrailingTextProperty);
        set => SetValue(TrailingTextProperty, value);
    }

    /// <summary>true = 墨底反白页头；false = 纸底（默认）</summary>
    public bool IsDark
    {
        get => (bool)GetValue(IsDarkProperty);
        set => SetValue(IsDarkProperty, value);
    }

    /// <summary>图标颜色；缺省跟随主题（纸底朱砂 / 墨底白）</summary>
    public Color? IconColor
    {
        get => (Color?)GetValue(IconColorProperty);
        set => SetValue(IconColorProperty, value);
    }

    /// <summary>左侧竖条与强调色；缺省朱砂 Seal</summary>
    public Color? AccentColor
    {
        get => (Color?)GetValue(AccentColorProperty);
        set => SetValue(AccentColorProperty, value);
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
        // 插槽初始同步（属性随后由调用页 XAML 设置，propertyChanged 会再赋值）
        StatsPresenter.Content = Stats as View;
        StatsHost.IsVisible = Stats != null;
        ActionsPresenter.Content = HeaderActions as View;
        ApplyLayout();
        ApplyTheme();
        ApplyBack();
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
        // 图标
        var hasGlyph = !string.IsNullOrEmpty(Glyph);
        GlyphLabel.Text = Glyph;
        GlyphLabel.IsVisible = hasGlyph;

        // 英文 kicker
        var hasKicker = !string.IsNullOrEmpty(Kicker);
        KickerLabel.Text = Kicker?.ToUpperInvariant();
        KickerLabel.IsVisible = hasKicker;

        // 标题
        TitleLabel.Text = Title;

        // 副标题
        var hasSub = !string.IsNullOrEmpty(Subtitle);
        SubTitleLabel.Text = Subtitle;
        SubTitleLabel.IsVisible = hasSub;

        // 尾注
        var hasTrailing = !string.IsNullOrEmpty(TrailingText);
        TrailingLabel.Text = TrailingText;
        TrailingLabel.IsVisible = hasTrailing && !hasSub;
    }

    private void ApplyTheme()
    {
        var accent = AccentColor ?? Res("Seal") ?? Colors.Firebrick;

        // 左侧竖条 + 底部双线恒定为纸墨风签名，随强调色
        TabStrip.Color = accent;

        if (IsDark)
        {
            // 墨底反白
            BodyGrid.BackgroundColor = Res("Ink") ?? Colors.Black;
            RuleStrong.Color = accent;
            RuleThin.Color = Res("Ink2") ?? Colors.DimGray;

            var fg = Res("Paper") ?? Colors.White;
            TitleLabel.TextColor = fg;
            KickerLabel.TextColor = accent;
            SubTitleLabel.TextColor = Res("Gray400") ?? Colors.LightGray;
            TrailingLabel.TextColor = Res("Gray400") ?? Colors.LightGray;
            GlyphLabel.TextColor = IconColor ?? accent;
            BackBtn.IconColor = Res("Paper") ?? Colors.White;
        }
        else
        {
            // 纸底（默认）
            BodyGrid.BackgroundColor = Res("CardBackground") ?? Colors.Beige;
            RuleStrong.Color = Res("Ink") ?? Colors.Black;
            RuleThin.Color = Res("LineStrong") ?? Colors.DarkGray;

            TitleLabel.TextColor = Res("TextPrimary") ?? Colors.Black;
            KickerLabel.TextColor = accent;
            SubTitleLabel.TextColor = Res("Muted") ?? Colors.Gray;
            TrailingLabel.TextColor = Res("Muted") ?? Colors.Gray;
            GlyphLabel.TextColor = IconColor ?? accent;
            BackBtn.IconColor = IconColor ?? Res("Seal") ?? Colors.Firebrick;
        }
    }

    private void ApplyBack()
    {
        BackBtn.Command = BackCommand;
        BackBtn.Text = BackText;
        BackBtn.IsVisible = BackCommand != null;
    }
}
