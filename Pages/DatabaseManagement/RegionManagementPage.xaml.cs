using NewCosmos.ViewModels.DatabaseManagement;

namespace NewCosmos.Pages.DatabaseManagement;

public partial class RegionManagementPage : ContentPage
{
    private readonly RegionManagementViewModel _viewModel;

    public RegionManagementPage(RegionManagementViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // C 组：权限与数据加载移出 push 关键路径（跨网络查询会让页面"迟迟不出现"）
        _viewModel.StartLoadingInBackground();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();
    }
}
