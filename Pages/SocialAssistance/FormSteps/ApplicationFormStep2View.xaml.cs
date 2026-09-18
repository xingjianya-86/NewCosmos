using NewCosmos.Models.Entities;
using NewCosmos.ViewModels.SocialAssistance;

namespace NewCosmos.Pages.SocialAssistance.FormSteps;

/// <summary>
/// 申请表单 Step2：家庭成员（由宿主页面按 CurrentStep 懒加载）
/// </summary>
public partial class ApplicationFormStep2View : ContentView
{
    public ApplicationFormStep2View()
    {
        InitializeComponent();
    }

    private ApplicationFormViewModel? ViewModel => BindingContext as ApplicationFormViewModel;

    /// <summary>
    /// 家庭成员身份证号变化时自动更新性别、年龄和身体状况
    /// </summary>
    private void OnFamilyMemberIdCardChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is Entry entry && entry.BindingContext is FamilyMember member)
        {
            ViewModel?.OnIdCardChanged(member);
        }
    }

    /// <summary>
    /// 家庭成员残疾证号变化时自动解析残疾类型、等级并更新身体状况
    /// </summary>
    private void OnFamilyMemberDisabilityCardNoChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is Entry entry && entry.BindingContext is FamilyMember member)
        {
            ViewModel?.OnDisabilityCertificateChanged(member);
        }
    }

    /// <summary>
    /// 家庭成员疾病分类变化时更新身体状况
    /// </summary>
    private void OnFamilyMemberDiseaseCategoryChanged(object? sender, EventArgs e)
    {
        if (sender is Picker picker && picker.BindingContext is FamilyMember member)
        {
            ViewModel?.OnDiseaseCategoryChanged(member);
        }
    }

    /// <summary>
    /// 家庭成员疾病编码变化：命中 ICD-10 字典即时回填二级疾病名称并归类一级疾病
    /// （WinUI3 绑定默认失焦才提交源，故取 e.NewTextValue 即时联动）
    /// </summary>
    private void OnFamilyMemberDiseaseCodeChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not Entry entry || entry.BindingContext is not FamilyMember member) return;

        // 先写回实体属性（与绑定提交一致），再触发字典回填与一级归类（同步 Picker 选中项）
        member.DiseaseCode = e.NewTextValue ?? string.Empty;
        member.ApplyDiseaseCode(e.NewTextValue, ViewModel?.DiseaseCategoryOptions);
    }
}
