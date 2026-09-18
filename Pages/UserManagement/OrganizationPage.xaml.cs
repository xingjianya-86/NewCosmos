using NewCosmos.ViewModels.UserManagement;

namespace NewCosmos.Pages.UserManagement;

public partial class OrganizationPage : ContentPage
{
    private readonly OrganizationTreeViewModel _viewModel;

    public OrganizationPage(OrganizationTreeViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        var userId = App.CurrentUserId ?? 1;
        await _viewModel.LoadPermissionsAsync(userId);
        await _viewModel.LoadTreeAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();
    }
}