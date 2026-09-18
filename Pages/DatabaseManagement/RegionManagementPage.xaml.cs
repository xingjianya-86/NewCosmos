using NewCosmos.ViewModels.DatabaseManagement;

namespace NewCosmos.Pages.DatabaseManagement;

public partial class RegionManagementPage : ContentPage
{
    private readonly RegionManagementViewModel _viewModel;

    public RegionManagementPage(RegionManagementViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var userId = App.CurrentUserId ?? 1;
        await _viewModel.InitializePermissionsAsync(userId);
        await _viewModel.OnAppearingAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();
    }
}
