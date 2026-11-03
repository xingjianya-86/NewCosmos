using NewCosmos.Models.NavigationData;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.Recovery;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端·后补追缴（停止人员追缴 + 我的追缴记录）。复用桌面 <see cref="RecoverySearchViewModel"/>。
/// </summary>
public partial class MobileRecoverySearchPage : ContentPage, IParameterizedPage<RecoverySearchParameter>
{
    private readonly RecoverySearchViewModel _viewModel;

    public MobileRecoverySearchPage(RecoverySearchViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    /// <summary>导航参数注入（与桌面孪生页同契约，否则 Android 参数被静默丢弃）</summary>
    Task IParameterizedPage<RecoverySearchParameter>.SetParameterAsync(RecoverySearchParameter parameter)
    {
        _viewModel.ApplyNavigationParameter(parameter);
        return Task.CompletedTask;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // 统计 + 按页签/参数的初始列表加载（与桌面页统一入口，替代原「统计 + 历史页签单独加载」）
        await _viewModel.LoadInitialListAsync();
    }
}
