using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.TempRelief;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端·临时救助申请表单（类型与申请人 / 申请信息 / 金额与公示 / 家庭成员 四分区）。
/// 复用 <see cref="MobileTempReliefFormViewModel"/>（继承桌面 VM），参数注入与桌面页同契约。
/// </summary>
public partial class MobileTempReliefFormPage : ContentPage, IParameterizedPage<FormPageParameter>
{
    private readonly MobileTempReliefFormViewModel _viewModel;

    public MobileTempReliefFormPage(MobileTempReliefFormViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    /// <summary>导航参数入口（接口显式实现，仅经 NavigateToPageAsync 单一通道调用）。</summary>
    Task IParameterizedPage<FormPageParameter>.SetParameterAsync(FormPageParameter parameter)
        => SetOperationModeCoreAsync(parameter.Mode, parameter.ApplicationId);

    private async Task SetOperationModeCoreAsync(FormOperationMode mode, long? applicationId = null)
    {
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

    /// <summary>疾病编码输入变化时即时写回实体，触发编码→名称自动回填。</summary>
    private void OnDiseaseCodeTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is Entry entry && entry.BindingContext is TempReliefDisease disease)
        {
            disease.DiseaseCode = e.NewTextValue ?? string.Empty;
        }
    }
}
