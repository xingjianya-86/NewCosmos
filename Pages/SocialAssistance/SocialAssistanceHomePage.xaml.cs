using NewCosmos.ViewModels.SocialAssistance;

namespace NewCosmos.Pages.SocialAssistance;

public partial class SocialAssistanceHomePage : ContentPage
{
    private readonly SocialAssistanceHomeViewModel _viewModel;

    public SocialAssistanceHomePage(SocialAssistanceHomeViewModel viewModel)
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
