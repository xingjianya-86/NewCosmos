using NewCosmos.ViewModels.ElderlyBenefits;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端·高龄津贴发放模块首页。复用桌面 <see cref="ElderlyBenefitsHomeViewModel"/>，仅替换为手机卡片布局。
/// </summary>
public partial class MobileElderlyBenefitsHomePage : ContentPage
{
    private readonly ElderlyBenefitsHomeViewModel _viewModel;

    public MobileElderlyBenefitsHomePage(ElderlyBenefitsHomeViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.InitializeAsync();
    }
}
