using NewCosmos.ViewModels.UserManagement;

namespace NewCosmos.Pages.UserManagement;

public partial class OrganizationImportPage : ContentPage
{
    private readonly OrganizationImportViewModel _viewModel;

    public OrganizationImportPage(OrganizationImportViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();
    }
}
