using NewCosmos.Models.Entities;
using NewCosmos.ViewModels.Recovery;

namespace NewCosmos.Pages.Recovery;

public partial class RecoveryManualPage : ContentPage
{
    private readonly RecoveryManualViewModel _viewModel;

    public RecoveryManualPage(RecoveryManualViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
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
