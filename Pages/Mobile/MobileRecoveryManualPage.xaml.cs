using NewCosmos.Models.Entities;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.Recovery;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端·手工录入追缴。复用桌面 <see cref="RecoveryManualViewModel"/>；
/// 支持 RecoveryRecord 参数用于编辑草稿。
/// </summary>
public partial class MobileRecoveryManualPage : ContentPage, IParameterizedPage<RecoveryRecord>
{
    private readonly RecoveryManualViewModel _viewModel;

    public MobileRecoveryManualPage(RecoveryManualViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
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
