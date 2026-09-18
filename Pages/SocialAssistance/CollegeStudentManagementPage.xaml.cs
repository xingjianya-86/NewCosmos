using NewCosmos.ViewModels.SocialAssistance;

namespace NewCosmos.Pages.SocialAssistance;

public partial class CollegeStudentManagementPage : ContentPage
{
    private readonly CollegeStudentManagementViewModel _viewModel;

    public CollegeStudentManagementPage(CollegeStudentManagementViewModel viewModel)
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
