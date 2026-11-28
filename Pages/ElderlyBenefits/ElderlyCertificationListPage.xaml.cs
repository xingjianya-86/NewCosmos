using NewCosmos.Services.Core;
using NewCosmos.ViewModels.ElderlyBenefits;

namespace NewCosmos.Pages.ElderlyBenefits;

public partial class ElderlyCertificationListPage : ContentPage
{
    private readonly ElderlyCertificationListViewModel _viewModel;
    private readonly ILoggerService _logger;

    public ElderlyCertificationListPage(ElderlyCertificationListViewModel viewModel, ILoggerService logger)
    {
        _logger = logger;
        _logger.LogPageLoad("ElderlyCertificationListPage", true);

        try
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }
        catch (Exception ex)
        {
            _logger.LogPageLoad("ElderlyCertificationListPage", false, ex.Message);
            throw;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }
}
