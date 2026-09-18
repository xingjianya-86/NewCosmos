using NewCosmos.ViewModels.ElderlyBenefits;

namespace NewCosmos.Pages.ElderlyBenefits;

public partial class ElderlyBenefitsHomePage : ContentPage
{
    private readonly ElderlyBenefitsHomeViewModel _viewModel;

    public ElderlyBenefitsHomePage(ElderlyBenefitsHomeViewModel viewModel)
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
