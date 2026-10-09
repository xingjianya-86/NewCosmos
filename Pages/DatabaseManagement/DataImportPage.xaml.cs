using NewCosmos.ViewModels.DatabaseManagement;

namespace NewCosmos.Pages.DatabaseManagement;

public partial class DataImportPage : ContentPage
{
    private readonly DataImportViewModel _viewModel;

    public DataImportPage(DataImportViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // C 组：与其余子页保持一致，页面加载移出 push 关键路径
        _viewModel.StartLoadingInBackground();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();
    }
}
