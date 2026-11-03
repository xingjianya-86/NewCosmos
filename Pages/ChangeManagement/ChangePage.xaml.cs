using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.ChangeManagement;

namespace NewCosmos.Pages.ChangeManagement;

public partial class ChangePage : ContentPage, IParameterizedPage<long>
{
    private readonly ChangeViewModel _viewModel;

    public ChangePage(ChangeViewModel vm)
    {
        InitializeComponent();
        _viewModel = vm;
        BindingContext = _viewModel;
    }

    /// <summary>导航参数入口：携带档案ID直接进入（如列表页"变更"按钮）</summary>
    Task IParameterizedPage<long>.SetParameterAsync(long applicationId)
    {
        // 抑制紧随的首次 OnAppearing 重复刷新（SetParameterAsync 先于 PushAsync/OnAppearing 执行）
        _viewModel.SuppressNextAppearRefresh();
        return _viewModel.LoadApplicationInfoAsync(applicationId);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.InitializeHeaderAsync();

        // 从变更流程（如户主死亡整户停止 / 恢复保障）返回本页时刷新已选档案：
        // 状态、家庭人口、变更入口可用性（单人户智能化）、本月已办标记随之更新
        _viewModel.RefreshOnAppear();
    }
}
