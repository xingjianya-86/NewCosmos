using NewCosmos.Models.NavigationData;
using NewCosmos.ViewModels.Base;

namespace NewCosmos.Pages.ChangeManagement;

public partial class HouseholdDeathChangePage : ContentPage,
    IParameterizedPage<long>, IParameterizedPage<HouseholdDeathPrefill>
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

    /// <summary>联动预填入口（死亡情况说明「户主死亡」卡片）：加载档案后回填死亡日期/时刻/原因/证明编号</summary>
    async Task IParameterizedPage<HouseholdDeathPrefill>.SetParameterAsync(HouseholdDeathPrefill prefill)
    {
        await _viewModel.LoadDataAsync(prefill.ApplicationId);
        _viewModel.ApplyDeathPrefill(prefill.DeathDate, prefill.DeathTime, prefill.DeathReason, prefill.DeathCertificateNo);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // 数据加载由导航参数注入触发
    }
}
