using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.DutyManagement;

namespace NewCosmos.Pages.DutyManagement;

/// <summary>
/// 班务调整管理页：请假 / 串班（单向转让）/ 代班 的登记、发起与管理唯一完整界面。
/// 支持参数化导航（值班表点人名跳转预填）。
/// </summary>
public partial class DutyAdjustPage : ContentPage, IParameterizedPage<DutyAdjustNavParam>
{
    private readonly DutyAdjustViewModel _viewModel;

    public DutyAdjustPage(DutyAdjustViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    /// <summary>接收导航参数（值班表点人名跳转预填；实际处理延后到 OnAppearingAsync）</summary>
    public Task SetParameterAsync(DutyAdjustNavParam parameter)
    {
        _viewModel.SetNavigationParameter(parameter);
        return Task.CompletedTask;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializePermissionsAsync(App.CurrentUserId ?? 1);
        await _viewModel.OnAppearingAsync();
    }
}
