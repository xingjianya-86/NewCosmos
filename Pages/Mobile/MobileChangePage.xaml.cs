using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.ChangeManagement;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端·保障对象动态管理入口。复用桌面 <see cref="ChangeViewModel"/>；
/// 支持携带档案ID直接进入（IParameterizedPage&lt;long&gt;），无参时通过搜索选择档案。
/// </summary>
public partial class MobileChangePage : ContentPage, IParameterizedPage<long>
{
    private readonly ChangeViewModel _viewModel;

    public MobileChangePage(ChangeViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    /// <summary>导航参数入口：携带档案ID直接进入（接口显式实现，仅经 NavigateToPageAsync 单一通道调用）。</summary>
    Task IParameterizedPage<long>.SetParameterAsync(long applicationId)
        => _viewModel.LoadApplicationInfoAsync(applicationId);

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.InitializeHeaderAsync();
    }
}
