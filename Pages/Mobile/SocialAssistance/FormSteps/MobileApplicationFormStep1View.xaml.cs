using NewCosmos.ViewModels.SocialAssistance;

namespace NewCosmos.Pages.Mobile.SocialAssistance.FormSteps;

/// <summary>
/// 手机版申请表单 Step1：基本信息（由手机表单页按 CurrentStep 懒加载）。
/// 复用 <see cref="ApplicationFormViewModel"/>，仅替换为单列手机布局。
/// </summary>
public partial class MobileApplicationFormStep1View : ContentView
{
    public MobileApplicationFormStep1View()
    {
        InitializeComponent();
    }

    private ApplicationFormViewModel? ViewModel => BindingContext as ApplicationFormViewModel;

    /// <summary>申请人疾病编码变化：命中 ICD-10 字典即时回填二级疾病名称并归类一级疾病分类。</summary>
    private void OnApplicantDiseaseCodeChanged(object? sender, TextChangedEventArgs e)
    {
        ViewModel?.ApplyApplicantDiseaseCode(e.NewTextValue);
    }

    /// <summary>点击忽略 AI 建议条（隐藏；已自动勾选的保持不变，由经办人自行取消）</summary>
    private void OnAiSuggestionDismissed(object? sender, TappedEventArgs e)
        => ViewModel?.DismissAiSeveritySuggestion();
}
