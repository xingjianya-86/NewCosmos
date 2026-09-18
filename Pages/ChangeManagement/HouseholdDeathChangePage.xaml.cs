using NewCosmos.ViewModels.Base;

namespace NewCosmos.Pages.ChangeManagement;

public partial class HouseholdDeathChangePage : ContentPage, IParameterizedPage<long>
{
    private readonly ViewModels.ChangeManagement.HouseholdDeathChangeViewModel _viewModel;

    public HouseholdDeathChangePage(ViewModels.ChangeManagement.HouseholdDeathChangeViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    /// <summary>导航参数入口（接口显式实现，仅经 NavigateToPageAsync 单一通道调用）</summary>
    Task IParameterizedPage<long>.SetParameterAsync(long applicationId)
        => _viewModel.LoadDataAsync(applicationId);

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // 数据加载由导航参数注入触发
    }
}
