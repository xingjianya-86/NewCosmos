using NewCosmos.Models.NavigationData;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.Recovery;

namespace NewCosmos.Pages.Recovery;

public partial class RecoveryManualPage : ContentPage, IParameterizedPage<RecoveryManualParameter>
{
    private readonly RecoveryManualViewModel _viewModel;

    public RecoveryManualPage(RecoveryManualViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    /// <summary>导航参数注入：Record 非空 = 编辑草稿；null = 新建（原无参跳转由此承接）</summary>
    Task IParameterizedPage<RecoveryManualParameter>.SetParameterAsync(RecoveryManualParameter parameter)
    {
        if (parameter?.Record != null)
        {
            _viewModel.LoadRecord(parameter.Record);
        }
        return Task.CompletedTask;
    }

    /// <summary>身份证变化 → 满 18 位自动带出（WinUI 绑定失焦才提交源，VM 内主动写回）</summary>
    private void OnIdCardChanged(object? sender, TextChangedEventArgs e)
        => _viewModel.OnIdCardTextChanged(e.NewTextValue ?? string.Empty);

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }
}
