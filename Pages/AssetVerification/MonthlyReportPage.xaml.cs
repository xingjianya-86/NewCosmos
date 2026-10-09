using NewCosmos.ViewModels.AssetVerification;

namespace NewCosmos.Pages.AssetVerification;

public partial class MonthlyReportPage : ContentPage
{
    private readonly MonthlyReportViewModel _viewModel;

    public MonthlyReportPage(MonthlyReportViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        await _viewModel.LoadStatsCommand.ExecuteAsync(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }

    private async void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MonthlyReportViewModel.SelectedMonth)
            || e.PropertyName == nameof(MonthlyReportViewModel.SelectedWeek))
        {
            await _viewModel.LoadStatsCommand.ExecuteAsync(null);
        }
    }
}
