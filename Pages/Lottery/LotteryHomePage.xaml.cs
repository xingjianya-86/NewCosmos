using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Helpers;
using NewCosmos.ViewModels.Lottery;

namespace NewCosmos.Pages.Lottery;

/// <summary>
/// 彩票模块首页
/// </summary>
public partial class LotteryHomePage : ContentPage
{
    private readonly LotteryHomeViewModel _viewModel;

    public LotteryHomePage(LotteryHomeViewModel viewModel)
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
