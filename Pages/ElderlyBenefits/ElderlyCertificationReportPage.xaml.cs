using NewCosmos.Services.Core;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.ElderlyBenefits;

namespace NewCosmos.Pages.ElderlyBenefits;

public partial class ElderlyCertificationReportPage : ContentPage, IParameterizedPage<long>
{
    private readonly ElderlyCertificationReportViewModel _viewModel;
    private readonly ILoggerService _logger;

    public ElderlyCertificationReportPage(
        ElderlyCertificationReportViewModel viewModel,
        ILoggerService logger)
    {
        _logger = logger;
        _logger.LogPageLoad("ElderlyCertificationReportPage", true);

        try
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }
        catch (Exception ex)
        {
            _logger.LogPageLoad("ElderlyCertificationReportPage", false, ex.Message);
            throw;
        }
    }

    /// <summary>导航参数入口：注入认证批次ID，页面显示时再加载批次并定位。</summary>
    Task IParameterizedPage<long>.SetParameterAsync(long campaignId)
    {
        _viewModel.Initialize(campaignId);
        return Task.CompletedTask;
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
