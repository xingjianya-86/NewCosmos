using NewCosmos.ViewModels.DatabaseManagement;

namespace NewCosmos.Pages.DatabaseManagement;

public partial class DatabaseManagementPage : ContentPage
{
    private readonly DatabaseManagementViewModel _viewModel;

    public DatabaseManagementPage(DatabaseManagementViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // C 组：权限检查与四段数据加载移出 push 关键路径——跨网络单查询 0.6-1.4s 时，
        // 在 OnAppearing 里 await 会让 PushAsync 等到加载完成才结束，页面表现为"点击后迟迟不出现"
        _viewModel.StartLoadingInBackground();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();
    }
}