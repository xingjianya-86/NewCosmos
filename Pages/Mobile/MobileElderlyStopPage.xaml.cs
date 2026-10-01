using NewCosmos.Models.Entities;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.ElderlyBenefits;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端·高龄津贴停发办理。复用桌面 <see cref="ElderlyStopViewModel"/>，参数注入同契约。
/// </summary>
public partial class MobileElderlyStopPage : ContentPage, IParameterizedPage<ElderlyApplication>
{
    private readonly ElderlyStopViewModel _viewModel;

    public MobileElderlyStopPage(ElderlyStopViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    /// <summary>导航参数入口：加载待停发登记（接口显式实现，仅经 NavigateToPageAsync 单一通道调用）。</summary>
    Task IParameterizedPage<ElderlyApplication>.SetParameterAsync(ElderlyApplication app)
    {
        _viewModel.LoadApplication(app);
        return Task.CompletedTask;
    }
}
