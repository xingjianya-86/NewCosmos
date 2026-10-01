using NewCosmos.ViewModels.ElderlyBenefits;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端·高龄津贴停发办理列表（在享人员 + 当前库/导入库搜索）。
/// 复用桌面 <see cref="ElderlyStopListViewModel"/>。
/// </summary>
public partial class MobileElderlyStopListPage : ContentPage
{
    private readonly ElderlyStopListViewModel _viewModel;

    public MobileElderlyStopListPage(ElderlyStopListViewModel viewModel)
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
