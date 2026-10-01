using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.ElderlyBenefits;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端·高龄津贴申请登记表单（基本信息 / 代办代领 / 判定认证 三分区）。
/// 复用桌面 <see cref="ElderlyApplicationFormViewModel"/>，参数注入与桌面页同契约。
/// </summary>
public partial class MobileElderlyApplicationFormPage : ContentPage, IParameterizedPage<ElderlyFormPageParameter>
{
    private readonly ElderlyApplicationFormViewModel _viewModel;

    public MobileElderlyApplicationFormPage(ElderlyApplicationFormViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    /// <summary>导航参数入口（接口显式实现，仅经 NavigateToPageAsync 单一通道调用）。</summary>
    Task IParameterizedPage<ElderlyFormPageParameter>.SetParameterAsync(ElderlyFormPageParameter parameter)
        => SetOperationModeCoreAsync(parameter.Mode, parameter.ApplicationId, parameter.NavigateToStopAfterSave,
                                     parameter.PrefillIdCard, parameter.PrefillName);

    private async Task SetOperationModeCoreAsync(FormOperationMode mode, long? applicationId = null,
        bool navigateToStopAfterSave = false, string? prefillIdCard = null, string? prefillName = null)
    {
        _viewModel.OperationMode = mode;
        _viewModel.PendingStopApplicationId = applicationId ?? 0;
        _viewModel.SetNavigateToStopAfterSave(navigateToStopAfterSave);

        if (mode == FormOperationMode.Create)
        {
            await _viewModel.InitializeAsync();

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
