using NewCosmos.ViewModels.AssetVerification;

namespace NewCosmos.Pages.Mobile;

public partial class MobileQuickAssetVerificationPage : ContentPage
{
    private readonly QuickAssetVerificationViewModel _viewModel;

    public MobileQuickAssetVerificationPage(QuickAssetVerificationViewModel viewModel)
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
