using NewCosmos.ViewModels.TempRelief;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端·临时救助申请列表（草稿/已确认/已终止 三 Tab）。复用桌面 <see cref="TempReliefListViewModel"/>。
/// </summary>
public partial class MobileTempReliefListPage : ContentPage
{
    private readonly TempReliefListViewModel _viewModel;

    public MobileTempReliefListPage(TempReliefListViewModel viewModel)
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
