using NewCosmos.ViewModels.SocialAssistance;

namespace NewCosmos.Pages.SocialAssistance.FormSteps;

/// <summary>
/// 申请表单 Step1：基本信息（由宿主页面按 CurrentStep 懒加载）
/// </summary>
public partial class ApplicationFormStep1View : ContentView
{
    public ApplicationFormStep1View()
    {
        InitializeComponent();
    }

    private ApplicationFormViewModel? ViewModel => BindingContext as ApplicationFormViewModel;

    /// <summary>
    /// 申请人疾病编码变化：命中 ICD-10 字典即时回填二级疾病名称并归类一级疾病分类
    /// （WinUI3 绑定默认失焦才提交源，故取 e.NewTextValue 即时联动）
    /// </summary>
    private void OnApplicantDiseaseCodeChanged(object? sender, TextChangedEventArgs e)
    {
        ViewModel?.ApplyApplicantDiseaseCode(e.NewTextValue);
    }
}
