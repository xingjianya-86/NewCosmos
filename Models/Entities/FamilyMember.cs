using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using NewCosmos.Constants;
using NewCosmos.Models;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Services.System;
using Microsoft.Extensions.DependencyInjection;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 家庭成员实体，对对应 nc_biz_family_members     /// </summary>
public class FamilyMember : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // 独立的地址选项集合
    public ObservableCollection<string> MemberCityOptions { get; } = new();
    public ObservableCollection<string> MemberDistrictOptions { get; } = new();
    public ObservableCollection<string> MemberTownOptions { get; } = new();
    public ObservableCollection<string> MemberVillageOptions { get; } = new();

    // ServiceProvider依赖
    private IServiceProvider? _serviceProvider;
    public void SetServiceProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// 家庭成员ID，主键
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 申请ID
    /// </summary>
    public long ApplicationId { get; set; }

    /// <summary>
    /// 姓名
    /// </summary>
    [Required(ErrorMessage = "姓名不能为空")]
    private string _name = string.Empty;
    public string Name
    {
        get => _name;
        set
        {
            if (_name != value)
            {
                _name = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(NameIsValid));
            }
        }
    }

    /// <summary>
    /// 身份证号
    /// </summary>
    [Required(ErrorMessage = "身份证号不能为空")]
    private string _idCard = string.Empty;
    public string IdCard
    {
        get => _idCard;
        set
        {
            if (_idCard != value)
            {
                _idCard = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IdCardIsValid));
                UpdateGenderAndAgeFromIdCard();
            }
        }
    }

    /// <summary>
    /// 是否申请    /// </summary>
    public bool IsApplicant { get; set; }

    /// <summary>
    /// 是否户主
    /// </summary>
    public bool IsHouseholdHead { get; set; }

    /// <summary>
    /// 是否有重    /// </summary>
    public bool HasSevereIllness { get; set; }

    /// <summary>
    /// 性别
    /// </summary>
    public string Gender { get; set; } = string.Empty;

    /// <summary>
    /// 出生日期
    /// </summary>
    public DateTime BirthDate { get; set; }

    /// <summary>
    /// 年龄
    /// </summary>
    public int? Age { get; set; }

    /// <summary>
    /// 民族
    /// </summary>
    public string Ethnicity { get; set; } = string.Empty;

    /// <summary>
    /// 民族（DictItemOption，用于Picker绑定）
    /// </summary>
    private DictItemOption? _selectedEthnicity;
    public DictItemOption? SelectedEthnicity
    {
        get => _selectedEthnicity;
        set
        {
            if (_selectedEthnicity != value)
            {
                _selectedEthnicity = value;
                Ethnicity = value?.Key ?? string.Empty;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 电话
    /// </summary>
    private string _phone = string.Empty;
    public string Phone
    {
        get => _phone;
        set
        {
            if (_phone != value)
            {
                _phone = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PhoneIsValid));
            }
        }
    }

    /// <summary>
    /// 户籍类型
    /// </summary>
    public string HukouType { get; set; } = string.Empty;

    /// <summary>
    /// 户籍类型（DictItemOption，用于Picker绑定）
    /// </summary>
    private DictItemOption? _selectedHukouType;
    public DictItemOption? SelectedHukouType
    {
        get => _selectedHukouType;
        set
        {
            if (_selectedHukouType != value)
            {
                _selectedHukouType = value;
                HukouType = value?.Key ?? string.Empty;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 户籍地址
    /// </summary>
    public string HukouAddress { get; set; } = string.Empty;

    /// <summary>
    /// 家庭住址-省份
    /// </summary>
    private string _homeProvince = string.Empty;
    public string HomeProvince
    {
        get => _homeProvince;
        set
        {
            if (_homeProvince != value)
            {
                _homeProvince = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 是否正在加载默认值（阻止级联触发）
    /// </summary>
    private bool _isLoadingDefaults;

    /// <summary>
    /// 家庭住址-城市
    /// </summary>
    private string _homeCity = string.Empty;
    public string HomeCity
    {
        get => _homeCity;
        set
        {
            if (_homeCity != value)
            {
                _homeCity = value;
                OnPropertyChanged();
                if (!_isLoadingDefaults)
                    _ = LoadDistrictsForCityAsync(value);
            }
        }
    }

    /// <summary>
    /// 家庭住址-区县
    /// </summary>
    private string _homeDistrict = string.Empty;
    public string HomeDistrict
    {
        get => _homeDistrict;
        set
        {
            if (_homeDistrict != value)
            {
                _homeDistrict = value;
                OnPropertyChanged();
                if (!_isLoadingDefaults)
                    _ = LoadTownsForDistrictAsync(value);
            }
        }
    }

    /// <summary>
    /// 家庭住址-乡镇
    /// </summary>
    private string _homeTown = string.Empty;
    public string HomeTown
    {
        get => _homeTown;
        set
        {
            if (_homeTown != value)
            {
                _homeTown = value;
                OnPropertyChanged();
                if (!_isLoadingDefaults)
                    _ = LoadVillagesForTownAsync(value);
            }
        }
    }

    /// <summary>
    /// 家庭住址-村社区
    /// </summary>
    private string _homeVillage = string.Empty;
    public string HomeVillage
    {
        get => _homeVillage;
        set
        {
            if (_homeVillage != value)
            {
                _homeVillage = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 家庭住址-详细地址
    /// </summary>
    public string HomeAddress { get; set; } = string.Empty;

    /// <summary>
    /// 户籍地址-省份
    /// </summary>
    private string _hukouProvince = string.Empty;
    public string HukouProvince
    {
        get => _hukouProvince;
        set
        {
            if (_hukouProvince != value)
            {
                _hukouProvince = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 户籍地址-    /// </summary>
    public string HukouCity { get; set; } = string.Empty;

    /// <summary>
    /// 户籍地址-区县
    /// </summary>
    public string HukouDistrict { get; set; } = string.Empty;

    /// <summary>
    /// 户籍地址-乡镇
    /// </summary>
    public string HukouTown { get; set; } = string.Empty;

    /// <summary>
    /// 婚姻状况
    /// </summary>
    public string MaritalStatus { get; set; } = string.Empty;

    /// <summary>
    /// 婚姻状况（DictItemOption，用于Picker绑定）
    /// </summary>
    private DictItemOption? _selectedMaritalStatus;
    public DictItemOption? SelectedMaritalStatus
    {
        get => _selectedMaritalStatus;
        set
        {
            if (_selectedMaritalStatus != value)
            {
                _selectedMaritalStatus = value;
                MaritalStatus = value?.Key ?? string.Empty;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 文化程度
    /// </summary>
    public string EducationLevel { get; set; } = string.Empty;

    /// <summary>
    /// 文化程度（DictItemOption，用于Picker绑定）
    /// </summary>
    private DictItemOption? _selectedEducationLevel;
    public DictItemOption? SelectedEducationLevel
    {
        get => _selectedEducationLevel;
        set
        {
            if (_selectedEducationLevel != value)
            {
                _selectedEducationLevel = value;
                EducationLevel = value?.Key ?? string.Empty;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 政治面貌
    /// </summary>
    public string PoliticalStatus { get; set; } = string.Empty;

    /// <summary>
    /// 政治面貌（DictItemOption，用于Picker绑定）
    /// </summary>
    private DictItemOption? _selectedPoliticalStatus;
    public DictItemOption? SelectedPoliticalStatus
    {
        get => _selectedPoliticalStatus;
        set
        {
            if (_selectedPoliticalStatus != value)
            {
                _selectedPoliticalStatus = value;
                PoliticalStatus = value?.Key ?? string.Empty;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 与户主关系
    /// </summary>
    public string RelationshipToHead { get; set; } = string.Empty;

    /// <summary>
    /// 与户主关系（DictItemOption，用于Picker绑定）
    /// </summary>
    private DictItemOption? _selectedRelationshipToHead;
    public DictItemOption? SelectedRelationshipToHead
    {
        get => _selectedRelationshipToHead;
        set
        {
            if (_selectedRelationshipToHead != value)
            {
                _selectedRelationshipToHead = value;
                RelationshipToHead = value?.Key ?? string.Empty;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 健康状况
    /// </summary>
    public string HealthStatus { get; set; } = string.Empty;

    /// <summary>
    /// 健康状况显示文本（中文）
    /// </summary>
    public string HealthStatusDisplay => HealthStatus switch
    {
        "Healthy" => "健康或良好",
        "Weak" => "一般或较弱",
        "SevereIllness" => "重病",
        "SevereDisability" => "重残",
        "SevereIllnessAndDisability" => "重病且重残",
        _ => HealthStatus
    };

    /// <summary>
    /// 劳动能力
    /// </summary>
    public string WorkCapacity { get; set; } = string.Empty;

    /// <summary>
    /// 免于劳动力判定（因照顾本户重病/重残亲属，经办人人工判定复选框）
    /// </summary>
    private bool _isLaborExempt;
    public bool IsLaborExempt
    {
        get => _isLaborExempt;
        set
        {
            if (_isLaborExempt != value)
            {
                _isLaborExempt = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 疾病名称（二级疾病，可由疾病编码自动回填或手工录入）
    /// </summary>
    private string _diseaseName = string.Empty;
    public string DiseaseName
    {
        get => _diseaseName;
        set
        {
            if (_diseaseName != value)
            {
                _diseaseName = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 疾病编码（ICD-10）；命中内置字典自动回填疾病名称与一级疾病分类（见 ApplyDiseaseCode）
    /// </summary>
    private string _diseaseCode = string.Empty;
    public string DiseaseCode
    {
        get => _diseaseCode;
        set
        {
            if (_diseaseCode != value)
            {
                _diseaseCode = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 二级疾病（DictItemOption，用于Picker绑定）
    /// </summary>
    private DictItemOption? _selectedDiseaseNameObj;
    public DictItemOption? SelectedDiseaseNameObj
    {
        get => _selectedDiseaseNameObj;
        set
        {
            if (_selectedDiseaseNameObj != value)
            {
                _selectedDiseaseNameObj = value;
                DiseaseName = value?.Key ?? string.Empty;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 疾病分类
    /// </summary>
    public string DiseaseCategory { get; set; } = string.Empty;

    /// <summary>
    /// 疾病分类（DictItemOption，用于Picker绑定）
    /// </summary>
    private DictItemOption? _selectedDiseaseCategory;
    public DictItemOption? SelectedDiseaseCategory
    {
        get => _selectedDiseaseCategory;
        set
        {
            if (_selectedDiseaseCategory != value)
            {
                _selectedDiseaseCategory = value;
                DiseaseCategory = value?.Key ?? string.Empty;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 次要疾病名称
    /// </summary>
    public string SecondaryDisease { get; set; } = string.Empty;

    /// <summary>
    /// 所属分类（共同生活成员/赡养抚养扶养    /// </summary>
    public string MemberCategory { get; set; } = string.Empty;

    /// <summary>
    /// 是否残疾
    /// </summary>
    public bool IsDisabled { get; set; }

    /// <summary>
    /// 残疾证号
    /// </summary>
    private string _disabilityCertificateNo = string.Empty;
    public string DisabilityCertificateNo
    {
        get => _disabilityCertificateNo;
        set
        {
            if (_disabilityCertificateNo != value)
            {
                _disabilityCertificateNo = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 残疾类型
    /// </summary>
    public string DisabilityType { get; set; } = string.Empty;

    /// <summary>
    /// 残疾等级
    /// </summary>
    public string DisabilityLevel { get; set; } = string.Empty;

    /// <summary>
    /// 残疾等级 Key（用于对比判断）
    /// </summary>
    public string DisabilityLevelKey { get; set; } = string.Empty;

    /// <summary>残疾类型 key → 字典显示值（XAML 绑定用；不参与判定）</summary>
    public string DisabilityTypeDisplay => DictDisplayHelper.GetDisabilityTypeDisplay(DisabilityType);

    /// <summary>残疾等级 key → 字典显示值（XAML 绑定用；不参与判定）</summary>
    public string DisabilityLevelDisplay => DictDisplayHelper.GetDisabilityLevelDisplay(DisabilityLevel);

    /// <summary>用于判定的残疾等级 key：会话内优先 DisabilityLevelKey，库内 DisabilityLevel 即 key</summary>
    public string DisabilityLevelKeyResolved =>
        !string.IsNullOrEmpty(DisabilityLevelKey) ? DisabilityLevelKey : DisabilityLevel;

    /// <summary>
    /// 是否重度残疾
    /// </summary>
    public bool IsSevereDisability { get; set; }

    /// <summary>
    /// 是否有重病（经办人人工判定复选框；分类认定按此字段与健康状态双通道判定）
    /// </summary>
    private bool _isSevereDisease;
    public bool IsSevereDisease
    {
        get => _isSevereDisease;
        set
        {
            if (_isSevereDisease != value)
            {
                _isSevereDisease = value;
                OnPropertyChanged();
                ApplySevereDiseaseToHealthStatus(value);
            }
        }
    }

    /// <summary>
    /// 勾选"是否重病"同步健康状态为重病；取消勾选仅在当前为重病时回退为健康
    /// （不破坏重残/重病且重残等其他状态）
    /// </summary>
    private void ApplySevereDiseaseToHealthStatus(bool isChecked)
    {
        if (isChecked)
            HealthStatus = HealthStatusConstants.SEVERE_DISEASE;
        else if (HealthStatus == HealthStatusConstants.SEVERE_DISEASE)
            HealthStatus = HealthStatusConstants.HEALTHY;

        OnPropertyChanged(nameof(HealthStatus));
        OnPropertyChanged(nameof(HealthStatusDisplay));
    }

    /// <summary>
    /// 疾病编码命中 ICD-10 字典时自动回填二级疾病名称并归类一级疾病分类；
    /// 未命中不改动（允许手工录入）。categoryOptions 传入时同步 Picker 选中项。
    /// 由页面 code-behind 的 TextChanged 即时调用（WinUI3 绑定默认失焦才提交源）。
    /// </summary>
    public void ApplyDiseaseCode(string? code, IEnumerable<DictItemOption>? categoryOptions = null)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        if (!Icd10Catalog.TryGetName(code, out var name)) return;

        DiseaseName = name;

        var category = Icd10Catalog.MapCategory(code);
        if (string.IsNullOrEmpty(category)) return;

        DiseaseCategory = category;
        OnPropertyChanged(nameof(DiseaseCategory));

        if (categoryOptions != null)
        {
            var match = categoryOptions.FirstOrDefault(o => o.Key == category);
            if (match != null) SelectedDiseaseCategory = match;
        }
    }

    /// <summary>
    /// 疾病信息是否需要用户确认（导入数据格式不一致时为true）
    /// </summary>
    public bool IsDiseaseUnconfirmed { get; set; }

    /// <summary>
    /// 就业状态
    /// </summary>
    public string EmploymentStatus { get; set; } = string.Empty;

    /// <summary>
    /// 就业状态（DictItemOption，用于Picker绑定）
    /// </summary>
    private DictItemOption? _selectedEmploymentStatus;
    public DictItemOption? SelectedEmploymentStatus
    {
        get => _selectedEmploymentStatus;
        set
        {
            if (_selectedEmploymentStatus != value)
            {
                _selectedEmploymentStatus = value;
                EmploymentStatus = value?.Key ?? string.Empty;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 工作单位
    /// </summary>
    public string WorkUnit { get; set; } = string.Empty;

    /// <summary>
    /// 主要收入来源
    /// </summary>
    public string MainIncomeSource { get; set; } = string.Empty;

    /// <summary>
    /// 主要收入来源（DictItemOption，用于Picker绑定）
    /// </summary>
    private DictItemOption? _selectedIncomeSource;
    public DictItemOption? SelectedIncomeSource
    {
        get => _selectedIncomeSource;
        set
        {
            if (_selectedIncomeSource != value)
            {
                _selectedIncomeSource = value;
                MainIncomeSource = value?.Key ?? string.Empty;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 年收    /// </summary>
    public decimal AnnualIncome { get; set; }

    // ── 赡养人特有字段（member_category = "赡养抚养扶养" 时使用）──

    /// <summary>
    /// 人员类型：赡养/抚养/扶养
    /// </summary>
    public string PersonType { get; set; } = string.Empty;

    /// <summary>
    /// 年赡养费（元）
    /// </summary>
    public decimal AnnualSupportFee { get; set; }

    /// <summary>
    /// 月赡养费（元）
    /// </summary>
    public decimal MonthlySupportFee { get; set; }

    /// <summary>
    /// 是否有赡养能力
    /// </summary>
    public bool IsSupportAbility { get; set; } = true;

    /// <summary>
    /// 月收入能力（元）
    /// </summary>
    public decimal MonthlyIncomeCapacity { get; set; }

    /// <summary>
    /// 赡养月数（赡养人字段，对应 support_months 列）
    /// </summary>
    public int? SupportMonths { get; set; }

    /// <summary>
    /// 家庭人口
    /// </summary>
    public int? FamilySize { get; set; }

    /// <summary>
    /// 赡养人家庭人数
    /// </summary>
    public int? SupporterFamilySize { get; set; }

    /// <summary>
    /// 是否单独救助
    /// </summary>
    public bool IsSingleRescue { get; set; }

    /// <summary>
    /// 自理能力
    /// </summary>
    public string SelfCareAbility { get; set; } = string.Empty;

    /// <summary>
    /// 性别/年龄显示文本
    /// </summary>
    public string GenderAgeDisplay { get; set; } = string.Empty;

    /// <summary>
    /// 户主姓名（用于大学生管理可关联成员显示）
    /// </summary>
    public string HouseholdHeadName { get; set; } = string.Empty;

    /// <summary>
    /// 所在地显示文本（乡镇+村），优先使用 SQL 映射值
    /// </summary>
    private string _homeLocationDisplay = string.Empty;
    public string HomeLocationDisplay
    {
        get => string.IsNullOrWhiteSpace(_homeLocationDisplay)
            ? string.Join(" ", new[] { HomeTown, HomeVillage }.Where(s => !string.IsNullOrWhiteSpace(s)))
            : _homeLocationDisplay;
        set
        {
            if (_homeLocationDisplay != value)
            {
                _homeLocationDisplay = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 姓名是否有效
    /// </summary>
    public bool NameIsValid => !string.IsNullOrWhiteSpace(Name) && Name.Length >= 2;

    /// <summary>
    /// 身份证号是否有效
    /// </summary>
    public bool IdCardIsValid => !string.IsNullOrWhiteSpace(IdCard) && IdCard.Length == 18;

    /// <summary>
    /// 电话是否有效
    /// </summary>
    public bool PhoneIsValid => !string.IsNullOrWhiteSpace(Phone) && Phone.Length == 11;

    /// <summary>
    /// 从身份证号更新性别和年    /// </summary>
    public void UpdateGenderAndAgeFromIdCard()
    {
        if (!string.IsNullOrEmpty(IdCard) && IdCard.Length >= 17)
        {
            var genderDigit = IdCard[16] - '0';
            Gender = genderDigit % 2 == 1 ? "男" : "女";

            var age = Helpers.IdCardValidator.ExtractAgeBasic(IdCard);
            Age = age;
            GenderAgeDisplay = age.HasValue ? $"{Gender} {age.Value}岁" : Gender;
        }
        else
        {
            Gender = string.Empty;
            Age = null;
            GenderAgeDisplay = string.Empty;
        }

        OnPropertyChanged(nameof(Gender));
        OnPropertyChanged(nameof(Age));
        OnPropertyChanged(nameof(GenderAgeDisplay));
        OnPropertyChanged(nameof(BirthDate));
    }

    /// <summary>
    /// 从残疾证号解析残疾类型和等级（存字典 key，与主表/导入路径一致）
    /// </summary>
    public void ParseDisabilityCertificate(
        Dictionary<char, string> typeMap,
        Dictionary<char, string> levelMap)
    {
        if (string.IsNullOrWhiteSpace(DisabilityCertificateNo) || DisabilityCertificateNo.Length < 2)
        {
            DisabilityType = string.Empty;
            DisabilityLevel = string.Empty;
            DisabilityLevelKey = string.Empty;
            OnPropertyChanged(nameof(DisabilityType));
            OnPropertyChanged(nameof(DisabilityLevel));
            OnPropertyChanged(nameof(DisabilityLevelKey));
            OnPropertyChanged(nameof(DisabilityTypeDisplay));
            OnPropertyChanged(nameof(DisabilityLevelDisplay));
            return;
        }

        var typeKey = typeMap.TryGetValue(DisabilityCertificateNo[^2], out var tk) ? tk : string.Empty;
        var levelKey = levelMap.TryGetValue(DisabilityCertificateNo[^1], out var lk) ? lk : string.Empty;

        DisabilityType = typeKey;
        DisabilityLevel = levelKey;
        DisabilityLevelKey = levelKey;

        OnPropertyChanged(nameof(DisabilityType));
        OnPropertyChanged(nameof(DisabilityLevel));
        OnPropertyChanged(nameof(DisabilityLevelKey));
        OnPropertyChanged(nameof(DisabilityTypeDisplay));
        OnPropertyChanged(nameof(DisabilityLevelDisplay));
    }

    /// <summary>
    /// 清空所有残疾相关字段，并触发属性变更通知（UI 同步更新）
    /// </summary>
    public void ClearDisabilityInfo()
    {
        DisabilityType = string.Empty;
        DisabilityLevel = string.Empty;
        DisabilityLevelKey = string.Empty;
        IsDisabled = false;
        IsSevereDisability = false;
        OnPropertyChanged(nameof(DisabilityType));
        OnPropertyChanged(nameof(DisabilityLevel));
        OnPropertyChanged(nameof(DisabilityLevelKey));
        OnPropertyChanged(nameof(DisabilityTypeDisplay));
        OnPropertyChanged(nameof(DisabilityLevelDisplay));
        OnPropertyChanged(nameof(IsDisabled));
        OnPropertyChanged(nameof(IsSevereDisability));
    }

    /// <summary>
    /// 更新身体状况（与 Step 1 逻辑一致，使用 Key 值判断）
    /// <summary>
    /// 根据疾病和残疾情况更新健康状态
    /// </summary>
    public void UpdateHealthStatus(string noDiseaseKey, List<string> severeLevelKeys,
        string? diseaseCategoryKey)
    {
        bool hasDisease = !string.IsNullOrEmpty(diseaseCategoryKey) && diseaseCategoryKey != noDiseaseKey;
        bool hasDisability = !string.IsNullOrEmpty(DisabilityType);
        bool hasSevereDisability = hasDisability
            && !string.IsNullOrEmpty(DisabilityLevelKeyResolved)
            && severeLevelKeys.Contains(DisabilityLevelKeyResolved);
        bool hasNonSevereDisability = hasDisability && !hasSevereDisability;

        // 残疾标记按等级派生并落库（is_disabled / is_severe_disability），供分类判定与档案统计使用
        IsDisabled = hasDisability;
        IsSevereDisability = hasSevereDisability;

        if (hasDisease && hasSevereDisability)
            HealthStatus = HealthStatusConstants.SEVERE_DISEASE_AND_DISABILITY;
        else if (hasDisease)
            HealthStatus = HealthStatusConstants.SEVERE_DISEASE;
        else if (hasSevereDisability)
            HealthStatus = HealthStatusConstants.SEVERE_DISABILITY;
        else if (hasNonSevereDisability)
            HealthStatus = HealthStatusConstants.FAIR_OR_WEAK;
        else if (Age.HasValue && Age.Value >= 60)
            HealthStatus = HealthStatusConstants.FAIR_OR_WEAK;
        else
            HealthStatus = HealthStatusConstants.HEALTHY;

        OnPropertyChanged(nameof(HealthStatus));
        OnPropertyChanged(nameof(HealthStatusDisplay));
    }

    /// <summary>
    /// 二级疾病选项（每个成员独立维护）
    /// </summary>
    public ObservableCollection<DictItemOption> SecondaryDiseaseOptions { get; } = new();

    /// <summary>
    /// 从 DiseaseNames 字典加载二级疾病选项
    /// </summary>
    public void LoadSecondaryDiseases(Dictionary<string, List<DiseaseItem>> diseaseCategoryMap)
    {
        SecondaryDiseaseOptions.Clear();
        if (string.IsNullOrEmpty(DiseaseCategory))
            return;

        if (diseaseCategoryMap.TryGetValue(DiseaseCategory, out var diseases))
        {
            foreach (var disease in diseases.OrderBy(x => x.SortOrder))
            {
                SecondaryDiseaseOptions.Add(new DictItemOption { Key = disease.Key, Display = disease.Key });
            }
        }
    }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 是否选中（用于成员选择弹窗，不映射到数据库）
    /// </summary>
    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>
    /// 删除时间
    /// </summary>
    public DateTime DeletedAt { get; set; }

    #region 地址级联加载方法

    /// <summary>
    /// 初始化地址选项集合（加载指定地区的选项）
    /// </summary>
    public async Task LoadInitialAddressOptionsAsync(string? city, string? district, string? town)
    {
        if (_serviceProvider == null) return;

        _isLoadingDefaults = true;
        try
        {
            var regionService = _serviceProvider.GetRequiredService<IRegionService>();

            // 加载城市选项
            var citiesResult = await regionService.GetCitiesAsync();
            MemberCityOptions.Clear();
            if (citiesResult.IsSuccess)
            {
                foreach (var c in citiesResult.Value)
                    MemberCityOptions.Add(c.CityName);
            }

            // 加载区县选项
            if (!string.IsNullOrEmpty(city))
            {
                await LoadDistrictsForCityAsync(city);
            }

            // 加载乡镇选项
            if (!string.IsNullOrEmpty(district))
            {
                await LoadTownsForDistrictAsync(district);
            }

            // 加载村社区选项
            if (!string.IsNullOrEmpty(town))
            {
                await LoadVillagesForTownAsync(town);
            }

            // 设置家庭住址省份默认值（如果未设置）
            if (string.IsNullOrEmpty(HomeProvince))
            {
                HomeProvince = "黑龙江省";
            }

            // 设置户籍地址默认值（如果未设置）
            if (string.IsNullOrEmpty(HukouProvince))
            {
                HukouProvince = "黑龙江省";
            }
            if (string.IsNullOrEmpty(HukouCity) && !string.IsNullOrEmpty(city))
            {
                HukouCity = city;
            }
            if (string.IsNullOrEmpty(HukouDistrict) && !string.IsNullOrEmpty(district))
            {
                HukouDistrict = district;
            }
            if (string.IsNullOrEmpty(HukouTown) && !string.IsNullOrEmpty(town))
            {
                HukouTown = town;
            }
        }
        finally
        {
            _isLoadingDefaults = false;
        }
    }

    /// <summary>
    /// 根据城市加载区县选项
    /// </summary>
    private async Task LoadDistrictsForCityAsync(string city)
    {
        if (_serviceProvider == null || string.IsNullOrEmpty(city)) return;

        var regionService = _serviceProvider.GetRequiredService<IRegionService>();
        var countiesResult = await regionService.GetCountiesByCityAsync(city);

        MemberDistrictOptions.Clear();
        if (countiesResult.IsSuccess)
        {
            foreach (var county in countiesResult.Value)
                MemberDistrictOptions.Add(county.CountyName);
        }
    }

    /// <summary>
    /// 根据区县加载乡镇选项
    /// </summary>
    private async Task LoadTownsForDistrictAsync(string district)
    {
        if (_serviceProvider == null || string.IsNullOrEmpty(district)) return;

        var regionService = _serviceProvider.GetRequiredService<IRegionService>();
        
        // 先获取所有县区，找到匹配的县区ID
        var countiesResult = await regionService.GetCountiesAsync();
        MemberTownOptions.Clear();
        
        if (countiesResult.IsSuccess)
        {
            var county = countiesResult.Value?.FirstOrDefault(c => c.CountyName == district);
            if (county != null)
            {
                var townsResult = await regionService.GetTownsByCountyIdAsync(county.Id);
                if (townsResult.IsSuccess)
                {
                    foreach (var town in townsResult.Value)
                        MemberTownOptions.Add(town.TownName);
                }
            }
        }
    }

    /// <summary>
    /// 根据乡镇加载村社区选项
    /// </summary>
    private async Task LoadVillagesForTownAsync(string town)
    {
        if (_serviceProvider == null || string.IsNullOrEmpty(town)) return;

        var regionService = _serviceProvider.GetRequiredService<IRegionService>();
        
        // 先搜索乡镇，找到匹配的乡镇ID
        var townsResult = await regionService.SearchTownsAsync(town);
        MemberVillageOptions.Clear();
        
        if (townsResult.IsSuccess)
        {
            var townEntity = townsResult.Value?.FirstOrDefault(t => t.TownName == town);
            if (townEntity != null)
            {
                var villagesResult = await regionService.GetVillagesByTownIdAsync(townEntity.Id);
                if (villagesResult.IsSuccess)
                {
                    foreach (var village in villagesResult.Value)
                        MemberVillageOptions.Add(village.VillageName);
                }
            }
        }
    }

    #endregion
}
