using NewCosmos.ViewModels.DutyManagement;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端·值班表（只读查看）。复用桌面 <see cref="DutyScheduleViewModel"/>；
/// 手机端仅保留按月查看（月份切换 + 每日四组值班人/电话），生成与调整/导出打印由电脑端完成。
/// </summary>
public partial class MobileDutySchedulePage : ContentPage
{
    private readonly DutyScheduleViewModel _viewModel;

    public MobileDutySchedulePage(DutyScheduleViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializePermissionsAsync(App.CurrentUserId ?? 1);
        await _viewModel.OnAppearingAsync();
    }
}
