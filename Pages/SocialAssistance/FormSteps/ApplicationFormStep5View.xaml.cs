using NewCosmos.Models.Entities;
using NewCosmos.ViewModels.SocialAssistance;

namespace NewCosmos.Pages.SocialAssistance.FormSteps;

/// <summary>
/// 申请表单 Step5：分类认定（由宿主页面按 CurrentStep 懒加载）
/// </summary>
public partial class ApplicationFormStep5View : ContentView
{
    public ApplicationFormStep5View()
    {
        InitializeComponent();
    }

    private ApplicationFormViewModel? ViewModel => BindingContext as ApplicationFormViewModel;

    /// <summary>
    /// 照料人身份证号变化时自动更新性别和年龄
    /// </summary>
    private void OnCaregiverIdCardChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is Entry entry && entry.BindingContext is Caregiver caregiver)
        {
            ViewModel?.OnCaregiverIdCardChanged(caregiver);
        }
    }
}
