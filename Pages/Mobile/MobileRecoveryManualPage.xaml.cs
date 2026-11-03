using NewCosmos.Models.NavigationData;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.Recovery;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端·手工录入追缴。复用桌面 <see cref="RecoveryManualViewModel"/>；
/// 导航参数与桌面孪生页同契约 <see cref="RecoveryManualParameter"/>（Record 非空=编辑草稿，null=新建）。
/// </summary>
public partial class MobileRecoveryManualPage : ContentPage, IParameterizedPage<RecoveryManualParameter>
{
    private readonly RecoveryManualViewModel _viewModel;

    public MobileRecoveryManualPage(RecoveryManualViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    Task IParameterizedPage<RecoveryManualParameter>.SetParameterAsync(RecoveryManualParameter parameter)
    {
        if (parameter?.Record != null)
        {
            _viewModel.LoadRecord(parameter.Record);
        }
        return Task.CompletedTask;
    }

    /// <summary>身份证变化 → 满 18 位自动带出（复用桌面 RecoveryManualViewModel 同一逻辑）</summary>
    private void OnIdCardChanged(object? sender, TextChangedEventArgs e)
        => _viewModel.OnIdCardTextChanged(e.NewTextValue ?? string.Empty);

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }
}
