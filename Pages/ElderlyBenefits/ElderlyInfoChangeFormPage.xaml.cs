using NewCosmos.Services.Core;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.ElderlyBenefits;

namespace NewCosmos.Pages.ElderlyBenefits;

public partial class ElderlyInfoChangeFormPage : ContentPage, IParameterizedPage<long>
{
    private readonly ElderlyInfoChangeFormViewModel _viewModel;
    private readonly ILoggerService _logger;

    public ElderlyInfoChangeFormPage(
        ElderlyInfoChangeFormViewModel viewModel,
        ILoggerService logger)
    {
        _logger = logger;
        _logger.LogPageLoad("ElderlyInfoChangeFormPage", true);

        try
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }
        catch (Exception ex)
        {
            _logger.LogPageLoad("ElderlyInfoChangeFormPage", false, ex.Message);
            throw;
        }
    }

    /// <summary>导航参数：认证任务ID；页面显示时加载任务与申请主表当前值。</summary>
    Task IParameterizedPage<long>.SetParameterAsync(long taskId)
    {
        _viewModel.Initialize(taskId);
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
