using NewCosmos.Services.Core;
using NewCosmos.ViewModels.SocialAssistance;

namespace NewCosmos.Pages.SocialAssistance;

public partial class RemoteAssistanceListPage : ContentPage
{
    private readonly RemoteAssistanceListViewModel _viewModel;
    private readonly ILoggerService _logger;

    public RemoteAssistanceListPage(RemoteAssistanceListViewModel viewModel, ILoggerService logger)
    {
        _logger = logger;
        _logger.LogPageLoad("RemoteAssistanceListPage", true);

        try
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }
        catch (Exception ex)
        {
            _logger.LogPageLoad("RemoteAssistanceListPage", false, ex.Message);
            throw;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _logger.Debug("[PAGE] RemoteAssistanceListPage OnAppearing");

        try
        {
            await _viewModel.OnAppearingAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "异地协助列表页加载失败");
        }
    }
}
