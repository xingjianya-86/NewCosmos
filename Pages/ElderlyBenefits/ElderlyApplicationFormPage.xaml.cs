using NewCosmos.Services.Core;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.ElderlyBenefits;

namespace NewCosmos.Pages.ElderlyBenefits;

public partial class ElderlyApplicationFormPage : ContentPage, IParameterizedPage<ElderlyFormPageParameter>
{
    private readonly ElderlyApplicationFormViewModel _viewModel;
    private readonly ILoggerService _logger;

    public ElderlyApplicationFormPage(ElderlyApplicationFormViewModel viewModel, ILoggerService logger)
    {
        _logger = logger;
        _logger.LogPageLoad("ElderlyApplicationFormPage", true);

        try
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }
        catch (Exception ex)
        {
            _logger.LogPageLoad("ElderlyApplicationFormPage", false, ex.Message);
            throw;
        }
    }

    /// <summary>导航参数入口（接口显式实现，仅经 NavigateToPageAsync 单一通道调用）</summary>
    Task IParameterizedPage<ElderlyFormPageParameter>.SetParameterAsync(ElderlyFormPageParameter parameter)
        => SetOperationModeCoreAsync(parameter.Mode, parameter.ApplicationId, parameter.NavigateToStopAfterSave,
                                     parameter.PrefillIdCard, parameter.PrefillName);

    /// <summary>设置操作模式核心逻辑（Create/Edit/View，含保存后跳停发的联动开关 + 下月待办预填）</summary>
    private async Task SetOperationModeCoreAsync(FormOperationMode mode, long? applicationId = null,
        bool navigateToStopAfterSave = false, string? prefillIdCard = null, string? prefillName = null)
    {
        _logger.Debug($"[PAGE] 设置普惠高龄表单操作模式: {mode}");

        _viewModel.OperationMode = mode;
        _viewModel.PendingStopApplicationId = applicationId ?? 0;
        _viewModel.SetNavigateToStopAfterSave(navigateToStopAfterSave);

        if (mode == FormOperationMode.Create)
        {
            await _viewModel.InitializeAsync();

            // 下月待办「待新增」快捷办理：预填身份证/姓名（身份证预填后自动判类 + 补发计算）
            if (!string.IsNullOrWhiteSpace(prefillIdCard))
            {
                _viewModel.Name = prefillName?.Trim() ?? string.Empty;
                _viewModel.IdCard = prefillIdCard.Trim();
            }
        }
        else if (applicationId.HasValue && applicationId.Value > 0)
        {
            await _viewModel.InitializeAsync();
            await _viewModel.LoadAsync(applicationId.Value);
        }
    }
}
