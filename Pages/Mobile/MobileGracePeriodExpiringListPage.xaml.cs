using NewCosmos.ViewModels.SocialAssistance;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端·渐退期管理列表。复用桌面 <see cref="GracePeriodExpiringListViewModel"/>，列表中可"重新核算"进入经济复核。
/// </summary>
public partial class MobileGracePeriodExpiringListPage : ContentPage
{
    private readonly GracePeriodExpiringListViewModel _viewModel;

    public MobileGracePeriodExpiringListPage(GracePeriodExpiringListViewModel viewModel)
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
