using NewCosmos.ViewModels.Auth;

namespace NewCosmos.Pages.Auth;

public partial class LoginPage : ContentPage
{
    private readonly LoginViewModel _viewModel;

    public LoginPage(LoginViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
        // 永劫回归特效每删除一个人员 → 顺序切换一张背景图（仅 Windows）
        if (DeviceInfo.Platform == DevicePlatform.WinUI)
            EternalRegressionEffect.PersonDeleted += OnEternalPersonDeleted;
    }

    private void OnEternalPersonDeleted(object? sender, EventArgs e)
    {
        _viewModel.ChangeBackgroundCommand.Execute(null);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();
        EternalRegressionEffect.PersonDeleted -= OnEternalPersonDeleted;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        
        // 在页面加载时初始化记住设置
        await _viewModel.LoadRememberSettingsCommand.ExecuteAsync(null);

        // 登录窗前静默检查更新（强制更新在 VM 内拦截；失败/断网仅记日志不阻断登录）
        _ = _viewModel.CheckForUpdatesOnStartupAsync();
    }
}