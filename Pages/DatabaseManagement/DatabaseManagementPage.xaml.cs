using NewCosmos.ViewModels.DatabaseManagement;

namespace NewCosmos.Pages.DatabaseManagement;

public partial class DatabaseManagementPage : ContentPage
{
    private readonly DatabaseManagementViewModel _viewModel;

    public DatabaseManagementPage(DatabaseManagementViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializePermissionsAsync(App.CurrentUserId ?? 1);
        await _viewModel.OnAppearingAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();
    }
}