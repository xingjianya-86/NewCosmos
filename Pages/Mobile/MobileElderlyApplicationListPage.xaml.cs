using NewCosmos.ViewModels.ElderlyBenefits;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端·高龄津贴申请登记列表（草稿/已确认/已停发 三 Tab）。
/// 复用桌面 <see cref="ElderlyApplicationListViewModel"/>。
/// </summary>
public partial class MobileElderlyApplicationListPage : ContentPage
{
    private readonly ElderlyApplicationListViewModel _viewModel;

    public MobileElderlyApplicationListPage(ElderlyApplicationListViewModel viewModel)
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
