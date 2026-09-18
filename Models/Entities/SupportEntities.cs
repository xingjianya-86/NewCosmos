using CommunityToolkit.Mvvm.ComponentModel;
using NewCosmos.Models.Results;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 赡养/抚养/扶养义务人适配器（UI 绑定用，底层数据存储在 nc_biz_family_members 表）
/// </summary>
public partial class Supporter : ObservableObject
{
    /// <summary>对应 nc_biz_family_members.id</summary>
    public long Id { get; set; }
    public long ApplicationId { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>身份证号码</summary>
    [ObservableProperty]
    private string _idCard = string.Empty;

    /// <summary>赡养/抚养/扶养</summary>
    public string PersonType { get; set; } = string.Empty;

    /// <summary>与户主关系（relationship_to_head）</summary>
    public string Relationship { get; set; } = string.Empty;

    /// <summary>月赡养费（元/月）</summary>
    [ObservableProperty]
    private decimal _monthlySupportFee;

    /// <summary>赡养月数（月）</summary>
    [ObservableProperty]
    private int _supportMonths = 12;

    /// <summary>赡养家庭人数（人）</summary>
    [ObservableProperty]
    private int _supporterFamilySize = 1;

    /// <summary>年赡养费（元）= 月赡养费 × 月数 × 家庭人数，随三要素联动</summary>
    public decimal AnnualSupportFee { get; set; }

    /// <summary>是否有赡养能力（切换触发 PropertyChanged，供 ViewModel 联动收入重算与月费清零）</summary>
    [ObservableProperty]
    private bool _isSupportAbility = true;

    // ── 个人信息（由 Step2 家庭成员导入承载并随赡养人保存，Step3 卡片不编辑）──

    /// <summary>性别</summary>
    public string Gender { get; set; } = string.Empty;

    /// <summary>年龄</summary>
    public int? Age { get; set; }

    /// <summary>民族（字典 Key）</summary>
    public string Ethnicity { get; set; } = string.Empty;

    /// <summary>联系方式</summary>
    public string Phone { get; set; } = string.Empty;

    /// <summary>户口性质</summary>
    public string HukouType { get; set; } = string.Empty;

    /// <summary>婚姻状况</summary>
    public string MaritalStatus { get; set; } = string.Empty;

    /// <summary>文化程度</summary>
    public string EducationLevel { get; set; } = string.Empty;

    /// <summary>政治面貌</summary>
    public string PoliticalStatus { get; set; } = string.Empty;

    /// <summary>健康状况</summary>
    public string HealthStatus { get; set; } = string.Empty;

    /// <summary>工作单位</summary>
    public string WorkUnit { get; set; } = string.Empty;

    /// <summary>就业状况（字典 Key）</summary>
    public string EmploymentStatus { get; set; } = string.Empty;

    /// <summary>主要收入来源（字典 Key）</summary>
    public string MainIncomeSource { get; set; } = string.Empty;

    /// <summary>月收入能力</summary>
    public string WorkCapacity { get; set; } = string.Empty;

    /// <summary>年收入</summary>
    public decimal AnnualIncome { get; set; }

    /// <summary>家庭住址-省份</summary>
    public string HomeProvince { get; set; } = string.Empty;

    /// <summary>家庭住址-城市</summary>
    public string HomeCity { get; set; } = string.Empty;

    /// <summary>家庭住址-区县</summary>
    public string HomeDistrict { get; set; } = string.Empty;

    /// <summary>家庭住址-乡镇</summary>
    public string HomeTown { get; set; } = string.Empty;

    /// <summary>家庭住址-村社区</summary>
    public string HomeVillage { get; set; } = string.Empty;

    /// <summary>家庭住址-详细地址</summary>
    public string HomeAddress { get; set; } = string.Empty;

    /// <summary>户籍地址-省份</summary>
    public string HukouProvince { get; set; } = string.Empty;

    /// <summary>户籍地址-城市</summary>
    public string HukouCity { get; set; } = string.Empty;

    /// <summary>户籍地址-区县</summary>
    public string HukouDistrict { get; set; } = string.Empty;

    /// <summary>户籍地址-乡镇</summary>
    public string HukouTown { get; set; } = string.Empty;

    partial void OnMonthlySupportFeeChanged(decimal value) => RecalculateAnnualFee();
    partial void OnSupportMonthsChanged(int value) => RecalculateAnnualFee();
    partial void OnSupporterFamilySizeChanged(int value) => RecalculateAnnualFee();

    private void RecalculateAnnualFee()
    {
        var months = SupportMonths > 0 ? SupportMonths : 1;
        var size = SupporterFamilySize > 0 ? SupporterFamilySize : 1;
        AnnualSupportFee = Math.Round(MonthlySupportFee * months * size, 2);
        OnPropertyChanged(nameof(AnnualSupportFee));
    }

    // Picker 绑定辅助属性
    [ObservableProperty]
    private DictItemOption? _selectedPersonType;
    partial void OnSelectedPersonTypeChanged(DictItemOption? value) => PersonType = value?.Key ?? string.Empty;

    [ObservableProperty]
    private DictItemOption? _selectedRelationship;
    partial void OnSelectedRelationshipChanged(DictItemOption? value) => Relationship = value?.Key ?? string.Empty;
}

/// <summary>
/// 照料人实体（纯 POCO，对应 nc_biz_caregivers 表）
/// 字段与共同生活成员对齐
/// </summary>
public partial class Caregiver : ObservableObject
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }
    public long CaredMemberId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string IdCard { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Ethnicity { get; set; } = string.Empty;
    public string MaritalStatus { get; set; } = string.Empty;
    public string HukouType { get; set; } = string.Empty;
    public string EducationLevel { get; set; } = string.Empty;
    public string PoliticalStatus { get; set; } = string.Empty;
    public string HealthStatus { get; set; } = string.Empty;
    public string EmploymentStatus { get; set; } = string.Empty;
    public string MainIncomeSource { get; set; } = string.Empty;
    public string WorkUnit { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime DeletedAt { get; set; }

    // 性别+年龄显示（可设置属性，与 FamilyMember 保持一致）
    public string GenderAgeDisplay { get; set; } = string.Empty;

    // Picker 选项属性（用于数据绑定）
    public DictItemOption? SelectedEthnicity { get; set; }
    public DictItemOption? SelectedMaritalStatus { get; set; }
    public DictItemOption? SelectedEducationLevel { get; set; }
    public DictItemOption? SelectedPoliticalStatus { get; set; }
    public DictItemOption? SelectedHealthStatus { get; set; }

    private DictItemOption? _selectedEmploymentStatus;
    public DictItemOption? SelectedEmploymentStatus
    {
        get => _selectedEmploymentStatus;
        set
        {
            _selectedEmploymentStatus = value;
            EmploymentStatus = value?.Key ?? string.Empty;
        }
    }

    private DictItemOption? _selectedIncomeSource;
    public DictItemOption? SelectedIncomeSource
    {
        get => _selectedIncomeSource;
        set
        {
            _selectedIncomeSource = value;
            MainIncomeSource = value?.Key ?? string.Empty;
        }
    }

    /// <summary>
    /// 触发 UI 刷新（用于外部修改属性后通知绑定）
    /// </summary>
    public void NotifyDisplayChanged()
    {
        OnPropertyChanged(nameof(Gender));
        OnPropertyChanged(nameof(Age));
        OnPropertyChanged(nameof(GenderAgeDisplay));
    }
}

/// <summary>
/// 监护人实体（纯 POCO，对应 nc_biz_guardians 表）
/// </summary>
public class Guardian
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string IdCard { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Ethnicity { get; set; } = string.Empty;
    public string MaritalStatus { get; set; } = string.Empty;
    public string EmploymentStatus { get; set; } = string.Empty;
    public string WorkUnit { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string MainIncomeSource { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime DeletedAt { get; set; }
}

/// <summary>
/// 自理能力评估实体（纯 POCO，对应 nc_biz_self_care_assessments 表）
/// </summary>
public class SelfCareAssessment
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }
    public long MemberId { get; set; }
    public bool CanEatIndependently { get; set; } = true;
    public bool CanDressIndependently { get; set; } = true;
    public bool CanGetInOutOfBed { get; set; } = true;
    public bool CanUseToiletIndependently { get; set; } = true;
    public bool CanWalkIndoors { get; set; } = true;
    public bool CanBatheIndependently { get; set; } = true;
    public DateTime AssessmentDate { get; set; }
    public string AssessorName { get; set; } = string.Empty;
    public string Remarks { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime DeletedAt { get; set; }
}

/// <summary>
/// 近亲属备案实体（纯 POCO，对应 nc_biz_kinship_filings 表）
/// </summary>
public class KinshipFiling
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }
    public string HandlerName { get; set; } = string.Empty;
    public string HandlerRelation { get; set; } = string.Empty;
    public string HandlerPhone { get; set; } = string.Empty;
    public string HandlerWorkUnit { get; set; } = string.Empty;
    public string HandlerPosition { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime DeletedAt { get; set; }
}
