using NewCosmos.ViewModels.Reporting;

namespace NewCosmos.Pages.Reporting;

public partial class MonthlyReportPage : ContentPage
{
    private readonly MonthlyReportMainViewModel _viewModel;

    public MonthlyReportPage(MonthlyReportMainViewModel viewModel)
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

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();
    }
}