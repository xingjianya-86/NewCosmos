using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.ElderlyBenefits;

namespace NewCosmos.Pages.ElderlyBenefits;

public partial class ElderlyCertificationStopPreviewPage : ContentPage, IParameterizedPage<ElderlyCertCampaign>
{
    private readonly ElderlyCertificationStopPreviewViewModel _viewModel;
    private readonly ILoggerService _logger;

    public ElderlyCertificationStopPreviewPage(
        ElderlyCertificationStopPreviewViewModel viewModel,
        ILoggerService logger)
    {
        _logger = logger;
        _logger.LogPageLoad("ElderlyCertificationStopPreviewPage", true);

        try
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }
        catch (Exception ex)
        {
            _logger.LogPageLoad("ElderlyCertificationStopPreviewPage", false, ex.Message);
            throw;
        }
    }

    /// <summary>导航参数入口：注入认证批次快照，页面显示时再刷新批次状态与停发候选。</summary>
    Task IParameterizedPage<ElderlyCertCampaign>.SetParameterAsync(ElderlyCertCampaign campaign)
    {
        _viewModel.Initialize(campaign);
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
