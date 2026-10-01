using NewCosmos.Models.Entities;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.Recovery;

namespace NewCosmos.Pages.Recovery;

public partial class RecoveryManualPage : ContentPage, IParameterizedPage<RecoveryRecord>
{
    private readonly RecoveryManualViewModel _viewModel;

    public RecoveryManualPage(RecoveryManualViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    /// <summary>编辑草稿：参数化入口（仅经 NavigateToPageAsync 单一通道调用）</summary>
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
