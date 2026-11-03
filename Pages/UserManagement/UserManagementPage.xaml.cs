using NewCosmos.ViewModels.UserManagement;

namespace NewCosmos.Pages.UserManagement;

public partial class UserManagementPage : ContentPage
{
    private readonly UserManagementViewModel _viewModel;

    public UserManagementPage(UserManagementViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // 未登录/会话已清时用 0（无任何权限），绝不能兜底成 1（历史 BUG：会得到管理员权限视图）
        var userId = App.CurrentUserId ?? 0;
        await _viewModel.InitializePermissionsAsync(userId);
        await _viewModel.OnAppearingAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();
    }
}
