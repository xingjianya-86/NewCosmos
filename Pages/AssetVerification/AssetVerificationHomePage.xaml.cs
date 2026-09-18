using NewCosmos.ViewModels.AssetVerification;

namespace NewCosmos.Pages.AssetVerification;

public partial class AssetVerificationHomePage : ContentPage
{
    private readonly AssetVerificationHomeViewModel _viewModel;

    public AssetVerificationHomePage(AssetVerificationHomeViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.InitializeAsync();
    }
}
