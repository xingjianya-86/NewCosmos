using NewCosmos.ViewModels.Mobile;

namespace NewCosmos.Pages.Mobile;

public partial class MobileMainPage : ContentPage
{
    private readonly MobileMainViewModel _viewModel;

    public MobileMainPage(MobileMainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializeAsync();
    }
}
