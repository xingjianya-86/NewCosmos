using NewCosmos.ViewModels.ArchiveManagement;

namespace NewCosmos.Pages.ArchiveManagement;

public partial class MonthlyPublicityPage : ContentPage
{
    private readonly MonthlyPublicityViewModel _viewModel;

    public MonthlyPublicityPage(MonthlyPublicityViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }
}
