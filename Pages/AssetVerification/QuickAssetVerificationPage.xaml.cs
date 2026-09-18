using NewCosmos.ViewModels.AssetVerification;

namespace NewCosmos.Pages.AssetVerification;

public partial class QuickAssetVerificationPage : ContentPage
{
    private readonly QuickAssetVerificationViewModel _viewModel;

    public QuickAssetVerificationPage(QuickAssetVerificationViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        try
        {
            await _viewModel.InitializeAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CRITICAL] 快速核查页面 OnAppearing 异常: {ex}");
        }
    }
}