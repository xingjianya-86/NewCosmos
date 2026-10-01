using NewCosmos.ViewModels.AssetVerification;

namespace NewCosmos.Pages.Mobile;

public partial class MobileMonthlyAssetAuditPage : ContentPage
{
    private readonly MonthlyAssetAuditViewModel _viewModel;

    public MobileMonthlyAssetAuditPage(MonthlyAssetAuditViewModel viewModel)
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
