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
            Serilog.Log.Error(ex, "[QuickAssetVerificationPage] OnAppearing 初始化异常，页面可能保持空白");
        }
    }
}