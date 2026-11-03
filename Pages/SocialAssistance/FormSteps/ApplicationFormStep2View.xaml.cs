namespace NewCosmos.Pages.SocialAssistance.FormSteps;

/// <summary>
/// 申请表单 Step2：家庭成员（由宿主页面按 CurrentStep 懒加载）。
/// 2026-11 模板收敛：两段成员卡（共同生活/赡养抚养扶养）已抽为 components:FamilyMemberCardView 共享组件，
/// 身份证/残疾证/疾病编码的 TextChanged 联动随之迁入组件 code-behind（调 Host 即本页 VM），本文件不再有事件处理器。
/// </summary>
public partial class ApplicationFormStep2View : ContentView
{
    public ApplicationFormStep2View()
    {
        InitializeComponent();
    }
}
