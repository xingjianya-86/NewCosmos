using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.ChangeManagement;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端·户主死亡变更。复用桌面 <see cref="HouseholdDeathChangeViewModel"/>，参数注入同契约。
/// </summary>
public partial class MobileHouseholdDeathChangePage : ContentPage, IParameterizedPage<long>
{
    private readonly HouseholdDeathChangeViewModel _viewModel;

    public MobileHouseholdDeathChangePage(HouseholdDeathChangeViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    /// <summary>导航参数入口（接口显式实现，仅经 NavigateToPageAsync 单一通道调用）。</summary>
    Task IParameterizedPage<long>.SetParameterAsync(long applicationId)
        => _viewModel.LoadDataAsync(applicationId);
}
