using NewCosmos.Models.Entities;
using NewCosmos.ViewModels.SocialAssistance;

namespace NewCosmos.Pages.Mobile.SocialAssistance.FormSteps;

/// <summary>
/// 手机版申请表单 Step5：分类认定（由手机表单页按 CurrentStep 懒加载）。
/// </summary>
public partial class MobileApplicationFormStep5View : ContentView
{
    public MobileApplicationFormStep5View()
    {
        InitializeComponent();
    }

    private ApplicationFormViewModel? ViewModel => BindingContext as ApplicationFormViewModel;

    /// <summary>照料人身份证号变化时自动更新性别和年龄。</summary>
    private void OnCaregiverIdCardChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is Entry entry && entry.BindingContext is Caregiver caregiver)
        {
            ViewModel?.OnCaregiverIdCardChanged(caregiver);
        }
    }
}
