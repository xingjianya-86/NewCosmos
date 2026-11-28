using NewCosmos.Services.Core;
using NewCosmos.ViewModels.ElderlyBenefits;

namespace NewCosmos.Pages.ElderlyBenefits;

public partial class ElderlyInfoChangeListPage : ContentPage
{
    private readonly ElderlyInfoChangeListViewModel _viewModel;
    private readonly ILoggerService _logger;

    public ElderlyInfoChangeListPage(
        ElderlyInfoChangeListViewModel viewModel,
        ILoggerService logger)
    {
        _logger = logger;
        _logger.LogPageLoad("ElderlyInfoChangeListPage", true);

        try
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }
        catch (Exception ex)
        {
            _logger.LogPageLoad("ElderlyInfoChangeListPage", false, ex.Message);
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
