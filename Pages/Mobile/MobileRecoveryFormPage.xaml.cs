using NewCosmos.Models.NavigationData;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.Recovery;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端·追缴信息填写（搜索录入）。复用桌面 <see cref="RecoveryFormViewModel"/>。
/// 导航参数与桌面孪生页同契约 <see cref="RecoveryFormParameter"/>（新建 Person / 编辑 Record）。
/// </summary>
public partial class MobileRecoveryFormPage : ContentPage,
    IParameterizedPage<RecoveryFormParameter>
{
    private readonly RecoveryFormViewModel _viewModel;

    public MobileRecoveryFormPage(RecoveryFormViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    Task IParameterizedPage<RecoveryFormParameter>.SetParameterAsync(RecoveryFormParameter parameter)
    {
        if (parameter == null) return Task.CompletedTask;

        if (parameter.Record != null)
            _viewModel.LoadRecord(parameter.Record);
        else if (parameter.Person != null)
            _viewModel.LoadPersonInfo(parameter.Person);

        return Task.CompletedTask;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }
}
