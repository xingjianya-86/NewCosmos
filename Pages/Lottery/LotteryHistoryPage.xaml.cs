using NewCosmos.ViewModels.Lottery;

namespace NewCosmos.Pages.Lottery;

/// <summary>
/// 彩票历史记录页面
/// </summary>
public partial class LotteryHistoryPage : ContentPage
{
    private readonly LotteryHistoryViewModel _viewModel;

    public LotteryHistoryPage(LotteryHistoryViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializeAsync();
    }
}
