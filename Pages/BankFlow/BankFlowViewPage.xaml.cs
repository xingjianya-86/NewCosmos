using NewCosmos.Services.Core;
using NewCosmos.ViewModels.BankFlow;

namespace NewCosmos.Pages.BankFlow;

public partial class BankFlowViewPage : ContentPage
{
    private readonly BankFlowViewViewModel _viewModel;
    private readonly ILoggerService _logger;

    public BankFlowViewPage(BankFlowViewViewModel viewModel, ILoggerService logger)
    {
        _logger = logger;
        _logger.LogPageLoad("BankFlowViewPage", true);

        try
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }
        catch (Exception ex)
        {
            _logger.LogPageLoad("BankFlowViewPage", false, ex.Message);
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
