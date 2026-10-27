using NewCosmos.ViewModels.ArchiveManagement;

namespace NewCosmos.Pages.ArchiveManagement;

/// <summary>死亡情况说明页（村内死亡申报 → 身份聚合 → 数据提醒 → 联动 → 文书出具）</summary>
public partial class DeathStatementPage : ContentPage
{
    private readonly DeathStatementViewModel _viewModel;

    public DeathStatementPage(DeathStatementViewModel viewModel)
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
