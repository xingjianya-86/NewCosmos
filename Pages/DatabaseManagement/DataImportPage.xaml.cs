using NewCosmos.ViewModels.DatabaseManagement;

namespace NewCosmos.Pages.DatabaseManagement;

public partial class DataImportPage : ContentPage
{
    private readonly DataImportViewModel _viewModel;

    public DataImportPage(DataImportViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();
    }
}
