using NewCosmos.ViewModels.ElderlyBenefits;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端·高龄津贴复核列表（待复核队列 + 在享检索）。复用桌面 <see cref="ElderlyReviewListViewModel"/>。
/// </summary>
public partial class MobileElderlyReviewListPage : ContentPage
{
    private readonly ElderlyReviewListViewModel _viewModel;

    public MobileElderlyReviewListPage(ElderlyReviewListViewModel viewModel)
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

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();
    }
}
