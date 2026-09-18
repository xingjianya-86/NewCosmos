using NewCosmos.Services.Core;
using NewCosmos.ViewModels.Config;

namespace NewCosmos.Pages.Config;

public partial class NetworkAccessPage : ContentPage
{
    private readonly NetworkAccessViewModel _viewModel;
    private readonly ILoggerService _logger;

    public NetworkAccessPage(NetworkAccessViewModel viewModel, ILoggerService logger)
    {
        _logger = logger;
        _logger.LogPageLoad("NetworkAccessPage", true);

        try
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }
        catch (Exception ex)
        {
            _logger.LogPageLoad("NetworkAccessPage", false, ex.Message);
            throw;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }
}
