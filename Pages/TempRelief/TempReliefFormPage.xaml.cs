using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.TempRelief;

namespace NewCosmos.Pages.TempRelief;

public partial class TempReliefFormPage : ContentPage, IParameterizedPage<FormPageParameter>
{
    private readonly TempReliefFormViewModel _viewModel;
    private readonly ILoggerService _logger;

    public TempReliefFormPage(TempReliefFormViewModel viewModel, ILoggerService logger)
    {
        _logger = logger;
        _logger.LogPageLoad("TempReliefFormPage", true);

        try
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }
        catch (Exception ex)
        {
            _logger.LogPageLoad("TempReliefFormPage", false, ex.Message);
            throw;
        }
    }

    /// <summary>导航参数入口（接口显式实现，仅经 NavigateToPageAsync 单一通道调用）</summary>
    Task IParameterizedPage<FormPageParameter>.SetParameterAsync(FormPageParameter parameter)
        => SetOperationModeCoreAsync(parameter.Mode, parameter.ApplicationId);

    /// <summary>设置操作模式核心逻辑（Create/Edit/View）</summary>
    private async Task SetOperationModeCoreAsync(FormOperationMode mode, long? applicationId = null)
    {
        _logger.Debug($"[PAGE] 设置临时救助表单操作模式: {mode}");

        _viewModel.OperationMode = mode;

        if (mode == FormOperationMode.Create)
        {
            await _viewModel.InitializeAsync();
        }
        else if (applicationId.HasValue && applicationId.Value > 0)
        {
            await _viewModel.InitializeAsync();
            await _viewModel.LoadAsync(applicationId.Value);
        }
    }

    /// <summary>
    /// 疾病编码输入变化时即时写回实体，触发编码→名称自动回填（WinUI3 绑定默认失焦才提交）
    /// </summary>
    private void OnDiseaseCodeTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is Entry entry && entry.BindingContext is TempReliefDisease disease)
        {
            disease.DiseaseCode = e.NewTextValue ?? string.Empty;
        }
    }
}
