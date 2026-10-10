using NewCosmos.Services.Core;
using NewCosmos.ViewModels.Config;

namespace NewCosmos.Pages.Config;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;
    private readonly ILoggerService _logger;

    public SettingsPage(SettingsViewModel viewModel, ILoggerService logger)
    {
        _logger = logger;
        _logger.LogPageLoad("SettingsPage", true);

        try
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }
        catch (Exception ex)
        {
            _logger.LogPageLoad("SettingsPage", false, ex.Message);
            throw;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }
}
