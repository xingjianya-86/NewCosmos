using NewCosmos.Models.Entities;
using NewCosmos.ViewModels.SocialAssistance;

namespace NewCosmos.Components;

/// <summary>
/// 家庭成员卡共享视图（2026-11 表单模板收敛）：原 Step2 两段 ~190 行重复模板收敛为组件。
/// VM 经 <see cref="Host"/> 传入（选项列表 + 三个事件联动方法）；成员实体继承自外层 DataTemplate 的 BindingContext。
/// </summary>
public partial class FamilyMemberCardView : ContentView
{
    public static readonly BindableProperty HostProperty =
        BindableProperty.Create(nameof(Host), typeof(ApplicationFormViewModel), typeof(FamilyMemberCardView));

    /// <summary>户籍区模式：false=共享只读（共同生活，读 SharedHukou*）；true=独立可编辑（赡养，成员自身户籍级联）</summary>
    public static readonly BindableProperty HukouEditableProperty =
        BindableProperty.Create(nameof(HukouEditable), typeof(bool), typeof(FamilyMemberCardView), false);

    public static readonly BindableProperty SharedHukouCityProperty =
        BindableProperty.Create(nameof(SharedHukouCity), typeof(string), typeof(FamilyMemberCardView), string.Empty);

    public static readonly BindableProperty SharedHukouDistrictProperty =
        BindableProperty.Create(nameof(SharedHukouDistrict), typeof(string), typeof(FamilyMemberCardView), string.Empty);

    public static readonly BindableProperty SharedHukouTownProperty =
        BindableProperty.Create(nameof(SharedHukouTown), typeof(string), typeof(FamilyMemberCardView), string.Empty);

    /// <summary>显示「免于劳动力判定」勾选（仅共同生活成员）</summary>
    public static readonly BindableProperty ShowLaborExemptProperty =
        BindableProperty.Create(nameof(ShowLaborExempt), typeof(bool), typeof(FamilyMemberCardView), false);

    /// <summary>显示「家庭人口」输入（仅赡养抚养扶养人员）</summary>
    public static readonly BindableProperty ShowFamilySizeProperty =
        BindableProperty.Create(nameof(ShowFamilySize), typeof(bool), typeof(FamilyMemberCardView), false);

    public static readonly BindableProperty RemoveCommandProperty =
        BindableProperty.Create(nameof(RemoveCommand), typeof(System.Windows.Input.ICommand), typeof(FamilyMemberCardView));

    public ApplicationFormViewModel? Host
    {
        get => (ApplicationFormViewModel?)GetValue(HostProperty);
        set => SetValue(HostProperty, value);
    }

    public bool HukouEditable
    {
        get => (bool)GetValue(HukouEditableProperty);
        set => SetValue(HukouEditableProperty, value);
    }

    public string? SharedHukouCity
    {
        get => (string?)GetValue(SharedHukouCityProperty);
        set => SetValue(SharedHukouCityProperty, value);
    }

    public string? SharedHukouDistrict
    {
        get => (string?)GetValue(SharedHukouDistrictProperty);
        set => SetValue(SharedHukouDistrictProperty, value);
    }

    public string? SharedHukouTown
    {
        get => (string?)GetValue(SharedHukouTownProperty);
        set => SetValue(SharedHukouTownProperty, value);
    }

    public bool ShowLaborExempt
    {
        get => (bool)GetValue(ShowLaborExemptProperty);
        set => SetValue(ShowLaborExemptProperty, value);
    }

    public bool ShowFamilySize
    {
        get => (bool)GetValue(ShowFamilySizeProperty);
        set => SetValue(ShowFamilySizeProperty, value);
    }

    public System.Windows.Input.ICommand? RemoveCommand
    {
        get => (System.Windows.Input.ICommand?)GetValue(RemoveCommandProperty);
        set => SetValue(RemoveCommandProperty, value);
    }

    public FamilyMemberCardView()
    {
        InitializeComponent();
    }

    /// <summary>成员身份证号变化：自动更新性别/年龄/身体状况（与原 Step2 code-behind 同逻辑）</summary>
    private void OnFamilyMemberIdCardChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is Entry entry && entry.BindingContext is FamilyMember member)
            Host?.OnIdCardChanged(member);
    }

    /// <summary>成员残疾证号变化：自动解析残疾类型/等级并更新身体状况</summary>
    private void OnFamilyMemberDisabilityCardNoChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is Entry entry && entry.BindingContext is FamilyMember member)
            Host?.OnDisabilityCertificateChanged(member);
    }

    /// <summary>
    /// 成员疾病编码变化：命中 ICD-10 字典即时回填二级疾病名称并归类一级疾病
    /// （WinUI3 绑定默认失焦才提交源，故取 e.NewTextValue 即时联动；与原 Step2 code-behind 同逻辑）
    /// </summary>
    private void OnFamilyMemberDiseaseCodeChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not Entry entry || entry.BindingContext is not FamilyMember member) return;

        // 先写回实体属性（与绑定提交一致），再触发字典回填与一级归类（同步 Picker 选中项）
        member.DiseaseCode = e.NewTextValue ?? string.Empty;
        member.ApplyDiseaseCode(e.NewTextValue, Host?.DiseaseCategoryOptions);
    }
}
