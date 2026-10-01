using NewCosmos.Services.Domain.Printing;
using NewCosmos.ViewModels.Main;

namespace NewCosmos.Pages.Main;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;
    private readonly IPrintAgentService _printAgentService;

    public MainPage(MainViewModel viewModel, IPrintAgentService printAgentService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _printAgentService = printAgentService;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // 启动推送打印代理（仅 Windows 端生效）：后台轮询并执行手机端推送的打印任务
        _printAgentService.Start();

        await _viewModel.InitializeAsync();

        // 首页是侧边栏唯一就地切换项；其余 TAB 均为跳转型（PushAsync 覆盖本页），
        // 返回主页时统一复位为首页高亮
        VisualStateManager.GoToState(NavHome, "Selected");
    }
}
