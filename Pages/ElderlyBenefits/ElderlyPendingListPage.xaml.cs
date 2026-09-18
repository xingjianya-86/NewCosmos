using NewCosmos.Services.Core;
using NewCosmos.ViewModels.ElderlyBenefits;

namespace NewCosmos.Pages.ElderlyBenefits;

public partial class ElderlyPendingListPage : ContentPage
{
    private readonly ElderlyPendingListViewModel _viewModel;
    private readonly ILoggerService _logger;

    public ElderlyPendingListPage(ElderlyPendingListViewModel viewModel, ILoggerService logger)
    {
        _logger = logger;
        _logger.LogPageLoad("ElderlyPendingListPage", true);

        try
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }
        catch (Exception ex)
        {
            _logger.LogPageLoad("ElderlyPendingListPage", false, ex.Message);
            throw;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }
}
