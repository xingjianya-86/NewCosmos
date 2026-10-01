using NewCosmos.ViewModels.ArchiveManagement;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端·救助档案检索查阅（只读）。复用桌面 <see cref="ArchiveQueryViewModel"/>；
/// 手机端仅保留检索/查阅：当前库可跳转只读申请档案，历史库展示摘要与成员明细；证明打印由电脑端完成。
/// </summary>
public partial class MobileArchiveQueryPage : ContentPage
{
    private readonly ArchiveQueryViewModel _viewModel;

    public MobileArchiveQueryPage(ArchiveQueryViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();
    }
}
