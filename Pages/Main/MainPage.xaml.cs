using NewCosmos.ViewModels.Main;

namespace NewCosmos.Pages.Main;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;

    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        await _viewModel.InitializeAsync();

        // 首页是侧边栏唯一就地切换项；其余 TAB 均为跳转型（PushAsync 覆盖本页），
        // 返回主页时统一复位为首页高亮
        VisualStateManager.GoToState(NavHome, "Selected");
    }
}
