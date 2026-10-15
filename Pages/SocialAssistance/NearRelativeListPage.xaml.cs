using NewCosmos.ViewModels.SocialAssistance;

namespace NewCosmos.Pages.SocialAssistance;

public partial class NearRelativeListPage : ContentPage
{
    private readonly NearRelativeListViewModel _viewModel;

    public NearRelativeListPage(NearRelativeListViewModel viewModel)
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
