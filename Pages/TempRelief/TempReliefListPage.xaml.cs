using NewCosmos.Services.Core;
using NewCosmos.ViewModels.TempRelief;

namespace NewCosmos.Pages.TempRelief;

public partial class TempReliefListPage : ContentPage
{
    private readonly TempReliefListViewModel _viewModel;
    private readonly ILoggerService _logger;

    public TempReliefListPage(TempReliefListViewModel viewModel, ILoggerService logger)
    {
        _logger = logger;
        _logger.LogPageLoad("TempReliefListPage", true);

        try
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }
        catch (Exception ex)
        {
            _logger.LogPageLoad("TempReliefListPage", false, ex.Message);
            throw;
        }
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
