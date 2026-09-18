using NewCosmos.ViewModels.SocialAssistance;
namespace NewCosmos.Pages.SocialAssistance;
public partial class FamilyMembersPage : ContentPage
{
    public FamilyMembersPage(FamilyMembersViewModel vm) { InitializeComponent(); BindingContext = vm; }
}
