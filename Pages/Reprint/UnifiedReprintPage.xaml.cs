using NewCosmos.Services.Core;
using NewCosmos.ViewModels.Reprint;

namespace NewCosmos.Pages.Reprint;

public partial class UnifiedReprintPage : ContentPage
{
    private readonly UnifiedReprintViewModel _viewModel;
    private readonly ILoggerService _logger;

    public UnifiedReprintPage(UnifiedReprintViewModel viewModel, ILoggerService logger)
    {
        _logger = logger;
        _logger.LogPageLoad("UnifiedReprintPage", true);

        try
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }
        catch (Exception ex)
        {
            _logger.LogPageLoad("UnifiedReprintPage", false, ex.Message);
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

    /// <summary>带初始域导航：定位到指定域（入口页传 DomainKey）</summary>
    public void PrepareForDomain(string domainKey)
    {
        _viewModel.PrepareForDomain(domainKey);
    }
}
