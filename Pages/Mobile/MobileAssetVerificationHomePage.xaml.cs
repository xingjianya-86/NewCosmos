using NewCosmos.ViewModels.AssetVerification;

namespace NewCosmos.Pages.Mobile;

public partial class MobileAssetVerificationHomePage : ContentPage
{
    private readonly AssetVerificationHomeViewModel _viewModel;

    public MobileAssetVerificationHomePage(AssetVerificationHomeViewModel viewModel)
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