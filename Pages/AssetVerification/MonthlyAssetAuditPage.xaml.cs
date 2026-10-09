using NewCosmos.ViewModels.AssetVerification;

namespace NewCosmos.Pages.AssetVerification;

public partial class MonthlyAssetAuditPage : ContentPage
{
    private readonly MonthlyAssetAuditViewModel _viewModel;

    public MonthlyAssetAuditPage(MonthlyAssetAuditViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();
    }

    /// <summary>
    /// 搜索框回车触发搜索（与"搜索"按钮同一命令）
    /// </summary>
    private void OnSearchEntryCompleted(object? sender, EventArgs e)
    {
        if (_viewModel.SearchCommand.CanExecute(null))
            _viewModel.SearchCommand.Execute(null);
    }
}
