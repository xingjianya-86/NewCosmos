using NewCosmos.ViewModels.SocialAssistance;
namespace NewCosmos.Pages.SocialAssistance;
public partial class ClassificationPage : ContentPage
{
    public ClassificationPage(ClassificationViewModel vm) { InitializeComponent(); BindingContext = vm; }
}
