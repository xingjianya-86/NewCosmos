using NewCosmos.ViewModels.UserManagement;

namespace NewCosmos.Pages.UserManagement;

public partial class OnlineSessionsPage : ContentPage
{
    private readonly OnlineSessionsViewModel _viewModel;

    public OnlineSessionsPage(OnlineSessionsViewModel viewModel)
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

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();
    }
}
