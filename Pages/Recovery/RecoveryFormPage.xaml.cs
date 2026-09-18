using NewCosmos.Models.Entities;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.Recovery;

namespace NewCosmos.Pages.Recovery;

public partial class RecoveryFormPage : ContentPage, IParameterizedPage<StoppedPersonDto>
{
    private readonly RecoveryFormViewModel _viewModel;

    public RecoveryFormPage(RecoveryFormViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    Task IParameterizedPage<StoppedPersonDto>.SetParameterAsync(StoppedPersonDto parameter)
    {
        if (parameter != null)
        {
            _viewModel.LoadPersonInfo(parameter);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 编辑草稿：回填记录数据
    /// </summary>
    public void SetEditRecord(RecoveryRecord record)
    {
        _viewModel.LoadRecord(record);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }
}
