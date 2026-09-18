using NewCosmos.ViewModels.Lottery;

namespace NewCosmos.Pages.Lottery;

/// <summary>
/// 彩票中奖结果页面
/// </summary>
public partial class LotteryResultPage : ContentPage
{
    private readonly LotteryResultViewModel _viewModel;

    public LotteryResultPage(LotteryResultViewModel viewModel)
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
