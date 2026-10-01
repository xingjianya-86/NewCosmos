using NewCosmos.ViewModels.Recovery;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 手机端·后补追缴（停止人员追缴 + 我的追缴记录）。复用桌面 <see cref="RecoverySearchViewModel"/>。
/// </summary>
public partial class MobileRecoverySearchPage : ContentPage
{
    private readonly RecoverySearchViewModel _viewModel;

    public MobileRecoverySearchPage(RecoverySearchViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadStatsAsync();
        if (_viewModel.IsHistoryMode)
        {
            await _viewModel.LoadHistoryAsync();
        }
    }
}
