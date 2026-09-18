using NewCosmos.ViewModels.ArchiveManagement;

namespace NewCosmos.Pages.ArchiveManagement;

public partial class ProofUnitTemplateManagePage : ContentPage
{
    private readonly ProofUnitTemplateManageViewModel _viewModel;

    public ProofUnitTemplateManagePage(ProofUnitTemplateManageViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }
}