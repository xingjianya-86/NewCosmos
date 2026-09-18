using NewCosmos.Services.Core;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.ElderlyBenefits;

namespace NewCosmos.Pages.ElderlyBenefits;

public partial class ElderlyReviewPage : ContentPage, IParameterizedPage<ElderlyReviewPageParameter>
{
    private readonly ElderlyReviewViewModel _viewModel;
    private readonly ILoggerService _logger;

    public ElderlyReviewPage(ElderlyReviewViewModel viewModel, ILoggerService logger)
    {
        _logger = logger;
        _logger.LogPageLoad("ElderlyReviewPage", true);

        try
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }
        catch (Exception ex)
        {
            _logger.LogPageLoad("ElderlyReviewPage", false, ex.Message);
            throw;
        }
    }

    /// <summary>导航参数入口：定位人员并重评（仅经 NavigateToPageAsync 单一通道调用）</summary>
    Task IParameterizedPage<ElderlyReviewPageParameter>.SetParameterAsync(ElderlyReviewPageParameter parameter)
        => _viewModel.LoadAsync(parameter);
}
