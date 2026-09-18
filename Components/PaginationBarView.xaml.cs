namespace NewCosmos.Components;

/// <summary>
/// 标准分页栏：绑定 PagedSearchViewModelBase 家族的标准分页契约
/// （PageStatusText / PageIndex / TotalPages / PreviousPageCommand / NextPageCommand / CanGoPrevious / CanGoNext）。
/// 直接放入页面即可使用（BindingContext 自动继承宿主 VM）。
/// </summary>
public partial class PaginationBarView : ContentView
{
    public static readonly BindableProperty ShowStatusTextProperty =
        BindableProperty.Create(nameof(ShowStatusText), typeof(bool), typeof(PaginationBarView), true);

    /// <summary>是否显示左侧"共 N 条"统计文案；迷你布局（窄侧栏内）可关闭</summary>
    public bool ShowStatusText
    {
        get => (bool)GetValue(ShowStatusTextProperty);
        set => SetValue(ShowStatusTextProperty, value);
    }

    public PaginationBarView()
    {
        InitializeComponent();
    }
}
