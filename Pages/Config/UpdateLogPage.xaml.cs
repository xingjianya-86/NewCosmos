using NewCosmos.ViewModels.Config;

namespace NewCosmos.Pages.Config;

/// <summary>
/// 更新日志：当前版本、检查更新、升级历史（本机轨迹 + 服务端全量 changelog.json）。
/// 入口：系统设置 → 更新日志、主页顶栏、手机工作台。
/// </summary>
public partial class UpdateLogPage : ContentPage
{
    private readonly UpdateLogViewModel _viewModel;

    public UpdateLogPage(UpdateLogViewModel viewModel)
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
