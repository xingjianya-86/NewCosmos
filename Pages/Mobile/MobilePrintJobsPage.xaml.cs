using NewCosmos.ViewModels.Mobile;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端·打印任务查看。查看推送打印队列状态，失败任务可重试。
/// </summary>
public partial class MobilePrintJobsPage : ContentPage
{
    private readonly MobilePrintJobsViewModel _viewModel;

    public MobilePrintJobsPage(MobilePrintJobsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }
}
