using NewCosmos.ViewModels.Lottery;

namespace NewCosmos.Pages.Lottery;

/// <summary>
/// 彩票预测页面
/// </summary>
public partial class LotteryPredictionPage : ContentPage
{
    private readonly LotteryPredictionViewModel _viewModel;

    public LotteryPredictionPage(LotteryPredictionViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }
}
