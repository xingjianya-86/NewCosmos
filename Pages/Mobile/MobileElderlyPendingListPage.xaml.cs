using NewCosmos.ViewModels.ElderlyBenefits;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端·高龄津贴下月待办（待新增 / 需停旧增新 两 Tab）。复用桌面 <see cref="ElderlyPendingListViewModel"/>。
/// </summary>
public partial class MobileElderlyPendingListPage : ContentPage
{
    private readonly ElderlyPendingListViewModel _viewModel;

    public MobileElderlyPendingListPage(ElderlyPendingListViewModel viewModel)
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
}
