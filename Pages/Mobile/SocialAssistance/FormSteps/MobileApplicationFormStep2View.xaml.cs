using NewCosmos.Models.Entities;
using NewCosmos.ViewModels.SocialAssistance;

namespace NewCosmos.Pages.Mobile.SocialAssistance.FormSteps;

/// <summary>
/// 手机版申请表单 Step2：家庭成员（由手机表单页按 CurrentStep 懒加载）。
/// 事件联动逻辑与桌面版一致，仅布局改为单列。
/// </summary>
public partial class MobileApplicationFormStep2View : ContentView
{
    public MobileApplicationFormStep2View()
    {
        InitializeComponent();
    }

    private ApplicationFormViewModel? ViewModel => BindingContext as ApplicationFormViewModel;

    /// <summary>身份证号变化时自动更新性别、年龄和身体状况。</summary>
    private void OnFamilyMemberIdCardChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is Entry entry && entry.BindingContext is FamilyMember member)
        {
            ViewModel?.OnIdCardChanged(member);
        }
    }

    /// <summary>残疾证号变化时自动解析残疾类型、等级并更新身体状况。</summary>
    private void OnFamilyMemberDisabilityCardNoChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is Entry entry && entry.BindingContext is FamilyMember member)
        {
            ViewModel?.OnDisabilityCertificateChanged(member);
        }
    }

    /// <summary>疾病分类变化时更新身体状况。</summary>
    private void OnFamilyMemberDiseaseCategoryChanged(object? sender, EventArgs e)
    {
        if (sender is Picker picker && picker.BindingContext is FamilyMember member)
        {
            ViewModel?.OnDiseaseCategoryChanged(member);
        }
    }

    /// <summary>疾病编码变化：命中 ICD-10 字典即时回填二级疾病名称并归类一级疾病。</summary>
    private void OnFamilyMemberDiseaseCodeChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not Entry entry || entry.BindingContext is not FamilyMember member) return;

        member.DiseaseCode = e.NewTextValue ?? string.Empty;
        member.ApplyDiseaseCode(e.NewTextValue, ViewModel?.DiseaseCategoryOptions);
    }
}
