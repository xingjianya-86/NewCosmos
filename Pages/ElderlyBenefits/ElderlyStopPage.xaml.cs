using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.ElderlyBenefits;

namespace NewCosmos.Pages.ElderlyBenefits;

public partial class ElderlyStopPage : ContentPage, IParameterizedPage<ElderlyApplication>
{
    private readonly ElderlyStopViewModel _viewModel;
    private readonly ILoggerService _logger;

    public ElderlyStopPage(ElderlyStopViewModel viewModel, ILoggerService logger)
    {
        _logger = logger;
        _logger.LogPageLoad("ElderlyStopPage", true);

        try
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }
        catch (Exception ex)
        {
            _logger.LogPageLoad("ElderlyStopPage", false, ex.Message);
            throw;
        }
    }

    /// <summary>导航参数入口：加载待停发登记（接口显式实现，仅经 NavigateToPageAsync 单一通道调用）</summary>
    Task IParameterizedPage<ElderlyApplication>.SetParameterAsync(ElderlyApplication app)
    {
        _viewModel.LoadApplication(app);
        return Task.CompletedTask;
    }
}
