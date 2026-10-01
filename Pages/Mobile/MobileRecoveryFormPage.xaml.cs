using NewCosmos.Models.Entities;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.Recovery;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端·追缴信息填写（搜索录入）。复用桌面 <see cref="RecoveryFormViewModel"/>。
/// 支持 StoppedPersonDto（新建）与 RecoveryRecord（编辑草稿）两种参数。
/// </summary>
public partial class MobileRecoveryFormPage : ContentPage,
    IParameterizedPage<StoppedPersonDto>,
    IParameterizedPage<RecoveryRecord>
{
    private readonly RecoveryFormViewModel _viewModel;

    public MobileRecoveryFormPage(RecoveryFormViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    Task IParameterizedPage<StoppedPersonDto>.SetParameterAsync(StoppedPersonDto parameter)
    {
        if (parameter != null) _viewModel.LoadPersonInfo(parameter);
        return Task.CompletedTask;
    }

    Task IParameterizedPage<RecoveryRecord>.SetParameterAsync(RecoveryRecord record)
    {
        _viewModel.LoadRecord(record);
        return Task.CompletedTask;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }
}
