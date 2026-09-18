using NewCosmos.Services.Core;
using NewCosmos.ViewModels.ElderlyBenefits;

namespace NewCosmos.Pages.ElderlyBenefits;

public partial class ElderlyReportPage : ContentPage
{
    private readonly ElderlyReportViewModel _viewModel;
    private readonly ILoggerService _logger;

    public ElderlyReportPage(ElderlyReportViewModel viewModel, ILoggerService logger)
    {
        _logger = logger;
        _logger.LogPageLoad("ElderlyReportPage", true);

        try
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }
        catch (Exception ex)
        {
            _logger.LogPageLoad("ElderlyReportPage", false, ex.Message);
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
