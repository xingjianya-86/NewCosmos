using NewCosmos.ViewModels.ArchiveManagement;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端死亡情况说明页（与桌面页共用 <see cref="DeathStatementViewModel"/>，孪生页模式同 MobileArchiveQueryPage）。
/// 本机只做检索/申报/身份与提醒查看 + 推送打印；PDF 预览与实际打印由 PC 端完成（Android 无 Office COM）。
/// </summary>
public partial class MobileDeathStatementPage : ContentPage
{
    private readonly DeathStatementViewModel _viewModel;

    public MobileDeathStatementPage(DeathStatementViewModel viewModel)
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
