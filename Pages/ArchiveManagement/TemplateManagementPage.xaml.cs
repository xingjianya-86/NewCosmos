using NewCosmos.ViewModels.ArchiveManagement;

namespace NewCosmos.Pages.ArchiveManagement;

public partial class TemplateManagementPage : ContentPage
{
    private readonly TemplateManagementViewModel _viewModel;

    public TemplateManagementPage(TemplateManagementViewModel vm)
    {
        InitializeComponent();
        _viewModel = vm;
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }
}
