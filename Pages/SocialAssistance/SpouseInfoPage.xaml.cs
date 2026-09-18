using NewCosmos.Helpers;
using NewCosmos.ViewModels.SocialAssistance;

namespace NewCosmos.Pages.SocialAssistance;

public partial class SpouseInfoPage : ContentPage
{
    public SpouseInfoPage(SpouseInfoViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        GenderPicker.ItemsSource = PageDefaultValues.GenderOptions;
        EducationPicker.ItemsSource = PageDefaultValues.EducationOptions;
        HealthPicker.ItemsSource = PageDefaultValues.HealthOptions;
        WorkCapacityPicker.ItemsSource = PageDefaultValues.WorkCapacityOptions;
    }
}
