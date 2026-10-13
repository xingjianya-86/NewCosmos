using NewCosmos.ViewModels.Reporting;

namespace NewCosmos.Pages.Reporting;

public partial class MonthlyReportPage : ContentPage
{
    private readonly MonthlyReportMainViewModel _viewModel;

    public MonthlyReportPage(MonthlyReportMainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
        ApplyPhoneLayout();
    }

    /// <summary>
    /// 手机布局适配：双列（440 表单 + 预览）在手机屏宽下左列被裁、右列挤压成条
    /// （即"点击左侧"神秘右侧）。手机改上下两行：上=表单面板，下=HTML 预览（WebView）；
    /// 出席弹窗宽度按屏宽收敛。桌面/平板保持原双列布局。
    /// </summary>
    private void ApplyPhoneLayout()
    {
        if (DeviceInfo.Idiom != DeviceIdiom.Phone)
            return;

        ContentGrid.ColumnDefinitions.Clear();
        ContentGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        ContentGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        ContentGrid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        Grid.SetColumn(PreviewColumn, 0);
        Grid.SetRow(PreviewColumn, 1);

        // 出席/缺席弹窗：560 固定宽在手机上溢出，按屏宽减边距收敛
        var popupWidth = Math.Min(560, DeviceDisplay.Current.MainDisplayInfo.Width / DeviceDisplay.Current.MainDisplayInfo.Density - 32);
        AttendancePopupBorder.WidthRequest = popupWidth;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();
    }
}