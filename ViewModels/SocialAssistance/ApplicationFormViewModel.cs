using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Enums;
using NewCosmos.Models.Exceptions;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.AssetVerification;
using NewCosmos.Services.Domain.ChangeManagement;
using NewCosmos.Services.Domain.SocialAssistance;

using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.System;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.ChangeManagement;
using System.Collections.ObjectModel;
using System.ComponentModel;

using Application = NewCosmos.Models.Entities.Application;

namespace NewCosmos.ViewModels.SocialAssistance;

public partial class ApplicationFormViewModel : FormViewModelBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IApplicationService _applicationService;
    private readonly IPersonSearchService _personSearchService;
    private readonly IFamilyMemberService _familyMemberService;
    private readonly IClassificationService _classificationService;
    private readonly IIncomeCalculationService _incomeCalculationService;
    private readonly IGuaranteeAmountService _guaranteeAmountService;
    private readonly IGracePeriodService _gracePeriodService;
    private readonly IRegionService _regionService;
    private readonly IDictionaryService _dictionaryService;
    private readonly IUserService _userService;
    private readonly IOrganizationService _organizationService;
    private readonly ILandContractService _landContractService;
    private readonly ISubsidyDataService _subsidyDataService;
    private readonly IAssetVerificationService _assetVerificationService;
    private readonly IStandardConfigService _standardConfigService;
    private readonly IEconomicDetailService _economicDetailService;
    private readonly IImportedArchiveService _importedArchiveService;
    private readonly ISupporterService _supporterService;
    private readonly ICaregiverService _caregiverService;
    private readonly IHouseholdSurveyService _householdSurveyService;
    private readonly IChangeService _changeService;
    private readonly ILoggerService _logger;

    /// <summary>文书装配串行闸（静态跨实例）：ArchiveProductionViewModel 实例状态不可并发（原 DocumentSheetService 语义）。</summary>
    private static readonly SemaphoreSlim _documentBuildGate = new(1, 1);

    private long _applicationId;
    private bool _isLoadingDefaults;


    /// <summary>
    /// 乐观并发令牌：加载/保存申请时数据库中的 updated_at。
    /// 保存时随实体传给 UpdateAsync（WHERE updated_at 守卫），null 表示尚未加载（跳过检查）。
    /// </summary>
    private DateTime? _loadedUpdatedAt;

    /// <summary>
    /// 加载时数据库中的 current_step。
    /// 变更流程模式（复核/家庭修正/编辑家庭信息/成员变更）的 UI 步骤被强制归位（3/1/2），
    /// 不代表档案真实进度，保存时必须回写加载值，否则会把已归档的 step=6 冲成 UI 步数，
    /// 档案从「已完结档案」掉进「已建档未提交」。0 = 尚未加载（新建）。
    /// </summary>
    private int _loadedCurrentStep;

    /// <summary>
    /// 加载时数据库中的分类结果（覆写前真旧值）。
    /// Step5「分类判定」会先落库覆写 classification_result/total_guarantee_amount，
    /// 变更流程（经济复核/成员变更）的"新旧对比"必须用此处捕获的值，否则恒判"无变化"。
    /// </summary>
    private string? _loadedClassification;

    /// <summary>
    /// 加载时数据库中的保障金合计（户月 + 分类施保 + 照料费，覆写前真旧值）。同上。
    /// </summary>
    private decimal? _loadedTotalGuaranteeAmount;

    /// <summary>
    /// 向导入口（LoadApplicationAsync）固化的复核前主表快照（真旧值）。
    /// Step5 分类判定与 SaveEconomicDetailsAsync（步骤保存/复核保存）随后都会覆写本档收入列，
    /// 保存时再读库拿到的"旧值"已是覆写后值（Before 快照 / old_per_capita_income 失真）——
    /// 经济复核/成员变更 context.Old* 必须优先用本快照；null（未走加载路径）时消费端回退读库。
    /// </summary>
    private Application? _entryApplicationSnapshot;

    /// <summary>成员变更模式：加载时的家庭人数（变更前口径，Before 快照用）</summary>
    private int _loadedFamilySize;

    /// <summary>成员变更模式：加载时的成员快照（实体引用，供增/减员判定与明细取数）</summary>
    private List<FamilyMember> _loadedMemberEntities = new();

    /// <summary>成员变更模式：加载时的成员名单（身份证→姓名，变更类型判定与变更摘要用）</summary>
    private Dictionary<string, string> _loadedMembersByIdCard = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>成员变更模式：增员登记原因（按新增成员对象引用暂存；弹窗确认后写入）</summary>
    private readonly Dictionary<FamilyMember, MemberChangeReasonResult> _addedMemberReasons =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>成员变更模式：减员登记（成员+原因；确认后移除列表前暂存）</summary>
    private readonly List<(FamilyMember Member, MemberChangeReasonResult Reason)> _removedMemberEntries = new();

    // 表单未编辑但需原样保留的主表字段（BuildApplication 不赋值会用默认值覆盖）
    private string _loadedSupportMode = string.Empty;
    private long _loadedSupportInstitutionId;
    private int _loadedConfirmedFamilySize;
    private decimal _loadedPersonCategoryProtectionTotalAmount;
    private bool _loadedIsSpecialApproval;
    private long? _loadedSpecialApprovalId;

    // 映射字典
    public Dictionary<char, string> DisabilityTypeKeyMap => _disabilityTypeKeyMap;
    public Dictionary<char, string> DisabilityLevelKeyMap => _disabilityLevelKeyMap;
    public Dictionary<string, string> DisabilityTypeDisplayMap => _disabilityTypeDisplayMap;
    public Dictionary<string, string> DisabilityLevelDisplayMap => _disabilityLevelDisplayMap;
    private Dictionary<char, string> _disabilityTypeKeyMap = new();
    private Dictionary<char, string> _disabilityLevelKeyMap = new();
    private Dictionary<string, string> _disabilityTypeDisplayMap = new();
    private Dictionary<string, string> _disabilityLevelDisplayMap = new();
    private Dictionary<string, List<DiseaseItem>> _diseaseCategoryMap = new();
    private Dictionary<string, int> _townIdMap = new();
    private Services.Domain.SocialAssistance.ClassificationResult? _lastClassificationResult;
    private bool _isLoadingData;

    // 特殊项 key
    private string _noDiseaseKey = string.Empty;
    private string _selectPlaceholderKey = string.Empty;
    private List<string> _severeLevelKeys = new();

    // 搜索结果
    [ObservableProperty]
    private ObservableCollection<PersonSearchResult> _searchResults = new();

    [ObservableProperty]
    private PersonSearchResult? _selectedSearchResult;

    partial void OnSelectedSearchResultChanged(PersonSearchResult? oldValue, PersonSearchResult? newValue)
    {
        if (oldValue != null) oldValue.IsSelected = false;
        if (newValue != null) newValue.IsSelected = true;
    }

    [ObservableProperty]
    private bool _isSearchPopupVisible;

    #region 基本信息属性

    [ObservableProperty]
    private string _applicantName = string.Empty;

    [ObservableProperty]
    private string _applicantIdCard = string.Empty;

    [ObservableProperty]
    private string _applicantPhone = string.Empty;

    [ObservableProperty]
    private string _address = string.Empty;

    [ObservableProperty]
    private string _hukouAddress = string.Empty;

    /// <summary>开户行（银行卡开户行）</summary>
    [ObservableProperty]
    private string _bankName = string.Empty;

    /// <summary>银行卡号</summary>
    [ObservableProperty]
    private string _bankAccount = string.Empty;

    [ObservableProperty]
    private DictItemOption? _hukouType;

    [ObservableProperty]
    private string _district = string.Empty;

    [ObservableProperty]
    private string _town = string.Empty;

    [ObservableProperty]
    private string _village = string.Empty;

    [ObservableProperty]
    private int _familySize = 1;

    [ObservableProperty]
    private string _applicationReason = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ApplicationReasonDetailLength))]
    private string _applicationReasonDetail = string.Empty;

    /// <summary>申请理由详情字数（Step4 字数统计绑定使用）</summary>
    public int ApplicationReasonDetailLength => ApplicationReasonDetail?.Length ?? 0;

    [ObservableProperty]
    private string _gender = string.Empty;

    [ObservableProperty]
    private string _detectedGender = string.Empty;

    [ObservableProperty]
    private int? _detectedAge;

    [ObservableProperty]
    private string _detectedAgeDisplay = string.Empty;

    [ObservableProperty]
    private string _detectedGenderDisplay = string.Empty;

    [ObservableProperty]
    private string _disabilityCardNo = string.Empty;

    [ObservableProperty]
    private DictItemOption? _selectedEthnicity;

    [ObservableProperty]
    private DictItemOption? _selectedEducationLevel;

    [ObservableProperty]
    private DictItemOption? _selectedPoliticalStatus;

    [ObservableProperty]
    private string _selectedRelation = string.Empty;

    [ObservableProperty]
    private DictItemOption? _selectedApplicationReasonObj;

    partial void OnSelectedApplicationReasonObjChanged(DictItemOption? value)
    {
        if (value != null)
        {
            ApplicationReasonDetail = GenerateApplicationReasonDetail(value.Display);
        }
    }

    private string GenerateApplicationReasonDetail(string reason)
    {
        // 与月报会议记录共用 FamilySituationTextBuilder，保证口径一致、金额自洽
        var ctx = new FamilySituationContext
        {
            ApplicantName = ApplicantName,
            HukouType = HukouType?.Display ?? "",
            FamilySize = FamilySize,
            SickMembers = FamilyMembers
                .Where(m => m.HealthStatus == "重病" || m.HealthStatus == "重残" || m.IsSevereDisease)
                .Select(m => (m.Name, m.HealthStatus))
                .ToList(),
            Reason = reason,
            WorkIncomeTotal = WorkIncomeTotal,
            BusinessIncomeTotal = BusinessIncomeTotal,
            PropertyIncomeTotal = PropertyIncomeTotal,
            TransferIncomeTotal = TransferIncomeTotal,
            OtherIncomeTotal = OtherIncomeTotal,
            AlimonyIncome = AlimonyIncome,
            TotalFamilyIncome = TotalFamilyIncome,
            PerCapitaIncome = PerCapitaIncome,
            TotalAnnualIncome = TotalAnnualIncome,
            PerCapitaAnnualIncome = PerCapitaAnnualIncome,
            RigidExpenditure = RigidExpenditure,
            FamilyLandArea = FamilyLandArea,
            LandIncomeTotal = LandIncomeTotal,
            SubsidyTotal = SubsidyTotal,
            PropertyCount = FamilyProperties.Count,
            VehicleCount = Vehicles.Count,
            MachineryCount = Machineries.Count,
            SupporterGroups = Supporters
                .GroupBy(s => s.PersonType)
                .Select(g => (g.Key, g.Count(), g.Sum(s => s.AnnualSupportFee)))
                .ToList()
        };
        return FamilySituationTextBuilder.Build(ctx);
    }

    #endregion

    #region 省市区联动属性

    [ObservableProperty]
    private string _selectedProvince = string.Empty;

    [ObservableProperty]
    private string _selectedCity = string.Empty;

    [ObservableProperty]
    private string _selectedDistrict = string.Empty;

    [ObservableProperty]
    private string _selectedTown = string.Empty;

    [ObservableProperty]
    private string _selectedVillage = string.Empty;

    public ObservableCollection<string> ProvinceOptions { get; } = new();
    public ObservableCollection<string> CityOptions { get; } = new();
    public ObservableCollection<string> DistrictOptions { get; } = new();
    public ObservableCollection<string> TownOptions { get; } = new();
    public ObservableCollection<string> VillageOptions { get; } = new();

    #endregion

    #region 户籍所在地属性

    [ObservableProperty]
    private string _hukouProvince = DefaultValuesConstants.HOME_PROVINCE;

    [ObservableProperty]
    private string _hukouCity = string.Empty;

    [ObservableProperty]
    private string _hukouDistrict = string.Empty;

    [ObservableProperty]
    private string _hukouTown = string.Empty;

    #endregion

    #region 字典数据属性

    [ObservableProperty]
    private DictItemOption? _selectedSupportModeObj;

    partial void OnSelectedSupportModeObjChanged(DictItemOption? value)
    {
        if (value != null)
        {
            DestituteSupportType = value.Key;
            OnPropertyChanged(nameof(IsCentralizedSupport));

            // 如果选择集中供养，加载供养机构列表
            if (IsCentralizedSupport)
            {
                _ = LoadInstitutionOptionsAsync();
            }
        }
    }

    /// <summary>
    /// 加载供养机构选项
    /// </summary>
    private async Task LoadInstitutionOptionsAsync()
    {
        // 供养机构表(nc_biz_support_institutions)已不存在，此方法为空操作
        await Task.CompletedTask;
    }

    [ObservableProperty]
    private DictItemOption? _selectedMaritalStatus;

    public ObservableCollection<DictItemOption> SupportModeOptions { get; } = new();
    public ObservableCollection<DictItemOption> ApplicationReasonOptions { get; } = new();
    public ObservableCollection<DictItemOption> GenderOptions { get; } = new();
    public ObservableCollection<DictItemOption> EthnicityOptions { get; } = new();
    public ObservableCollection<DictItemOption> MaritalStatusOptions { get; } = new();
    public ObservableCollection<DictItemOption> EducationLevelOptions { get; } = new();
    public ObservableCollection<DictItemOption> PoliticalStatusOptions { get; } = new();

    #endregion

    #region 健康与残疾信息属性

    [ObservableProperty]
    private DictItemOption? _selectedDiseaseCategoryObj;

    [ObservableProperty]
    private DictItemOption? _selectedDiseaseNameObj;

    [ObservableProperty]
    private DictItemOption? _selectedDisabilityTypeObj;

    [ObservableProperty]
    private DictItemOption? _selectedDisabilityLevelObj;

    [ObservableProperty]
    private DictItemOption? _selectedHealthStatusObj;

    /// <summary>疾病编码（ICD-10）；命中内置字典自动回填二级疾病名称与一级分类</summary>
    [ObservableProperty]
    private string _diseaseCode = string.Empty;

    /// <summary>是否重病（经办人人工判定复选框；勾选同步健康状况为重病）</summary>
    [ObservableProperty]
    private bool _isSevereDisease;

    /// <summary>二级疾病文本（疾病编码自动带出或手工录入；保存到 secondary_disease_name）</summary>
    [ObservableProperty]
    private string _diseaseNameText = string.Empty;

    // key 属性（用于基于 item_key 的逻辑判断）
    [ObservableProperty]
    private string _selectedDiseaseCategoryKey = string.Empty;

    [ObservableProperty]
    private string _selectedDisabilityTypeKey = string.Empty;

    [ObservableProperty]
    private string _selectedDisabilityLevelKey = string.Empty;

    public ObservableCollection<DictItemOption> DiseaseCategoryOptions { get; } = new();
    public ObservableCollection<DictItemOption> DiseaseNameOptions { get; } = new();
    public ObservableCollection<DictItemOption> DisabilityTypeOptions { get; } = new();
    public ObservableCollection<DictItemOption> DisabilityLevelOptions { get; } = new();
    public ObservableCollection<DictItemOption> HealthStatusOptions { get; } = new();

    public ObservableCollection<DictItemOption> EmploymentStatusOptions { get; } = new();

    public ObservableCollection<DictItemOption> IncomeSourceOptions { get; } = new();

    /// <summary>
    /// 收入明细人员选择器选项（户主 + 共同生活成员）
    /// </summary>
    public ObservableCollection<FamilyMember> IncomeMemberOptions { get; } = new();

    #endregion

    #region 表单字段 ViewModel

    public FormFieldDescriptor ApplicantNameField { get; } = new()
    {
        Label = "姓名",
        Placeholder = "请输入姓名",
        IsRequired = true,
        MinLength = 2,
        MaxLength = 20
    };

    public FormFieldDescriptor ApplicantIdCardField { get; } = new()
    {
        Label = "身份证号",
        Placeholder = "请输入18位身份证号",
        IsRequired = true,
        ValidationPattern = @"^\d{17}[\dXx]$"
    };

    public FormFieldDescriptor DisabilityCardNoField { get; } = new()
    {
        Label = "残疾证号",
        Placeholder = "请输入残疾证号"
    };

    public FormFieldDescriptor PhoneField { get; } = new()
    {
        Label = "联系方式",
        Placeholder = "请输入手机号",
        IsRequired = true,
        ValidationPattern = @"^1\d{10}$"
    };

    public FormFieldDescriptor HukouAddressField { get; } = new()
    {
        Label = "户籍地址",
        Placeholder = "请输入户籍地址",
        MaxLength = 200
    };

    #endregion

    #region 入户调查属性

    [ObservableProperty]
    private DateTime? _surveyDate;

    /// <summary>
    /// 补全模式：入户调查日期锁定值（导入库纳入时间=建档时间 created_at），保存时强制写回。
    /// 仅数据补全模式锁定，其他模式为 null（不干预）。
    /// </summary>
    private DateTime? _lockedSurveyDate;

    /// <summary>
    /// 入户调查日期是否锁定：仅数据补全模式（导入库建档）为 true，日期取纳入时间不可修改。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSurveyDateEditable))]
    private bool _isSurveyDateLocked;

    /// <summary>入户调查日期是否可编辑（补全模式锁定）</summary>
    public bool IsSurveyDateEditable => !IsSurveyDateLocked;

    [ObservableProperty]
    private string _surveyorName = string.Empty;

    [ObservableProperty]
    private string _surveyorOrganization = string.Empty;

    [ObservableProperty]
    private string _respondentName = string.Empty;

    [ObservableProperty]
    private string _respondentRelation = string.Empty;

    [ObservableProperty]
    private string _surveyNotes = string.Empty;

    [ObservableProperty]
    private string _surveyConclusion = "属实";

    public List<string> SurveyConclusionOptions { get; } = new() { "属实", "不属实", "部分属实" };

    #endregion

    #region 经济信息属性

    [ObservableProperty]
    private decimal _workIncomeTotal;

    [ObservableProperty]
    private decimal _businessIncomeTotal;

    [ObservableProperty]
    private decimal _propertyIncomeTotal;

    [ObservableProperty]
    private decimal _transferIncomeTotal;

    [ObservableProperty]
    private decimal _otherIncomeTotal;

    [ObservableProperty]
    private decimal _alimonyIncome;

    /// <summary>
    /// 家庭年总收入（年值权威口径，由 CalculateAnnualFamilyIncome 一次性舍入）
    /// </summary>
    [ObservableProperty]
    private decimal _totalAnnualIncome;

    /// <summary>
    /// 人均年收入（年值权威口径 = 年总收入 ÷ 家庭人数）
    /// </summary>
    [ObservableProperty]
    private decimal _perCapitaAnnualIncome;

    [ObservableProperty]
    private decimal _totalFamilyIncome;

    [ObservableProperty]
    private decimal _perCapitaIncome;

    [ObservableProperty]
    private decimal _rigidExpenditure;

    /// <summary>
    /// 收入小计（年·毛）= 月项×12 + 赡养年值 + 土地年值 + 补贴年值（不含刚性支出扣减）
    /// </summary>
    public decimal IncomeSubtotal => (WorkIncomeTotal + BusinessIncomeTotal + PropertyIncomeTotal +
        TransferIncomeTotal + OtherIncomeTotal) * 12 + AlimonyIncome + LandIncomeTotal + SubsidyTotal;

    /// <summary>
    /// 总收入（年·净）= 年值权威口径（毛收入 − 刚性支出×12，一次性舍入）
    /// </summary>
    public decimal TotalFamilyIncomeAnnual => TotalAnnualIncome;

    /// <summary>
    /// 人均年收入（年·净）= 年值权威口径
    /// </summary>
    public decimal PerCapitaIncomeAnnual => PerCapitaAnnualIncome;

    /// <summary>
    /// 计算指定类型的刚性支出汇总（通用方法）
    /// </summary>
    private decimal GetRigidExpenditureByType(string expenditureType) =>
        RigidExpenditures.Where(e => e.ExpenditureType == expenditureType).Sum(e => e.Amount);

    public decimal MedicalExpenditure => GetRigidExpenditureByType(DictionaryConstants.RigidExpenditureType.MEDICAL);
    public decimal EducationExpenditure => GetRigidExpenditureByType(DictionaryConstants.RigidExpenditureType.EDUCATION);
    public decimal DisabilityRehabExpenditure => GetRigidExpenditureByType(DictionaryConstants.RigidExpenditureType.DISABILITY_REHAB);
    public decimal LivingExpenditure => GetRigidExpenditureByType(DictionaryConstants.RigidExpenditureType.LIVING);

    #endregion

    #region 土地信息属性

    [ObservableProperty]
    private decimal _familyLandArea;

    [ObservableProperty]
    private decimal _selfFarmedLandArea;

    [ObservableProperty]
    private decimal _subleasedLandArea;

    [ObservableProperty]
    private decimal _contractedLandArea;

    [ObservableProperty]
    private decimal _landIncomeTotal;

    [ObservableProperty]
    private decimal _subsidyTotal;

    // ===== 土地确权归户表（新） =====

    [ObservableProperty]
    private ObservableCollection<LandConfirmationGroup> _landConfirmationGroups = new();

    private int _nextGroupId = 1;

    [ObservableProperty]
    private decimal _totalConfirmedLandArea;

    [ObservableProperty]
    private decimal _totalLandShares;

    [ObservableProperty]
    private int _totalLandPersonCount;

    [ObservableProperty]
    private decimal _familyLandShares;

    [ObservableProperty]
    private int _familyLandPersonCount;

    public decimal CalculatedPerPersonArea =>
        FamilyLandPersonCount > 0 ? TotalConfirmedLandArea / FamilyLandPersonCount : 0;

    public string LandCalculationFormula =>
        $"{TotalConfirmedLandArea:F2}亩 ÷ {FamilyLandPersonCount}人 = {CalculatedPerPersonArea:F2}亩/人";

    public List<string> LandStatusOptions { get; } = new(LandStatusConstants.All);

    #endregion

    #region 分类判定属性

    [ObservableProperty]
    private string? _classificationResult;

    [ObservableProperty]
    private string? _classificationDescription;

    [ObservableProperty]
    private decimal _guaranteeAmount;

    [ObservableProperty]
    private decimal _classifiedSubsidyAmount;

    [ObservableProperty]
    private string _classifiedSubsidyType = string.Empty;

    [ObservableProperty]
    private decimal _caregiverSubsidyAmount;

    [ObservableProperty]
    private decimal _totalGuaranteeAmount;

    [ObservableProperty]
    private bool _isEligible;

    [ObservableProperty]
    private string? _ineligibleReason;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsClassificationLocked))]
    private bool _isClassificationDone;

    [ObservableProperty]
    private string _determinationBasis = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _determinationDetails = new();

    [ObservableProperty]
    private string _classifiedSubsidyTypes = string.Empty;

    [ObservableProperty]
    private int _classifiedSubsidyCount;

    [ObservableProperty]
    private decimal _classifiedSubsidyPerPerson;

    [ObservableProperty]
    private bool _isDetermining;

    #endregion

    #region 渐退期属性

    [ObservableProperty]
    private bool _isInGracePeriod;

    [ObservableProperty]
    private int _gracePeriodMonths;

    [ObservableProperty]
    private DateTime? _gracePeriodStartDate;

    [ObservableProperty]
    private DateTime? _gracePeriodEndDate;

    [ObservableProperty]
    private string? _originalClassificationResult;

    [ObservableProperty]
    private decimal? _originalGuaranteeAmount;

    /// <summary>
    /// 变更链上游档案的原分类结果（仅内存承载，用于渐退期"低保→低收入"判定）
    /// </summary>
    private string? _originalClassificationContext;

    /// <summary>
    /// 变更链上游档案的原月保障金额（户主死亡等停旧建新：渐退封顶比较用"原有享受额度"）
    /// </summary>
    private decimal? _originalGuaranteeContext;

    /// <summary>
    /// 渐退期内实际应发月保障金（原额超户口类型上限时已封顶；null=未封顶/未进渐退）
    /// </summary>
    [ObservableProperty]
    private decimal? _graceGrantAmount;

    /// <summary>
    /// 本会话是否已写入过渐退超限 FundChange（防重复写月报减发）
    /// </summary>
    private bool _graceFundChangeRecorded;

    /// <summary>
    /// 渐退期状态文本（供 Step5 判定结果展示）
    /// </summary>
    public string GracePeriodStatusText
    {
        get
        {
            if (!IsInGracePeriod) return "无渐退期";
            if (GracePeriodEndDate == null) return "渐退期中";
            var daysLeft = (GracePeriodEndDate.Value.Date - DateTime.Today).Days;
            return daysLeft >= 0
                ? $"剩余 {daysLeft} 天（至 {GracePeriodEndDate:yyyy-MM-dd}）"
                : $"已结束（{GracePeriodEndDate:yyyy-MM-dd}）";
        }
    }

    partial void OnIsInGracePeriodChanged(bool value) => OnPropertyChanged(nameof(GracePeriodStatusText));

    partial void OnGracePeriodEndDateChanged(DateTime? value) => OnPropertyChanged(nameof(GracePeriodStatusText));

    #endregion

    #region 单人保属性

    /// <summary>
    /// 是否需要创建单人保草稿
    /// </summary>
    [ObservableProperty]
    private bool _needsSingleRescueDraft;

    /// <summary>
    /// 符合单人保条件的成员列表
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<FamilyMember> _singleRescueMembers = new();

    /// <summary>
    /// 单人保提示信息
    /// </summary>
    public string SingleRescueHint => NeedsSingleRescueDraft && SingleRescueMembers.Count > 0
        ? $"⚠️ 户主/家庭成员 {string.Join("、", SingleRescueMembers.Select(m => m.Name))} 符合单人保政策，建议创建单人保申请"
        : string.Empty;

    /// <summary>
    /// 当前申请是否为单人保（编辑时锁定经济状况）
    /// </summary>
    [ObservableProperty]
    private bool _isSingleRescueApplication;

    /// <summary>
    /// 当前申请状态
    /// </summary>
    [ObservableProperty]
    private string _applicationStatus = ApplicationStatusCodes.DRAFT;

    /// <summary>
    /// 原始状态（加载时记录，补全模式保存时保留原状态，不降级为 Draft）
    /// </summary>
    private string _originalStatus = ApplicationStatusCodes.DRAFT;

    /// <summary>
    /// 是否补全模式（导入库建档后的数据补全，经济信息可编辑、保存保持原状态）
    /// </summary>
    public bool IsCompletionMode => OperationMode == FormOperationMode.Completion;

    /// <summary>
    /// 是否经济复核模式（锁定户主Step1与成员Step2，放开经济Step3/调查Step4/认定Step5）
    /// </summary>
    public bool IsReviewMode => OperationMode == FormOperationMode.Review;

    /// <summary>
    /// 是否家庭信息修正模式（可编辑全部步骤，但户主姓名/身份证锁定；仅限本月本周期档案）
    /// </summary>
    public bool IsFamilyCorrectionMode => OperationMode == FormOperationMode.ReviewWithFamilyCorrection;

    /// <summary>
    /// 是否编辑家庭信息模式（可编辑全部步骤，但户主姓名/身份证锁定；无周期限制）
    /// </summary>
    public bool IsEditFamilyInfoMode => OperationMode == FormOperationMode.EditFamilyInfo;

    /// <summary>
    /// 是否家庭成员变更模式（锁定户主Step1，放开成员Step2增删，重新认定后停旧建新）
    /// </summary>
    public bool IsMemberChangeMode => OperationMode == FormOperationMode.MemberChange;

    /// <summary>
    /// 是否查看模式（档案只读查看：全部字段不可编辑，仅浏览）
    /// </summary>
    public bool IsViewMode => OperationMode == FormOperationMode.View;

    /// <summary>
    /// 是否可编辑（非查看模式）
    /// </summary>
    public bool IsEditable => OperationMode != FormOperationMode.View;

    /// <summary>
    /// 是否可保存（草稿/提交按钮可见性）
    /// </summary>
    public bool IsSavable => OperationMode != FormOperationMode.View;

    /// <summary>
    /// 保存草稿按钮可见（非查看、非补全、非复核）
    /// </summary>
    public bool IsSaveDraftVisible => IsNotCompletionMode && IsEditable;

    /// <summary>
    /// 提交按钮可见（最后一步且可编辑）
    /// </summary>
    public bool IsSubmitVisible => IsLastStep && IsEditable;

    /// <summary>
    /// 是否非特殊模式（控制"保存草稿"按钮可见性：补全/复核/家庭修正/编辑家庭信息/成员变更模式隐藏）
    /// </summary>
    public bool IsNotCompletionMode => !IsCompletionMode && !IsReviewMode && !IsFamilyCorrectionMode && !IsEditFamilyInfoMode && !IsMemberChangeMode;

    /// <summary>
    /// 分类结果是否锁定：
    /// ① 补全模式下建档时已认定的结果不允许重新判定；
    /// ② 已确定档案（Approved/Stopped）在普通编辑/查看打开时锁定，仅合法变更流程（经济复核/家庭修正/编辑家庭信息）允许重算。
    /// </summary>
    public bool IsClassificationLocked =>
        (IsCompletionMode && IsClassificationDone)
        || (IsDeterminedStatus && !IsChangeMode);

    /// <summary>是否为已确定档案（Approved/Stopped）</summary>
    private bool IsDeterminedStatus =>
        ApplicationStatus is ApplicationStatusCodes.APPROVED or ApplicationStatusCodes.STOPPED;

    /// <summary>是否为允许重新判定的变更流程模式（经济复核/家庭信息修正/编辑家庭信息/家庭成员变更）</summary>
    private bool IsChangeMode =>
        IsReviewMode || IsFamilyCorrectionMode || IsEditFamilyInfoMode || IsMemberChangeMode;

    /// <summary>
    /// Step1 户主信息是否可编辑（复核/成员变更/查看模式锁定，家庭信息修正/编辑家庭信息模式允许编辑）
    /// </summary>
    public bool IsStep1Editable => !((IsReviewMode || IsMemberChangeMode) && !IsFamilyCorrectionMode && !IsEditFamilyInfoMode) && !IsViewMode;

    /// <summary>
    /// Step2 家庭成员是否可编辑（复核/查看模式锁定，家庭信息修正/编辑家庭信息模式允许编辑）
    /// </summary>
    public bool IsStep2Editable => !(IsReviewMode && !IsFamilyCorrectionMode && !IsEditFamilyInfoMode) && !IsViewMode;

    /// <summary>
    /// 户主姓名/身份证是否锁定（家庭信息修正/编辑家庭信息模式下锁定，防止修改户主核心身份信息）
    /// </summary>
    public bool IsApplicantIdentityLocked => IsFamilyCorrectionMode || IsEditFamilyInfoMode;

    /// <summary>
    /// 经济状况是否可编辑（草稿状态或补全/复核/家庭修正/编辑家庭信息/成员变更模式可编辑，其他状态锁定）
    /// </summary>
    public bool IsEconomyEditable => (ApplicationStatus == ApplicationStatusCodes.DRAFT || IsCompletionMode || IsReviewMode || IsFamilyCorrectionMode || IsEditFamilyInfoMode || IsMemberChangeMode) && !IsSingleRescueApplication && !IsViewMode;

    #endregion

    #region 照料相关属性

    [ObservableProperty]
    private string _caregiverType = CaregiverTypeConstants.NONE;

    [ObservableProperty]
    private string _destituteSupportType = string.Empty;

    /// <summary>
    /// 是否为特困分类（用于显示供养方式选择）
    /// </summary>
    public bool IsDestituteClassification => ClassificationResult != null &&
        (ClassificationResult.Contains("Destitute") || ClassificationResult.Contains("特困"));

    /// <summary>
    /// 是否为集中供养
    /// </summary>
    public bool IsCentralizedSupport => DestituteSupportType == "Centralized";

    #endregion

    #region 能力鉴定属性

    /// <summary>
    /// 能力鉴定：能完成项目数
    /// </summary>
    [ObservableProperty]
    private int _capabilityCompletedItems = 6;

    /// <summary>
    /// 能力鉴定：自理能力等级
    /// </summary>
    [ObservableProperty]
    private string _capabilitySelfCareLevel = DictionaryConstants.CapabilityLevel.FULL_SELF_CARE;

    /// <summary>
    /// 能力鉴定：进食能力
    /// </summary>
    [ObservableProperty]
    private bool _capabilityEating = true;

    /// <summary>
    /// 能力鉴定：穿衣能力
    /// </summary>
    [ObservableProperty]
    private bool _capabilityDressing = true;

    /// <summary>
    /// 能力鉴定：上下床能力
    /// </summary>
    [ObservableProperty]
    private bool _capabilityGettingInOutOfBed = true;

    /// <summary>
    /// 能力鉴定：如厕能力
    /// </summary>
    [ObservableProperty]
    private bool _capabilityUsingToilet = true;

    /// <summary>
    /// 能力鉴定：室内行走能力
    /// </summary>
    [ObservableProperty]
    private bool _capabilityIndoorWalking = true;

    /// <summary>
    /// 能力鉴定：洗澡能力
    /// </summary>
    [ObservableProperty]
    private bool _capabilityBathing = true;

    /// <summary>
    /// 进食能力状态文本
    /// </summary>
    public string CapabilityEatingStatus => CapabilityEating ? "✓ 能" : "✗ 不能";

    /// <summary>
    /// 穿衣能力状态文本
    /// </summary>
    public string CapabilityDressingStatus => CapabilityDressing ? "✓ 能" : "✗ 不能";

    /// <summary>
    /// 上下床能力状态文本
    /// </summary>
    public string CapabilityBedStatus => CapabilityGettingInOutOfBed ? "✓ 能" : "✗ 不能";

    /// <summary>
    /// 如厕能力状态文本
    /// </summary>
    public string CapabilityToiletStatus => CapabilityUsingToilet ? "✓ 能" : "✗ 不能";

    /// <summary>
    /// 室内行走能力状态文本
    /// </summary>
    public string CapabilityWalkingStatus => CapabilityIndoorWalking ? "✓ 能" : "✗ 不能";

    /// <summary>
    /// 洗澡能力状态文本
    /// </summary>
    public string CapabilityBathingStatus => CapabilityBathing ? "✓ 能" : "✗ 不能";

    #endregion

    /// <summary>
    /// 供养机构选项
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<SupportInstitution> _institutionOptions = new();

    /// <summary>
    /// 选中的供养机构
    /// </summary>
    [ObservableProperty]
    private SupportInstitution? _selectedInstitution;

    /// <summary>
    /// 是否有选中的机构
    /// </summary>
    public bool HasSelectedInstitution => SelectedInstitution != null;

    /// <summary>
    /// 选中机构的费用显示
    /// </summary>
    public string SelectedInstitutionFeeDisplay => SelectedInstitution != null
        ? $"费用: {SelectedInstitution.TotalFee:F0}元/月"
        : string.Empty;

    /// <summary>
    /// 选中机构的床位显示
    /// </summary>
    public string SelectedInstitutionBedDisplay => SelectedInstitution != null
        ? $"床位: {SelectedInstitution.CurrentOccupancy}/{SelectedInstitution.Capacity} (可用{SelectedInstitution.AvailableBeds})"
        : string.Empty;

    #region 集合属性

    [ObservableProperty]
    private ObservableCollection<FamilyMember> _familyMembers = new();

    /// <summary>
    /// 共同生活成员列表（独立集合，通过 RefreshDerivedCollections 同步）
    /// </summary>
    public ObservableCollection<FamilyMember> SharedLivingMembers { get; } = new();

    /// <summary>
    /// 赡养抚养扶养人列表（独立集合，通过 RefreshDerivedCollections 同步）
    /// </summary>
    public ObservableCollection<FamilyMember> SupportMembers { get; } = new();

    /// <summary>
    /// 刷新派生集合（共同生活成员、赡养抚养人）
    /// </summary>
    private void RefreshDerivedCollections()
    {
        SharedLivingMembers.Clear();
        SupportMembers.Clear();

        foreach (var m in FamilyMembers)
        {
            if (m.MemberCategory == MemberCategoryConstants.SHARED_LIVING && !m.IsHouseholdHead)
                SharedLivingMembers.Add(m);
            else if (m.MemberCategory == MemberCategoryConstants.SUPPORT)
                SupportMembers.Add(m);
        }

        UpdateFamilySize();
    }

    [ObservableProperty]
    private ObservableCollection<Supporter> _supporters = new();

    [ObservableProperty]
    private ObservableCollection<Caregiver> _caregivers = new();

    [ObservableProperty]
    private ObservableCollection<Guardian> _guardians = new();

    [ObservableProperty]
    private ObservableCollection<LaborIncome> _laborIncomes = new();

    [ObservableProperty]
    private ObservableCollection<BusinessIncome> _businessIncomes = new();

    [ObservableProperty]
    private ObservableCollection<RigidExpenditure> _rigidExpenditures = new();

    [ObservableProperty]
    private ObservableCollection<LandRegistration> _landRegistrations = new();

    [ObservableProperty]
    private ObservableCollection<Subsidy> _subsidies = new();

    [ObservableProperty]
    private ObservableCollection<PropertyIncome> _propertyIncomes = new();

    [ObservableProperty]
    private ObservableCollection<TransferIncome> _transferIncomes = new();

    [ObservableProperty]
    private ObservableCollection<OtherIncome> _otherIncomes = new();

    [ObservableProperty]
    private ObservableCollection<FamilyProperty> _familyProperties = new();

    [ObservableProperty]
    private ObservableCollection<Vehicle> _vehicles = new();

    [ObservableProperty]
    private ObservableCollection<BreedingIncome> _breedingIncomes = new();

    [ObservableProperty]
    private ObservableCollection<FinancialAsset> _financialAssets = new();

    #region CollectionView 动态高度（ScrollView 内 CollectionView 需固定高度，按条数估算）

    /// <summary>
    /// 农业补贴列表高度（单列，每卡约 140px）。
    /// 有内容时始终返回最大高度（560），避免删除项后父 ScrollView 内容高度骤减
    /// 导致滚动位置重置到页面顶部（"回弹"问题）。仅清空时缩至最小高度。
    /// </summary>
    public double SubsidyListHeight => Subsidies.Count > 0 ? UIConstants.SUBSIDY_LIST_MAX_HEIGHT : UIConstants.LIST_MIN_HEIGHT;

    /// <summary>
    /// 刚性支出列表高度（2 列网格，每行约 150px）。
    /// 有内容时恒为最大高度（600），避免删除回弹。清空时缩至最小。
    /// </summary>
    public double RigidExpenditureListHeight => RigidExpenditures.Count > 0 ? UIConstants.RIGID_EXPENDITURE_LIST_MAX_HEIGHT : UIConstants.LIST_MIN_HEIGHT;

    /// <summary>
    /// 赡养人列表高度（2 列网格，每行约 260px）。
    /// 有内容时恒为最大高度（780），避免删除回弹。清空时缩至最小。
    /// </summary>
    public double SupporterListHeight => Supporters.Count > 0 ? UIConstants.SUPPORTER_LIST_MAX_HEIGHT : UIConstants.LIST_MIN_HEIGHT;

    #endregion

    [ObservableProperty]
    private ObservableCollection<Machinery> _machineries = new();

    [ObservableProperty]
    private FinancialAsset _financialAsset = new();

    [ObservableProperty]
    private FamilyMember? _selectedMember;

    #endregion

    #region 经济状况分组展开/折叠状态

    [ObservableProperty]
    private bool _isIncomeExpanded = true;

    [ObservableProperty]
    private bool _isLandExpanded = true;

    [ObservableProperty]
    private bool _isSubsidyExpanded = true;

    [ObservableProperty]
    private bool _isRigidExpenditureExpanded = true;

    [ObservableProperty]
    private bool _isPropertyExpanded = true;

    [ObservableProperty]
    private bool _isSupportExpanded = true;

    #endregion

    #region 土地确权属性

    [ObservableProperty]
    private string _landConfirmationContractorName = string.Empty;

    [ObservableProperty]
    private string _landConfirmationVillage = string.Empty;

    [ObservableProperty]
    private decimal _landConfirmationTotalArea;

    [ObservableProperty]
    private decimal _landConfirmationTotalShares;

    [ObservableProperty]
    private decimal _landConfirmationDryFieldArea;

    [ObservableProperty]
    private decimal _landConfirmationWetFieldArea;

    #endregion

    #region 土地收入单价（默认值，后续从配置读取）

    [ObservableProperty]
    private decimal _selfFarmUnitPrice = IncomeTypeConstants.LandUnitPrice.SELF_FARM;

    [ObservableProperty]
    private decimal _subleaseUnitPrice = IncomeTypeConstants.LandUnitPrice.SUBLEASE;

    [ObservableProperty]
    private decimal _contractUnitPrice = IncomeTypeConstants.LandUnitPrice.CONTRACT;

    #endregion

    #region 下拉选项

    public List<DictItemOption> HukouTypeOptions { get; } = new()
    {
        new DictItemOption { Key = ClassificationConstants.HukouType.RURAL, Display = "农村户口" },
        new DictItemOption { Key = ClassificationConstants.HukouType.URBAN, Display = "城市户口" }
    };

    public List<DictItemOption> RelationOptions { get; } = new()
    {
        new DictItemOption { Key = DictionaryConstants.FamilyRelationship.HEAD, Display = "本人/户主" },
        new DictItemOption { Key = DictionaryConstants.FamilyRelationship.SPOUSE, Display = "配偶" },
        new DictItemOption { Key = DictionaryConstants.FamilyRelationship.SON, Display = "儿子" },
        new DictItemOption { Key = DictionaryConstants.FamilyRelationship.DAUGHTER, Display = "女儿" },
        new DictItemOption { Key = DictionaryConstants.FamilyRelationship.GRANDCHILD, Display = "孙子女" },
        new DictItemOption { Key = DictionaryConstants.FamilyRelationship.PARENT, Display = "父母" },
        new DictItemOption { Key = DictionaryConstants.FamilyRelationship.GRANDPARENT, Display = "祖父母" },
        new DictItemOption { Key = DictionaryConstants.FamilyRelationship.SIBLING, Display = "兄弟姐妹" },
        new DictItemOption { Key = DictionaryConstants.FamilyRelationship.OTHER, Display = "其他" }
    };

    /// <summary>
    /// 成员关系选项（排除"本人/户主"，用于新增家庭成员）
    /// </summary>
    public List<DictItemOption> MemberRelationOptions { get; } = new()
    {
        new DictItemOption { Key = DictionaryConstants.FamilyRelationship.SPOUSE, Display = "配偶" },
        new DictItemOption { Key = DictionaryConstants.FamilyRelationship.SON, Display = "儿子" },
        new DictItemOption { Key = DictionaryConstants.FamilyRelationship.DAUGHTER, Display = "女儿" },
        new DictItemOption { Key = DictionaryConstants.FamilyRelationship.GRANDCHILD, Display = "孙子女" },
        new DictItemOption { Key = DictionaryConstants.FamilyRelationship.PARENT, Display = "父母" },
        new DictItemOption { Key = DictionaryConstants.FamilyRelationship.GRANDPARENT, Display = "祖父母" },
        new DictItemOption { Key = DictionaryConstants.FamilyRelationship.SIBLING, Display = "兄弟姐妹" },
        new DictItemOption { Key = DictionaryConstants.FamilyRelationship.OTHER, Display = "其他" }
    };

    public List<DictItemOption> ApplicationReasons { get; } = new()
    {
        new DictItemOption { Key = "家庭困难", Display = "家庭困难" },
        new DictItemOption { Key = "因病致贫", Display = "因病致贫" },
        new DictItemOption { Key = "因残致贫", Display = "因残致贫" },
        new DictItemOption { Key = "因学致贫", Display = "因学致贫" },
        new DictItemOption { Key = "因灾致贫", Display = "因灾致贫" },
        new DictItemOption { Key = "其他原因", Display = "其他原因" }
    };

    public List<DictItemOption> CaregiverTypeOptions { get; } = new()
    {
        new DictItemOption { Key = CaregiverTypeConstants.NONE, Display = "无" },
        new DictItemOption { Key = CaregiverTypeConstants.FAMILY, Display = "家属照料" },
        new DictItemOption { Key = CaregiverTypeConstants.INSTITUTION, Display = "机构照料" }
    };

    public List<DictItemOption> PersonTypeOptions { get; } = new()
    {
        new DictItemOption { Key = "赡养", Display = "赡养" },
        new DictItemOption { Key = "抚养", Display = "抚养" },
        new DictItemOption { Key = "扶养", Display = "扶养" }
    };

    public List<DictItemOption> RigidExpenditureTypeOptions { get; } = new()
    {
        new DictItemOption { Key = DictionaryConstants.RigidExpenditureType.MEDICAL, Display = "医疗" },
        new DictItemOption { Key = DictionaryConstants.RigidExpenditureType.EDUCATION, Display = "教育" },
        new DictItemOption { Key = DictionaryConstants.RigidExpenditureType.DISABILITY_REHAB, Display = "残疾康复" },
        new DictItemOption { Key = DictionaryConstants.RigidExpenditureType.LIVING, Display = "生活" }
    };

    public List<string> PropertyIncomeTypeOptions { get; } = new()
    {
        "存款利息",
        "房产租金",
        "股息红利",
        "无形资产收益",
        "其他"
    };

    public List<string> LaborSubTypeOptions { get; } = new()
    {
        IncomeTypeConstants.LaborSubType.WAGE,
        IncomeTypeConstants.LaborSubType.BONUS,
        IncomeTypeConstants.LaborSubType.ALLOWANCE
    };

    public List<string> BusinessSubTypeOptions { get; } = new()
    {
        IncomeTypeConstants.BusinessSubType.AGRICULTURE,
        IncomeTypeConstants.BusinessSubType.COMMERCE
    };

    public List<string> TransferSubTypeOptions { get; } = new()
    {
        IncomeTypeConstants.TransferSubType.PENSION,
        IncomeTypeConstants.TransferSubType.LOW_INCOME_SUBSIDY,
        IncomeTypeConstants.TransferSubType.DISABILITY_SUBSIDY,
        IncomeTypeConstants.TransferSubType.OTHER_SUBSIDY
    };

    public List<string> LandUsageOptions { get; } = new()
    {
        DictionaryConstants.LandUsage.SELF_FARM,
        DictionaryConstants.LandUsage.SUBLEASE,
        DictionaryConstants.LandUsage.CONTRACT
    };

    public List<string> SubsidyTypeOptions { get; } = new()
    {
        DictionaryConstants.SubsidyType.LAND_FERTILITY,
        DictionaryConstants.SubsidyType.SOYBEAN,
        DictionaryConstants.SubsidyType.CORN,
        DictionaryConstants.SubsidyType.SURFACE_WATER_RICE,
        DictionaryConstants.SubsidyType.GROUND_WATER_RICE,
        DictionaryConstants.SubsidyType.ROTATION
    };

    #endregion

    #region 初始化状态

    [ObservableProperty]
    private bool _isInitialized;
    /// <summary>
    /// 本次会话是否已判定/加载过渐退期状态（M1）：
    /// 仅在 load GetActiveAsync 或分类判定赋值后为 true。
    /// 为 false 时保存不得 ClearAsync——内存默认 false 会误清服务端活动记录（含户主死亡新建档渐退期）。
    /// </summary>
    private bool _gracePeriodEvaluated;

    #endregion

    #region 资产核查数据 + 右侧字段选择

    /// <summary>
    /// 资产核查数据源
    /// </summary>
    [ObservableProperty]
    private AssetVerificationDetail? _assetCheckData;

    /// <summary>
    /// 右侧可用字段列表
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<FieldItem> _availableFields = new();

    /// <summary>
    /// 家庭成员列表（来自资产核查）
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<AssetVerificationFamilyMember> _assetCheckFamilyMembers = new();

    /// <summary>
    /// 是否有资产核查数据
    /// </summary>
    public bool HasAssetCheckData => AssetCheckData != null;

    /// <summary>
    /// 数据来源描述
    /// </summary>
    [ObservableProperty]
    private string _dataSourceDescription = string.Empty;

    /// <summary>
    /// 成员导入弹窗是否可见
    /// </summary>
    [ObservableProperty]
    private bool _isMemberImportPopupVisible;

    /// <summary>
    /// 可选择的家庭成员列表（用于导入弹窗）
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<AssetVerificationFamilyMember> _selectableFamilyMembers = new();

    /// <summary>
    /// 选中的成员数量
    /// </summary>
    public int SelectedMemberCount => SelectableFamilyMembers.Count(m => m.IsSelected);

    /// <summary>
    /// 从资产核查记录加载数据
    /// </summary>
    public async Task LoadFromAssetCheckAsync(long assetCheckId)
    {
        _isLoadingData = true;
        try
        {
            IsBusy = true;
            LoadingMessage = "正在加载资产核查数据...";

            // 确保城市数据已加载
            if (CityOptions.Count == 0)
            {
                _logger.Info("城市数据为空，重新加载");
                await LoadRegionDataAsync();
            }

            // 确保默认地区已设置
            await EnsureRegionOptionsLoadedAsync();

            _logger.Info($"地区数据状态: City={SelectedCity}, District={SelectedDistrict}, Town={SelectedTown}, CityOptions={CityOptions.Count}");

            var detailResult = await _assetVerificationService.GetDetailByIdAsync(assetCheckId);
            if (detailResult.IsFailure || detailResult.Value == null)
            {
                _logger.Error("加载资产核查数据失败: " + detailResult.Message);
                return;
            }

            AssetCheckData = detailResult.Value;
            DataSourceDescription = $"来源: 资产核查 | {AssetCheckData.ApplicantName}";

            BuildAvailableFields();
            BuildAssetCheckFamilyMembers();

            OnPropertyChanged(nameof(HasAssetCheckData));

            _logger.LogBusiness("从资产核查加载数据完成",
                ("ApplicantName", AssetCheckData.ApplicantName),
                ("FieldsCount", AvailableFields.Count.ToString()),
                ("MembersCount", AssetCheckFamilyMembers.Count.ToString()));
        }
        catch (Exception ex)
        {
            _logger.Error("加载资产核查数据失败: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
            _isLoadingData = false;
        }
    }

    /// <summary>
    /// 构建可用字段列表
    /// </summary>
    private void BuildAvailableFields()
    {
        AvailableFields.Clear();

        if (AssetCheckData == null) return;

        // 基本信息
        AvailableFields.Add(new FieldItem("申请人姓名", AssetCheckData.ApplicantName, nameof(ApplicantName), "基本信息"));
        AvailableFields.Add(new FieldItem("身份证号", AssetCheckData.ApplicantIdCard, nameof(ApplicantIdCard), "基本信息"));
        AvailableFields.Add(new FieldItem("联系电话", AssetCheckData.ContactPhone, nameof(ApplicantPhone), "基本信息"));
        AvailableFields.Add(new FieldItem("申请原因", AssetCheckData.ApplicationReason, nameof(ApplicationReason), "基本信息"));
        AvailableFields.Add(new FieldItem("申请日期", AssetCheckData.ApplicationDate.ToString("yyyy-MM-dd"), "ApplicationDate", "基本信息"));

        // 地址信息
        AvailableFields.Add(new FieldItem("家庭地址", AssetCheckData.FamilyAddress, nameof(Address), "地址信息"));
        AvailableFields.Add(new FieldItem("社区", AssetCheckData.Community, nameof(SelectedVillage), "地址信息"));

        // 代理人信息
        if (AssetCheckData.HasAgent)
        {
            AvailableFields.Add(new FieldItem("代理人姓名", AssetCheckData.AgentName, "AgentName", "代理人"));
            AvailableFields.Add(new FieldItem("代理人身份证", AssetCheckData.AgentIdCard, "AgentIdCard", "代理人"));
            AvailableFields.Add(new FieldItem("代理关系", AssetCheckData.AgentRelationship, "AgentRelationship", "代理人"));
        }

        // 经办人信息
        AvailableFields.Add(new FieldItem("经办人", AssetCheckData.OperatorName, "OperatorName", "经办人"));
        AvailableFields.Add(new FieldItem("经办单位", AssetCheckData.OperatorUnitName, "OperatorUnit", "经办人"));
    }

    /// <summary>
    /// 构建家庭成员列表
    /// </summary>
    private void BuildAssetCheckFamilyMembers()
    {
        AssetCheckFamilyMembers.Clear();

        if (AssetCheckData?.FamilyMembers == null) return;

        foreach (var member in AssetCheckData.FamilyMembers)
        {
            AssetCheckFamilyMembers.Add(member);
        }
    }

    /// <summary>
    /// 填充单个字段到表单
    /// </summary>
    [RelayCommand]
    private async Task FillFieldAsync(FieldItem? field)
    {
        if (field == null || string.IsNullOrWhiteSpace(field.Value)) return;

        try
        {
            switch (field.TargetProperty)
            {
                case nameof(ApplicantName):
                    ApplicantName = field.Value;
                    break;
                case nameof(ApplicantIdCard):
                    ApplicantIdCard = field.Value;
                    break;
                case nameof(ApplicantPhone):
                    ApplicantPhone = field.Value;
                    break;
                case nameof(Address):
                    Address = field.Value;
                    break;
                case nameof(ApplicationReason):
                    // 查找对应的申请原因选项
                    var reasonOption = ApplicationReasonOptions.FirstOrDefault(o => o.Key == field.Value || o.Display == field.Value);
                    if (reasonOption != null)
                        SelectedApplicationReasonObj = reasonOption;
                    break;
            }

            field.IsFilled = true;
            var maskedFieldValue = field.TargetProperty switch
            {
                nameof(ApplicantIdCard) => DataMasker.MaskIdCard(field.Value),
                nameof(ApplicantPhone) => DataMasker.MaskPhone(field.Value),
                nameof(ApplicantName) => DataMasker.MaskName(field.Value),
                _ => field.Value
            };
            _logger.LogBusiness("填充字段", ("Field", field.DisplayName), ("Value", maskedFieldValue));
        }
        catch (Exception ex)
        {
            _logger.Error("填充字段失败: " + ex.Message);
        }

        await Task.CompletedTask;
    }

    /// <summary>
    /// 全选字段
    /// </summary>
    [RelayCommand]
    private void SelectAllFields()
    {
        foreach (var field in AvailableFields)
            field.IsSelected = true;
    }

    /// <summary>
    /// 取消全选
    /// </summary>
    [RelayCommand]
    private void DeselectAllFields()
    {
        foreach (var field in AvailableFields)
            field.IsSelected = false;
    }

    /// <summary>
    /// 填充所有选中的字段
    /// </summary>
    [RelayCommand]
    private async Task FillSelectedFieldsAsync()
    {
        var selectedFields = AvailableFields.Where(f => f.IsSelected && f.HasValue).ToList();
        foreach (var field in selectedFields)
        {
            await FillFieldAsync(field);
        }
    }

    /// <summary>
    /// 一键导入所有家庭成员（显示弹窗让用户选择）
    /// </summary>
    [RelayCommand]
    private async Task ImportAllFamilyMembersAsync()
    {
        ShowMemberImportPopup();
        await Task.CompletedTask;
    }

    /// <summary>
    /// 显示成员导入弹窗
    /// </summary>
    [RelayCommand]
    private void ShowMemberImportPopup()
    {
        SelectableFamilyMembers.Clear();

        if (AssetCheckData?.FamilyMembers != null)
        {
            foreach (var member in AssetCheckData.FamilyMembers)
            {
                member.IsSelected = true;  // 默认全选
                SelectableFamilyMembers.Add(member);
            }
        }

        IsMemberImportPopupVisible = true;
        OnPropertyChanged(nameof(SelectedMemberCount));
    }

    /// <summary>
    /// 取消成员导入
    /// </summary>
    [RelayCommand]
    private void CancelMemberImport()
    {
        IsMemberImportPopupVisible = false;
    }

    /// <summary>
    /// 全选成员
    /// </summary>
    [RelayCommand]
    private void SelectAllMembers()
    {
        foreach (var member in SelectableFamilyMembers)
            member.IsSelected = true;
        OnPropertyChanged(nameof(SelectedMemberCount));
    }

    /// <summary>
    /// 取消全选成员
    /// </summary>
    [RelayCommand]
    private void DeselectAllMembers()
    {
        foreach (var member in SelectableFamilyMembers)
            member.IsSelected = false;
        OnPropertyChanged(nameof(SelectedMemberCount));
    }

    /// <summary>
    /// 导入选中的成员
    /// </summary>
    [RelayCommand]
    private async Task ImportSelectedMembersAsync()
    {
        var selectedMembers = SelectableFamilyMembers.Where(m => m.IsSelected).ToList();

        IsMemberImportPopupVisible = false;

        if (selectedMembers.Count == 0)
        {
            _logger.Warn("未选择任何成员");
            return;
        }

        await ImportMembersAsync(selectedMembers);
    }

    /// <summary>
    /// 导入指定的成员列表
    /// </summary>
    private async Task ImportMembersAsync(List<AssetVerificationFamilyMember> members)
    {
        try
        {
            IsBusy = true;
            LoadingMessage = "正在导入家庭成员...";

            // 确保地址选项已加载
            await EnsureRegionOptionsLoadedAsync();

            _logger.Info($"导入成员时地区状态: City={SelectedCity}, District={SelectedDistrict}, Town={SelectedTown}");

            FamilyMembers.Clear();
            foreach (var memberData in members)
            {
                var member = new FamilyMember
                {
                    Name = memberData.Name ?? string.Empty,
                    IdCard = memberData.IdCard ?? string.Empty,
                    RelationshipToHead = memberData.Relationship ?? string.Empty,
                    MemberCategory = memberData.IsHead ? MemberCategoryConstants.HOUSEHOLD_HEAD : MemberCategoryConstants.SHARED_LIVING,
                    Ethnicity = DefaultValuesConstants.ETHNICITY_KEY,
                    MaritalStatus = DefaultValuesConstants.MARITAL_STATUS_KEY,
                    EducationLevel = DefaultValuesConstants.EDUCATION_LEVEL_KEY,
                    PoliticalStatus = DefaultValuesConstants.POLITICAL_STATUS_KEY,
                    HealthStatus = DefaultValuesConstants.HEALTH_STATUS_KEY,
                    DiseaseCategory = DefaultValuesConstants.DISEASE_CATEGORY_KEY,
                    HomeProvince = DefaultValuesConstants.HOME_PROVINCE,
                    HomeCity = SelectedCity ?? string.Empty,
                    HomeDistrict = SelectedDistrict ?? string.Empty,
                    HomeTown = SelectedTown ?? string.Empty,
                    HomeVillage = SelectedVillage ?? string.Empty,
                    HomeAddress = Address ?? string.Empty,
                    HukouProvince = DefaultValuesConstants.HOME_PROVINCE,
                    HukouCity = SelectedCity ?? string.Empty,
                    HukouDistrict = SelectedDistrict ?? string.Empty,
                    HukouTown = SelectedTown ?? string.Empty,
                    HukouAddress = HukouAddress,
                    CreatedAt = DateTime.Now
                };
                member.SetRegionService(_regionService);
                await member.LoadInitialAddressOptionsAsync(SelectedCity, SelectedDistrict, SelectedTown);
                SyncMemberDictOptions(member);
                FamilyMembers.Add(member);
            }

            UpdateFamilySize();
            RefreshDerivedCollections();

            _logger.LogBusiness("导入家庭成员完成", ("Count", FamilyMembers.Count));
        }
        catch (Exception ex)
        {
            _logger.Error("导入家庭成员失败: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion

    public ApplicationFormViewModel(
        IServiceProvider serviceProvider,
        IApplicationService applicationService,
        IPersonSearchService personSearchService,
        IFamilyMemberService familyMemberService,
        IClassificationService classificationService,
        IIncomeCalculationService incomeCalculationService,
        IGuaranteeAmountService guaranteeAmountService,
        IGracePeriodService gracePeriodService,
        IRegionService regionService,
        IDictionaryService dictionaryService,
        IUserService userService,
        IOrganizationService organizationService,
        ILandContractService landContractService,
        ISubsidyDataService subsidyDataService,
        IAssetVerificationService assetVerificationService,
        IStandardConfigService standardConfigService,
        IEconomicDetailService economicDetailService,
        IImportedArchiveService importedArchiveService,
        ISupporterService supporterService,
        ICaregiverService caregiverService,
        IHouseholdSurveyService householdSurveyService,
        IChangeService changeService,
        ILoggerService logger)
    {
        _serviceProvider = serviceProvider;
        _applicationService = applicationService;
        _personSearchService = personSearchService;
        _familyMemberService = familyMemberService;
        _classificationService = classificationService;
        _incomeCalculationService = incomeCalculationService;
        _guaranteeAmountService = guaranteeAmountService;
        _gracePeriodService = gracePeriodService;
        _regionService = regionService;
        _dictionaryService = dictionaryService;
        _userService = userService;
        _organizationService = organizationService;
        _landContractService = landContractService;
        _subsidyDataService = subsidyDataService;
        _assetVerificationService = assetVerificationService;
        _standardConfigService = standardConfigService;
        _economicDetailService = economicDetailService;
        _importedArchiveService = importedArchiveService;
        _supporterService = supporterService;
        _caregiverService = caregiverService;
        _householdSurveyService = householdSurveyService;
        _changeService = changeService;
        _logger = logger;

        TotalSteps = 5;

        // 经济数据集合：增删项 → 重建订阅并重算；项内属性变化 → 防抖重算
        WireCollection(LaborIncomes, ScheduleRecalculate);
        WireCollection(BusinessIncomes, ScheduleRecalculate);
        WireCollection(PropertyIncomes, ScheduleRecalculate);
        WireCollection(TransferIncomes, ScheduleRecalculate);
        WireCollection(OtherIncomes, ScheduleRecalculate);
        WireCollection(RigidExpenditures, ScheduleRecalculate);
        WireCollection(Subsidies, ScheduleRecalculate);
        // CollectionView 动态高度：增删项时通知高度变化
        Subsidies.CollectionChanged += (_, _) => OnPropertyChanged(nameof(SubsidyListHeight));
        RigidExpenditures.CollectionChanged += (_, _) => OnPropertyChanged(nameof(RigidExpenditureListHeight));
        Supporters.CollectionChanged += (_, _) => OnPropertyChanged(nameof(SupporterListHeight));
        // 以下集合为纯 POCO（无项内通知），仅增删时触发防抖重算
        BreedingIncomes.CollectionChanged += (_, _) => { if (!_isLoadingData) ScheduleRecalculate(); };
        LandRegistrations.CollectionChanged += (_, _) => { if (!_isLoadingData) ScheduleRecalculate(); };
        WireCollection(Supporters, ScheduleRecalculate);
        WireCollection(LandConfirmationGroups, ScheduleLandRecalculate);

        FamilyMembers.CollectionChanged += (_, _) =>
        {
            if (!_isLoadingData && LandConfirmationGroups.Count > 0)
                InitializePersonsForAllGroups();
        };
    }

    #region 经济数据实时订阅（输入即刷新，防抖保性能）

    /// <summary>已订阅 PropertyChanged 的经济数据实体（防止重复订阅 / 卸载泄漏）</summary>
    private readonly HashSet<object> _subscribedEconomyItems = new();

    /// <summary>
    /// 赡养人关闭"有赡养能力"开关时暂存的月费（重开时恢复用）。
    /// 按 Supporter 对象引用为 key，生命周期随本表单实例（ViewModel 为 Transient）。
    /// </summary>
    private readonly Dictionary<Supporter, decimal> _supporterFeeBackup = new();

    private bool _recalcScheduled;
    private bool _landRecalcScheduled;

    /// <summary>
    /// 监听集合增删：重建子项订阅，并触发对应重算（传入重算子）
    /// </summary>
    private void WireCollection<T>(ObservableCollection<T> collection, Action recalculate) where T : INotifyPropertyChanged
    {
        collection.CollectionChanged += (_, _) =>
        {
            SyncSubscriptions(collection);
            if (_isLoadingData) return;
            recalculate();
        };
    }

    /// <summary>
    /// 差异收敛订阅：新增项订阅 PropertyChanged，已移除项退订（兼容 Clear/Reset）
    /// </summary>
    private void SyncSubscriptions<T>(ObservableCollection<T> collection) where T : INotifyPropertyChanged
    {
        var current = new HashSet<object>(collection.Cast<object>());
        foreach (var stale in _subscribedEconomyItems.Where(o => o is T && !current.Contains(o)).ToList())
        {
            ((INotifyPropertyChanged)stale).PropertyChanged -= OnEconomyItemPropertyChanged;
            _subscribedEconomyItems.Remove(stale);
        }
        foreach (var item in collection)
        {
            if (_subscribedEconomyItems.Add(item))
                item.PropertyChanged += OnEconomyItemPropertyChanged;
        }
    }

    /// <summary>
    /// 实体项内属性变化 → 触发重算（土地组走土地汇总，其余走收入重算）
    /// </summary>
    private void OnEconomyItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isLoadingData) return;
        // 赡养人"有赡养能力"开关：先联动月费清零/恢复，再走防抖重算
        if (sender is Supporter supporter && e.PropertyName == nameof(Supporter.IsSupportAbility))
            HandleSupporterAbilityToggled(supporter);
        if (sender is LandConfirmationGroup)
            ScheduleLandRecalculate();
        else
            ScheduleRecalculate();
    }

    /// <summary>
    /// 赡养人"有赡养能力"开关联动：
    /// - 关闭：暂存当前月费（>0 才暂存）并清零 → 年赡养费联动为 0（卡片清零），收入汇总排除该人；
    /// - 打开：若当前月费已清零且有备份则恢复（不覆盖用户重填值），收入汇总重新计入。
    /// 仅在用户切换时触发（加载赋值在 _isLoadingData=true 下被上方短路，历史数据不受影响）。
    /// </summary>
    private void HandleSupporterAbilityToggled(Supporter supporter)
    {
        if (supporter.IsSupportAbility)
        {
            if (supporter.MonthlySupportFee <= 0 && _supporterFeeBackup.TryGetValue(supporter, out var backup))
            {
                supporter.MonthlySupportFee = backup;
            }
            _supporterFeeBackup.Remove(supporter);
        }
        else
        {
            if (supporter.MonthlySupportFee > 0)
                _supporterFeeBackup[supporter] = supporter.MonthlySupportFee;
            supporter.MonthlySupportFee = 0;
        }
    }

    /// <summary>
    /// 防抖调度收入重算：150ms 内连续变更合并为一次（UI 线程执行，无积压）
    /// </summary>
    private void ScheduleRecalculate()
    {
        if (_recalcScheduled) return;
        var dispatcher = Microsoft.Maui.Controls.Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            _recalcScheduled = false;
            SafeRecalculateIncome();
            return;
        }
        _recalcScheduled = true;
        dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), () =>
        {
            _recalcScheduled = false;
            SafeRecalculateIncome();
        });
    }

    /// <summary>
    /// 防抖调度土地汇总重算（CalculateLandConfirmationArea 末尾自行调用 RecalculateIncome）
    /// </summary>
    private void ScheduleLandRecalculate()
    {
        if (_landRecalcScheduled) return;
        var dispatcher = Microsoft.Maui.Controls.Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            _landRecalcScheduled = false;
            SafeCalculateLandConfirmationArea();
            return;
        }
        _landRecalcScheduled = true;
        dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(150), () =>
        {
            _landRecalcScheduled = false;
            SafeCalculateLandConfirmationArea();
        });
    }

    /// <summary>延迟回调重算收入：异常记录且不崩溃（防调度回调内 RecalculateIncome 抛异常导致闪退）</summary>
    private void SafeRecalculateIncome()
    {
        try
        {
            RecalculateIncome();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "延迟收入重算失败");
        }
    }

    /// <summary>延迟回调土地汇总重算：异常记录且不崩溃</summary>
    private void SafeCalculateLandConfirmationArea()
    {
        try
        {
            CalculateLandConfirmationArea();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "延迟土地汇总重算失败");
        }
    }

    #endregion

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    #region 模式和步骤属性

    /// <summary>
    /// 模式描述
    /// </summary>
    public string ModeDescription => OperationMode switch
    {
        FormOperationMode.Create => "新建申请",
        FormOperationMode.Edit => "编辑申请",
        FormOperationMode.View => "查看申请",
        FormOperationMode.Completion => "数据补全",
        FormOperationMode.Review => "经济状况复核",
        _ => "新建申请"
    };

    /// <summary>
    /// 模式横幅是否可见（特殊模式：补全/复核/家庭修正/编辑家庭信息/成员变更显示，提醒用户当前处于何种操作模式）
    /// </summary>
    public bool IsModeBannerVisible => OperationMode is FormOperationMode.Completion or FormOperationMode.Review or FormOperationMode.ReviewWithFamilyCorrection or FormOperationMode.EditFamilyInfo or FormOperationMode.MemberChange;

    /// <summary>
    /// 模式横幅提示文案（含图标与说明）
    /// </summary>
    public string ModeBannerText => OperationMode switch
    {
        FormOperationMode.Completion => "数据补全模式\n补全建档缺失信息，保存不改变认定结果",
        FormOperationMode.Review => "经济状况复核模式\n户主/成员已锁定，可调整经济并重新认定",
        FormOperationMode.ReviewWithFamilyCorrection => "家庭信息修正模式\n可修正家庭信息并重新认定（户主姓名/身份证锁定）",
        FormOperationMode.EditFamilyInfo => "编辑家庭信息模式\n可编辑家庭信息（户主姓名/身份证锁定）",
        FormOperationMode.MemberChange => "家庭成员变更模式\n可增减家庭成员并调整经济，重新认定后旧档案停用、生成新档案",
        _ => string.Empty
    };

    /// <summary>
    /// 步骤1可见性
    /// </summary>
    public bool IsStep1Visible => CurrentStep == 1;

    /// <summary>
    /// 步骤2可见性
    /// </summary>
    public bool IsStep2Visible => CurrentStep == 2;

    /// <summary>
    /// 步骤3可见性
    /// </summary>
    public bool IsStep3Visible => CurrentStep == 3;

    /// <summary>
    /// 步骤4可见性
    /// </summary>
    public bool IsStep4Visible => CurrentStep == 4;

    /// <summary>
    /// 步骤5可见性
    /// </summary>
    public bool IsStep5Visible => CurrentStep == 5;

    /// <summary>
    /// 步骤1颜色
    /// </summary>
    public Color Step1Color => CurrentStep >= 1 ? Colors.Blue : Colors.Gray;

    /// <summary>
    /// 步骤2颜色
    /// </summary>
    public Color Step2Color => CurrentStep >= 2 ? Colors.Blue : Colors.Gray;

    /// <summary>
    /// 步骤3颜色
    /// </summary>
    public Color Step3Color => CurrentStep >= 3 ? Colors.Blue : Colors.Gray;

    /// <summary>
    /// 步骤4颜色
    /// </summary>
    public Color Step4Color => CurrentStep >= 4 ? Colors.Blue : Colors.Gray;

    /// <summary>
    /// 步骤5颜色
    /// </summary>
    public Color Step5Color => CurrentStep >= 5 ? Colors.Blue : Colors.Gray;

    #endregion

    #region 卡片式步骤指示器样式属性

    private static readonly Color PrimaryColor = Color.FromArgb("#3B82F6");
    private static readonly Color PrimaryLightColor = Color.FromArgb("#EBF5FF");
    private static readonly Color Gray100Color = Color.FromArgb("#F1F5F9");
    private static readonly Color Gray300Color = Color.FromArgb("#D1D5DB");
    private static readonly Color Gray500Color = Color.FromArgb("#6B7280");

    private static readonly SolidColorBrush PrimaryBrush = new(PrimaryColor);
    private static readonly SolidColorBrush PrimaryLightBrush = new(PrimaryLightColor);
    private static readonly SolidColorBrush Gray100Brush = new(Gray100Color);
    private static readonly SolidColorBrush Gray300Brush = new(Gray300Color);

    /// <summary>
    /// 获取步骤卡片背景色（通用方法）
    /// </summary>
    public Brush GetStepCardBgColor(int step) => CurrentStep == step ? PrimaryLightBrush : (CurrentStep > step ? PrimaryLightBrush : Gray100Brush);

    /// <summary>
    /// 获取步骤卡片边框色（通用方法）
    /// </summary>
    public Brush GetStepCardBorderColor(int step) => CurrentStep == step ? PrimaryBrush : (CurrentStep > step ? PrimaryBrush : Gray300Brush);

    /// <summary>
    /// 获取步骤文字颜色（通用方法）
    /// </summary>
    public Color GetStepTextColor(int step) => CurrentStep >= step ? PrimaryColor : Gray500Color;

    /// <summary>
    /// 获取连接线颜色（通用方法）
    /// </summary>
    public Color GetLineColor(int step) => CurrentStep > step ? PrimaryColor : Gray300Color;

    // 保持向后兼容的属性（委托给通用方法）
    public Brush Step1CardBgColor => GetStepCardBgColor(1);
    public Brush Step1CardBorderColor => GetStepCardBorderColor(1);
    public Color Step1TextColor => GetStepTextColor(1);
    public Brush Step2CardBgColor => GetStepCardBgColor(2);
    public Brush Step2CardBorderColor => GetStepCardBorderColor(2);
    public Color Step2TextColor => GetStepTextColor(2);
    public Brush Step3CardBgColor => GetStepCardBgColor(3);
    public Brush Step3CardBorderColor => GetStepCardBorderColor(3);
    public Color Step3TextColor => GetStepTextColor(3);
    public Brush Step4CardBgColor => GetStepCardBgColor(4);
    public Brush Step4CardBorderColor => GetStepCardBorderColor(4);
    public Color Step4TextColor => GetStepTextColor(4);
    public Brush Step5CardBgColor => GetStepCardBgColor(5);
    public Brush Step5CardBorderColor => GetStepCardBorderColor(5);
    public Color Step5TextColor => GetStepTextColor(5);
    public Color Line1Color => GetLineColor(1);
    public Color Line2Color => GetLineColor(2);
    public Color Line3Color => GetLineColor(3);
    public Color Line4Color => GetLineColor(4);

    /// <summary>
    /// 步骤跳转命令
    /// </summary>
    [RelayCommand]
    private void NavigateToStep(string stepNumber)
    {
        if (int.TryParse(stepNumber, out var step) && step >= 1 && step <= TotalSteps)
        {
            _logger.LogBusiness("步骤跳转", ("目标步骤", step));
            CurrentStep = step;
        }
    }

    /// <summary>
    /// 重写步骤变化通知方法
    /// </summary>
    protected override void OnStepChanged()
    {
        base.OnStepChanged();
        
        OnPropertyChanged(nameof(IsStep1Visible));
        OnPropertyChanged(nameof(IsStep2Visible));
        OnPropertyChanged(nameof(IsStep3Visible));
        OnPropertyChanged(nameof(IsStep4Visible));
        OnPropertyChanged(nameof(IsStep5Visible));
        OnPropertyChanged(nameof(IsNotFirstStep));
        OnPropertyChanged(nameof(IsNotLastStep));
        OnPropertyChanged(nameof(IsSubmitVisible));
        
        OnPropertyChanged(nameof(Step1CardBgColor));
        OnPropertyChanged(nameof(Step1CardBorderColor));
        OnPropertyChanged(nameof(Step1TextColor));
        OnPropertyChanged(nameof(Step2CardBgColor));
        OnPropertyChanged(nameof(Step2CardBorderColor));
        OnPropertyChanged(nameof(Step2TextColor));
        OnPropertyChanged(nameof(Step3CardBgColor));
        OnPropertyChanged(nameof(Step3CardBorderColor));
        OnPropertyChanged(nameof(Step3TextColor));
        OnPropertyChanged(nameof(Step4CardBgColor));
        OnPropertyChanged(nameof(Step4CardBorderColor));
        OnPropertyChanged(nameof(Step4TextColor));
        OnPropertyChanged(nameof(Step5CardBgColor));
        OnPropertyChanged(nameof(Step5CardBorderColor));
        OnPropertyChanged(nameof(Step5TextColor));
        
        OnPropertyChanged(nameof(Line1Color));
        OnPropertyChanged(nameof(Line2Color));
        OnPropertyChanged(nameof(Line3Color));
        OnPropertyChanged(nameof(Line4Color));
    }

    #endregion

    /// <summary>
    /// 初始化（加载字典数据和配置）
    /// </summary>
    public async Task InitializeAsync()
    {
        if (IsInitialized) return;

        try
        {
            IsBusy = true;
            LoadingMessage = "正在加载基础数据...";

            // 加载字典数据
            await LoadDictionariesAsync();

            // 加载映射数据
            await LoadDisabilityMappingsAsync();
            await LoadDiseaseMappingsAsync();
            LoadSpecialKeys();

            // 设置默认值
            SetDefaultValues();

            // 默认选中"无任何疾病"
            if (DiseaseCategoryOptions.Any(o => o.Key == DefaultValuesConstants.DISEASE_CATEGORY_KEY))
                SelectedDiseaseCategoryObj = DiseaseCategoryOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.DISEASE_CATEGORY_KEY);

            // 加载省市区数据
            await LoadRegionDataAsync();

            // 加载默认地区（基于当前用户单位）
            await LoadDefaultRegionAsync();

            // 加载调查员所属机构
            await LoadSurveyorOrganizationAsync();

            IsInitialized = true;
            _logger.LogBusiness("初始化完成");

            // 同步字段值到 FormFieldDescriptor 并触发验证
            ApplicantNameField.Value = ApplicantName;
            ApplicantIdCardField.Value = ApplicantIdCard;
            PhoneField.Value = ApplicantPhone;

            // 字典数据加载完成后刷新 Picker 绑定
            ForceRefreshPickerBindings();

            // 刷新收入明细人员选项
            RefreshIncomeMemberOptions();
        }
        catch (Exception ex)
        {
            _logger.Error($"初始化失败: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 加载字典数据
    /// </summary>
    private async Task LoadDictionariesAsync()
    {
        await LoadDictionaryAsync("Gender", GenderOptions);
        await LoadDictionaryAsync("Ethnicities", EthnicityOptions);
        await LoadDictionaryAsync("MaritalStatuses", MaritalStatusOptions);
        await LoadDictionaryAsync("EducationLevels", EducationLevelOptions);
        await LoadDictionaryAsync("PoliticalStatuses", PoliticalStatusOptions);
        await LoadDictionaryAsync("SupportModes", SupportModeOptions);
        await LoadDictionaryAsync("ApplicationReasons", ApplicationReasonOptions);
        await LoadDictionaryAsync("DiseaseCategories", DiseaseCategoryOptions);
        await LoadDictionaryAsync("DisabilityTypes", DisabilityTypeOptions);
        await LoadDictionaryAsync("DisabilityLevels", DisabilityLevelOptions);
        await LoadDictionaryAsync("HealthStatuses", HealthStatusOptions);
        await LoadDictionaryAsync("EmploymentStatuses", EmploymentStatusOptions);
        await LoadDictionaryAsync("IncomeSources", IncomeSourceOptions);
    }

    /// <summary>
    /// 加载字典数据（通用方法）
    /// </summary>
    private async Task LoadDictionaryAsync(string category, ObservableCollection<DictItemOption> target)
    {
        try
        {
            var result = await _dictionaryService.GetItemsByCategoryAsync(category);
            if (result.IsSuccess)
            {
                target.Clear();
                foreach (var item in result.Value)
                {
                    target.Add(new DictItemOption { Key = item.ItemKey, Display = item.ItemValue });
                }
            }
            else
            {
                _logger.Warn($"加载字典数据失败 [{category}]: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"加载字典数据失败 [{category}]: {ex.Message}");
        }
    }

    /// <summary>
    /// 加载残疾类型/等级映射（从字典数据，不硬编码）
    /// </summary>
    private async Task LoadDisabilityMappingsAsync()
    {
        try
        {
            var typesResult = await _dictionaryService.GetItemsByCategoryAsync(DisabilityConstants.DISABILITY_TYPES_CATEGORY);
            if (typesResult.IsSuccess)
            {
                _disabilityTypeKeyMap.Clear();
                _disabilityTypeDisplayMap.Clear();
                char code = '1';
                foreach (var item in typesResult.Value.OrderBy(x => x.SortOrder))
                {
                    _disabilityTypeKeyMap[code++] = item.ItemKey;
                    _disabilityTypeDisplayMap[item.ItemKey] = item.ItemValue;
                }
            }

            var levelsResult = await _dictionaryService.GetItemsByCategoryAsync(DisabilityConstants.DISABILITY_LEVELS_CATEGORY);
            if (levelsResult.IsSuccess)
            {
                _disabilityLevelKeyMap.Clear();
                _disabilityLevelDisplayMap.Clear();
                char code = '1';
                foreach (var item in levelsResult.Value.OrderBy(x => x.SortOrder))
                {
                    var key = item.ItemKey;
                    _disabilityLevelKeyMap[code++] = key;
                    _disabilityLevelDisplayMap[key] = item.ItemValue;
                }
            }

            _logger.Info("残疾映射加载完成");
        }
        catch (Exception ex)
        {
            _logger.Error($"加载残疾映射失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 加载疾病分类映射（从 DiseaseNames 字典）
    /// </summary>
    private async Task LoadDiseaseMappingsAsync()
    {
        try
        {
            var result = await _dictionaryService.GetItemsByCategoryAsync(DiseaseConstants.DISEASE_NAMES_CATEGORY);
            if (result.IsSuccess)
            {
                _diseaseCategoryMap.Clear();
                foreach (var item in result.Value)
                {
                    // 使用 Description 作为键（疾病分类名称）
                    if (!_diseaseCategoryMap.TryGetValue(item.Description, out var list))
                    {
                        list = new List<DiseaseItem>();
                        _diseaseCategoryMap[item.Description] = list;
                    }
                    list.Add(new DiseaseItem
                    {
                        Key = item.ItemKey,
                        Value = item.ItemValue,
                        SortOrder = item.SortOrder
                    });
                }
            }
            _logger.Info("疾病映射加载完成");
        }
        catch (Exception ex)
        {
            _logger.Error($"加载疾病映射失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 加载特殊项 key
    /// </summary>
    private void LoadSpecialKeys()
    {
        try
        {
            if (DiseaseCategoryOptions.Any(o => o.Key == DefaultValuesConstants.DISEASE_CATEGORY_KEY))
                _noDiseaseKey = DefaultValuesConstants.DISEASE_CATEGORY_KEY;

            _severeLevelKeys = _disabilityLevelDisplayMap
                .Where(x => x.Value.StartsWith("一级") || x.Value.StartsWith("二级"))
                .Select(x => x.Key)
                .ToList();

            _logger.Info("特殊项 key 加载完成");
        }
        catch (Exception ex)
        {
            _logger.Error($"加载特殊项 key 失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 设置默认值（从字典数据获取，不硬编码）
    /// </summary>
    private void SetDefaultValues()
    {
        try
        {
            if (SelectedEthnicity == null)
                SelectedEthnicity = EthnicityOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.ETHNICITY_KEY);

            if (SelectedMaritalStatus == null)
                SelectedMaritalStatus = MaritalStatusOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.MARITAL_STATUS_KEY);

            if (SelectedEducationLevel == null)
                SelectedEducationLevel = EducationLevelOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.EDUCATION_LEVEL_KEY);

            if (SelectedPoliticalStatus == null)
                SelectedPoliticalStatus = PoliticalStatusOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.POLITICAL_STATUS_KEY);

            if (SelectedApplicationReasonObj == null)
                SelectedApplicationReasonObj = ApplicationReasonOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.APPLICATION_REASON_KEY);

            if (SelectedHealthStatusObj == null)
                SelectedHealthStatusObj = HealthStatusOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.HEALTH_STATUS_KEY);

            if (SelectedDiseaseCategoryObj == null)
                SelectedDiseaseCategoryObj = DiseaseCategoryOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.DISEASE_CATEGORY_KEY);

            if (SelectedSupportModeObj == null)
                SelectedSupportModeObj = SupportModeOptions.FirstOrDefault(o => o.Key == ClassificationConstants.SupportMode.SCATTERED);

            if (HukouType == null)
                HukouType = HukouTypeOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.HUKOU_TYPE_KEY);

            SurveyDate = DateTime.Today;
            SurveyorName = App.CurrentUserFullName;

            _logger.Info("默认值设置完成");
        }
        catch (Exception ex)
        {
            _logger.Error($"设置默认值失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 加载省市区数据
    /// </summary>
    private async Task LoadRegionDataAsync()
    {
        try
        {
            ProvinceOptions.Clear();
            ProvinceOptions.Add(DefaultValuesConstants.HOME_PROVINCE);
            if (string.IsNullOrEmpty(SelectedProvince))
                SelectedProvince = DefaultValuesConstants.HOME_PROVINCE;

            var citiesResult = await _regionService.GetCitiesAsync();
            _logger.Info($"城市数据加载结果: IsSuccess={citiesResult.IsSuccess}, Count={citiesResult.Value?.Count ?? 0}");
            
            if (citiesResult.IsSuccess && citiesResult.Value != null)
            {
                CityOptions.Clear();
                foreach (var city in citiesResult.Value)
                {
                    CityOptions.Add(city.CityName);
                }
                _logger.Info($"CityOptions 加载完成: {CityOptions.Count} 个城市");
            }
            else
            {
                _logger.Warn($"城市数据加载失败: {citiesResult.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"加载省市区数据失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 加载默认地区（基于当前用户单位）
    /// </summary>
    private async Task LoadDefaultRegionAsync()
    {
        try
        {
            var orgId = App.CurrentUserOrganizationId;
            if (!orgId.HasValue || orgId.Value <= 0)
            {
                _logger.Warn("当前用户未配置组织单位，跳过默认地区加载");
                return;
            }

            var orgResult = await _organizationService.GetByIdAsync(orgId.Value);
            if (!orgResult.IsSuccess || orgResult.Value == null)
            {
                _logger.Warn($"组织单位查询失败: OrgId={orgId.Value}");
                return;
            }

            var org = orgResult.Value;
            _logger.Info($"组织机构数据: CityName={org.CityName}, CountyName={org.CountyName}, TownName={org.TownName}, VillageName={org.VillageName}");

            _isLoadingDefaults = true;

            // 1. 设置城市
            if (!string.IsNullOrEmpty(org.CityName))
            {
                SelectedCity = org.CityName;
                await LoadDistrictsAsync(org.CityName);
            }
            else if (CityOptions.Count > 0)
            {
                // 如果组织机构没有城市信息，使用第一个可用城市
                SelectedCity = CityOptions[0];
                await LoadDistrictsAsync(SelectedCity);
            }

            // 2. 设置区县
            if (!string.IsNullOrEmpty(org.CountyName))
            {
                SelectedDistrict = org.CountyName;
                await LoadTownsForDistrictAsync(org.CountyName);
            }
            else if (DistrictOptions.Count > 0)
            {
                // 如果组织机构没有区县信息，使用第一个可用区县
                SelectedDistrict = DistrictOptions[0];
                await LoadTownsForDistrictAsync(SelectedDistrict);
            }

            // 3. 设置乡镇
            if (!string.IsNullOrEmpty(org.TownName))
            {
                SelectedTown = org.TownName;
                await LoadVillagesForTownAsync(org.TownName);
            }
            else if (TownOptions.Count > 0)
            {
                // 如果组织机构没有乡镇信息，使用第一个可用乡镇
                SelectedTown = TownOptions[0];
                await LoadVillagesForTownAsync(SelectedTown);
            }

            // 4. 设置村庄
            if (!string.IsNullOrEmpty(org.VillageName))
            {
                SelectedVillage = org.VillageName;
            }
            else if (VillageOptions.Count > 0)
            {
                // 如果组织机构没有村庄信息，使用第一个可用村庄
                SelectedVillage = VillageOptions[0];
            }

            // 5. 同步设置户籍地址（与申请人地址一致）
            HukouProvince = DefaultValuesConstants.HOME_PROVINCE;
            HukouCity = SelectedCity;
            HukouDistrict = SelectedDistrict;
            HukouTown = SelectedTown;

            // 6. 更新户籍地址
            UpdateHukouAddress();

            _isLoadingDefaults = false;

            _logger.Info($"默认地区加载完成: {SelectedCity} {SelectedDistrict} {SelectedTown} {SelectedVillage}");
        }
        catch (Exception ex)
        {
            _isLoadingDefaults = false;
            _logger.Error($"加载默认地区失败: {ex.Message}");
        }
    }

    private async Task LoadSurveyorOrganizationAsync()
    {
        try
        {
            var orgId = App.CurrentUserOrganizationId;
            if (!orgId.HasValue) return;

            var orgResult = await _organizationService.GetByIdAsync(orgId.Value);
            if (orgResult.IsSuccess && orgResult.Value != null)
                SurveyorOrganization = orgResult.Value.Name ?? "";
        }
        catch (Exception ex)
        {
            _logger.Error($"加载调查员机构失败: {ex.Message}");
        }
    }

    public async Task LoadApplicationAsync(long applicationId)
    {
        _applicationId = applicationId;
        // 保留调用方设置的 OperationMode（Edit/View/Completion/Review），
        // 仅防御性兜底：Create 误入加载路径时改为 Edit
        if (OperationMode == FormOperationMode.Create)
            OperationMode = FormOperationMode.Edit;
        _isLoadingData = true;

        await ExecuteAsync(async ct =>
        {
            _logger.LogBusiness("加载申请详情", ("ApplicationId", applicationId));

            var result = await _applicationService.GetByIdAsync(applicationId, ct);

            if (result.IsSuccess && result.Value != null)
            {
                var app = result.Value;
                // 向导入口固化复核前快照：Step5 判定与 SaveEconomicDetailsAsync 随后会覆写本档，
                // 此后读库不再是复核前状态（真旧值口径与 _loadedClassification 一致）
                _entryApplicationSnapshot = app;
                // 记录乐观并发令牌（加载时的 updated_at），保存时随实体传回做并发守卫
                _loadedUpdatedAt = app.UpdatedAt == default ? null : app.UpdatedAt;
                // 记录库中真实步骤：变更流程模式保存时回写，防止 UI 归位步数冲掉 current_step
                _loadedCurrentStep = app.CurrentStep;
                // 覆写前真旧值：Step5 分类判定会覆写 classification_result/total_guarantee_amount，
                // 变更流程的新旧对比必须用此处捕获值，否则恒判"无变化"
                _loadedClassification = app.ClassificationResult;
                _loadedTotalGuaranteeAmount = app.TotalGuaranteeAmount;
                ApplicantName = app.ApplicantName;
                ApplicantIdCard = app.ApplicantIdCard;
                Gender = app.Gender ?? string.Empty;
                ApplicantPhone = app.ApplicantPhone ?? string.Empty;
                Address = app.Address ?? string.Empty;
                HukouType = HukouTypeOptions.FirstOrDefault(o => o.Key == (string.IsNullOrEmpty(app.HukouType) ? DefaultValuesConstants.HUKOU_TYPE_KEY : app.HukouType));
                FamilySize = app.FamilySize;
                SelectedApplicationReasonObj = ApplicationReasonOptions.FirstOrDefault(o => o.Key == (string.IsNullOrEmpty(app.ApplicationReason) ? DefaultValuesConstants.APPLICATION_REASON_KEY : app.ApplicationReason));
                ApplicationReasonDetail = app.ApplicationReasonDetail ?? string.Empty;

                // Step1 补充字段
                HukouAddress = app.HukouAddress ?? string.Empty;
                BankName = app.BankName ?? string.Empty;
                BankAccount = app.BankAccount ?? string.Empty;

                // 级联加载地址选项（全部在 _isLoadingDefaults 保护内，防止 Setter 级联覆盖）
                // 注意：InitializeAsync → LoadDefaultRegionAsync 已从 org 加载了默认地址
                // 仅当 DB 有非空值时才覆盖默认值；空值不覆盖，保留 org 默认
                _isLoadingDefaults = true;
                try
                {
                    if (!string.IsNullOrEmpty(app.Province))
                        SelectedProvince = app.Province;

                    if (!string.IsNullOrEmpty(app.City))
                    {
                        SelectedCity = app.City;
                        await LoadDistrictsAsync(SelectedCity);
                        if (!string.IsNullOrEmpty(app.District))
                        {
                            SelectedDistrict = app.District;
                            await LoadTownsForDistrictAsync(SelectedDistrict);
                            if (!string.IsNullOrEmpty(app.Town))
                            {
                                SelectedTown = app.Town;
                                await LoadVillagesForTownAsync(SelectedTown);
                                if (!string.IsNullOrEmpty(app.Community))
                                    SelectedVillage = app.Community;
                                else if (VillageOptions.Count > 0)
                                    SelectedVillage = VillageOptions[0];
                            }
                        }
                    }
                }
                finally
                {
                    _isLoadingDefaults = false;
                }

                SelectedEthnicity = EthnicityOptions.FirstOrDefault(o => o.Key == (string.IsNullOrEmpty(app.Ethnicity) ? DefaultValuesConstants.ETHNICITY_KEY : app.Ethnicity));
                SelectedMaritalStatus = MaritalStatusOptions.FirstOrDefault(o => o.Key == (string.IsNullOrEmpty(app.MaritalStatus) ? DefaultValuesConstants.MARITAL_STATUS_KEY : app.MaritalStatus));
                SelectedEducationLevel = EducationLevelOptions.FirstOrDefault(o => o.Key == (string.IsNullOrEmpty(app.EducationLevel) ? DefaultValuesConstants.EDUCATION_LEVEL_KEY : app.EducationLevel));
                SelectedPoliticalStatus = PoliticalStatusOptions.FirstOrDefault(o => o.Key == (string.IsNullOrEmpty(app.PoliticalStatus) ? DefaultValuesConstants.POLITICAL_STATUS_KEY : app.PoliticalStatus));
                DisabilityCardNo = app.DisabilityCardNo ?? string.Empty;
                SelectedDiseaseCategoryObj = DiseaseCategoryOptions.FirstOrDefault(o => o.Key == (string.IsNullOrEmpty(app.DiseaseName) ? DefaultValuesConstants.DISEASE_CATEGORY_KEY : app.DiseaseName));
                // 加载二级疾病选项并恢复选中状态
                if (!string.IsNullOrEmpty(app.DiseaseName))
                {
                    LoadSecondaryDiseases(app.DiseaseName);
                    if (!string.IsNullOrEmpty(app.SecondaryDiseaseName))
                    {
                        SelectedDiseaseNameObj = DiseaseNameOptions.FirstOrDefault(o => o.Key == app.SecondaryDiseaseName);
                    }
                }
                SelectedDisabilityTypeObj = DisabilityTypeOptions.FirstOrDefault(o => o.Key == (string.IsNullOrEmpty(app.DisabilityType) ? "" : app.DisabilityType));
                SelectedDisabilityLevelObj = DisabilityLevelOptions.FirstOrDefault(o => o.Key == (string.IsNullOrEmpty(app.DisabilityLevel) ? "" : app.DisabilityLevel));
                // 恢复残疾字段 key（UpdateHealthStatus 需要这些值才能正确推导）
                if (!string.IsNullOrEmpty(app.DisabilityType))
                    SelectedDisabilityTypeKey = app.DisabilityType;
                if (!string.IsNullOrEmpty(app.DisabilityLevel))
                    SelectedDisabilityLevelKey = app.DisabilityLevel;
                SelectedHealthStatusObj = HealthStatusOptions.FirstOrDefault(o => o.Key == (string.IsNullOrEmpty(app.HealthStatus) ? DefaultValuesConstants.HEALTH_STATUS_KEY : app.HealthStatus));
                // 健康状况是(疾病分类,残疾等级,年龄)的派生字段——在恢复所有依赖字段后重新推导，覆盖可能过时的 DB 值
                UpdateHealthStatus();

                // 恢复疾病编码 / 是否重病 / 二级疾病文本（重病勾选联动在 OnIsSevereDiseaseChanged 中生效）
                DiseaseCode = app.DiseaseCode ?? string.Empty;
                IsSevereDisease = app.IsSevereDisease;
                DiseaseNameText = app.SecondaryDiseaseName ?? string.Empty;

                // ── 第4步：验证映射结果，输出诊断日志 ──
                _logger.LogBusiness("Picker映射诊断",
                    ("HukouType", HukouType?.Key ?? "NULL"),
                    ("Gender", Gender ?? "NULL"),
                    ("Ethnicity", SelectedEthnicity?.Key ?? "NULL"),
                    ("MaritalStatus", SelectedMaritalStatus?.Key ?? "NULL"),
                    ("EducationLevel", SelectedEducationLevel?.Key ?? "NULL"),
                    ("PoliticalStatus", SelectedPoliticalStatus?.Key ?? "NULL"),
                    ("HealthStatus", SelectedHealthStatusObj?.Key ?? "NULL"),
                    ("DiseaseCategory", SelectedDiseaseCategoryObj?.Key ?? "NULL"),
                    ("DiseaseName", SelectedDiseaseNameObj?.Key ?? "NULL"),
                    ("DisabilityType", SelectedDisabilityTypeObj?.Key ?? "NULL"),
                    ("DisabilityLevel", SelectedDisabilityLevelObj?.Key ?? "NULL"),
                    ("ApplicationReason", SelectedApplicationReasonObj?.Key ?? "NULL"),
                    ("Province", SelectedProvince ?? "NULL"),
                    ("City", SelectedCity ?? "NULL"),
                    ("District", SelectedDistrict ?? "NULL"),
                    ("Town", SelectedTown ?? "NULL"),
                    ("Village", SelectedVillage ?? "NULL"),
                    ("Address", Address ?? "NULL"),
                    ("HukouAddress", HukouAddress ?? "NULL"),
                    ("DisabilityCardNo", DisabilityCardNo ?? "NULL"),
                    ("ApplicantName", ApplicantName ?? "NULL"),
                    ("ApplicantPhone", ApplicantPhone ?? "NULL"),
                    ("DB_Province", app.Province ?? "NULL"),
                    ("DB_City", app.City ?? "NULL"),
                    ("DB_District", app.District ?? "NULL"),
                    ("DB_Town", app.Town ?? "NULL"),
                    ("DB_Community", app.Community ?? "NULL"),
                    ("DB_Address", app.Address ?? "NULL"),
                    ("DB_HukouAddress", app.HukouAddress ?? "NULL"),
                    ("DB_DisabilityCardNo", app.DisabilityCardNo ?? "NULL"),
                    ("DB_DiseaseName", app.DiseaseName ?? "NULL"));

                // 经济信息
                WorkIncomeTotal = app.WorkIncomeTotal;
                BusinessIncomeTotal = app.BusinessIncomeTotal;
                PropertyIncomeTotal = app.PropertyIncomeTotal;
                TransferIncomeTotal = app.TransferIncomeTotal;
                OtherIncomeTotal = app.OtherIncomeTotal;
                AlimonyIncome = app.AlimonyIncome;
                TotalFamilyIncome = app.TotalFamilyIncome;
                PerCapitaIncome = app.PerCapitaIncome;
                TotalAnnualIncome = app.TotalAnnualIncome;
                PerCapitaAnnualIncome = app.PerCapitaAnnualIncome;
                RigidExpenditure = app.RigidExpenditure;

                // 土地信息
                FamilyLandArea = app.FamilyLandArea;
                SelfFarmedLandArea = app.SelfFarmedLandArea;
                SubleasedLandArea = app.SubleasedLandArea;
                ContractedLandArea = app.ContractedLandArea;
                LandIncomeTotal = app.LandIncomeTotal;
                SubsidyTotal = app.SubsidyTotal;

                // 分类结果
                ClassificationResult = app.ClassificationResult;
                ClassificationDescription = ClassificationConstants.ConvertFromCode(app.ClassificationResult ?? "");
                GuaranteeAmount = app.HouseholdMonthlyGuaranteeAmount;
                ClassifiedSubsidyAmount = app.ClassifiedSubsidyAmount;
                ClassifiedSubsidyType = app.ClassifiedSubsidyType ?? string.Empty;
                CaregiverSubsidyAmount = app.CaregiverSubsidyAmount;
                TotalGuaranteeAmount = app.TotalGuaranteeAmount;
                IsEligible = app.IsEligible;

                // 渐退期：主表列已迁移到独立表 nc_biz_grace_periods
                var graceRes = await _gracePeriodService.GetActiveAsync(applicationId, ct);
                if (graceRes.IsSuccess && graceRes.Value != null)
                {
                    IsInGracePeriod = true;
                    GracePeriodMonths = graceRes.Value.GracePeriodMonths ?? 0;
                    GracePeriodStartDate = graceRes.Value.StartDate;
                    GracePeriodEndDate = graceRes.Value.EndDate;
                    OriginalClassificationResult = graceRes.Value.OriginalClassification ?? string.Empty;
                    OriginalGuaranteeAmount = graceRes.Value.OriginalGuaranteeAmount;
                    GraceGrantAmount = graceRes.Value.GraceGrantAmount;
                    _gracePeriodEvaluated = true;
                }
                else
                {
                    IsInGracePeriod = false;
                    GracePeriodMonths = 0;
                    GracePeriodStartDate = null;
                    GracePeriodEndDate = null;
                    OriginalClassificationResult = string.Empty;
                    OriginalGuaranteeAmount = 0;
                    GraceGrantAmount = null;
                    _gracePeriodEvaluated = true;
                }

                // 变更链新建档案（户主死亡停旧建新等）：取上游档案原分类，
                // 供渐退期判定"低保→低收入"识别渐变前原分类（主表列已删，仅内存承载）
                _originalClassificationContext = null;
                _originalGuaranteeContext = null;
                if (app.OriginalApplicationId > 0)
                {
                    var oldAppRes = await _applicationService.GetByIdAsync(app.OriginalApplicationId, ct);
                    if (oldAppRes.IsSuccess && oldAppRes.Value != null)
                    {
                        _originalClassificationContext = oldAppRes.Value.ClassificationResult;
                        _originalGuaranteeContext = oldAppRes.Value.TotalGuaranteeAmount;
                    }
                }

                // 照料
                CaregiverType = app.CaregiverType ?? CaregiverTypeConstants.NONE;
                DestituteSupportType = app.DestituteSupportType ?? string.Empty;
                // 同步回填供养方式 Picker（预选默认值会造成"已选择"假象，须按数据匹配回显）
                SelectedSupportModeObj = SupportModeOptions.FirstOrDefault(o => o.Key == DestituteSupportType);

                // 单人保标记
                IsSingleRescueApplication = app.IsSingleRescue;
                _originalStatus = app.Status ?? ApplicationStatusCodes.DRAFT;
                ApplicationStatus = _originalStatus;
                OnPropertyChanged(nameof(IsEconomyEditable));

                // 表单不编辑、但保存时必须原样回写的主表字段（否则被实体默认值清零）
                _loadedSupportMode = app.SupportMode ?? string.Empty;
                _loadedSupportInstitutionId = app.SupportInstitutionId;
                _loadedConfirmedFamilySize = app.ConfirmedFamilySize;
                _loadedPersonCategoryProtectionTotalAmount = app.PersonCategoryProtectionTotalAmount;
                _loadedIsSpecialApproval = app.IsSpecialApproval;
                _loadedSpecialApprovalId = app.SpecialApprovalId;

                // 单人保申请自动设置分类已完成（分类结果已锁定）
                if (IsSingleRescueApplication && !string.IsNullOrEmpty(ClassificationResult))
                {
                    IsClassificationDone = true;
                }

                // 补全模式：建档时的认定结果作为只读事实锁定，不重新判定
                if (IsCompletionMode && !string.IsNullOrEmpty(ClassificationResult))
                {
                    IsClassificationDone = true;
                    OnPropertyChanged(nameof(IsClassificationLocked));
                }

                // 通知补全/复核模式相关属性刷新（OperationMode 变化后依赖属性需手动通知）
                OnPropertyChanged(nameof(IsCompletionMode));
                OnPropertyChanged(nameof(IsReviewMode));
                OnPropertyChanged(nameof(IsViewMode));
                OnPropertyChanged(nameof(IsEditable));
                OnPropertyChanged(nameof(IsSavable));
                OnPropertyChanged(nameof(IsNotCompletionMode));
                OnPropertyChanged(nameof(IsSaveDraftVisible));
                OnPropertyChanged(nameof(IsSubmitVisible));
                OnPropertyChanged(nameof(IsStep1Editable));
                OnPropertyChanged(nameof(IsStep2Editable));
                OnPropertyChanged(nameof(IsEconomyEditable));
                OnPropertyChanged(nameof(IsClassificationLocked));
                OnPropertyChanged(nameof(IsModeBannerVisible));
                OnPropertyChanged(nameof(ModeBannerText));
                OnPropertyChanged(nameof(IsFamilyCorrectionMode));
                OnPropertyChanged(nameof(IsEditFamilyInfoMode));
                OnPropertyChanged(nameof(IsMemberChangeMode));
                OnPropertyChanged(nameof(IsApplicantIdentityLocked));

                // 恢复当前步骤：补全模式 UI 归位到第5步（展示认定结果、提交按钮可见），
                // 复核模式 UI 归位到第3步（从经济步骤开始）；
                // 成员变更模式 UI 归位到第2步（从家庭成员开始）；
                // 家庭修正/编辑家庭信息模式 UI 归位到第1步（从户主信息开始）；数据库 current_step 保持原值由保存时写回
                CurrentStep = IsCompletionMode ? 5
                    : IsReviewMode ? 3
                    : IsMemberChangeMode ? 2
                    : (IsFamilyCorrectionMode || IsEditFamilyInfoMode) ? 1
                    : (app.CurrentStep > 0 ? app.CurrentStep : 1);

                // 加载家庭成员
                var membersResult = await _familyMemberService.GetByApplicationIdAsync(applicationId, ct);
                if (membersResult.IsFailure)
                {
                    _logger.Error($"家庭成员加载失败: {membersResult.Message}");
                    throw new BusinessException(ErrorCodes.DB_QUERY_ERROR, "家庭成员加载失败，已中止操作以避免保存时覆盖数据");
                }
                if (membersResult.Value?.Count > 0)
                {
                    FamilyMembers.Clear();
                    foreach (var member in membersResult.Value)
                    {
                        member.SetRegionService(_regionService);
                        await member.LoadInitialAddressOptionsAsync(
                            member.HomeCity, member.HomeDistrict, member.HomeTown);
                        SyncMemberDictOptions(member);
                        FamilyMembers.Add(member);
                    }
                    RefreshDerivedCollections();
                    _logger.LogBusiness("家庭成员加载完成", ("Count", FamilyMembers.Count));
                }

                // 成员变更模式的"变更前"快照：人数与成员名单（供 Before 快照与变更类型判定），
                // 必须在用户编辑成员之前捕获
                _loadedFamilySize = app.FamilySize;
                _loadedMemberEntities = membersResult.IsSuccess
                    ? membersResult.Value ?? new List<FamilyMember>()
                    : new List<FamilyMember>();
                _loadedMembersByIdCard = _loadedMemberEntities
                    .Where(m => !string.IsNullOrWhiteSpace(m.IdCard))
                    .GroupBy(m => m.IdCard.Trim(), StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().Name ?? string.Empty, StringComparer.OrdinalIgnoreCase);
                _addedMemberReasons.Clear();
                _removedMemberEntries.Clear();

                // 加载赡养人
                var supporterService = _supporterService;
                var supportersResult = await supporterService.GetByApplicationIdAsync(applicationId, ct);
                if (supportersResult.IsFailure)
                {
                    _logger.Error($"赡养人加载失败: {supportersResult.Message}");
                    throw new BusinessException(ErrorCodes.DB_QUERY_ERROR, "赡养人加载失败，已中止操作以避免保存时覆盖数据");
                }
                if (supportersResult.Value?.Count > 0)
                {
                    Supporters.Clear();
                    foreach (var supporter in supportersResult.Value)
                    {
                        if (supporter.MonthlySupportFee <= 0 && supporter.AnnualSupportFee > 0)
                        {
                            supporter.MonthlySupportFee = Math.Round(supporter.AnnualSupportFee / 12, 2);
                            supporter.SupportMonths = 12;
                        }
                        supporter.SupporterFamilySize = supporter.SupporterFamilySize > 0
                            ? supporter.SupporterFamilySize
                            : 1;
                        SyncSupporterPickerOptions(supporter);
                        Supporters.Add(supporter);
                    }
                    _logger.LogBusiness("赡养人加载完成", ("Count", Supporters.Count));
                }

                // 加载照料人
                var caregiverService = _caregiverService;
                var caregiversResult = await caregiverService.GetByApplicationIdAsync(applicationId, ct);
                if (caregiversResult.IsFailure)
                {
                    _logger.Error($"照料人加载失败: {caregiversResult.Message}");
                    throw new BusinessException(ErrorCodes.DB_QUERY_ERROR, "照料人加载失败，已中止操作以避免保存时覆盖数据");
                }
                if (caregiversResult.Value?.Count > 0)
                {
                    Caregivers.Clear();
                    foreach (var caregiver in caregiversResult.Value)
                    {
                        caregiver.SelectedEthnicity = EthnicityOptions.FirstOrDefault(o => o.Key == caregiver.Ethnicity);
                        caregiver.SelectedMaritalStatus = MaritalStatusOptions.FirstOrDefault(o => o.Key == caregiver.MaritalStatus);
                        caregiver.SelectedEducationLevel = EducationLevelOptions.FirstOrDefault(o => o.Key == caregiver.EducationLevel);
                        caregiver.SelectedPoliticalStatus = PoliticalStatusOptions.FirstOrDefault(o => o.Key == caregiver.PoliticalStatus);
                        caregiver.SelectedHealthStatus = HealthStatusOptions.FirstOrDefault(o => o.Key == caregiver.HealthStatus);
                        caregiver.SelectedEmploymentStatus = EmploymentStatusOptions.FirstOrDefault(o => o.Key == caregiver.EmploymentStatus);
                        caregiver.SelectedIncomeSource = IncomeSourceOptions.FirstOrDefault(o => o.Key == caregiver.MainIncomeSource);
                        Caregivers.Add(caregiver);
                    }
                    _logger.LogBusiness("照料人加载完成", ("Count", Caregivers.Count));
                }

                // 加载入户调查
                var surveyService = _householdSurveyService;
                var surveyResult = await surveyService.GetByApplicationIdAsync(applicationId, ct);
                if (surveyResult.IsFailure)
                {
                    _logger.Error($"入户调查加载失败: {surveyResult.Message}");
                }
                if (surveyResult.Value != null)
                {
                    var survey = surveyResult.Value;
                    SurveyDate = survey.SurveyDate;
                    SurveyorName = survey.SurveyorName;
                    SurveyorOrganization = survey.SurveyorOrganization;
                    RespondentName = survey.RespondentName;
                    RespondentRelation = survey.RespondentRelation;
                    SurveyConclusion = survey.SurveyConclusion;
                    SurveyNotes = survey.SurveyNotes;
                    _logger.LogBusiness("入户调查加载完成");
                }

                // 数据补全模式（导入库建档）：入户调查日期统一取纳入时间（建档时间 created_at，即纳入月份首日）
                // 并锁定，防止人工改动；仅补全模式锁定，其他模式不干预（保持已存值可编辑）。
                if (IsCompletionMode)
                {
                    _lockedSurveyDate = app.CreatedAt.Date;
                    SurveyDate = _lockedSurveyDate;
                    IsSurveyDateLocked = true;
                    _logger.LogBusiness("补全模式入户调查日期锁定为纳入时间",
                        ("ApplicationId", applicationId),
                        ("Date", _lockedSurveyDate.Value.ToString("yyyy-MM-dd")));
                }
                else
                {
                    _lockedSurveyDate = null;
                    IsSurveyDateLocked = false;
                }

                // 加载经济明细
                var economicResult = await _economicDetailService.LoadAllAsync(applicationId, ct);
                if (economicResult.IsSuccess)
                {
                    LaborIncomes.Clear();
                    BusinessIncomes.Clear();
                    PropertyIncomes.Clear();
                    TransferIncomes.Clear();
                    OtherIncomes.Clear();
                    Subsidies.Clear();
                    BreedingIncomes.Clear();
                    RigidExpenditures.Clear();

                    foreach (var item in economicResult.Value.LaborIncomes) LaborIncomes.Add(item);
                    foreach (var item in economicResult.Value.BusinessIncomes) BusinessIncomes.Add(item);
                    foreach (var item in economicResult.Value.PropertyIncomes) PropertyIncomes.Add(item);
                    foreach (var item in economicResult.Value.TransferIncomes) TransferIncomes.Add(item);
                    foreach (var item in economicResult.Value.OtherIncomes) OtherIncomes.Add(item);
                    foreach (var item in economicResult.Value.Subsidies) Subsidies.Add(item);
                    foreach (var item in economicResult.Value.BreedingIncomes) BreedingIncomes.Add(item);
                    foreach (var item in economicResult.Value.RigidExpenditures) RigidExpenditures.Add(item);

                    // 加载家庭财产
                    FamilyProperties.Clear();
                    foreach (var item in economicResult.Value.FamilyProperties) FamilyProperties.Add(item);

                    // 加载车辆
                    Vehicles.Clear();
                    foreach (var item in economicResult.Value.Vehicles) Vehicles.Add(item);

                    // 加载农机具
                    Machineries.Clear();
                    foreach (var item in economicResult.Value.Machineries) Machineries.Add(item);

                    // 加载金融资产
                    FinancialAssets.Clear();
                    foreach (var item in economicResult.Value.FinancialAssets) FinancialAssets.Add(item);

                    // 加载土地详情
                    LandRegistrations.Clear();
                    foreach (var item in economicResult.Value.LandRegistrations) LandRegistrations.Add(item);

                    // 加载土地确权归户表
                    LandConfirmationGroups.Clear();
                    foreach (var item in economicResult.Value.LandConfirmationGroups) LandConfirmationGroups.Add(item);

                    // 为归户表自动补充缺失的家庭成员人员
                    if (LandConfirmationGroups.Count > 0)
                    {
                        InitializePersonsForAllGroups();

                        // 为加载的归户表注册事件处理器
                        foreach (var group in LandConfirmationGroups)
                        {
                            group.Records.CollectionChanged += (s, e) => CalculateLandConfirmationArea();
                            group.Persons.CollectionChanged += (s, e) => CalculateLandConfirmationArea();
                        }

                        // 计算土地收入和面积
                        CalculateLandConfirmationArea();
                    }
                    else
                    {
                        RecalculateIncome();
                    }

                    _logger.LogBusiness("经济明细加载完成");
                }
                else
                {
                    _logger.Error($"加载经济明细失败: {economicResult.Message}");
                    await _serviceProvider.GetRequiredService<IDialogService>()
                        .DisplayAlertAsync("警告", $"加载经济明细失败: {economicResult.Message}", "确定");
                }

                _logger.LogBusiness("申请详情加载成功");
            }
            else
            {
                _logger.Error($"LoadApplication 失败: {result.Message}");
            }
        }, "加载申请详情...");

        _isLoadingData = false;

        // 数据加载完成后刷新 Picker 绑定
        ForceRefreshPickerBindings();

        // 刷新收入明细人员选项
        RefreshIncomeMemberOptions();

        // 恢复收入明细中各成员的 SelectedIncomeMember 引用（从 MemberName 反查匹配）
        RestoreIncomeMemberSelection();
    }


    /// <summary>
    /// 强制刷新所有 Picker 绑定（解决懒加载视图加入视觉树后 SelectedItem 不显示的问题）
    /// </summary>
    public void ForceRefreshPickerBindings()
    {
        OnPropertyChanged(nameof(SelectedProvince));
        OnPropertyChanged(nameof(SelectedCity));
        OnPropertyChanged(nameof(SelectedDistrict));
        OnPropertyChanged(nameof(SelectedTown));
        OnPropertyChanged(nameof(SelectedVillage));
        OnPropertyChanged(nameof(SelectedEthnicity));
        OnPropertyChanged(nameof(SelectedMaritalStatus));
        OnPropertyChanged(nameof(HukouType));
        OnPropertyChanged(nameof(SelectedEducationLevel));
        OnPropertyChanged(nameof(SelectedPoliticalStatus));
        OnPropertyChanged(nameof(SelectedDiseaseCategoryObj));
        OnPropertyChanged(nameof(SelectedDiseaseNameObj));
    }

    #region 家庭成员命令

    [RelayCommand]
    private async Task AddSharedLivingMemberAsync()
    {
        _logger.LogBusiness("添加共同生活成员");

        // 家庭成员变更模式：先登记增员原因与事由日期（取消则不新增）
        MemberChangeReasonResult? reason = null;
        if (IsMemberChangeMode)
        {
            reason = await ShowMemberChangeReasonPopupAsync(isRemove: false, member: null);
            if (reason == null) return;
        }

        var member = CreateDefaultFamilyMember(MemberCategoryConstants.SHARED_LIVING);
        if (reason != null)
            _addedMemberReasons[member] = reason;
        FamilyMembers.Add(member);
        RefreshDerivedCollections();
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task AddSupportMemberAsync()
    {
        _logger.LogBusiness("添加赡养抚养扶养人");

        // 家庭成员变更模式：先登记增员原因与事由日期（取消则不新增）
        MemberChangeReasonResult? reason = null;
        if (IsMemberChangeMode)
        {
            reason = await ShowMemberChangeReasonPopupAsync(isRemove: false, member: null);
            if (reason == null) return;
        }

        await EnsureRegionOptionsLoadedAsync();

        var member = CreateDefaultFamilyMember(MemberCategoryConstants.SUPPORT);
        member.HukouProvince = DefaultValuesConstants.HOME_PROVINCE;
        member.HukouCity = SelectedCity ?? string.Empty;
        member.HukouDistrict = SelectedDistrict ?? string.Empty;
        member.HukouTown = SelectedTown ?? string.Empty;

        if (reason != null)
            _addedMemberReasons[member] = reason;
        FamilyMembers.Add(member);
        RefreshDerivedCollections();
        await Task.CompletedTask;
    }

    /// <summary>
    /// 弹出增/减员登记弹窗（家庭成员变更模式专用）：确认返回登记信息，取消返回 null。
    /// 弹窗由调用方 Push/Pop（结果模式与 SelectMembersPopup 一致）。
    /// </summary>
    private async Task<MemberChangeReasonResult?> ShowMemberChangeReasonPopupAsync(bool isRemove, FamilyMember? member)
    {
        var popup = _serviceProvider.GetRequiredService<Pages.ChangeManagement.MemberChangeReasonPopup>();
        popup.Initialize(isRemove, member);

        var navigation = Helpers.WindowNavigator.CurrentPage?.Navigation;
        if (navigation == null)
        {
            _logger.Warn("当前页面为空，无法弹出成员变更登记弹窗");
            return null;
        }
        await navigation.PushModalAsync(popup);
        var result = await popup.Result;
        if (navigation.ModalStack.Contains(popup))
        {
            await navigation.PopModalAsync();
        }
        return result;
    }

    /// <summary>
    /// 创建带默认值的家庭成员
    /// </summary>
    private FamilyMember CreateDefaultFamilyMember(string memberCategory)
    {
        var member = new FamilyMember
        {
            ApplicationId = _applicationId,
            MemberCategory = memberCategory,
            RelationshipToHead = MemberRelationOptions.FirstOrDefault()?.Key ?? string.Empty,
            Ethnicity = GetDefaultKey(EthnicityOptions, DefaultValuesConstants.ETHNICITY_KEY),
            MaritalStatus = GetDefaultKey(MaritalStatusOptions, DefaultValuesConstants.MARITAL_STATUS_KEY),
            EducationLevel = GetDefaultKey(EducationLevelOptions, DefaultValuesConstants.EDUCATION_LEVEL_KEY),
            PoliticalStatus = GetDefaultKey(PoliticalStatusOptions, DefaultValuesConstants.POLITICAL_STATUS_KEY),
            HealthStatus = GetDefaultKey(HealthStatusOptions, DefaultValuesConstants.HEALTH_STATUS_KEY),
            DiseaseCategory = GetDefaultKey(DiseaseCategoryOptions, DefaultValuesConstants.DISEASE_CATEGORY_KEY),
            HomeProvince = DefaultValuesConstants.HOME_PROVINCE,
            HomeCity = SelectedCity ?? string.Empty,
            HomeDistrict = SelectedDistrict ?? string.Empty,
            HomeTown = SelectedTown ?? string.Empty,
            HomeVillage = SelectedVillage ?? string.Empty,
            HomeAddress = Address ?? string.Empty,
            HukouAddress = HukouAddress,
            HukouProvince = DefaultValuesConstants.HOME_PROVINCE,
            CreatedAt = DateTime.Now
        };

        member.SetRegionService(_regionService);
        _ = member.LoadInitialAddressOptionsAsync(SelectedCity, SelectedDistrict, SelectedTown);
        SyncMemberDictOptions(member);

        return member;
    }

    /// <summary>
    /// 获取字典选项的默认Key（优先使用DefaultValuesConstants中的值）
    /// </summary>
    private string GetDefaultKey(ObservableCollection<DictItemOption> options, string defaultKey)
    {
        if (options.Any(o => o.Key == defaultKey))
            return defaultKey;
        return options.FirstOrDefault()?.Key ?? string.Empty;
    }

    [RelayCommand]
    private async Task RemoveFamilyMemberAsync(FamilyMember? member)
    {
        if (member == null) return;

        if (member.RelationshipToHead == "本人/户主" || member.IsHouseholdHead)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "户主不能直接删除，请先变更户主", "确定");
            return;
        }

        // 检查是否只剩户主
        var nonHeadCount = FamilyMembers.Count(m => m.RelationshipToHead != "本人/户主");
        if (nonHeadCount <= 0)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "已经是最后一个家庭成员，不能删除", "确定");
            return;
        }

        // 家庭成员变更模式：先登记减员原因与事由日期（取消则不减员）
        MemberChangeReasonResult? reason = null;
        if (IsMemberChangeMode)
        {
            reason = await ShowMemberChangeReasonPopupAsync(isRemove: true, member: member);
            if (reason == null) return;
        }

        // 删除赡养抚养扶养人时同步移除 Step3 赡养人条目（同一 DB 行的两个视图，否则保存时会复活）
        if (member.MemberCategory == MemberCategoryConstants.SUPPORT)
        {
            var memberIdCard = (member.IdCard ?? "").Trim();
            var match = Supporters.FirstOrDefault(s =>
                (!string.IsNullOrWhiteSpace(s.IdCard) && string.Equals(s.IdCard.Trim(), memberIdCard, StringComparison.OrdinalIgnoreCase))
                || string.Equals(s.Name, member.Name, StringComparison.Ordinal));
            if (match != null)
            {
                _supporterFeeBackup.Remove(match);
                Supporters.Remove(match);
            }
        }

        if (reason != null)
            _removedMemberEntries.Add((member, reason));

        FamilyMembers.Remove(member);
        RefreshDerivedCollections();
        RecalculateIncome();
    }

    private void UpdateFamilySize()
    {
        FamilySize = SharedLivingMembers.Count + 1; // +1 是户主
    }

    [RelayCommand]
    private async Task SetAsHeadAsync(FamilyMember? member)
    {
        if (member == null) return;

        var currentHead = FamilyMembers.FirstOrDefault(m => m.RelationshipToHead == "本人/户主");
        if (currentHead != null)
            currentHead.RelationshipToHead = "其他";

        member.RelationshipToHead = "本人/户主";
        RefreshDerivedCollections();
        await Task.CompletedTask;
    }

    #endregion

    #region 赡养人辅助方法

    /// <summary>
    /// 获取缓存的低保标准（内存缓存，避免每次添加/导入赡养人都查 DB）。
    /// 仅用于赡养费"默认预填值"；读取失败返回 0 并告警——禁止回退过期硬编码标准。
    /// 判定与保障金计算走 ClassificationService（配置缺失会显式失败）。
    /// </summary>
    private decimal _cachedSubsistenceStandard;
    private bool _subsistenceStandardLoaded;

    private async Task<decimal> GetCachedSubsistenceStandardAsync()
    {
        if (_subsistenceStandardLoaded && _cachedSubsistenceStandard > 0)
            return _cachedSubsistenceStandard;

        try
        {
            bool isRural = HukouType?.Key != "Urban";
            var standardType = isRural ? "RuralSubsistenceStandard" : "UrbanSubsistenceStandard";
            var result = await _standardConfigService.GetStandardValueAsync(standardType);
            if (result.IsSuccess && result.Value > 0)
                _cachedSubsistenceStandard = result.Value;
            else
                _logger.Warn($"低保标准读取失败，赡养费默认值置 0：{result.Message}");
        }
        catch (Exception ex)
        {
            _logger.Warn($"低保标准读取异常，赡养费默认值置 0：{ex.Message}");
        }
        _subsistenceStandardLoaded = true;
        return _cachedSubsistenceStandard;
    }

    /// <summary>
    /// 批量查询身份证关联的档案分类结果（一次 DB 查询替代 N+1）
    /// </summary>
    private async Task<Dictionary<string, string>> BatchGetClassificationByIdCardsAsync(List<string> idCards)
    {
        var map = new Dictionary<string, string>();
        if (idCards.Count == 0) return map;

        try
        {
            var result = await _applicationService.GetClassificationsByIdCardsAsync(idCards, CancellationToken);
            if (result.IsSuccess && result.Value != null)
                return result.Value;
            _logger.Warn($"批量查询赡养人档案失败: {result.Message}");
        }
        catch (Exception ex)
        {
            _logger.Warn($"批量查询赡养人档案失败: {ex.Message}");
        }
        return map;
    }

    /// <summary>
    /// 同步判定赡养人是否有赡养能力（基于内存数据，无 DB 查询）
    /// </summary>
    private bool DetermineSupportAbilityFromMember(FamilyMember member, Dictionary<string, string> classificationMap)
    {
        // 规则1：健康/残疾状况判定（重病、重残含三级智力/精神 → 无赡养能力）
        if (DictionaryConstants.HealthStatus.HasSevereDisease(member.HealthStatus)
            || DictionaryConstants.HealthStatus.HasSevereDisability(member.HealthStatus)
            || ClassificationConstants.DisabilityLevel.IsSevereForAssistance(
                member.DisabilityLevelKeyResolved, member.DisabilityType))
        {
            return false;
        }

        // 规则2：年龄判定（< 18岁 或 > 70岁 均无赡养能力）
        if (member.Age.HasValue && (member.Age < ClassificationConstants.SUPPORT_ABILITY_MIN_AGE || member.Age > ClassificationConstants.SUPPORT_ABILITY_MAX_AGE))
        {
            return false;
        }

        // 规则3：经济状况判定（从内存缓存查，无 DB 查询）
        if (!string.IsNullOrWhiteSpace(member.IdCard) &&
            classificationMap.TryGetValue(member.IdCard.Trim(), out var classification))
        {
            if (classification.Contains("Subsistence") || classification.Contains("LowIncome"))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 由家庭成员（赡养抚养扶养人）构建 Supporter（默认赡养费 + 赡养能力判定结果）
    /// </summary>
    private Supporter BuildSupporterFromMember(FamilyMember member, decimal defaultMonthlyFee, bool hasSupportAbility)
    {
        return new Supporter
        {
            ApplicationId = _applicationId,
            Id = member.Id,
            Name = member.Name,
            IdCard = member.IdCard,
            Relationship = member.RelationshipToHead,
            SelectedRelationship = MemberRelationOptions.FirstOrDefault(o => o.Key == member.RelationshipToHead),
            PersonType = "赡养",
            SelectedPersonType = PersonTypeOptions.FirstOrDefault(o => o.Key == "赡养"),
            MonthlySupportFee = defaultMonthlyFee,
            SupportMonths = 12,
            SupporterFamilySize = 1,
            IsSupportAbility = hasSupportAbility,
            Gender = member.Gender,
            Age = member.Age,
            Ethnicity = member.Ethnicity,
            Phone = member.Phone,
            HukouType = member.HukouType,
            MaritalStatus = member.MaritalStatus,
            EducationLevel = member.EducationLevel,
            PoliticalStatus = member.PoliticalStatus,
            HealthStatus = member.HealthStatus,
            WorkUnit = member.WorkUnit,
            EmploymentStatus = member.EmploymentStatus,
            MainIncomeSource = member.MainIncomeSource,
            WorkCapacity = member.WorkCapacity,
            AnnualIncome = member.AnnualIncome,
            HomeProvince = member.HomeProvince,
            HomeCity = member.HomeCity,
            HomeDistrict = member.HomeDistrict,
            HomeTown = member.HomeTown,
            HomeVillage = member.HomeVillage,
            HomeAddress = member.HomeAddress,
            HukouProvince = member.HukouProvince,
            HukouCity = member.HukouCity,
            HukouDistrict = member.HukouDistrict,
            HukouTown = member.HukouTown
        };
    }

    /// <summary>
    /// 保存前把 Step2"赡养抚养扶养"成员合并进 Supporters 集合（仅补缺失，不覆盖已有编辑值）。
    /// 未点"导入家庭成员"时保障其赡养费计入收入并随本单保存，避免被整表软删丢失。
    /// 已有匹配项以家庭成员为准同步基础字段（保留 Step3 编辑的赡养费/月数/人数/能力/人员类型）。
    /// </summary>
    private async Task MergeSupportMembersIntoSupportersAsync()
    {
        var missing = new List<FamilyMember>();
        foreach (var member in SupportMembers)
        {
            if (string.IsNullOrWhiteSpace(member.Name) || string.IsNullOrWhiteSpace(member.IdCard))
                continue;

            var existing = Supporters.FirstOrDefault(s =>
                (!string.IsNullOrWhiteSpace(s.IdCard) && string.Equals(s.IdCard.Trim(), member.IdCard.Trim(), StringComparison.OrdinalIgnoreCase))
                || string.Equals(s.Name, member.Name, StringComparison.Ordinal));

            if (existing == null)
                missing.Add(member);
            else
                SyncSupporterBasicsFromMember(existing, member);
        }

        if (missing.Count == 0) return;

        decimal subsistenceStandard = await GetCachedSubsistenceStandardAsync();
        decimal defaultMonthlyFee = Math.Round(subsistenceStandard * IncomeTypeConstants.DEFAULT_SUPPORT_FEE_RATIO, 2);

        var idCards = missing.Select(m => m.IdCard!.Trim()).Distinct().ToList();
        var classificationMap = await BatchGetClassificationByIdCardsAsync(idCards);

        foreach (var member in missing)
        {
            var hasAbility = DetermineSupportAbilityFromMember(member, classificationMap);
            Supporters.Add(BuildSupporterFromMember(member, defaultMonthlyFee, hasAbility));
            _logger.LogBusiness("保存前合并赡养人", ("Name", DataMasker.MaskName(member.Name)));
        }
    }

    /// <summary>
    /// 以家庭成员为准同步已有赡养人的基础字段（保留 Step3 编辑的赡养费/月数/家庭人数/能力/人员类型）
    /// </summary>
    private void SyncSupporterBasicsFromMember(Supporter supporter, FamilyMember member)
    {
        if (member.Id > 0) supporter.Id = member.Id;
        supporter.ApplicationId = _applicationId;
        supporter.Name = member.Name ?? string.Empty;
        supporter.IdCard = member.IdCard ?? string.Empty;
        supporter.Relationship = member.RelationshipToHead ?? string.Empty;
        supporter.SelectedRelationship = MemberRelationOptions.FirstOrDefault(o => o.Key == member.RelationshipToHead);
        supporter.Gender = member.Gender ?? string.Empty;
        supporter.Age = member.Age;
        supporter.Ethnicity = member.Ethnicity ?? string.Empty;
        supporter.Phone = member.Phone ?? string.Empty;
        supporter.HukouType = member.HukouType ?? string.Empty;
        supporter.MaritalStatus = member.MaritalStatus ?? string.Empty;
        supporter.EducationLevel = member.EducationLevel ?? string.Empty;
        supporter.PoliticalStatus = member.PoliticalStatus ?? string.Empty;
        supporter.HealthStatus = member.HealthStatus ?? string.Empty;
        supporter.WorkUnit = member.WorkUnit ?? string.Empty;
        supporter.EmploymentStatus = member.EmploymentStatus ?? string.Empty;
        supporter.MainIncomeSource = member.MainIncomeSource ?? string.Empty;
        supporter.WorkCapacity = member.WorkCapacity ?? string.Empty;
        supporter.AnnualIncome = member.AnnualIncome;
        supporter.HomeProvince = member.HomeProvince ?? string.Empty;
        supporter.HomeCity = member.HomeCity ?? string.Empty;
        supporter.HomeDistrict = member.HomeDistrict ?? string.Empty;
        supporter.HomeTown = member.HomeTown ?? string.Empty;
        supporter.HomeVillage = member.HomeVillage ?? string.Empty;
        supporter.HomeAddress = member.HomeAddress ?? string.Empty;
        supporter.HukouProvince = member.HukouProvince ?? string.Empty;
        supporter.HukouCity = member.HukouCity ?? string.Empty;
        supporter.HukouDistrict = member.HukouDistrict ?? string.Empty;
        supporter.HukouTown = member.HukouTown ?? string.Empty;
    }

    #endregion

    #region 赡养人命令

    [RelayCommand]
    private async Task AddSupporterAsync(string? personType)
    {
        var type = personType ?? "赡养";
        _logger.LogBusiness($"添加{type}人");

        // 低保标准：优先用缓存，避免每次添加都查 DB
        decimal subsistenceStandard = await GetCachedSubsistenceStandardAsync();
        decimal defaultMonthlyFee = Math.Round(subsistenceStandard * IncomeTypeConstants.DEFAULT_SUPPORT_FEE_RATIO, 2);
        decimal defaultAnnualFee = defaultMonthlyFee * 12;

        var supporter = new Supporter
        {
            ApplicationId = _applicationId,
            PersonType = type,
            SelectedPersonType = PersonTypeOptions.FirstOrDefault(o => o.Key == type),
            MonthlySupportFee = defaultMonthlyFee,
            SupportMonths = 12,
            SupporterFamilySize = 1,
            IsSupportAbility = true,
            Relationship = "",
            SelectedRelationship = null
        };

        // 手动添加时无法判定赡养能力（无 FamilyMember 关联），默认为有赡养能力
        // 导入时由 ImportSupportMembersAsync 在创建前判定

        _logger.LogBusiness($"添加{type}人完成",
            ("PersonType", type),
            ("MonthlyFee", defaultMonthlyFee.ToString()),
            ("AnnualFee", defaultAnnualFee.ToString()),
            ("IsSupportAbility", supporter.IsSupportAbility.ToString()));

        Supporters.Add(supporter);
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RemoveSupporterAsync(Supporter? supporter)
    {
        if (supporter == null) return;

        _logger.LogBusiness("删除赡养人", ("Name", DataMasker.MaskName(supporter.Name)));

        _supporterFeeBackup.Remove(supporter);
        Supporters.Remove(supporter);
        RecalculateIncome();
        await Task.CompletedTask;
    }

    /// <summary>
    /// 综合判定赡养人是否有赡养能力（基于 FamilyMember 数据）
    /// 规则：
    /// 1. 重病/重残（含三级智力、三级精神）→ 无赡养能力
    /// 2. 年龄 &lt; 18 或 &gt; 70 → 无赡养能力
    /// 3. 本身是低保/低收入 → 无赡养能力
    /// </summary>
    private async Task<bool> DetermineSupportAbilityFromMemberAsync(FamilyMember member)
    {
        // 规则1：健康/残疾状况判定（重病、重残含三级智力/精神 → 无赡养能力）
        if (DictionaryConstants.HealthStatus.HasSevereDisease(member.HealthStatus)
            || DictionaryConstants.HealthStatus.HasSevereDisability(member.HealthStatus)
            || ClassificationConstants.DisabilityLevel.IsSevereForAssistance(
                member.DisabilityLevelKeyResolved, member.DisabilityType))
        {
            _logger.Info($"赡养人{DataMasker.MaskName(member.Name)}因健康状况或残疾等级判定为无赡养能力");
            return false;
        }

        // 规则2：年龄判定（< 18岁 或 > 70岁 均无赡养能力）
        if (member.Age.HasValue && (member.Age < ClassificationConstants.SUPPORT_ABILITY_MIN_AGE || member.Age > ClassificationConstants.SUPPORT_ABILITY_MAX_AGE))
        {
            _logger.Info($"赡养人{DataMasker.MaskName(member.Name)}因年龄({member.Age})判定为无赡养能力");
            return false;
        }

        // 规则3：经济状况判定（查询是否有低保/低收入档案）
        if (!string.IsNullOrWhiteSpace(member.IdCard))
        {
            try
            {
                var appResult = await _applicationService.GetByIdCardAsync(member.IdCard);
                if (appResult.IsSuccess && appResult.Value?.Count > 0)
                {
                    var existingApp = appResult.Value.FirstOrDefault(a =>
                        a.Status == ApplicationStatusCodes.APPROVED || a.Status == ApplicationStatusCodes.COMPLETED);

                    if (existingApp != null)
                    {
                        var classification = existingApp.ClassificationResult;
                        if (!string.IsNullOrEmpty(classification) &&
                            (classification.Contains("Subsistence") || classification.Contains("LowIncome")))
                        {
                            _logger.Info($"赡养人{DataMasker.MaskName(member.Name)}因是低保/低收入({classification})判定为无赡养能力");
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"查询赡养人档案失败: {ex.Message}");
            }
        }

        return true;
    }

    [RelayCommand]
    private async Task ImportSupportMembersAsync()
    {
        var supportMembers = FamilyMembers
            .Where(m => m.MemberCategory == MemberCategoryConstants.SUPPORT)
            .ToList();

        if (supportMembers.Count == 0)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "请先在家庭成员页面添加赡养抚养扶养人", "确定");
            return;
        }

        // 低保标准：优先用缓存，避免每次导入都查 DB
        decimal subsistenceStandard = await GetCachedSubsistenceStandardAsync();
        decimal defaultMonthlyFee = Math.Round(subsistenceStandard * IncomeTypeConstants.DEFAULT_SUPPORT_FEE_RATIO, 2);

        // 批量查询赡养人关联的档案（替代 N+1 逐人查询）
        var idCards = supportMembers
            .Where(m => !string.IsNullOrWhiteSpace(m.IdCard))
            .Select(m => m.IdCard!.Trim())
            .Distinct()
            .ToList();
        var classificationMap = await BatchGetClassificationByIdCardsAsync(idCards);

        var count = 0;
        foreach (var member in supportMembers)
        {
            if (Supporters.Any(s => s.Name == member.Name))
                continue;

            // 基于 FamilyMember 数据 + 内存缓存判定赡养能力（无 DB 查询）
            var hasAbility = DetermineSupportAbilityFromMember(member, classificationMap);

            Supporters.Add(BuildSupporterFromMember(member, defaultMonthlyFee, hasAbility));
            count++;
        }

        if (count > 0)
        {
            RecalculateIncome();
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("成功", $"已导入 {count} 名赡养抚养扶养人", "确定");
        }
        else
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "所有赡养抚养扶养人已导入", "确定");
        }
    }

    #endregion

    #region 照料人命令

    [RelayCommand]
    private async Task AddCaregiverAsync()
    {
        _logger.LogBusiness("添加照料人");

        var caregiver = new Caregiver
        {
            ApplicationId = _applicationId,
            Relationship = MemberRelationOptions.FirstOrDefault()?.Key ?? string.Empty,
            Ethnicity = GetDefaultKey(EthnicityOptions, DefaultValuesConstants.ETHNICITY_KEY),
            MaritalStatus = GetDefaultKey(MaritalStatusOptions, DefaultValuesConstants.MARITAL_STATUS_KEY),
            EducationLevel = GetDefaultKey(EducationLevelOptions, DefaultValuesConstants.EDUCATION_LEVEL_KEY),
            PoliticalStatus = GetDefaultKey(PoliticalStatusOptions, DefaultValuesConstants.POLITICAL_STATUS_KEY),
            HealthStatus = GetDefaultKey(HealthStatusOptions, DefaultValuesConstants.HEALTH_STATUS_KEY),
            CreatedAt = DateTime.Now,
            SelectedEthnicity = EthnicityOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.ETHNICITY_KEY),
            SelectedMaritalStatus = MaritalStatusOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.MARITAL_STATUS_KEY),
            SelectedEducationLevel = EducationLevelOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.EDUCATION_LEVEL_KEY),
            SelectedPoliticalStatus = PoliticalStatusOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.POLITICAL_STATUS_KEY),
            SelectedHealthStatus = HealthStatusOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.HEALTH_STATUS_KEY)
        };

        Caregivers.Add(caregiver);
        await Task.CompletedTask;
    }

    [RelayCommand]
    private async Task RemoveCaregiverAsync(Caregiver? caregiver)
    {
        if (caregiver == null) return;

        _logger.LogBusiness("删除照料人", ("Name", DataMasker.MaskName(caregiver.Name)));
        Caregivers.Remove(caregiver);
        await Task.CompletedTask;
    }

    /// <summary>
    /// 照料人身份证号变化时自动更新性别和年龄
    /// </summary>
    public void OnCaregiverIdCardChanged(Caregiver caregiver)
    {
        if (caregiver == null || string.IsNullOrWhiteSpace(caregiver.IdCard))
            return;

        if (caregiver.IdCard.Length >= 17)
        {
            // 从身份证号推导性别（第17位奇数为男，偶数为女）
            var genderDigit = caregiver.IdCard[16] - '0';
            caregiver.Gender = genderDigit % 2 == 1 ? "男" : "女";

            // 从身份证号推导年龄
            var birthYearStr = caregiver.IdCard.Substring(6, 4);
            if (int.TryParse(birthYearStr, out var birthYear))
            {
                var now = DateTime.Now;
                var age = now.Year - birthYear;
                if (now.Month < int.Parse(caregiver.IdCard.Substring(10, 2)) ||
                    (now.Month == int.Parse(caregiver.IdCard.Substring(10, 2)) &&
                     now.Day < int.Parse(caregiver.IdCard.Substring(12, 2))))
                {
                    age--;
                }
                caregiver.Age = age;
            }

            // 设置性别+年龄显示（与 FamilyMember 保持一致）
            if (caregiver.Age > 0)
                caregiver.GenderAgeDisplay = $"{caregiver.Gender} {caregiver.Age}岁";
            else
                caregiver.GenderAgeDisplay = caregiver.Gender;

            // 触发 UI 刷新
            caregiver.NotifyDisplayChanged();

            _logger.Info($"照料人身份证号变化: 性别={caregiver.Gender}, 年龄={caregiver.Age}, 显示={caregiver.GenderAgeDisplay}");
        }
    }

    #endregion

    #region 收入/支出项命令（通用方法）

    /// <summary>
    /// 刷新收入明细人员选项（户主 + 共同生活成员）
    /// </summary>
    private void RefreshIncomeMemberOptions()
    {
        IncomeMemberOptions.Clear();
        if (!string.IsNullOrWhiteSpace(ApplicantName))
        {
            IncomeMemberOptions.Add(new FamilyMember
            {
                Name = ApplicantName,
                IdCard = ApplicantIdCard,
                Age = Helpers.IdCardValidator.ExtractAgeBasic(ApplicantIdCard),
                MemberCategory = MemberCategoryConstants.HOUSEHOLD_HEAD
            });
        }
        foreach (var m in FamilyMembers.Where(m => m.MemberCategory == Constants.MemberCategoryConstants.SHARED_LIVING && !string.IsNullOrWhiteSpace(m.Name)))
        {
            IncomeMemberOptions.Add(m);
        }
    }

    /// <summary>
    /// 数据库加载完成后，根据 MemberName + MemberIdCard 反查 IncomeMemberOptions 中匹配的
    /// FamilyMember 并回填各收入实体的 SelectedIncomeMember，使 UI Picker 显示正确。
    /// </summary>
    private void RestoreIncomeMemberSelection()
    {
        if (IncomeMemberOptions.Count == 0) return;

        void MatchCollection<T>(IEnumerable<T> items) where T : class
        {
            foreach (var item in items)
            {
                var name = typeof(T).GetProperty("MemberName")?.GetValue(item) as string;
                var idCard = typeof(T).GetProperty("MemberIdCard")?.GetValue(item) as string;
                if (string.IsNullOrWhiteSpace(name)) continue;

                var match = IncomeMemberOptions.FirstOrDefault(m =>
                    m.Name == name && (string.IsNullOrEmpty(idCard) || m.IdCard == idCard));
                if (match == null) continue;

                typeof(T).GetProperty("SelectedIncomeMember")?.SetValue(item, match);
            }
        }

        MatchCollection(LaborIncomes);
        MatchCollection(BusinessIncomes);
        MatchCollection(PropertyIncomes);
        MatchCollection(TransferIncomes);
    }

    /// <summary>
    /// 弹出选择器让用户选择收入归属人员
    /// </summary>
    private async Task<FamilyMember?> PickIncomeMemberAsync(string title)
    {
        if (IncomeMemberOptions.Count == 0)
            RefreshIncomeMemberOptions();

        if (IncomeMemberOptions.Count == 0)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "请先添加家庭成员", "确定");
            return null;
        }

        var names = IncomeMemberOptions.Select(m => m.Name).ToArray();
        var dialogService = _serviceProvider.GetRequiredService<IDialogService>();
        var result = await dialogService.DisplayActionSheetAsync(title, "取消", null, names);

        if (string.IsNullOrEmpty(result) || result == "取消")
            return null;

        return IncomeMemberOptions.FirstOrDefault(m => m.Name == result);
    }

    /// <summary>
    /// 添加收入项到集合（通用方法）
    /// </summary>
    private async Task AddIncomeItemAsync<T>(ObservableCollection<T> collection, T item, string itemName) where T : class
    {
        _logger.LogBusiness($"添加{itemName}");
        collection.Add(item);
        await Task.CompletedTask;
    }

    /// <summary>
    /// 删除收入项（需要重新计算收入）
    /// </summary>
    private async Task RemoveIncomeItemAsync<T>(ObservableCollection<T> collection, T? item, string itemName) where T : class
    {
        if (item == null) return;
        try
        {
            _logger.LogBusiness($"删除{itemName}");

            // 让出当前 UI 帧并稍作等待，待 WinUI 完成按钮点击后的焦点/布局处理后再移除行，
            // 规避"移除含焦点控件"导致的 WinUI 原生层崩溃（删除补贴闪退根因）。
            await Task.Yield();
            await Task.Delay(30);

            collection.Remove(item);
            RecalculateIncome();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"删除{itemName}失败");
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// 删除资产项（无需重新计算收入）
    /// </summary>
    private async Task RemoveAssetItemAsync<T>(ObservableCollection<T> collection, T? item, string itemName) where T : class
    {
        if (item == null) return;
        try
        {
            _logger.LogBusiness($"删除{itemName}");

            // 同上：让出 UI 帧后移除行，规避移除含焦点控件的 WinUI 崩溃
            await Task.Yield();
            await Task.Delay(30);

            collection.Remove(item);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"删除{itemName}失败");
        }
        await Task.CompletedTask;
    }

    #endregion

    #region 务工收入命令

    [RelayCommand]
    private async Task AddLaborIncomeAsync()
    {
        var member = await PickIncomeMemberAsync("选择务工人员");
        if (member == null) return;
        await AddIncomeItemAsync(LaborIncomes, new LaborIncome
        {
            ApplicationId = _applicationId,
            MemberId = member.Id,
            MemberName = member.Name,
            MemberIdCard = member.IdCard ?? "",
            MemberAge = member.Age ?? 0,
            MonthsWorked = 12,
            SelectedIncomeMember = member,
            CreatedAt = DateTime.Now
        }, "务工收入");
    }

    [RelayCommand]
    private async Task RemoveLaborIncomeAsync(LaborIncome? income) =>
        await RemoveIncomeItemAsync(LaborIncomes, income, "务工收入");

    #endregion

    #region 经营收入命令

    [RelayCommand]
    private async Task AddBusinessIncomeAsync()
    {
        var member = await PickIncomeMemberAsync("选择经营人员");
        if (member == null) return;
        await AddIncomeItemAsync(BusinessIncomes, new BusinessIncome
        {
            ApplicationId = _applicationId,
            MemberId = member.Id,
            MemberName = member.Name,
            MemberIdCard = member.IdCard ?? "",
            MemberAge = member.Age ?? 0,
            SelectedIncomeMember = member,
            CreatedAt = DateTime.Now
        }, "经营收入");
    }

    [RelayCommand]
    private async Task RemoveBusinessIncomeAsync(BusinessIncome? income) =>
        await RemoveIncomeItemAsync(BusinessIncomes, income, "经营收入");

    #endregion

    #region 刚性支出命令

    [RelayCommand]
    private async Task AddRigidExpenditureAsync() =>
        await AddIncomeItemAsync(RigidExpenditures, new RigidExpenditure { ApplicationId = _applicationId, ExpenditureType = DictionaryConstants.RigidExpenditureType.MEDICAL }, "刚性支出");

    [RelayCommand]
    private async Task RemoveRigidExpenditureAsync(RigidExpenditure? expenditure) =>
        await RemoveIncomeItemAsync(RigidExpenditures, expenditure, "刚性支出");

    #endregion

    #region 土地登记命令

    [RelayCommand]
    private async Task AddLandRegistrationAsync() =>
        await AddIncomeItemAsync(LandRegistrations, new LandRegistration { ApplicationId = _applicationId, LandUsage = DictionaryConstants.LandUsage.SELF_FARM, CreatedAt = DateTime.Now }, "土地登记");

    [RelayCommand]
    private async Task RemoveLandRegistrationAsync(LandRegistration? registration) =>
        await RemoveIncomeItemAsync(LandRegistrations, registration, "土地登记");

    #endregion

    #region 补贴命令

    [RelayCommand]
    private async Task AddSubsidyAsync() =>
        await AddIncomeItemAsync(Subsidies, new Subsidy { ApplicationId = _applicationId, SubsidyType = DictionaryConstants.SubsidyType.LAND_FERTILITY, Count = 1, RatioFactor = 1, CreatedAt = DateTime.Now }, "农业补贴");

    [RelayCommand]
    private async Task RemoveSubsidyAsync(Subsidy? subsidy) =>
        await RemoveIncomeItemAsync(Subsidies, subsidy, "农业补贴");

    #endregion

    #region 财产净收入命令

    [RelayCommand]
    private async Task AddPropertyIncomeAsync()
    {
        var member = await PickIncomeMemberAsync("选择财产归属人员");
        if (member == null) return;
        await AddIncomeItemAsync(PropertyIncomes, new PropertyIncome
        {
            ApplicationId = _applicationId,
            MemberId = member.Id,
            MemberName = member.Name,
            MemberIdCard = member.IdCard ?? "",
            MemberAge = member.Age ?? 0,
            SelectedIncomeMember = member,
            CreatedAt = DateTime.Now
        }, "财产净收入");
    }

    [RelayCommand]
    private async Task RemovePropertyIncomeAsync(PropertyIncome? income) =>
        await RemoveIncomeItemAsync(PropertyIncomes, income, "财产净收入");

    #endregion

    #region 转移净收入命令

    [RelayCommand]
    private async Task AddTransferIncomeAsync()
    {
        var member = await PickIncomeMemberAsync("选择转移收入人员");
        if (member == null) return;
        await AddIncomeItemAsync(TransferIncomes, new TransferIncome
        {
            ApplicationId = _applicationId,
            MemberId = member.Id,
            MemberName = member.Name,
            MemberIdCard = member.IdCard ?? "",
            MemberAge = member.Age ?? 0,
            MonthsOrTimes = 12,
            SelectedIncomeMember = member,
            CreatedAt = DateTime.Now
        }, "转移净收入");
    }

    [RelayCommand]
    private async Task RemoveTransferIncomeAsync(TransferIncome? income) =>
        await RemoveIncomeItemAsync(TransferIncomes, income, "转移净收入");

    #endregion

    #region 其他收入命令

    [RelayCommand]
    private async Task AddOtherIncomeAsync() =>
        await AddIncomeItemAsync(OtherIncomes, new OtherIncome { ApplicationId = _applicationId, CreatedAt = DateTime.Now }, "其他收入");

    [RelayCommand]
    private async Task RemoveOtherIncomeAsync(OtherIncome? income) =>
        await RemoveIncomeItemAsync(OtherIncomes, income, "其他收入");

    #endregion

    #region 通用添加收入命令

    [RelayCommand]
    private async Task AddIncomeAsync()
    {
        var options = new[] { "务工收入", "经营收入", "转移净收入", "财产净收入", "其他收入" };
        var dialogService = _serviceProvider.GetRequiredService<IDialogService>();
        var result = await dialogService.DisplayActionSheetAsync("选择收入类型", "取消", null, options);
        if (result == null || result == "取消") return;

        switch (result)
        {
            case "务工收入":
                await AddLaborIncomeAsync();
                break;
            case "经营收入":
                await AddBusinessIncomeAsync();
                break;
            case "转移净收入":
                await AddTransferIncomeAsync();
                break;
            case "财产净收入":
                await AddPropertyIncomeAsync();
                break;
            case "其他收入":
                await AddOtherIncomeAsync();
                break;
        }
    }

    #endregion

    #region 资产命令

    [RelayCommand]
    private async Task AddFamilyPropertyAsync() =>
        await AddIncomeItemAsync(FamilyProperties, new FamilyProperty { ApplicationId = _applicationId, CreatedAt = DateTime.Now }, "房产");

    [RelayCommand]
    private async Task RemoveFamilyPropertyAsync(FamilyProperty? property) =>
        await RemoveAssetItemAsync(FamilyProperties, property, "房产");

    [RelayCommand]
    private async Task AddVehicleAsync() =>
        await AddIncomeItemAsync(Vehicles, new Vehicle { ApplicationId = _applicationId, CreatedAt = DateTime.Now }, "车辆");

    [RelayCommand]
    private async Task RemoveVehicleAsync(Vehicle? vehicle) =>
        await RemoveAssetItemAsync(Vehicles, vehicle, "车辆");

    [RelayCommand]
    private async Task AddMachineryAsync() =>
        await AddIncomeItemAsync(Machineries, new Machinery { ApplicationId = _applicationId, Quantity = 1, CreatedAt = DateTime.Now }, "农机具");

    [RelayCommand]
    private async Task RemoveMachineryAsync(Machinery? machinery) =>
        await RemoveAssetItemAsync(Machineries, machinery, "农机具");

    private void SyncSupporterPickerOptions(Supporter supporter)
    {
        supporter.SelectedPersonType = PersonTypeOptions.FirstOrDefault(o => o.Key == supporter.PersonType);
        supporter.SelectedRelationship = MemberRelationOptions.FirstOrDefault(o => o.Key == supporter.Relationship);
    }

    #endregion

    #region 经济状况分组展开/折叠命令

    [RelayCommand]
    private void ToggleIncomeExpanded() => IsIncomeExpanded = !IsIncomeExpanded;

    [RelayCommand]
    private void ToggleLandExpanded() => IsLandExpanded = !IsLandExpanded;

    [RelayCommand]
    private void ToggleSubsidyExpanded() => IsSubsidyExpanded = !IsSubsidyExpanded;

    [RelayCommand]
    private void ToggleRigidExpenditureExpanded() => IsRigidExpenditureExpanded = !IsRigidExpenditureExpanded;

    [RelayCommand]
    private void TogglePropertyExpanded() => IsPropertyExpanded = !IsPropertyExpanded;

    [RelayCommand]
    private void ToggleSupportExpanded() => IsSupportExpanded = !IsSupportExpanded;

    #endregion


    [RelayCommand]
    private async Task ImportSubsidyDataAsync()
    {
        _logger.LogBusiness("导入农业补贴数据");

        try
        {
            IsBusy = true;
            var idCards = new List<string>();
            if (!string.IsNullOrWhiteSpace(ApplicantIdCard))
                idCards.Add(ApplicantIdCard);

            // 身份证来源：只取户主 + 共同生活成员，排除赡养抚养扶养人
            foreach (var member in SharedLivingMembers)
            {
                if (!string.IsNullOrWhiteSpace(member.IdCard) && !idCards.Contains(member.IdCard))
                    idCards.Add(member.IdCard);
            }

            var result = await _subsidyDataService.GetSubsidiesByIdCardsAsync(idCards);
            if (result.IsSuccess && result.Value?.Count > 0)
            {
                Subsidies.Clear();
                foreach (var record in result.Value)
                {
                    // 地力补贴 SQL 返回 area=0、amount=政府原始总金额；种植/轮作补贴返回实际面积。
                    // 地力补贴有面积但 SQL 未返回，用金额÷单价反推亩数。
                    var area = record.Area;
                    if (area == 0 && record.Amount > 0)
                    {
                        // 地力补贴：金额÷单价反推面积
                        var unitPrice = SubsidyPriceConstants.GetPrice(record.SubsidyType);
                        area = unitPrice > 0
                            ? (decimal)SubsidyPriceConstants.CalculateArea(record.Amount, unitPrice)
                            : 0;
                    }
                    var subsidy = new Models.Entities.Subsidy
                    {
                        ApplicationId = _applicationId,
                        SubsidyType = record.SubsidyType,
                        Area = area,
                        MemberName = record.Name,
                        MemberIdCard = record.IdCard,
                        CreatedAt = DateTime.Now
                    };
                    Subsidies.Add(subsidy);
                }
                RecalculateIncome();
                await _serviceProvider.GetRequiredService<IDialogService>()
                    .DisplayAlertAsync("成功", $"共导入 {result.Value.Count} 条补贴数据", "确定");
            }
            else
            {
                await _serviceProvider.GetRequiredService<IDialogService>()
                    .DisplayAlertAsync("提示", "未找到农业补贴数据", "确定");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"导入农业补贴失败: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    #region 收入计算

    /// <summary>
    /// 从土地登记信息更新土地面积汇总字段
    /// </summary>
    private void UpdateLandAreasFromRegistrations()
    {
        // 有归户表时，不从土地登记更新（归户表优先）
        if (LandConfirmationGroups.Count > 0)
            return;

        decimal selfFarmed = 0, subleased = 0, contracted = 0;

        foreach (var reg in LandRegistrations)
        {
            switch (reg.LandUsage)
            {
                case DictionaryConstants.LandUsage.SELF_FARM:
                    selfFarmed += (decimal)reg.Area;
                    break;
                case DictionaryConstants.LandUsage.SUBLEASE:
                    subleased += (decimal)reg.Area;
                    break;
                case DictionaryConstants.LandUsage.CONTRACT:
                    contracted += (decimal)reg.Area;
                    break;
            }
        }

        SelfFarmedLandArea = selfFarmed;
        SubleasedLandArea = subleased;
        ContractedLandArea = contracted;
    }

    /// <summary>
    /// 重新计算收入
    /// </summary>
    [RelayCommand]
    private void RecalculateIncome()
    {
        try
        {
            RecalculateIncomeCore();
        }
        catch (Exception ex)
        {
            // 收入重算绝不因异常崩溃：记录错误，保留上一轮计算结果
            _logger.LogError(ex, "收入重算失败");
        }
    }

    /// <summary>收入重算核心（异常由调用方捕获）</summary>
    private void RecalculateIncomeCore()
    {
        // 从土地登记信息更新汇总字段
        UpdateLandAreasFromRegistrations();

        // 【月收入】计算务工收入：Σ(每人月收入)
        WorkIncomeTotal = _incomeCalculationService.CalculateLaborIncome(LaborIncomes.ToList());

        // 【月收入】计算经营净收入：Σ(每人月收入)
        BusinessIncomeTotal = _incomeCalculationService.CalculateBusinessNetIncome(BusinessIncomes.ToList());

        // 【月收入】计算土地收入（年收入，不除以12）
        if (LandConfirmationGroups.Count == 0)
        {
            LandIncomeTotal = _incomeCalculationService.CalculateLandIncome(
                SelfFarmedLandArea, (double)SelfFarmUnitPrice,
                SubleasedLandArea, (double)SubleaseUnitPrice,
                ContractedLandArea, (double)ContractUnitPrice);
        }

        // 【年收入】计算农业补贴（年总额，不除以12）
        SubsidyTotal = _incomeCalculationService.CalculateSubsidyIncome(Subsidies.ToList());

        // 【年收入】计算赡养费（年值 = Σ每笔年赡养费）
        AlimonyIncome = _incomeCalculationService.CalculateAlimonyAnnual(Supporters.ToList());

        // 【月收入】计算刚性支出（月支出）
        RigidExpenditure = _incomeCalculationService.CalculateRigidExpenditure(RigidExpenditures.ToList());

        // 【月收入】计算财产净收入（月收入）
        PropertyIncomeTotal = PropertyIncomes.Sum(p => p.Amount);

        // 【月收入】计算转移净收入（月收入）
        TransferIncomeTotal = TransferIncomes.Sum(t => t.TotalAmount);

        // 【月收入】计算其他收入（月收入）
        OtherIncomeTotal = OtherIncomes.Sum(o => o.Amount);

        // 【年值基准】年家庭总收入 = Σ(月项×12) + 赡养年值 + 土地年值 + 补贴年值 − 刚性×12，一次性舍入
        TotalAnnualIncome = _incomeCalculationService.CalculateAnnualFamilyIncome(
            WorkIncomeTotal,
            BusinessIncomeTotal,
            PropertyIncomeTotal,
            TransferIncomeTotal,
            OtherIncomeTotal,
            AlimonyIncome,       // 年值
            LandIncomeTotal,     // 年值
            SubsidyTotal,        // 年值
            RigidExpenditure);   // 月值

        // 月值 = 年值÷12（分解显示口径，禁止从月值×12 反推年值）
        TotalFamilyIncome = _incomeCalculationService.MonthlyFromAnnual(TotalAnnualIncome);

        // 年人均 / 月人均（一次舍入，判定与落库同口径）
        PerCapitaAnnualIncome = _incomeCalculationService.CalculatePerCapitaAnnual(TotalAnnualIncome, FamilySize);
        PerCapitaIncome = _incomeCalculationService.PerCapitaMonthly(TotalAnnualIncome, FamilySize);

        // 通知刚性支出明细变化
        OnPropertyChanged(nameof(MedicalExpenditure));
        OnPropertyChanged(nameof(EducationExpenditure));
        OnPropertyChanged(nameof(DisabilityRehabExpenditure));
        OnPropertyChanged(nameof(LivingExpenditure));
        OnPropertyChanged(nameof(IncomeSubtotal));
        OnPropertyChanged(nameof(TotalFamilyIncomeAnnual));
        OnPropertyChanged(nameof(PerCapitaIncomeAnnual));
    }

    #endregion

    #region 分类判定

    [RelayCommand]
    private async Task ClassifyAsync()
    {
        await ExecuteAsync(async ct =>
        {
            _logger.LogBusiness("开始分类判定");

            // 构建申请对象（必须包含身份证号和健康状况，用于推导年龄和判断劳动能力）
            var application = new Application
            {
                Id = _applicationId,
                ApplicantIdCard = ApplicantIdCard,
                HealthStatus = SelectedHealthStatusObj?.Key ?? "",
                HukouType = HukouType?.Key ?? "",
                FamilySize = FamilySize,
                TotalFamilyIncome = TotalFamilyIncome,
                PerCapitaIncome = PerCapitaIncome,
                TotalAnnualIncome = TotalAnnualIncome,
                PerCapitaAnnualIncome = PerCapitaAnnualIncome,
                RigidExpenditure = RigidExpenditure,
                ClassificationResult = ClassificationResult,
                OriginalClassificationResult = _originalClassificationContext ?? ClassificationResult,
                CaregiverType = CaregiverType,
                DestituteSupportType = DestituteSupportType
            };

            // 执行分类判定
            var result = await _classificationService.DetermineClassificationAsync(
                application,
                FamilyMembers.ToList(),
                Supporters.ToList(),
                Caregivers.ToList(),
                ct);

            if (result.IsSuccess && result.Value != null)
            {
                var classification = result.Value;

                ClassificationResult = classification.Classification;
                ClassificationDescription = classification.Description;
                IsEligible = classification.IsEligible;
                IneligibleReason = classification.IneligibleReason;
                var priorGuaranteeForGrace = GuaranteeAmount;
                GuaranteeAmount = classification.GuaranteeAmount;

                // 分类施保
                if (classification.ClassifiedSubsidy != null)
                {
                    ClassifiedSubsidyType = classification.ClassifiedSubsidy.Types;
                    ClassifiedSubsidyAmount = classification.ClassifiedSubsidy.TotalAmount;
                }

                // 渐退期（1B：判定命中先弹确认页，确认后才应用；取消则本次不进渐退、可改数据再判定）
                if (classification.GracePeriod != null && classification.GracePeriod.IsEligible)
                {
                    var selectedMonths = await ShowGracePeriodConfirmAsync(classification, ct);
                    if (selectedMonths is int months && GracePeriodConstants.IsValidMonths(months))
                    {
                        IsInGracePeriod = true;
                        GracePeriodMonths = months;
                        GracePeriodStartDate = classification.GracePeriod.StartDate;
                        GracePeriodEndDate = classification.GracePeriod.StartDate.AddMonths(months).AddDays(-1);
                        OriginalClassificationResult = classification.GracePeriod.OriginalClassification;
                        await ApplyGraceCapAsync(priorGuaranteeForGrace, ct);
                    }
                    else
                    {
                        IsInGracePeriod = false;
                        GraceGrantAmount = null;
                    }
                }
                else
                {
                    IsInGracePeriod = false;
                    GraceGrantAmount = null;
                }
                _gracePeriodEvaluated = true;

                // 特困：自动计算照料护理费（集中供养不发钱，护理费为 0；仅分散供养按能力鉴定计算）
                // 非特困：清零旧照料费（H2——分类离开特困后禁止沿用旧值进总额）
                if (ClassificationConstants.IsCodeDestitute(ClassificationResult))
                {
                    CaregiverSubsidyAmount = IsCentralizedSupport
                        ? 0
                        : await _classificationService.CalculateCareAllowanceAsync(
                            _applicationId, ct);
                }
                else
                {
                    CaregiverSubsidyAmount = 0;
                }

                // 计算保障金总额
                TotalGuaranteeAmount = _guaranteeAmountService.CalculateTotalGuaranteeAmount(
                    GuaranteeAmount,
                    ClassifiedSubsidyAmount,
                    CaregiverSubsidyAmount);

                // 立即保存分类结果到数据库
                await SaveClassificationResultAsync();

                _logger.LogBusiness("分类判定完成",
                    ("分类", classification.Classification),
                    ("描述", classification.Description),
                    ("保障金额", TotalGuaranteeAmount));
            }
            else
            {
                _logger.Error($"Classification 失败: {result.Message}");
            }
        }, "分类判定中...");
    }

    [RelayCommand]
    private async Task DetermineClassificationAsync()
    {
        if (IsDetermining) return;

        // 补全模式：认定结果锁定，不允许重新判定（补全不应改变认定结果）
        if (IsCompletionMode)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "补全模式下认定结果已锁定，不允许重新分类判定", "确定");
            return;
        }

        // 单人保申请锁定分类结果，不允许重新判定
        if (IsSingleRescueApplication)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "该申请已标记为单人保，分类结果已锁定，无需重新判定", "确定");
            return;
        }

        // 已确定档案（Approved/Stopped）仅允许在合法变更流程模式下重算
        if (IsClassificationLocked)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "已确定的档案不允许重新分类判定，请通过经济复核/家庭信息修正等变更流程办理", "确定");
            return;
        }

        try
        {
            IsDetermining = true;
            _logger.LogBusiness("开始分类判定（Step5）");

            // 强制重新计算收入，确保汇总字段是最新的
            RecalculateIncome();

            var application = BuildApplication(ApplicationStatusCodes.DRAFT);
            // 变更链（户主死亡停旧建新等）：带上上游原分类，否则渐退期"低保→低收入"判不出
            application.OriginalClassificationResult =
                _originalClassificationContext ?? application.OriginalClassificationResult;

            var result = await _classificationService.DetermineClassificationAsync(
                application,
                FamilyMembers.ToList(),
                Supporters.ToList(),
                Caregivers.ToList(),
                CancellationToken.None);

            if (result.IsSuccess && result.Value != null)
            {
                var classification = result.Value;
                _lastClassificationResult = classification;

                ClassificationResult = classification.Classification;
                ClassificationDescription = classification.Description;
                IsEligible = classification.IsEligible;
                IneligibleReason = classification.IneligibleReason;
                var priorGuaranteeForGrace = GuaranteeAmount;
                GuaranteeAmount = classification.GuaranteeAmount;
                DeterminationBasis = classification.DeterminationBasis;

                DeterminationDetails.Clear();
                foreach (var detail in classification.DeterminationDetails)
                {
                    DeterminationDetails.Add(detail);
                }

                // 分类施保信息
                if (classification.ClassifiedSubsidy != null)
                {
                    ClassifiedSubsidyTypes = classification.ClassifiedSubsidy.Types;
                    ClassifiedSubsidyCount = classification.ClassifiedSubsidy.Count;
                    ClassifiedSubsidyPerPerson = classification.ClassifiedSubsidy.PerPersonAmount;
                    ClassifiedSubsidyAmount = classification.ClassifiedSubsidy.TotalAmount;
                }

                // 渐退期（1B：判定命中先弹确认页，确认后才应用；取消则本次不进渐退、可改数据再判定）
                if (classification.GracePeriod != null && classification.GracePeriod.IsEligible)
                {
                    var selectedMonths = await ShowGracePeriodConfirmAsync(classification, CancellationToken.None);
                    if (selectedMonths is int months && GracePeriodConstants.IsValidMonths(months))
                    {
                        IsInGracePeriod = true;
                        GracePeriodMonths = months;
                        GracePeriodStartDate = classification.GracePeriod.StartDate;
                        GracePeriodEndDate = classification.GracePeriod.StartDate.AddMonths(months).AddDays(-1);
                        OriginalClassificationResult = classification.GracePeriod.OriginalClassification;
                        await ApplyGraceCapAsync(priorGuaranteeForGrace, CancellationToken.None);
                    }
                    else
                    {
                        IsInGracePeriod = false;
                        GraceGrantAmount = null;
                    }
                }
                else
                {
                    IsInGracePeriod = false;
                    GraceGrantAmount = null;
                }
                _gracePeriodEvaluated = true;

                // 特困：自动计算照料护理费（集中供养不发钱，护理费为 0；仅分散供养按能力鉴定计算）
                // 非特困：清零旧照料费（H2——分类离开特困后禁止沿用旧值进总额）
                if (ClassificationConstants.IsCodeDestitute(ClassificationResult))
                {
                    CaregiverSubsidyAmount = IsCentralizedSupport
                        ? 0
                        : await _classificationService.CalculateCareAllowanceAsync(
                            _applicationId, CancellationToken.None);
                }
                else
                {
                    CaregiverSubsidyAmount = 0;
                }

                TotalGuaranteeAmount = _guaranteeAmountService.CalculateTotalGuaranteeAmount(
                    GuaranteeAmount,
                    ClassifiedSubsidyAmount,
                    CaregiverSubsidyAmount);

                IsClassificationDone = true;

                // 单人保信息
                NeedsSingleRescueDraft = classification.NeedsSingleRescueDraft;
                SingleRescueMembers.Clear();
                if (classification.SingleRescueMembers != null)
                {
                    foreach (var member in classification.SingleRescueMembers)
                    {
                        SingleRescueMembers.Add(member);
                    }
                }
                OnPropertyChanged(nameof(SingleRescueHint));

                // 通知特困分类属性变更
                OnPropertyChanged(nameof(IsDestituteClassification));

                // 立即保存分类结果到数据库
                await SaveClassificationResultAsync();

                _logger.LogBusiness("分类判定完成",
                    ("Classification", ClassificationResult),
                    ("Description", ClassificationDescription),
                    ("IsEligible", IsEligible),
                    ("GuaranteeAmount", GuaranteeAmount));
            }
            else
            {
                _logger.Error($"分类判定失败: {result.Message}");
                await _serviceProvider.GetRequiredService<IDialogService>()
                    .DisplayAlertAsync("错误", $"分类判定失败: {result.Message}", "确定");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"分类判定异常: {ex.Message}");
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("错误", $"分类判定异常: {ex.Message}", "确定");
        }
        finally
        {
            IsDetermining = false;
        }
    }

    /// <summary>
    /// 弹出渐退期确认页（modal，模式与 MemberChangeReasonPopup 一致）：
    /// 确认 => 所选月数（调用方应用渐退状态并继续保存）；取消/异常 => null（本次不进渐退）。
    /// </summary>
    private async Task<int?> ShowGracePeriodConfirmAsync(
        Services.Domain.SocialAssistance.ClassificationResult classification, CancellationToken ct)
    {
        try
        {
            var standard = await GetCachedSubsistenceStandardAsync();
            var grace = classification.GracePeriod;
            var parameter = new GracePeriodConfirmParameter(
                ClassificationConstants.ConvertFromCode(grace.OriginalClassification),
                ClassificationConstants.ConvertFromCode(classification.Classification),
                standard,
                PerCapitaIncome,
                TotalAnnualIncome,
                FamilySize,
                classification.GuaranteeAmount,
                grace.Months,
                grace.StartDate,
                grace.EndDate);

            ContentPage popup;
            Task<GracePeriodConfirmResult> resultTask;
            if (DeviceInfo.Platform == DevicePlatform.Android)
            {
                var mobilePopup = _serviceProvider.GetRequiredService<Pages.Mobile.MobileGracePeriodConfirmPage>();
                mobilePopup.Initialize(parameter);
                popup = mobilePopup;
                resultTask = mobilePopup.Result;
            }
            else
            {
                var desktopPopup = _serviceProvider.GetRequiredService<Pages.ChangeManagement.GracePeriodConfirmPage>();
                desktopPopup.Initialize(parameter);
                popup = desktopPopup;
                resultTask = desktopPopup.Result;
            }

            var navigation = Helpers.WindowNavigator.CurrentPage?.Navigation;
            if (navigation == null)
            {
                _logger.Warn("当前页面为空，无法弹出渐退期确认页");
                return null;
            }
            await navigation.PushModalAsync(popup);
            var result = await resultTask.WaitAsync(ct);
            if (navigation.ModalStack.Contains(popup))
            {
                await navigation.PopModalAsync();
            }
            return result.Confirmed ? result.SelectedMonths : null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.Error($"渐退期确认页异常: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 创建单人保草稿
    /// </summary>
    [RelayCommand]
    private async Task CreateSingleRescueDraftAsync()
    {
        if (!NeedsSingleRescueDraft || SingleRescueMembers.Count == 0)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "当前没有符合单人保条件的成员", "确定");
            return;
        }

        // 先保存当前申请
        if (_applicationId <= 0)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "请先保存当前申请，再创建单人保草稿", "确定");
            return;
        }

        var dialogService = _serviceProvider.GetRequiredService<IDialogService>();

        foreach (var member in SingleRescueMembers)
        {
            var result = await _applicationService.CreateSingleRescueDraftAsync(
                _applicationId, member, CancellationToken.None);

            if (result.IsSuccess)
            {
                _logger.LogBusiness("单人保草稿已创建",
                    ("SourceApplicationId", _applicationId),
                    ("NewApplicationId", result.Value),
                    ("MemberName", NewCosmos.Services.Core.DataMasker.MaskName(member.Name)));

                await dialogService.DisplayAlertAsync("成功",
                    $"已为 {member.Name} 创建单人保草稿申请，请在工作流中继续处理", "确定");
            }
            else
            {
                _logger.Error($"创建单人保草稿失败: {result.Message}");
                await dialogService.DisplayAlertAsync("错误",
                    $"为 {member.Name} 创建单人保草稿失败: {result.Message}", "确定");
            }
        }
    }

    /// <summary>
    /// 编辑能力鉴定
    /// </summary>
    [RelayCommand]
    private async Task EditCapabilityAssessmentAsync()
    {
        if (_applicationId <= 0)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "请先保存申请", "确定");
            return;
        }

        try
        {
            await NavigateToPageAsync<Pages.SocialAssistance.CapabilityAssessmentPage, ApplicationScopedParameter>(new ApplicationScopedParameter(_applicationId));
        }
        catch (Exception ex)
        {
            _logger.Error($"打开能力鉴定失败: {ex.Message}");
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("错误", $"打开能力鉴定失败: {ex.Message}", "确定");
        }
    }

    /// <summary>
    /// 发起一事一议（Step5 分类判定不符合时）
    /// </summary>
    [RelayCommand]
    private async Task OpenSpecialApprovalAsync()
    {
        // 补全模式：认定结果锁定、不改变认定状态，不触发一事一议（完成补全后在复核模式重新认定再申报）
        if (IsCompletionMode)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "数据补全模式下不可发起一事一议。请先完成补全保存，后续在经济状况复核模式重新认定后，按程序申报。", "确定");
            return;
        }

        if (_applicationId <= 0)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "请先保存申请再发起一事一议", "确定");
            return;
        }

        try
        {
            await NavigateToPageAsync<Pages.SocialAssistance.SpecialApprovalFormPage, ApplicationScopedParameter>(new ApplicationScopedParameter(_applicationId));
        }
        catch (Exception ex)
        {
            _logger.Error($"打开一事一议申报表失败: {ex.Message}");
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("错误", $"打开一事一议申报表失败: {ex.Message}", "确定");
        }
    }

    /// <summary>
    /// 保存分类结果到数据库
    /// </summary>
    private async Task SaveClassificationResultAsync()
    {
        if (_applicationId <= 0) return;

        try
        {
            // 保留原状态（Approved 等）——UpdateAsync 不写 status 列，此处为语义一致；
            // 新建/普通草稿兜底 Draft
            var originalStatus = string.IsNullOrEmpty(_originalStatus) ? ApplicationStatusCodes.DRAFT : _originalStatus;
            var application = BuildApplication(originalStatus);
            application.Id = _applicationId;
            // 变更流程模式（复核/成员变更等）必须放行非 Draft 档案：
            // UpdateAsync 默认只允许 Draft（ApplicationStateMachine.IsEditable），
            // 已归档 Approved 档案在 Step5 判定保存会报"当前状态不允许编辑"
            var allowNonEditable = IsCompletionMode || IsChangeMode;
            var updateResult = await _applicationService.UpdateAsync(application, CancellationToken.None, allowNonEditable);
            if (updateResult.IsSuccess)
            {
                // 本次更新改变了 updated_at，刷新并发令牌，避免后续保存误报冲突
                var refreshed = await _applicationService.GetByIdAsync(_applicationId, CancellationToken.None);
                if (refreshed.IsSuccess && refreshed.Value != null && refreshed.Value.UpdatedAt != default)
                    _loadedUpdatedAt = refreshed.Value.UpdatedAt;

                _logger.LogBusiness("分类结果已保存到数据库",
                    ("ApplicationId", _applicationId),
                    ("Classification", ClassificationResult ?? ""));

                // A1：Step5 分类确认即同步渐退行——不依赖完整保存，避免「只出文书不保存」时库里无行
                if (IsInGracePeriod && GracePeriodMonths > 0)
                    await SyncGracePeriodRecordAsync(CancellationToken.None);

                // 接续链（户主死亡/成员变更等停旧建新）Step5 判定跨大类时补写 CategoryAdd：
                // 月报「新增救助明细」跨类新增行与本档变更记录列表依赖此行（服务幂等，失败不阻断分类保存）
                await EnsureChainCategoryAddAsync(CancellationToken.None);
            }
            else
            {
                _logger.Error($"保存分类结果失败: {updateResult.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"保存分类结果异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 接续链跨大类 CategoryAdd 同步（服务端幂等：一致跳过/变化更新/回同大类软删）。
    /// 分类已落库，同步失败仅告警不抛出，避免分类保存被报告类记录拖失败。
    /// </summary>
    private async Task EnsureChainCategoryAddAsync(CancellationToken ct)
    {
        try
        {
            var result = await _changeService.EnsureChainCategoryAddAsync(
                _applicationId,
                string.IsNullOrEmpty(App.CurrentUserName) ? "System" : App.CurrentUserName,
                ct);
            if (result.IsSuccess)
            {
                if (result.Value is long changeId && changeId > 0)
                    _logger.LogBusiness("接续链跨类新增CategoryAdd已同步",
                        ("ApplicationId", _applicationId),
                        ("ChangeId", changeId));
            }
            else
            {
                _logger.Warn($"接续链跨类新增CategoryAdd同步失败: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"接续链跨类新增CategoryAdd同步异常: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ResetClassification()
    {
        // 补全模式：认定结果锁定，不允许重置（补全不应改变认定结果）
        if (IsCompletionMode) return;

        // 已确定档案（Approved/Stopped）在非变更流程模式下不允许重置
        if (IsClassificationLocked) return;

        IsClassificationDone = false;
        ClassificationResult = null;
        ClassificationDescription = null;
        IsEligible = false;
        IneligibleReason = null;
        GuaranteeAmount = 0;
        DeterminationBasis = string.Empty;
        DeterminationDetails.Clear();
        ClassifiedSubsidyTypes = string.Empty;
        ClassifiedSubsidyCount = 0;
        ClassifiedSubsidyPerPerson = 0;
        ClassifiedSubsidyAmount = 0;
        TotalGuaranteeAmount = 0;
        IsInGracePeriod = false;
        GracePeriodMonths = 0;
        GracePeriodStartDate = null;
        GracePeriodEndDate = null;
        OriginalClassificationResult = null;
        OriginalGuaranteeAmount = null;
        // 重置分类结果 ≠ 结清服务端渐退期：保存前须重新判定，禁止内存默认 false 触发 Clear
        _gracePeriodEvaluated = false;
        OnPropertyChanged(nameof(IsDestituteClassification));
        _logger.LogBusiness("重置分类判定结果");
    }

    /// <summary>
    /// 更新供养方式
    /// </summary>
    [RelayCommand]
    private async Task UpdateSupportModeAsync()
    {
        if (SelectedSupportModeObj == null)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "请选择供养方式", "确定");
            return;
        }

        DestituteSupportType = SelectedSupportModeObj.Key;
        _logger.LogBusiness("更新供养方式", ("Type", DestituteSupportType));

        // 重新执行分类判定
        await DetermineClassificationAsync();

        // 保存到数据库
        await SaveClassificationResultAsync();
    }

    /// <summary>
    /// 导航到档案制作页面
    /// </summary>
    private async Task NavigateToArchiveProductionAsync()
    {
        try
        {
            _logger.LogBusiness("开始导航到档案制作页面", ("ApplicationId", _applicationId));
            _logger.LogBusiness("档案制作页面导航开始", ("ApplicationId", _applicationId));
            // 参数先于 Push 注入：页面 OnAppearing 时数据已就绪（原"先推页再加载数据"的时序收敛）
            await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveProductionPage, ApplicationArchiveParameter>(new ApplicationArchiveParameter(_applicationId));
            _logger.LogBusiness("档案制作页面导航完成");
        }
        catch (Exception ex)
        {
            _logger.Error($"导航到档案制作页面失败: {ex.Message}\n{ex.StackTrace}");
        }
    }

    #endregion

    #region 渐退期命令

    [RelayCommand]
    private void SetGracePeriod()
    {
        var info = _gracePeriodService.SetGracePeriod(GracePeriodMonths, OriginalGuaranteeAmount);

        IsInGracePeriod = info.IsInGracePeriod;
        GracePeriodMonths = info.GracePeriodMonths;
        GracePeriodStartDate = info.GracePeriodStartDate;
        GracePeriodEndDate = info.GracePeriodEndDate;
        _gracePeriodEvaluated = true;

        _logger.LogBusiness("设置渐退期",
            ("月数", GracePeriodMonths),
            ("开始", GracePeriodStartDate?.ToString("yyyy-MM-dd") ?? string.Empty),
            ("结束", GracePeriodEndDate?.ToString("yyyy-MM-dd") ?? string.Empty));
    }

    [RelayCommand]
    private void ClearGracePeriod()
    {
        IsInGracePeriod = false;
        GracePeriodMonths = 0;
        GracePeriodStartDate = null;
        GracePeriodEndDate = null;
        OriginalClassificationResult = null;
        OriginalGuaranteeAmount = null;
        GraceGrantAmount = null;
        // 用户显式清除 → 允许保存时落库 Clear
        _gracePeriodEvaluated = true;

        _logger.LogBusiness("清除渐退期");
    }

    #endregion

    #region 保存草稿

    /// <summary>
    /// 保存草稿命令
    /// </summary>
    [RelayCommand]
    private async Task SaveDraftAsync()
    {
        // 查看模式禁止保存
        if (IsViewMode) return;

        // 草稿最小验证：姓名和身份证号不能为空
        if (string.IsNullOrWhiteSpace(ApplicantName))
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "请填写申请人姓名", "确定");
            return;
        }
        if (string.IsNullOrWhiteSpace(ApplicantIdCard))
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "请填写身份证号", "确定");
            return;
        }

        // 如果是特困分类，验证照料人信息
        if (IsDestituteClassification && Caregivers.Count == 0)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "特困供养需要填写照料人信息，请先添加照料人", "确定");
            return;
        }

        // 如果是特困分类，验证供养方式（以 Picker 实际选择为准）
        if (IsDestituteClassification && SelectedSupportModeObj == null)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "特困供养需要选择供养方式，请先选择集中供养或分散供养", "确定");
            return;
        }

        await ExecuteAsync(async ct =>
        {
            _logger.LogBusiness("保存草稿");
            // 补全模式 / 经济复核（Review）/ 家庭修正/编辑家庭信息模式：保存草稿保持原状态（Approved 等），不降级为 Draft；
            // 契合经济复核流程（ChangeViewModel.NavigateToEconomicReviewAsync 以 Review 模式打开表单）
            var saveStatus = (IsCompletionMode || IsReviewMode || IsFamilyCorrectionMode || IsEditFamilyInfoMode)
                ? (string.IsNullOrEmpty(_originalStatus) ? ApplicationStatusCodes.APPROVED : _originalStatus)
                : ApplicationStatusCodes.DRAFT;
            await SaveApplicationInternalAsync(saveStatus, ct);
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("成功", "草稿保存成功", "确定");
        }, "保存草稿中...");
    }

    /// <summary>
    /// 统一的申请保存内部方法（SaveDraft 和 ExecuteSave 共用）。
    /// 整个保存过程运行在同一个数据库事务中：主表 + 家庭成员 + 赡养人 + 入户调查/照料人 + 经济明细
    /// 任一环节失败即抛 BusinessException → 事务自动回滚，ExecuteAsync 捕获后向用户展示错误
    /// （成功提示只会在本方法无异常返回后出现）。
    /// 内部各服务通过 BeginTransactionScopeAsync 嵌套加入本环境事务（嵌套作用域提交权归最外层）。
    /// </summary>
    private async Task SaveApplicationInternalAsync(string status, CancellationToken ct)
    {
        // Step2 新增的赡养抚养扶养人合并进 Supporters（未点"导入家庭成员"也不丢；赡养费计入收入）
        await MergeSupportMembersIntoSupportersAsync();

        // 强制重新计算收入，确保汇总字段是最新的
        LoadingMessage = "正在计算收入...";
        RecalculateIncome();

        var application = BuildApplication(status);

        var wasCreateMode = _applicationId <= 0;
        try
        {
            await _applicationService.ExecuteInTransactionAsync(
                transactionCt => SaveApplicationCoreAsync(application, transactionCt), ct);
        }
        catch
        {
            // 新建流程失败时事务已回滚（记录不存在），还原为新建状态，
            // 避免用户重试时误走"更新不存在记录"的路径
            if (wasCreateMode)
            {
                _applicationId = 0;
                OperationMode = FormOperationMode.Create;
                _loadedUpdatedAt = null;
                _loadedCurrentStep = 0;
            }
            throw;
        }

        _logger.LogBusiness("申请保存成功", ("ApplicationId", _applicationId), ("Status", status));
    }

    /// <summary>
    /// 申请保存事务体（主表 + 家庭成员 + 赡养人 + 入户调查 + 经济明细 + 渐退期）。
    /// 由 <see cref="IApplicationService.ExecuteInTransactionAsync"/> 包裹：正常完成提交，异常回滚。
    /// </summary>
    private async Task SaveApplicationCoreAsync(Application application, CancellationToken ct)
    {
        LoadingMessage = "正在保存申请信息...";
        if (_applicationId > 0)
        {
            // 编辑模式：更新已有记录（携带乐观并发令牌）；补全模式允许更新已建档的 Approved 档案
            application.Id = _applicationId;
            var updateResult = await _applicationService.UpdateAsync(application, ct, IsCompletionMode || IsEditFamilyInfoMode || IsFamilyCorrectionMode);
            if (!updateResult.IsSuccess)
            {
                _logger.Error($"申请保存失败: {updateResult.Message}");
                throw new BusinessException(
                    string.IsNullOrEmpty(updateResult.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : updateResult.ErrorCode,
                    updateResult.Message ?? "申请保存失败");
            }
        }
        else
        {
            // 新建模式：删除同身份证旧草稿 + 创建新记录
            var existsResult = await _applicationService.GetByIdCardAsync(ApplicantIdCard, ct);
            if (existsResult.IsSuccess && existsResult.Value?.Count > 0)
            {
                foreach (var existing in existsResult.Value)
                {
                    if (existing.Status == ApplicationStatusCodes.DRAFT)
                    {
                        var deleteResult = await _applicationService.DeleteAsync(existing.Id, ct);
                        if (!deleteResult.IsSuccess)
                        {
                            _logger.Error($"删除旧草稿失败: {deleteResult.Message}");
                            throw new BusinessException(
                                string.IsNullOrEmpty(deleteResult.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : deleteResult.ErrorCode,
                                $"删除旧草稿失败: {deleteResult.Message}");
                        }
                        _logger.LogBusiness("已删除旧草稿", ("OldApplicationId", existing.Id));
                    }
                }
            }

            var createResult = await _applicationService.CreateAsync(application, ct);
            if (!createResult.IsSuccess)
            {
                _logger.Error($"申请创建失败: {createResult.Message}");
                throw new BusinessException(
                    string.IsNullOrEmpty(createResult.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : createResult.ErrorCode,
                    createResult.Message ?? "申请创建失败");
            }
            _applicationId = createResult.Value;
            OperationMode = FormOperationMode.Edit;
        }

        // 保存家庭成员（失败抛异常），返回 IdCard → 新 ID 映射
        LoadingMessage = "正在保存家庭成员...";
        var memberIdCardToNewId = await SaveFamilyMembersAsync(ct);

        // 保存赡养人（失败抛异常），传入新 ID 映射避免 DB 查询
        LoadingMessage = "正在保存赡养人...";
        await SaveSupportersAsync(memberIdCardToNewId, ct);

        // 保存入户调查（失败抛异常）
        LoadingMessage = "正在保存入户调查...";
        await SaveHouseholdSurveyAsync(ct);

        // 保存经济明细（可能需要较长时间，失败抛异常）
        LoadingMessage = "正在保存经济明细...";
        await SaveEconomicDetailsAsync(ct);

        // 同步渐退期记录到独立表 nc_biz_grace_periods（与主表同事务提交）
        await SyncGracePeriodRecordAsync(ct);

        // 刷新乐观并发令牌：在事务内读取本次写入的 updated_at，
        // 避免下一次保存因令牌过期而误报并发冲突
        var refreshedResult = await _applicationService.GetByIdAsync(_applicationId, ct);
        if (!refreshedResult.IsSuccess)
        {
            throw new BusinessException(
                string.IsNullOrEmpty(refreshedResult.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : refreshedResult.ErrorCode,
                refreshedResult.Message ?? "读取保存结果失败");
        }
        if (refreshedResult.Value != null && refreshedResult.Value.UpdatedAt != default)
            _loadedUpdatedAt = refreshedResult.Value.UpdatedAt;
    }

    /// <summary>
    /// 同步渐退期记录到独立表 nc_biz_grace_periods（与主表同事务）
    /// M1：仅当本会话已判定/加载过渐退期状态（_gracePeriodEvaluated）才允许 Clear——
    /// 内存默认 false 不得误清服务端活动记录（户主死亡新建档等）。
    /// </summary>
    private async Task SyncGracePeriodRecordAsync(CancellationToken ct)
    {
        if (IsInGracePeriod)
        {
            var start = GracePeriodStartDate ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var months = GracePeriodMonths > 0 ? GracePeriodMonths : GracePeriodConstants.DEFAULT_MONTHS;
            var end = GracePeriodEndDate ?? start.AddMonths(months).AddDays(-1);

            var actRes = await _gracePeriodService.ActivateAsync(
                _applicationId, months, start, end,
                string.IsNullOrEmpty(OriginalClassificationResult) ? null : OriginalClassificationResult,
                OriginalGuaranteeAmount > 0 ? OriginalGuaranteeAmount : null,
                GraceGrantAmount,
                ct);
            if (!actRes.IsSuccess)
            {
                throw new BusinessException(
                    string.IsNullOrEmpty(actRes.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : actRes.ErrorCode,
                    actRes.Message ?? "渐退期记录保存失败");
            }
            _gracePeriodEvaluated = true;

            // 超限封顶产生减发 → 补写 FundChange，供月报「保障金减发表」捕获（幂等：同户同额只写一次）
            if (OriginalGuaranteeAmount is decimal orig && orig > 0
                && GraceGrantAmount is decimal grant && grant > 0
                && orig > grant)
            {
                await RecordGraceCapFundChangeAsync(orig, grant, ct);
            }
        }
        else if (_gracePeriodEvaluated)
        {
            var clrRes = await _gracePeriodService.ClearAsync(_applicationId, ct);
            if (!clrRes.IsSuccess)
            {
                throw new BusinessException(
                    string.IsNullOrEmpty(clrRes.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : clrRes.ErrorCode,
                    clrRes.Message ?? "渐退期记录清除失败");
            }
        }
    }

    /// <summary>
    /// 保存赡养人（失败抛 BusinessException，由外层统一事务回滚）
    /// </summary>
    private async Task SaveSupportersAsync(Dictionary<string, long> memberIdCardToNewId, CancellationToken ct)
    {
        var supporterService = _supporterService;

        // 用 SaveFamilyMembersAsync 返回的 IdCard→新ID 映射同步 Supporters 的 ID，
        // 不再查 DB——新 ID 已在内存中，直接使用。
        // 身份证无匹配的家庭成员不再移除：可能是 Step3 手动新增或用户改过身份证的独立赡养人
        //（Step2 删除赡养人时已在 RemoveFamilyMemberAsync 同步移除对应 Supporter），
        // 静默移除会丢数据（曾导致"部分数据保存失败"）。
        foreach (var supporter in Supporters.ToList())
        {
            if (string.IsNullOrWhiteSpace(supporter.IdCard)) continue;

            var idCard = supporter.IdCard.Trim();
            if (memberIdCardToNewId.TryGetValue(idCard, out var newId))
            {
                supporter.Id = newId;
            }
        }

        // 直接保存当前内存中的赡养人列表（含用户编辑的赡养费等字段）
        var supporters = Supporters.ToList();
        var result = await supporterService.SaveAsync(_applicationId, supporters, ct);
        if (!result.IsSuccess)
        {
            _logger.Error($"赡养人保存失败: {result.Message}");
            throw new BusinessException(
                string.IsNullOrEmpty(result.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : result.ErrorCode,
                $"赡养人保存失败: {result.Message}");
        }
        _logger.LogBusiness("赡养人保存完成", ("Count", supporters.Count));
    }

    /// <summary>
    /// 保存入户调查数据
    /// </summary>
    private async Task SaveHouseholdSurveyAsync(CancellationToken ct)
    {
        // 入户调查无数据时仅跳过调查表本身；照料人必须无条件保存
        // （此前早退连带跳过照料人，特困申请只填照料人时会静默丢失）
        if (SurveyDate.HasValue || !string.IsNullOrWhiteSpace(SurveyorName))
        {
            var surveyService = _householdSurveyService;
            var survey = new HouseholdSurvey
            {
                ApplicationId = _applicationId,
                // 补全模式：入户调查日期强制取纳入时间（锁定值），防止绕过 UI 改动
                SurveyDate = _lockedSurveyDate ?? SurveyDate ?? DateTime.Today,
                SurveyorName = SurveyorName,
                SurveyorOrganization = SurveyorOrganization,
                RespondentName = RespondentName,
                RespondentRelation = RespondentRelation,
                ApplicationReason = SelectedApplicationReasonObj?.Key ?? "",
                ApplicationReasonDetail = ApplicationReasonDetail,
                SurveyConclusion = SurveyConclusion,
                SurveyNotes = SurveyNotes
            };
            var surveyResult = await surveyService.SaveAsync(_applicationId, survey, ct);
            if (!surveyResult.IsSuccess)
            {
                _logger.Error($"入户调查保存失败: {surveyResult.Message}");
                throw new BusinessException(
                    string.IsNullOrEmpty(surveyResult.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : surveyResult.ErrorCode,
                    $"入户调查保存失败: {surveyResult.Message}");
            }
        }

        // 保存照料人
        var caregiverService = _caregiverService;
        var caregiverResult = await caregiverService.SaveAsync(_applicationId, Caregivers.ToList(), ct);
        if (!caregiverResult.IsSuccess)
        {
            _logger.Error($"照料人保存失败: {caregiverResult.Message}");
            throw new BusinessException(
                string.IsNullOrEmpty(caregiverResult.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : caregiverResult.ErrorCode,
                $"照料人保存失败: {caregiverResult.Message}");
        }

        _logger.LogBusiness("入户调查+照料人保存完成");
    }

    /// <summary>
    /// 保存经济明细
    /// </summary>
    private async Task SaveEconomicDetailsAsync(CancellationToken ct)
    {
        var result = await _economicDetailService.SaveAllAsync(_applicationId,
            LaborIncomes.ToList(),
            BusinessIncomes.ToList(),
            PropertyIncomes.ToList(),
            TransferIncomes.ToList(),
            OtherIncomes.ToList(),
            Subsidies.ToList(),
            BreedingIncomes.ToList(),
            RigidExpenditures.ToList(),
            FamilyProperties.ToList(),
            Vehicles.ToList(),
            Machineries.ToList(),
            FinancialAssets.ToList(),
            LandRegistrations.ToList(),
            LandConfirmationGroups.ToList(),
            ct);

        if (result.IsSuccess)
        {
            _logger.LogBusiness("经济明细保存完成");

            // 同步更新 Application 表的收入汇总字段
            await UpdateApplicationIncomeAsync(ct);
        }
        else
        {
            _logger.Error($"经济明细保存失败: {result.Message}");
            // 注意：Result.ErrorCode 非 null（默认空串），必须用 IsNullOrEmpty 判断
            throw new BusinessException(
                string.IsNullOrEmpty(result.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : result.ErrorCode,
                result.Message ?? "经济明细保存失败");
        }
    }

    /// <summary>
    /// 更新 Application 表的收入汇总字段（失败抛 BusinessException，由外层统一事务回滚；
    /// 此前失败被静默吞掉，会造成明细与汇总不一致）
    /// </summary>
    private async Task UpdateApplicationIncomeAsync(CancellationToken ct)
    {
        var request = new EconomicInfoUpdateRequest
        {
            ApplicationId = _applicationId,
            WageIncome = WorkIncomeTotal,
            BusinessIncome = BusinessIncomeTotal,
            PropertyIncome = PropertyIncomeTotal,
            TransferIncome = TransferIncomeTotal,
            OtherIncome = OtherIncomeTotal,
            SupportIncome = AlimonyIncome,
            RigidExpenditure = RigidExpenditure,
            LandIncome = LandIncomeTotal,
            SubsidyIncome = SubsidyTotal,
            TotalAnnualIncome = TotalAnnualIncome,
            PerCapitaAnnualIncome = PerCapitaAnnualIncome,
            FamilySize = FamilySize,
            UpdatedBy = App.CurrentUserId?.ToString() ?? "system"
        };

        var updateResult = await _applicationService.UpdateEconomicInfoAsync(request, ct);
        if (!updateResult.IsSuccess)
        {
            _logger.Error($"Application收入汇总更新失败: {updateResult.Message}");
            throw new BusinessException(
                string.IsNullOrEmpty(updateResult.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : updateResult.ErrorCode,
                $"收入汇总更新失败: {updateResult.Message}");
        }

        _logger.LogBusiness("Application收入汇总已更新",
            ("ApplicationId", _applicationId),
            ("TotalIncome", TotalFamilyIncome),
            ("PerCapitaIncome", PerCapitaIncome));
    }

    /// <summary>
    /// 保存家庭成员（先删除旧的，再重新插入）。
    /// 任一成员保存失败会聚合为一个 BusinessException 抛出，由外层统一事务回滚，
    /// 避免"删了旧成员、只插入了一半新成员"的中间状态被提交。
    /// </summary>
    private async Task<Dictionary<string, long>> SaveFamilyMembersAsync(CancellationToken ct)
    {
        var idCardToNewId = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        // ── 第1步：先软删该申请的全部现有成员（同事务，失败整体回滚，零数据丢失风险）──
        // 这样做解决"先插后删+AddAsync防重复校验"的逻辑死锁：
        // 先插旧记录未删 → COUNT(*) > 0 → 校验必失败 → 保存必回滚
        var deleteResult = await _familyMemberService.DeleteByApplicationIdAsync(_applicationId, ct);
        if (deleteResult.IsFailure)
        {
            _logger.Error($"删除旧家庭成员失败: {deleteResult.Message}");
            throw new BusinessException(
                string.IsNullOrEmpty(deleteResult.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : deleteResult.ErrorCode,
                $"删除旧家庭成员失败: {deleteResult.Message}");
        }
        if (deleteResult.Value > 0)
            _logger.LogBusiness("已软删旧家庭成员", ("DeletedCount", deleteResult.Value.ToString()));

        // ── 第2步：插入所有新家庭成员（旧记录已删，AddAsync 防重复校验自然通过）──
        // 防重复：同批表单内可能携带重复身份证（历史导入脏数据、用户重复录入），
        // 保留首个出现、跳过后续重复并记日志——否则 AddAsync 的批内防重会命中刚插入的
        // 兄弟行，把整单保存卡死在"该身份证号已在此申请中存在"。
        var seenIdCards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skippedDuplicates = new List<string>();
        var savedCount = 0;
        var failures = new List<string>();
        foreach (var member in FamilyMembers)
        {
            if (string.IsNullOrWhiteSpace(member.Name) || string.IsNullOrWhiteSpace(member.IdCard))
                continue;

            var idCard = member.IdCard.Trim();
            if (!seenIdCards.Add(idCard))
            {
                var masked = DataMasker.MaskIdCard(idCard);
                _logger.Info($"跳过重复身份证成员: {DataMasker.MaskName(member.Name)}({masked})");
                skippedDuplicates.Add($"{DataMasker.MaskName(member.Name)}({masked})");
                continue;
            }

            var request = new FamilyMemberCreateRequest
            {
                ApplicationId = _applicationId,
                Name = member.Name,
                IdCard = member.IdCard,
                Relation = member.RelationshipToHead,
                Gender = member.Gender,
                Age = member.Age,
                Ethnicity = member.Ethnicity,
                Phone = member.Phone,
                HukouType = member.HukouType,
                HukouAddress = member.HukouAddress,
                HomeProvince = member.HomeProvince,
                HomeCity = member.HomeCity,
                HomeDistrict = member.HomeDistrict,
                HomeTown = member.HomeTown,
                HomeVillage = member.HomeVillage,
                HomeAddress = member.HomeAddress,
                HukouProvince = member.HukouProvince,
                HukouCity = member.HukouCity,
                HukouDistrict = member.HukouDistrict,
                HukouTown = member.HukouTown,
                MaritalStatus = member.MaritalStatus,
                EducationLevel = member.EducationLevel,
                PoliticalStatus = member.PoliticalStatus,
                HealthStatus = member.HealthStatus,
                HasSevereIllness = member.IsSevereDisability,
                HasDisability = member.IsDisabled,
                BirthDate = member.BirthDate,
                DisabilityType = member.DisabilityType,
                DisabilityLevel = member.DisabilityLevel,
                DisabilityCertificateNo = member.DisabilityCertificateNo,
                DiseaseCategory = member.DiseaseCategory,
                DiseaseName = member.DiseaseName,
                SecondaryDiseaseName = member.SecondaryDisease,
                DiseaseCode = member.DiseaseCode,
                IsSevereDisease = member.IsSevereDisease,
                IsLaborExempt = member.IsLaborExempt,
                IsHouseholdHead = member.IsHouseholdHead,
                MemberCategory = member.MemberCategory,
                EmploymentStatus = member.EmploymentStatus,
                WorkUnit = member.WorkUnit,
                MainIncomeSource = member.MainIncomeSource,
                AnnualIncome = member.AnnualIncome,
                WorkCapacity = member.WorkCapacity,
                MonthlyIncomeCapacity = member.MonthlyIncomeCapacity,
                FamilySize = member.FamilySize,
                PersonType = member.PersonType,
                AnnualSupportFee = member.AnnualSupportFee,
                MonthlySupportFee = member.MonthlySupportFee,
                SupportMonths = member.SupportMonths,
                IsSupportAbility = member.IsSupportAbility,
            };

            var result = await _familyMemberService.AddAsync(request, ct);
            if (result.IsSuccess)
            {
                savedCount++;
                // 收集 IdCard → 新 ID 映射，供 SaveSupportersAsync 使用（避免额外 DB 查询）
                if (result.Value > 0)
                    idCardToNewId[idCard] = result.Value;
            }
            else
            {
                _logger.Error($"保存家庭成员失败: {DataMasker.MaskName(member.Name)}, 错误: {result.Message}");
                failures.Add($"{member.Name}：{result.Message}");
            }
        }

        if (skippedDuplicates.Count > 0)
            _logger.LogBusiness("已跳过重复身份证成员", ("Skipped", string.Join("；", skippedDuplicates)));

        if (failures.Count > 0)
        {
            throw new BusinessException(ErrorCodes.DB_QUERY_ERROR,
                $"家庭成员保存失败（{failures.Count}/{FamilyMembers.Count}）：{string.Join("；", failures)}");
        }

        _logger.LogBusiness("家庭成员保存完成", ("Total", FamilyMembers.Count.ToString()), ("Saved", savedCount.ToString()));
        return idCardToNewId;
    }

    #endregion

    #region 验证和保存

    protected override Task<Result> ValidateCurrentStepAsync()
    {
        return Task.FromResult(CurrentStep switch
        {
            1 => ValidateStep1(),
            2 => ValidateStep2(),
            3 => ValidateStep3(),
            4 => ValidateStep4(),
            5 => ValidateStep5(),
            _ => Result.Success()
        });
    }

    private Result ValidateStep1()
    {
        if (string.IsNullOrWhiteSpace(ApplicantName))
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "申请人姓名不能为空");

        if (string.IsNullOrWhiteSpace(ApplicantIdCard))
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "申请人身份证号不能为空");

        if (!Helpers.IdCardValidator.IsValid(ApplicantIdCard))
            return Result.Failure(ErrorCodes.INVALID_ID_CARD, "身份证号格式不正确");

        return Result.Success();
    }

    private Result ValidateStep2()
    {
        if (FamilySize < 1)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "家庭人数必须大于0");

        // 验证家庭成员
        foreach (var member in FamilyMembers)
        {
            if (string.IsNullOrWhiteSpace(member.Name))
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, "家庭成员姓名不能为空");

            if (string.IsNullOrWhiteSpace(member.IdCard))
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, $"家庭成员 {member.Name} 的身份证号不能为空");

            if (!Helpers.IdCardValidator.IsValid(member.IdCard))
                return Result.Failure(ErrorCodes.INVALID_ID_CARD, $"家庭成员 {member.Name} 的身份证号格式不正确");

            if (string.IsNullOrWhiteSpace(member.RelationshipToHead))
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, $"家庭成员 {member.Name} 的与户主关系不能为空");
        }

        // 免于劳动力判定联动校验：任一成员勾选时，本户须存在重病或重残者（含户主本人）可供其照顾
        if (FamilyMembers.Any(m => m.IsLaborExempt))
        {
            var exemptNames = FamilyMembers.Where(m => m.IsLaborExempt)
                .Select(m => m.Name).ToList();

            // 照顾目标 = 户主本人（重病标记/重度残疾等级）+ 其他成员（重病/重残标记）
            bool headHasSevereCondition =
                IsSevereDisease   // 户主重病
                || (!string.IsNullOrEmpty(SelectedDisabilityLevelKey)
                    && _severeLevelKeys.Contains(SelectedDisabilityLevelKey));  // 户主重度残疾
            var hasCareTarget = headHasSevereCondition
                || FamilyMembers.Any(m =>
                    !m.IsLaborExempt && (m.IsSevereDisease || m.IsSevereDisability));
            if (!hasCareTarget)
            {
                var who = string.Join("、", exemptNames);
                return Result.Failure(ErrorCodes.VALIDATION_FAILED,
                    $"家庭成员 [{who}] 勾选了\"因照顾重病重残亲属免于劳动力判定\"，但本户未勾选任何重病或重残成员，请先在其他成员的健康状况中如实标注");
            }
        }

        return Result.Success();
    }

    private Result ValidateStep3()
    {
        // 验证务工收入
        foreach (var income in LaborIncomes)
        {
            if (income.MonthlyIncome < 0)
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, "务工收入不能为负数");
        }

        // 验证经营收入
        foreach (var income in BusinessIncomes)
        {
            if (income.MonthlyIncome < 0)
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, "经营收入不能为负数");
        }

        // 验证刚性支出
        foreach (var expenditure in RigidExpenditures)
        {
            if (expenditure.Amount < 0)
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, "刚性支出不能为负数");
        }

        // 验证土地面积
        if (FamilyLandArea < 0)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "家庭土地面积不能为负数");

        return Result.Success();
    }

    private Result ValidateStep4()
    {
        // 验证调查日期
        if (!SurveyDate.HasValue)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "请选择调查日期");

        // 验证调查员
        if (string.IsNullOrWhiteSpace(SurveyorName))
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "请填写调查员姓名");

        // 验证申请理由
        if (SelectedApplicationReasonObj == null)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "请选择申请理由");

        // 验证申请原因详情
        if (string.IsNullOrWhiteSpace(ApplicationReasonDetail))
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "请填写申请原因详情");

        return Result.Success();
    }

    private Result ValidateStep5()
    {
        // 强校验：必须已进行分类判定
        if (string.IsNullOrEmpty(ClassificationResult))
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "请先进行分类判定");

        // 变更流程模式（复核/家庭修正/编辑家庭信息/成员变更）：必须在本次会话点过 Step5「分类判定」。
        // 分类/保障金只有判定按钮才落库（SaveClassificationResultAsync → UpdateAsync），
        // 跳过判定直接保存会让复核/变更结果丢失（只留变更记录、分类与保障金不落库），
        // 且 ChangeService 的"新旧对比"因缺少判定环节而无法判定是否需停旧建新。
        if (IsChangeMode && !IsClassificationDone)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED,
                "请在 Step5 点击「分类判定」完成本次认定后再保存（变更流程必须以本次判定结果为准）");

        return Result.Success();
    }

    protected override Task<Result> ValidateAllStepsAsync()
    {
        var step1 = ValidateStep1();
        if (!step1.IsSuccess) return Task.FromResult(step1);

        var step2 = ValidateStep2();
        if (!step2.IsSuccess) return Task.FromResult(step2);

        return Task.FromResult(Result.Success());
    }

    /// <summary>
    /// 构建 Application 实体对象
    /// </summary>
    private Application BuildApplication(string status)
    {
        return new Application
        {
            Id = _applicationId,
            ApplicantName = ApplicantName,
            ApplicantIdCard = ApplicantIdCard,
            ApplicantPhone = ApplicantPhone,
            HukouType = HukouType?.Key ?? DefaultValuesConstants.HUKOU_TYPE_KEY,
            Gender = Gender,
            Province = SelectedProvince,
        City = SelectedCity,
            District = SelectedDistrict,
            Town = SelectedTown,
            Community = SelectedVillage,
            Address = Address,
            HukouAddress = HukouAddress,
            BankName = BankName?.Trim() ?? string.Empty,
            BankAccount = BankAccount?.Trim() ?? string.Empty,
            Ethnicity = SelectedEthnicity?.Key ?? DefaultValuesConstants.ETHNICITY_KEY,
            MaritalStatus = SelectedMaritalStatus?.Key ?? DefaultValuesConstants.MARITAL_STATUS_KEY,
            EducationLevel = SelectedEducationLevel?.Key ?? DefaultValuesConstants.EDUCATION_LEVEL_KEY,
            PoliticalStatus = SelectedPoliticalStatus?.Key ?? DefaultValuesConstants.POLITICAL_STATUS_KEY,
            DisabilityCardNo = DisabilityCardNo,
            DisabilityType = SelectedDisabilityTypeObj?.Key ?? "",
            DisabilityLevel = SelectedDisabilityLevelObj?.Key ?? "",
            HealthStatus = SelectedHealthStatusObj?.Key ?? DefaultValuesConstants.HEALTH_STATUS_KEY,
            DiseaseName = SelectedDiseaseCategoryObj?.Key ?? DefaultValuesConstants.DISEASE_CATEGORY_KEY,
            SecondaryDiseaseName = string.IsNullOrWhiteSpace(DiseaseNameText) ? (SelectedDiseaseNameObj?.Key ?? "") : DiseaseNameText,
            DiseaseCode = DiseaseCode ?? "",
            IsSevereDisease = IsSevereDisease,
            ApplicationReason = SelectedApplicationReasonObj?.Key ?? DefaultValuesConstants.APPLICATION_REASON_KEY,
            ApplicationReasonDetail = ApplicationReasonDetail,
            FamilySize = FamilySize,
            WorkIncomeTotal = WorkIncomeTotal,
            BusinessIncomeTotal = BusinessIncomeTotal,
            PropertyIncomeTotal = PropertyIncomeTotal,
            TransferIncomeTotal = TransferIncomeTotal,
            OtherIncomeTotal = OtherIncomeTotal,
            AlimonyIncome = AlimonyIncome,
            TotalFamilyIncome = TotalFamilyIncome,
            PerCapitaIncome = PerCapitaIncome,
            TotalAnnualIncome = TotalAnnualIncome,
            PerCapitaAnnualIncome = PerCapitaAnnualIncome,
            RigidExpenditure = RigidExpenditure,
            FamilyLandArea = FamilyLandArea,
            SelfFarmedLandArea = SelfFarmedLandArea,
            SubleasedLandArea = SubleasedLandArea,
            ContractedLandArea = ContractedLandArea,
            LandIncomeTotal = LandIncomeTotal,
            SubsidyTotal = SubsidyTotal,
            ClassificationResult = ClassificationResult,
            IsEligible = IsEligible,
            ClassifiedSubsidyType = ClassifiedSubsidyType,
            ClassifiedSubsidyAmount = ClassifiedSubsidyAmount,
            HouseholdMonthlyGuaranteeAmount = GuaranteeAmount,
            CaregiverSubsidyAmount = CaregiverSubsidyAmount,
            TotalGuaranteeAmount = TotalGuaranteeAmount,
            IsInGracePeriod = IsInGracePeriod,
            GracePeriodMonths = IsInGracePeriod ? GracePeriodMonths : null,
            GracePeriodStartDate = GracePeriodStartDate,
            GracePeriodEndDate = GracePeriodEndDate,
            // 变更链上游原分类优先（内存承载，主表列已删）；渐退确认后 VM 属性已有值时用 VM
            OriginalClassificationResult = _originalClassificationContext ?? OriginalClassificationResult,
            OriginalGuaranteeAmount = OriginalGuaranteeAmount,
            CaregiverType = CaregiverType,
            DestituteSupportType = DestituteSupportType,
            SupportInstitutionId = SelectedInstitution?.Id ?? _loadedSupportInstitutionId,
            SupportInstitutionName = SelectedInstitution?.Name ?? string.Empty,
            SupportInstitutionFee = SelectedInstitution?.TotalFee ?? 0,
            // 表单未编辑的主表字段：原样回写，避免保存时被默认值清零
            SupportMode = _loadedSupportMode,
            ConfirmedFamilySize = _loadedConfirmedFamilySize,
            PersonCategoryProtectionTotalAmount = _loadedPersonCategoryProtectionTotalAmount,
            IsSpecialApproval = _loadedIsSpecialApproval,
            SpecialApprovalId = _loadedSpecialApprovalId,
            // 补全模式保存时写回已建档标记（current_step=6），UI 归位5不落库；
            // 变更流程模式（复核/家庭修正/编辑家庭信息/成员变更）UI 步骤被强制归位（3/1/2），
            // 不代表档案真实进度 —— 回写加载时的库中步骤，否则已归档的 step=6 会被冲成 5，
            // 档案从「已完结档案」掉进「已建档未提交」。
            CurrentStep = IsCompletionMode ? 6
                : (IsChangeMode && _loadedCurrentStep > 0) ? _loadedCurrentStep
                : CurrentStep,
            Status = status,
            IsSingleRescue = IsSingleRescueApplication,
            UpdatedBy = "System",
            CreatedBy = OperationMode == FormOperationMode.Create ? "System" : null,
            // 乐观并发令牌约定：UpdatedAt 携带"加载时的 updated_at"；
            // default 表示无令牌（UpdateAsync 将跳过并发检查）
            UpdatedAt = _loadedUpdatedAt ?? default
        };
    }

    protected override async Task<Result> ExecuteSaveAsync()
    {
        // 查看模式禁止保存
        if (IsViewMode) return Result.Failure(ErrorCodes.VALIDATION_FAILED, "查看模式不可保存");

        var validation = ValidateStep1();
        if (!validation.IsSuccess) return validation;

        // 提交前强制 Step5 已分类判定（补全/成员变更走各自路径，不在这里拦）。
        // 复核模式必须拦：分类/保障金只有 Step5「分类判定」按钮才落库（SaveClassificationResultAsync），
        // 跳过判定直接保存会让复核结果丢失（只留变更记录、分类与保障金不落库）。
        if (!IsCompletionMode && !IsMemberChangeMode)
        {
            var step5 = ValidateStep5();
            if (!step5.IsSuccess) return step5;
        }

        // 补全模式：保存前提示是否仍有未补全的关键字段（不阻断，仅提醒）
        if (IsCompletionMode)
        {
            var reminder = BuildCompletionReminder();
            if (reminder != null)
            {
                var dialog = _serviceProvider.GetRequiredService<IDialogService>();
                var proceed = await dialog.DisplayAlertAsync("数据补全提醒", reminder, "继续保存", "返回补全");
                if (!proceed) return Result.Failure(ErrorCodes.VALIDATION_FAILED, "用户取消保存，返回继续补全");
            }
        }

        // 经济复核模式：走经济复核专用保存（重新判定 + 记录变更），完成后返回变更页
        // 家庭成员变更模式：走成员变更专用保存（成员增删落库 + 重新判定 + 停旧建新）
        // 家庭信息修正/编辑家庭信息模式走普通保存路径（直接更新原档案，不创建新档案）
        if (IsReviewMode)
        {
            return await ExecuteReviewSaveAsync();
        }
        if (IsMemberChangeMode)
        {
            return await ExecuteMemberChangeSaveAsync();
        }

        await ExecuteAsync(async ct =>
        {

            // 补全模式 / 经济复核（Review）/ 家庭修正/编辑家庭信息模式保存时保留原状态（Approved 等），其他模式按原逻辑保存为草稿
            var saveStatus = (IsCompletionMode || IsReviewMode || IsFamilyCorrectionMode || IsEditFamilyInfoMode)
                ? (string.IsNullOrEmpty(_originalStatus) ? ApplicationStatusCodes.APPROVED : _originalStatus)
                : ApplicationStatusCodes.DRAFT;
            await SaveApplicationInternalAsync(saveStatus, ct);
        }, "保存申请...");

        // 保存失败时不导航（ExecuteAsync 捕获异常后 ErrorMessage 非空）
        if (ErrorMessage != null) return Result.Failure(ErrorCodes.VALIDATION_FAILED, ErrorMessage);

        // 状态闭环：从资产核查任务进入并建档成功后，整户核查状态回写为 '2'（已建档），
        // 使工作流"有报告未建档"清单真实清零（失败容忍，不阻断保存流程）
        if (AssetCheckData != null && AssetCheckData.Id > 0 && _applicationId > 0)
        {
            try
            {
                var statusResult = await _assetVerificationService.UpdateCheckStatusAsync(AssetCheckData.Id, AssetCheckStatusConstants.INCLUDED, CancellationToken.None);
                if (statusResult.IsSuccess)
                    _logger.LogBusiness("核查任务状态闭环: 已建档", ("AssetCheckId", AssetCheckData.Id), ("ApplicationId", _applicationId));
                else
                    _logger.Error($"回写核查状态'{AssetCheckStatusConstants.INCLUDED}'失败: {statusResult.Message}");
            }
            catch (Exception ex)
            {
                _logger.Error($"回写核查状态'{AssetCheckStatusConstants.INCLUDED}'异常: {ex.Message}");
            }
        }

        // 保存成功后：补全模式返回上一页（变更页），其他模式导航到档案制作页面
        if (_applicationId > 0)
        {
            if (IsCompletionMode)
            {
                // 导入库建档档案补全保存后，标记已补全（此后经济复核进入复核模式，可重新判定分类）
                var markResult = await _applicationService.MarkDataCompletedAsync(_applicationId, CancellationToken.None);
                if (markResult.IsFailure)
                    _logger.Warn($"标记数据补全完成失败: {markResult.Message}");
                _logger.LogBusiness("数据补全保存完成，返回变更页", ("ApplicationId", _applicationId));

                // 数据补全并写入数据库后：删除对应的导入库家庭及人员行（防止重复建档/重复导入）
                var delResult = await _importedArchiveService.DeleteImportedFamilyByApplicationAsync(_applicationId, CancellationToken.None);
                if (delResult.IsFailure)
                    _logger.Warn($"删除导入库家庭失败: {delResult.Message}");

                var completionPage = Helpers.WindowNavigator.CurrentPage;
                if (completionPage != null)
                    await completionPage.Navigation.PopAsync();
                RestoreWindowTitleFromNavigation();
            }
            else if (IsInGracePeriod)
            {
                // 渐退期未满：提醒原/现保障金，然后进档案制作页（输出文书分类+预勾选，不进整档完成归档）
                await ShowGracePeriodSavedReminderAsync();
            }
            else
            {
                ClearDocumentOutputContext();
                await NavigateToArchiveProductionAsync();
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// 渐退封顶：原有享受额度 > 当前户口类型最低保障额×新家庭人数（不含分类施保）时，
    /// 渐退期内按上限发放；否则继续原额。原额优先取变更链上游档案（户主死亡停旧建新）。
    /// </summary>
    private async Task ApplyGraceCapAsync(decimal priorGuarantee, CancellationToken ct)
    {
        var original = _originalGuaranteeContext
            ?? (OriginalGuaranteeAmount is decimal og && og > 0 ? og : (decimal?)null)
            ?? (priorGuarantee > 0 ? priorGuarantee : (decimal?)null)
            ?? GuaranteeAmount;
        OriginalGuaranteeAmount = original;

        var standard = await GetCachedSubsistenceStandardAsync();
        if (standard <= 0 || FamilySize <= 0)
        {
            GraceGrantAmount = original;
            GuaranteeAmount = original;
            return;
        }

        var cap = standard * FamilySize;
        GraceGrantAmount = original > cap ? cap : original;
        // 渐退期内按原享受额（封顶后）发放，不按本次补差公式重算
        GuaranteeAmount = GraceGrantAmount.Value;

        _logger.LogBusiness("渐退封顶",
            ("原保障金", original),
            ("上限", cap),
            ("应发", GraceGrantAmount),
            ("人数", FamilySize));
    }

    /// <summary>
    /// 保存成功且处于渐退期：提醒原/现保障金与减发是否进月报，然后预置输出文书上下文并进档案制作页。
    /// </summary>
    private async Task ShowGracePeriodSavedReminderAsync()
    {
        var period = GracePeriodEndDate?.ToString("yyyy-MM-dd") ?? "—";
        var original = OriginalGuaranteeAmount ?? 0;
        var current = GraceGrantAmount ?? GuaranteeAmount;
        var reduce = original - current;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"已进入渐退期（{GracePeriodMonths} 个月，至 {period}）");
        sb.AppendLine($"原保障金：{original:F2} 元/月");
        if (reduce > 0)
        {
            sb.AppendLine($"现保障金：{current:F2} 元/月（超过本户口类型上限，已封顶）");
            sb.AppendLine($"减发金额：{reduce:F2} 元/月");
            sb.AppendLine("该减发将计入「保障金减发表」（月报）。");
        }
        else
        {
            sb.AppendLine($"现保障金：{current:F2} 元/月（与原额一致，无减发）");
        }
        sb.AppendLine("档案制作须待渐退期满后办理；可先在制作页「档案输出」打印所需文书。");

        var dialog = _serviceProvider.GetRequiredService<IDialogService>();
        await dialog.DisplayAlertAsync("渐退期确认", sb.ToString(), "确定");
        await OpenDocumentProductionAsync();
    }

    /// <summary>
    /// 预置输出文书上下文（OutputCategories/Prefilter/OperationOverride）并进档案制作页。
    /// 用户在制作页点「档案输出」→ Output 按变动分类加载并预勾选。
    /// 预勾选：告知书必选；有人员变动→增减员表；渐退→渐退审批表；有减发→保障金减少。
    /// </summary>
    [RelayCommand]
    private async Task ShowGraceDocumentSheetAsync()
    {
        if (_applicationId <= 0) return;
        await OpenDocumentProductionAsync();
    }

    private async Task OpenDocumentProductionAsync()
    {
        try
        {
            IsBusy = true;
            LoadingMessage = "正在准备文书输出...";

            // A1+：出文书前再同步一次——「只出文书不保存」也不带病出单
            if (IsInGracePeriod && GracePeriodMonths > 0)
                await SyncGracePeriodRecordAsync(CancellationToken.None);

            var preselect = new List<string> { DocumentTemplateNames.ChangeNotice }; // 告知书必选

            // 渐退核定 → 渐退审批表
            if (IsInGracePeriod)
                preselect.Add(DocumentTemplateNames.GraceApproval);

            // 有减发 → 保障金减少
            if (OriginalGuaranteeAmount is decimal og && GraceGrantAmount is decimal gg && og > gg && gg > 0)
                preselect.Add(DocumentTemplateNames.GrantReduce);

            // 有人员变动 → 增减员表（死亡链/成员增减记录存在）
            try
            {
                var changes = await _changeService.GetMemberChangeRecordsAsync(_applicationId, 5, CancellationToken.None);
                if (changes.IsSuccess && changes.Value is { Count: > 0 })
                    preselect.Add(DocumentTemplateNames.MemberChangeTable);
            }
            catch
            {
                // 取数失败不阻断：仅少预勾一张
            }

            ApplyDocumentOutputContext(preselect);
            await NavigateToArchiveProductionAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "打开文书输出失败");
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("错误", $"打开文书输出失败: {ex.Message}", "确定");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>写入输出文书上下文（进 Production 前调用；PrepareAndNavigateAsync 不会清这些字段）</summary>
    private void ApplyDocumentOutputContext(IEnumerable<string> preselectNames)
    {
        ClearDocumentOutputContext();
        PrintNavigationData.OutputCategories = Helpers.ArchiveCategoryResolver.DocumentOperationCategories;
        PrintNavigationData.OperationOverride = "人员变更";
        PrintNavigationData.PrefilterTemplateNames = preselectNames
            .Distinct(StringComparer.Ordinal).ToArray();
        PrintNavigationData.TemplateFilter = null; // 分类+预勾选，非强白名单
    }

    /// <summary>清理输出文书上下文（整档路径进入 Production 前调用）</summary>
    private void ClearDocumentOutputContext()
    {
        PrintNavigationData.OutputCategories = null;
        PrintNavigationData.OperationOverride = null;
        PrintNavigationData.PrefilterTemplateNames = null;
        PrintNavigationData.TemplateFilter = null;
    }

    /// <summary>
    /// 装配打印字段并进入档案输出页（保留：强白名单旁路，成员变更等若需直出仍可用）。
    /// 渐退/Step5 已改为 OpenDocumentProductionAsync → Production → Output。
    /// </summary>
    private async Task OpenDocumentOutputAsync(IReadOnlyList<string> templateNames, long applicationId)
    {
        if (applicationId <= 0 || templateNames == null || templateNames.Count == 0)
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("错误", "文书直出参数无效", "确定");
            return;
        }

        await _documentBuildGate.WaitAsync(CancellationToken.None);
        try
        {
            IsBusy = true;
            LoadingMessage = "正在装配文书字段...";

            // ArchiveProductionViewModel 为 Transient 且 BuildPrintDataAsync 使用实例内部字段状态（不可并发）——
            // 每次调用 DI 解析局部实例 + 静态 SemaphoreSlim 串行
            var production = _serviceProvider.GetRequiredService<ViewModels.ArchiveManagement.ArchiveProductionViewModel>();
            var printData = await production.BuildPrintDataAsync(applicationId);
            if (printData == null)
                throw new InvalidOperationException("文书字段装配失败，请确认档案数据完整");

            PrintNavigationData.BusinessType = string.IsNullOrEmpty(printData.BusinessType)
                ? "FamilyApplication"
                : printData.BusinessType;
            PrintNavigationData.BusinessId = printData.BusinessId ?? applicationId;
            PrintNavigationData.Classification = printData.Classification;
            PrintNavigationData.FieldData = new Dictionary<string, string>(printData.FieldData, StringComparer.Ordinal);
            PrintNavigationData.TableData = printData.TableData?.ToList() ?? new();
            PrintNavigationData.SupporterTableData = printData.SupporterTableData;
            PrintNavigationData.Status = ApplicationStatus ?? string.Empty;
            PrintNavigationData.TemplateFilter = templateNames.ToArray();

            _logger.Info($"文书直出: ApplicationId={applicationId}, 模板={string.Join("|", templateNames)}");

            await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveOutputPage>();
        }
        catch (Exception ex)
        {
            // 导航失败时清 PII，避免驻留
            PrintNavigationData.Clear();
            _logger.LogError(ex, "文书直出打开档案输出页失败");
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("错误", $"打开文书输出失败: {ex.Message}", "确定");
        }
        finally
        {
            _documentBuildGate.Release();
            IsBusy = false;
        }
    }

    /// <summary>
    /// 渐退超限减发：补写 change_type=FundChange（old&gt;new、同分类），供月报保障金减发表捕获。
    /// 幂等：同户同额已存在则跳过；本会话已写过也跳过。
    /// </summary>
    private async Task RecordGraceCapFundChangeAsync(decimal oldAmount, decimal newAmount, CancellationToken ct)
    {
        if (_graceFundChangeRecorded) return;

        try
        {
            var result = await _changeService.CreateGraceCapFundChangeAsync(
                _applicationId,
                ClassificationResult ?? string.Empty,
                OriginalClassificationResult ?? ClassificationResult ?? string.Empty,
                oldAmount,
                newAmount,
                ct);
            if (result.IsSuccess)
            {
                _graceFundChangeRecorded = true;
                _logger.LogBusiness("渐退超限减发已记入FundChange",
                    ("ApplicationId", _applicationId),
                    ("原保障金", oldAmount),
                    ("现保障金", newAmount));
            }
            else
            {
                _logger.Warn($"渐退超限FundChange写入失败: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"渐退超限FundChange写入异常: {ex.Message}");
        }
    }

    /// <summary>
    /// 补全模式下检查是否仍有未补全的关键字段，返回提醒文案；全部补全返回 null
    /// </summary>
    private string? BuildCompletionReminder()
    {
        var missing = new List<string>();

        // 户主基本信息（值为空才算未补全，导入库已带出的默认值不算缺失）
        if (SelectedEthnicity == null || string.IsNullOrWhiteSpace(SelectedEthnicity.Key))
            missing.Add("民族");
        if (SelectedHealthStatusObj == null || string.IsNullOrWhiteSpace(SelectedHealthStatusObj.Key))
            missing.Add("健康状况");

        // 经济分项（务工收入代表）
        if (WorkIncomeTotal <= 0 && BusinessIncomeTotal <= 0 && TransferIncomeTotal <= 0 && OtherIncomeTotal <= 0)
            missing.Add("经济分项收入");

        // 地区（乡镇/社区）
        if (string.IsNullOrWhiteSpace(SelectedTown) || string.IsNullOrWhiteSpace(SelectedVillage))
            missing.Add("地区（乡镇/村）");

        if (missing.Count == 0) return null;

        return $"以下关键信息尚未补全：{string.Join("、", missing)}。\n\n可点击「返回补全」继续完善，或「继续保存」暂时保留当前数据。";
    }

    /// <summary>
    /// 经济复核专用保存：构建复核上下文 → 执行重新判定并记录变更 → 返回变更页
    /// </summary>
    private async Task<Result> ExecuteReviewSaveAsync()
    {
        // 复核原因必填
        if (string.IsNullOrWhiteSpace(ApplicationReasonDetail))
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "请填写复核原因", "确定");
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "复核原因不能为空");
        }

        try
        {
            IsBusy = true;
            LoadingMessage = "经济状况复核中...";

            _logger.LogBusiness("开始经济状况复核", ("ApplicationId", _applicationId));

            // ── 覆写前旧值：优先用向导入口固化的复核前快照 ──
            // 步骤保存/Step5 判定落库在进入本方法前已把新数据写回本档，此时再读库
            // 拿到的"旧值"已是覆写后值（Before 快照 / old_per_capita_income 失真）。
            // 入口快照缺失（未走加载路径的异常场景）才回退保存时读库（历史行为），并记 Warn。
            Application? oldSnapshot = _entryApplicationSnapshot;
            if (oldSnapshot == null)
            {
                var oldSnapshotResult = await _applicationService.GetByIdAsync(_applicationId, CancellationToken.None);
                if (oldSnapshotResult.IsSuccess && oldSnapshotResult.Value != null)
                {
                    oldSnapshot = oldSnapshotResult.Value;
                }
                else
                {
                    _logger.Warn($"经济复核-覆写前旧值捕获失败: {oldSnapshotResult.Message}");
                }
            }

            // 经济复核模式下 Step3 经济明细可编辑，先持久化用户修改，再执行分类重新判定。
            // 否则用户在复核模式下修改的经济数据（务工/经营/补贴等）不会被保存。
            try
            {
                await SaveEconomicDetailsAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.Error($"经济复核-保存经济明细失败: {ex.Message}");
                await _serviceProvider.GetRequiredService<IDialogService>()
                    .DisplayAlertAsync("提示", $"经济明细保存失败：{ex.Message}", "确定");
                return Result.Failure(ErrorCodes.DB_QUERY_ERROR, $"经济明细保存失败：{ex.Message}");
            }

            var changeService = _changeService;
            var context = new EconomicReviewContext
            {
                ApplicationId = _applicationId,
                NewTotalFamilyIncome = TotalFamilyIncome,
                NewPerCapitaIncome = PerCapitaIncome,
                NewTotalAnnualIncome = TotalAnnualIncome,
                NewPerCapitaAnnualIncome = PerCapitaAnnualIncome,
                NewRigidExpenditure = RigidExpenditure,
                NewFamilySize = FamilySize,
                ReviewReason = ApplicationReasonDetail,
                OperatorName = string.IsNullOrEmpty(App.CurrentUserName) ? "System" : App.CurrentUserName,
                IsFamilyCorrection = IsFamilyCorrectionMode,
                // 覆写前捕获的真旧值（未捕获到为 null，ChangeService 回退读库）
                OldFamilySize = oldSnapshot?.FamilySize,
                OldTotalFamilyIncome = oldSnapshot?.TotalFamilyIncome,
                OldTotalAnnualIncome = oldSnapshot?.TotalAnnualIncome,
                OldPerCapitaIncome = oldSnapshot?.PerCapitaIncome,
                OldRigidExpenditure = oldSnapshot?.RigidExpenditure,
                // 分类/保障金用加载时刻的 _loaded*（与入口快照同一时刻捕获，口径一致；
                // Step5 判定落库会覆写 classification_result/total_guarantee_amount，读库会恒判"无变化"）
                OldClassification = _loadedClassification,
                OldGuaranteeAmount = _loadedTotalGuaranteeAmount,
                OldComponents = oldSnapshot == null ? null : new IncomeComponentValues
                {
                    WorkIncomeTotal = oldSnapshot.WorkIncomeTotal,
                    BusinessIncomeTotal = oldSnapshot.BusinessIncomeTotal,
                    PropertyIncomeTotal = oldSnapshot.PropertyIncomeTotal,
                    TransferIncomeTotal = oldSnapshot.TransferIncomeTotal,
                    OtherIncomeTotal = oldSnapshot.OtherIncomeTotal,
                    RigidExpenditure = oldSnapshot.RigidExpenditure,
                    AlimonyIncome = oldSnapshot.AlimonyIncome,
                    LandIncomeTotal = oldSnapshot.LandIncomeTotal,
                    SubsidyTotal = oldSnapshot.SubsidyTotal
                }
            };

            var result = await changeService.ExecuteEconomicReviewAsync(context, CancellationToken.None);
            if (result.IsFailure || !result.IsSuccess)
            {
                ErrorMessage = result.Message ?? "经济状况复核失败";
                await _serviceProvider.GetRequiredService<IDialogService>()
                    .DisplayAlertAsync("提示", ErrorMessage, "确定");
                return Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, ErrorMessage);
            }

            // 复核完成后返回变更页，出口三选一：整套档案 / 仅出文书（渐退减发）/ 保持整档
            _logger.LogBusiness("经济状况复核完成",
                ("ApplicationId", _applicationId),
                ("OldClassification", result.Value.OldClassification),
                ("NewClassification", result.Value.NewClassification),
                ("OldAmount", result.Value.OldGuaranteeAmount),
                ("NewAmount", result.Value.NewGuaranteeAmount));

            var reviewPage = Helpers.WindowNavigator.CurrentPage;
            if (reviewPage != null)
                await reviewPage.Navigation.PopAsync();

            // 未生成新档案（保障金额/分类/停保三项均未变化）：提示后停在变更页，不进档案制作
            if (!result.Value.Rebuilt)
            {
                await _serviceProvider.GetRequiredService<IDialogService>()
                    .DisplayAlertAsync("提示", "复核完成，保障金额无变化，未生成新档案", "确定");
                return Result.Success();
            }

            // 渐退/超限减发 → 仅出文书（档案制作+输出，预勾选）；否则保持整档 ArchiveProduction
            var hasGraceReduce = IsInGracePeriod
                || (OriginalGuaranteeAmount is decimal ro && GraceGrantAmount is decimal rg && ro > rg);
            if (hasGraceReduce)
            {
                var docs = new List<string> { DocumentTemplateNames.ChangeNotice };
                if (IsInGracePeriod)
                    docs.Add(DocumentTemplateNames.GraceApproval);
                if (OriginalGuaranteeAmount is decimal o2 && GraceGrantAmount is decimal g2 && o2 > g2)
                    docs.Add(DocumentTemplateNames.GrantReduce);
                try
                {
                    ApplyDocumentOutputContext(docs);
                    await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveProductionPage, ApplicationReviewArchiveParameter>(
                        new ApplicationReviewArchiveParameter(_applicationId, null, "经济复核"));
                }
                catch (Exception navEx)
                {
                    _logger.Error($"导航到渐退文书输出失败: {navEx.Message}");
                }
                return Result.Success();
            }

            try
            {
                ClearDocumentOutputContext();
                // 参数先于 Push 注入，替代原"Push 后 InitializeFromApplicationReviewAsync"时序
                await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveProductionPage, ApplicationReviewArchiveParameter>(new ApplicationReviewArchiveParameter(_applicationId));
            }
            catch (Exception navEx)
            {
                _logger.Error($"导航到经济复核档案输出失败: {navEx.Message}\n{navEx.StackTrace}");
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", $"经济状况复核失败：{ex.Message}", "确定");
            return Result.FromException(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 家庭成员变更专用保存：成员增删/赡养人/经济明细落库 → 重新判定并停旧建新 → 返回变更页并进入档案输出。
    /// 成员、赡养人、经济明细与变更服务共用同一环境事务：任一步失败整体回滚，
    /// 避免"成员已改、分类未重算"的中间状态入库。
    /// </summary>
    private async Task<Result> ExecuteMemberChangeSaveAsync()
    {
        // 变更原因必填（复用表单"原因详情"，写入变更记录/增减员调整表输出）
        if (string.IsNullOrWhiteSpace(ApplicationReasonDetail))
        {
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", "请填写变更原因（入户调查步骤的\"原因详情\"）", "确定");
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "变更原因不能为空");
        }

        IsBusy = true;
        LoadingMessage = "家庭成员变更处理中...";

        try
        {
            _logger.LogBusiness("开始家庭成员变更", ("ApplicationId", _applicationId));

            // 强制重新计算收入，确保汇总字段是最新的
            RecalculateIncome();

            // 变更类型与摘要：加载时名单 vs 当前名单（增员/减员/既有增又有减）
            var loadedIdCardSet = new HashSet<string>(_loadedMembersByIdCard.Keys, StringComparer.OrdinalIgnoreCase);
            var currentMembers = FamilyMembers
                .Where(m => !string.IsNullOrWhiteSpace(m.IdCard))
                .GroupBy(m => m.IdCard.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var addedMembers = currentMembers.Values
                .Where(m => !loadedIdCardSet.Contains(m.IdCard!.Trim()))
                .ToList();
            // 减员以登记记录为准（身份证被重新加回的不算减员）
            var removedEntries = _removedMemberEntries
                .Where(e => !string.IsNullOrWhiteSpace(e.Member.IdCard)
                    && !currentMembers.ContainsKey(e.Member.IdCard.Trim()))
                .ToList();

            // 新增行必须填全姓名/身份证号：否则会被保存静默跳过，增员登记与人数都会丢
            var blankAdded = FamilyMembers
                .Where(m => !_loadedMemberEntities.Contains(m, ReferenceEqualityComparer.Instance))
                .Where(m => string.IsNullOrWhiteSpace(m.Name) || string.IsNullOrWhiteSpace(m.IdCard))
                .ToList();
            if (blankAdded.Count > 0)
            {
                await _serviceProvider.GetRequiredService<IDialogService>()
                    .DisplayAlertAsync("提示", $"有 {blankAdded.Count} 名新增成员未填写姓名或身份证号，请补全后再提交", "确定");
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, "新增成员信息不完整");
            }

            // 增员/减员必须逐人登记原因（弹窗登记；此处兜底校验，防止旁路修改列表）
            var missingReason = addedMembers.Where(m => !_addedMemberReasons.ContainsKey(m)).Select(m => m.Name).ToList();
            missingReason.AddRange(removedEntries.Where(e => e.Reason == null).Select(e => e.Member.Name));
            if (missingReason.Count > 0)
            {
                await _serviceProvider.GetRequiredService<IDialogService>()
                    .DisplayAlertAsync("提示", $"以下成员缺少变更原因登记：{string.Join("、", missingReason)}", "确定");
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, "成员变更原因未登记");
            }

            var changeType = addedMembers.Count > 0 && removedEntries.Count == 0
                ? DictionaryConstants.ChangeType.MEMBER_ADD
                : removedEntries.Count > 0 && addedMembers.Count == 0
                    ? DictionaryConstants.ChangeType.MEMBER_REMOVE
                    : DictionaryConstants.ChangeType.MEMBER_MODIFY;
            var summaryParts = new List<string>();
            if (addedMembers.Count > 0)
            {
                summaryParts.Add($"新增 {string.Join("、", addedMembers.Select(m => $"{m.Name}（{_addedMemberReasons[m].ReasonName}）"))}");
            }
            if (removedEntries.Count > 0)
            {
                summaryParts.Add($"减员 {string.Join("、", removedEntries.Select(e => $"{e.Member.Name}（{e.Reason.ReasonName}）"))}");
            }
            var changeSummary = string.Join("；", summaryParts);

            // 无增员/减员不允许提交：否则会无意义地停旧建新
            if (addedMembers.Count == 0 && removedEntries.Count == 0)
            {
                await _serviceProvider.GetRequiredService<IDialogService>()
                    .DisplayAlertAsync("提示", "未检测到家庭成员变化（增员或减员），无需执行成员变更", "确定");
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, "未检测到家庭成员变化");
            }

            // 逐人变更明细（快照 + nc_biz_change_details；死亡减员联动死亡记录）
            var changeEntries = new List<MemberChangeEntry>();
            changeEntries.AddRange(addedMembers.Select(m =>
            {
                var r = _addedMemberReasons[m];
                return new MemberChangeEntry
                {
                    Direction = DictionaryConstants.ChangeType.MEMBER_ADD,
                    MemberId = m.Id,
                    Name = m.Name ?? string.Empty,
                    IdCard = m.IdCard ?? string.Empty,
                    RelationshipToHead = m.RelationshipToHead ?? string.Empty,
                    MemberCategory = m.MemberCategory ?? string.Empty,
                    ReasonCode = r.ReasonCode,
                    ReasonName = r.ReasonName,
                    EventDate = r.EventDate,
                    Remark = r.Remark
                };
            }));
            changeEntries.AddRange(removedEntries.Select(e => new MemberChangeEntry
            {
                Direction = DictionaryConstants.ChangeType.MEMBER_REMOVE,
                MemberId = e.Member.Id,
                Name = e.Member.Name ?? string.Empty,
                IdCard = e.Member.IdCard ?? string.Empty,
                RelationshipToHead = e.Member.RelationshipToHead ?? string.Empty,
                MemberCategory = e.Member.MemberCategory ?? string.Empty,
                ReasonCode = e.Reason.ReasonCode,
                ReasonName = e.Reason.ReasonName,
                EventDate = e.Reason.EventDate,
                Remark = e.Reason.Remark
            }));

            ChangeResult? changeValue = null;
            var changeResult = await _applicationService.ExecuteInTransactionForResultAsync(async transactionCt =>
            {
                // 1) 成员增删/赡养人/经济明细落库（同一环境事务，失败整体回滚）
                await MergeSupportMembersIntoSupportersAsync();
                var memberIdCardToNewId = await SaveFamilyMembersAsync(transactionCt);
                await SaveSupportersAsync(memberIdCardToNewId, transactionCt);
                await SaveEconomicDetailsAsync(transactionCt);

                // 2) 重新判定分类 + 停旧建新 + 变更记录（含逐人明细/死亡减员联动）
                var context = new MemberChangeContext
                {
                    ApplicationId = _applicationId,
                    ChangeType = changeType,
                    ChangeSummary = changeSummary,
                    ChangeReason = ApplicationReasonDetail,
                    OldFamilySize = _loadedFamilySize,
                    OldClassification = _loadedClassification,
                    OldGuaranteeAmount = _loadedTotalGuaranteeAmount,
                    // 复核前真旧值（入口快照）：本事务 SaveEconomicDetailsAsync 已先写回新收入，
                    // 读库拿到的是覆写后值（Before 快照 / old_per_capita_income 失真）；null 回退读库
                    OldTotalFamilyIncome = _entryApplicationSnapshot?.TotalFamilyIncome,
                    OldPerCapitaIncome = _entryApplicationSnapshot?.PerCapitaIncome,
                    OldRigidExpenditure = _entryApplicationSnapshot?.RigidExpenditure,
                    NewTotalFamilyIncome = TotalFamilyIncome,
                    NewPerCapitaIncome = PerCapitaIncome,
                    NewTotalAnnualIncome = TotalAnnualIncome,
                    NewPerCapitaAnnualIncome = PerCapitaAnnualIncome,
                    NewRigidExpenditure = RigidExpenditure,
                    NewFamilySize = FamilySize,
                    Entries = changeEntries,
                    OperatorName = string.IsNullOrEmpty(App.CurrentUserName) ? "System" : App.CurrentUserName
                };

                var result = await _changeService.ExecuteMemberChangeAsync(context, transactionCt);
                if (result.IsFailure || !result.IsSuccess)
                {
                    ErrorMessage = result.Message ?? "家庭成员变更失败";
                    return Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, ErrorMessage);
                }

                changeValue = result.Value;
                return Result.Success();
            }, CancellationToken.None);

            if (changeResult.IsFailure)
            {
                await _serviceProvider.GetRequiredService<IDialogService>()
                    .DisplayAlertAsync("提示", ErrorMessage ?? "家庭成员变更失败", "确定");
                return changeResult;
            }

            _logger.LogBusiness("家庭成员变更完成",
                ("ApplicationId", _applicationId),
                ("ChangeType", changeType),
                ("OldClassification", changeValue!.OldClassification),
                ("NewClassification", changeValue.NewClassification),
                ("NewApplicationId", changeValue.NewApplicationId));

            // 返回变更页，出口三选一：整套档案 / 仅出文书 / 稍后
            var navigationPage = Helpers.WindowNavigator.CurrentPage;
            if (navigationPage != null)
                await navigationPage.Navigation.PopAsync();

            var choice = await _serviceProvider.GetRequiredService<IDialogService>().DisplayActionSheetAsync(
                "成员变更完成",
                "取消",
                null,
                "整套档案",
                "仅出文书",
                "稍后再说");

            if (choice == "整套档案")
            {
                ClearDocumentOutputContext();
                try
                {
                    await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveProductionPage, ApplicationReviewArchiveParameter>(
                        new ApplicationReviewArchiveParameter(changeValue.NewApplicationId, _applicationId, "成员变更"));
                }
                catch (Exception navEx)
                {
                    _logger.Error($"导航到家庭成员变更档案输出失败: {navEx.Message}\n{navEx.StackTrace}");
                }
            }
            else if (choice == "仅出文书")
            {
                try
                {
                    ApplyDocumentOutputContext(new[]
                    {
                        DocumentTemplateNames.MemberChangeTable,
                        DocumentTemplateNames.ChangeNotice
                    });
                    await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveProductionPage, ApplicationReviewArchiveParameter>(
                        new ApplicationReviewArchiveParameter(changeValue.NewApplicationId, _applicationId, "成员变更"));
                }
                catch (Exception navEx)
                {
                    _logger.Error($"导航到成员变更文书输出失败: {navEx.Message}");
                }
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("提示", $"家庭成员变更失败：{ex.Message}", "确定");
            return Result.FromException(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    protected override async Task OnCancelAsync()
    {
        _logger.LogBusiness("取消编辑申请");
        await Task.CompletedTask;
    }

    #endregion

    /// <summary>
    /// 确保地区选项已加载（如有缺失则设置默认值）
    /// </summary>
    private async Task EnsureRegionOptionsLoadedAsync()
    {
        if (string.IsNullOrEmpty(SelectedCity) && CityOptions.Count > 0)
        {
            SelectedCity = CityOptions[0];
            await LoadDistrictsAsync(SelectedCity);
        }
        if (string.IsNullOrEmpty(SelectedDistrict) && DistrictOptions.Count > 0)
        {
            SelectedDistrict = DistrictOptions[0];
            await LoadTownsForDistrictAsync(SelectedDistrict);
        }
        if (string.IsNullOrEmpty(SelectedTown) && TownOptions.Count > 0)
        {
            SelectedTown = TownOptions[0];
            await LoadVillagesForTownAsync(SelectedTown);
        }
    }

    #region 省市区联动方法

    // 省市区镇级联加载共享取消源：新的级联触发会取消尚未完成的旧加载，避免乱序结果覆盖集合
    private CancellationTokenSource? _cascadeCts;

    private CancellationToken ResetCascadeToken()
    {
        _cascadeCts?.Cancel();
        _cascadeCts?.Dispose();
        _cascadeCts = new CancellationTokenSource();
        return _cascadeCts.Token;
    }

    partial void OnSelectedProvinceChanged(string value)
    {
        if (!string.IsNullOrEmpty(value) && !_isLoadingDefaults)
        {
            _ = LoadCitiesAsync(value, ResetCascadeToken());
        }
    }

    partial void OnSelectedCityChanged(string value)
    {
        if (!string.IsNullOrEmpty(value) && !_isLoadingDefaults)
        {
            _ = LoadDistrictsAsync(value, ResetCascadeToken());
            UpdateHukouAddress();
        }
    }

    partial void OnSelectedDistrictChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            try
            {
                if (!_isLoadingDefaults)
                {
                    _ = LoadTownsForDistrictAsync(value, ResetCascadeToken());
                    UpdateHukouAddress();
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"区县变更处理失败: {ex.Message}");
            }
        }
    }

    partial void OnSelectedTownChanged(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            try
            {
                if (!_isLoadingDefaults)
                {
                    _ = LoadVillagesForTownAsync(value, ResetCascadeToken());
                    UpdateHukouAddress();
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"乡镇变更处理失败: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 更新户籍地址（城市+区县+镇+村）
    /// </summary>
    private void UpdateHukouAddress()
    {
        HukouAddress = string.IsNullOrWhiteSpace(SelectedVillage)
            ? $"{SelectedCity}{SelectedDistrict}{SelectedTown}"
            : $"{SelectedCity}{SelectedDistrict}{SelectedTown}{SelectedVillage}";
    }

    private async Task LoadCitiesAsync(string province, CancellationToken cancellationToken = default)
    {
        try
        {
            var countiesResult = await _regionService.GetCountiesByCityAsync(province, cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;

            if (countiesResult.IsSuccess)
            {
                CityOptions.Clear();
                foreach (var county in countiesResult.Value)
                {
                    CityOptions.Add(county.CountyName);
                }
                if (CityOptions.Count > 0 && string.IsNullOrEmpty(SelectedCity))
                {
                    SelectedCity = CityOptions[0];
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 级联切换取消旧加载，静默处理
        }
        catch (Exception ex)
        {
            _logger.Error($"加载城市数据失败: {ex.Message}");
        }
    }

    private async Task LoadDistrictsAsync(string city, CancellationToken cancellationToken = default)
    {
        try
        {
            var countiesResult = await _regionService.GetCountiesByCityAsync(city, cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;

            if (countiesResult.IsSuccess)
            {
                DistrictOptions.Clear();
                foreach (var county in countiesResult.Value)
                {
                    DistrictOptions.Add(county.CountyName);
                }
                if (DistrictOptions.Count > 0 && string.IsNullOrEmpty(SelectedDistrict))
                {
                    SelectedDistrict = DistrictOptions[0];
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 级联切换取消旧加载，静默处理
        }
        catch (Exception ex)
        {
            _logger.Error($"加载区县数据失败: {ex.Message}");
        }
    }

    private async Task LoadTownsForDistrictAsync(string districtName, CancellationToken cancellationToken = default)
    {
        try
        {
            var countiesResult = await _regionService.GetCountiesByCityAsync(SelectedCity, cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;

            if (countiesResult.IsSuccess)
            {
                var county = countiesResult.Value.FirstOrDefault(c => c.CountyName == districtName);
                if (county != null)
                {
                    var townsResult = await _regionService.GetTownsByCountyIdAsync(county.Id, cancellationToken);
                    if (cancellationToken.IsCancellationRequested) return;

                    TownOptions.Clear();
                    _townIdMap.Clear();
                    if (townsResult.IsSuccess)
                    {
                        foreach (var town in townsResult.Value)
                        {
                            TownOptions.Add(town.TownName);
                            _townIdMap[town.TownName] = town.Id;
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 级联切换取消旧加载，静默处理
        }
        catch (Exception ex)
        {
            _logger.Error($"加载乡镇数据失败: {ex.Message}");
        }
    }

    private async Task LoadVillagesForTownAsync(string townName, CancellationToken cancellationToken = default)
    {
        try
        {
            int townId;
            if (_townIdMap.TryGetValue(townName, out var mappedId))
            {
                townId = mappedId;
                _logger.Info($"乡镇精确匹配: {townName} → Id={townId}");
            }
            else
            {
                _logger.Warn($"乡镇名 '{townName}' 不在映射中，尝试模糊搜索");
                var townsResult = await _regionService.SearchTownsAsync(townName, cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;

                if (!townsResult.IsSuccess || !townsResult.Value.Any())
                {
                    _logger.Warn($"乡镇 '{townName}' 搜索无结果，清空村庄列表");
                    VillageOptions.Clear();
                    return;
                }
                townId = townsResult.Value.First().Id;
                _logger.Info($"乡镇模糊匹配: {townName} → Id={townId}");
            }

            var villagesResult = await _regionService.GetVillagesByTownIdAsync(townId, cancellationToken);
            if (cancellationToken.IsCancellationRequested) return;

            VillageOptions.Clear();
            if (villagesResult.IsSuccess)
            {
                foreach (var village in villagesResult.Value)
                {
                    VillageOptions.Add(village.VillageName);
                }

                if (!_isLoadingDefaults && VillageOptions.Count > 0)
                {
                    SelectedVillage = VillageOptions[0];
                }

                _logger.Info($"加载村庄完成: 乡镇={townName}, 村庄数={VillageOptions.Count}");
            }
        }
        catch (OperationCanceledException)
        {
            // 级联切换取消旧加载，静默处理
        }
        catch (Exception ex)
        {
            _logger.Error($"加载村庄数据失败: {ex.Message}");
        }
    }

    #endregion

    #region 自动识别方法

    partial void OnApplicantIdCardChanged(string value)
    {
        value = value?.Trim() ?? string.Empty;
        ApplicantIdCardField.Value = value;

        if (!string.IsNullOrEmpty(value) && value.Length >= 17)
        {
            var genderDigit = value[16] - '0';
            DetectedGender = genderDigit % 2 == 1 ? "男" : "女";
            Gender = DetectedGender;

            DetectedAge = Helpers.IdCardValidator.ExtractAgeBasic(value);
        }
        else
        {
            DetectedGender = string.Empty;
            Gender = string.Empty;
            DetectedAge = null;
        }

        UpdateDetectedGenderDisplay();
        UpdateHealthStatus();
    }

    partial void OnApplicantNameChanged(string value)
    {
        ApplicantNameField.Value = value;
    }

    partial void OnApplicantPhoneChanged(string value)
    {
        PhoneField.Value = value;
    }

    partial void OnDetectedAgeChanged(int? value)
    {
        DetectedAgeDisplay = value.HasValue ? $"{value.Value}岁" : string.Empty;
        UpdateDetectedGenderDisplay();
    }

    private void UpdateDetectedGenderDisplay()
    {
        if (string.IsNullOrEmpty(DetectedGender))
        {
            DetectedGenderDisplay = string.Empty;
        }
        else
        {
            var agePart = string.IsNullOrEmpty(DetectedAgeDisplay) ? string.Empty : $" {DetectedAgeDisplay}";
            DetectedGenderDisplay = $"{DetectedGender}{agePart}";
        }
    }

    partial void OnDisabilityCardNoChanged(string value)
    {
        DisabilityCardNoField.Value = value;

        try
        {
            if (string.IsNullOrWhiteSpace(value) || value == "00")
            {
                SelectedDisabilityTypeObj = null;
                SelectedDisabilityLevelObj = null;
                SelectedDisabilityTypeKey = string.Empty;
                SelectedDisabilityLevelKey = string.Empty;
                UpdateHealthStatus();
                return;
            }

            if (value.Length >= 2)
            {
                var typeKey = _disabilityTypeKeyMap.TryGetValue(value[^2], out var tk) ? tk : string.Empty;
                var levelKey = _disabilityLevelKeyMap.TryGetValue(value[^1], out var lk) ? lk : string.Empty;
                var levelDisplay = _disabilityLevelDisplayMap.TryGetValue(levelKey, out var lv) ? lv : levelKey;

                SelectedDisabilityTypeObj = DisabilityTypeOptions.FirstOrDefault(o => o.Key == typeKey);
                SelectedDisabilityLevelObj = DisabilityLevelOptions.FirstOrDefault(o => o.Key == levelKey);
                SelectedDisabilityTypeKey = typeKey;
                SelectedDisabilityLevelKey = levelKey;
            }

            UpdateHealthStatus();
        }
        catch (Exception ex)
        {
            _logger.Error($"残疾证号变更处理失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 残疾等级显示值变化时同步更新 key
    /// </summary>
    partial void OnSelectedDisabilityLevelObjChanged(DictItemOption? value)
    {
        var key = value?.Key ?? "";
        SelectedDisabilityLevelKey = key;
    }

    /// <summary>
    /// 更新身体状况（使用 key 判断，不硬编码）
    /// </summary>
    private void UpdateHealthStatus()
    {
        try
        {
            bool hasDisease = !string.IsNullOrEmpty(SelectedDiseaseCategoryKey) &&
                              SelectedDiseaseCategoryKey != _noDiseaseKey;

            bool hasDisability = !string.IsNullOrEmpty(SelectedDisabilityTypeKey);

            bool hasSevereDisability = !string.IsNullOrEmpty(SelectedDisabilityLevelKey) &&
                                        _severeLevelKeys.Contains(SelectedDisabilityLevelKey);

            bool hasNonSevereDisability = hasDisability && !hasSevereDisability;

            var age = Helpers.IdCardValidator.ExtractAgeBasic(ApplicantIdCard);

            if (hasDisease && hasSevereDisability)
                SelectedHealthStatusObj = HealthStatusOptions.FirstOrDefault(o => o.Key == HealthStatusConstants.SEVERE_DISEASE_AND_DISABILITY);
            else if (hasDisease)
                SelectedHealthStatusObj = HealthStatusOptions.FirstOrDefault(o => o.Key == HealthStatusConstants.SEVERE_DISEASE);
            else if (hasSevereDisability)
                SelectedHealthStatusObj = HealthStatusOptions.FirstOrDefault(o => o.Key == HealthStatusConstants.SEVERE_DISABILITY);
            else if (hasNonSevereDisability)
                SelectedHealthStatusObj = HealthStatusOptions.FirstOrDefault(o => o.Key == HealthStatusConstants.FAIR_OR_WEAK);
            else if (age.HasValue && age.Value >= ClassificationConstants.ELDERLY_AGE)
                SelectedHealthStatusObj = HealthStatusOptions.FirstOrDefault(o => o.Key == HealthStatusConstants.FAIR_OR_WEAK);
            else
                SelectedHealthStatusObj = HealthStatusOptions.FirstOrDefault(o => o.Key == HealthStatusConstants.HEALTHY);

            // 人工判定"是否重病"优先：勾选时推导结果不低于"重病"
            if (IsSevereDisease &&
                (SelectedHealthStatusObj?.Key == HealthStatusConstants.HEALTHY ||
                 SelectedHealthStatusObj?.Key == HealthStatusConstants.FAIR_OR_WEAK))
            {
                SelectedHealthStatusObj = HealthStatusOptions.FirstOrDefault(o => o.Key == HealthStatusConstants.SEVERE_DISEASE);
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"更新身体状况失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 勾选"是否重病"同步申请人健康状况为重病；取消勾选仅在当前为重病时回退为健康。
    /// </summary>
    partial void OnIsSevereDiseaseChanged(bool value)
    {
        if (value)
            SelectedHealthStatusObj = HealthStatusOptions.FirstOrDefault(o => o.Key == HealthStatusConstants.SEVERE_DISEASE);
        else if (SelectedHealthStatusObj?.Key == HealthStatusConstants.SEVERE_DISEASE)
            SelectedHealthStatusObj = HealthStatusOptions.FirstOrDefault(o => o.Key == HealthStatusConstants.HEALTHY);
    }

    /// <summary>
    /// 申请人疾病编码命中 ICD-10 字典：回填二级疾病文本、按章节自动归类一级疾病分类。
    /// 由 Step1 code-behind 的 TextChanged 即时调用（WinUI3 绑定默认失焦才提交源）。
    /// </summary>
    public void ApplyApplicantDiseaseCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return;
        if (!Helpers.Icd10Catalog.TryGetName(code, out var name)) return;

        DiseaseNameText = name;

        var category = Helpers.Icd10Catalog.MapCategory(code);
        var match = DiseaseCategoryOptions.FirstOrDefault(o => o.Key == category);
        if (match != null && SelectedDiseaseCategoryObj?.Key != match.Key)
            SelectedDiseaseCategoryObj = match; // 触发级联加载 + UpdateHealthStatus
    }

    #endregion

    #region 疾病分类联动方法

    partial void OnSelectedDiseaseCategoryObjChanged(DictItemOption? value)
    {
        try
        {
            var key = value?.Key ?? "";
            SelectedDiseaseCategoryKey = key;
            if (!string.IsNullOrEmpty(key) && key != _noDiseaseKey)
            {
                LoadSecondaryDiseases(key);
            }
            else
            {
                DiseaseNameOptions.Clear();
                SelectedDiseaseNameObj = null;
            }
            UpdateHealthStatus();
        }
        catch (Exception ex)
        {
            _logger.Error($"疾病分类变更处理失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 从 DiseaseNames 字典加载二级分类
    /// </summary>
    private void LoadSecondaryDiseases(string categoryKey)
    {
        try
        {
            DiseaseNameOptions.Clear();
            if (_diseaseCategoryMap.TryGetValue(categoryKey, out var diseases))
            {
                foreach (var disease in diseases.OrderBy(x => x.SortOrder))
                {
                    // 使用 Key（疾病名称）作为 Display
                    DiseaseNameOptions.Add(new DictItemOption { Key = disease.Key, Display = disease.Key });
                }
                if (DiseaseNameOptions.Count > 0)
                {
                    SelectedDiseaseNameObj = DiseaseNameOptions[0];
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"加载二级疾病数据失败: {ex.Message}");
        }
    }

    #endregion

    #region 实时搜索方法

    [RelayCommand]
    private async Task SearchDiseaseCategoryAsync(string keyword)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                await LoadDictionaryAsync("DiseaseCategories", DiseaseCategoryOptions);
                return;
            }

            var result = await _dictionaryService.GetItemsByCategoryAsync("DiseaseCategories");
            if (result.IsSuccess)
            {
                DiseaseCategoryOptions.Clear();
                foreach (var item in result.Value.Where(x => x.ItemValue.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
                {
                    DiseaseCategoryOptions.Add(new DictItemOption { Key = item.ItemKey, Display = item.ItemValue });
                }
            }
            else
            {
                _logger.Warn($"搜索疾病分类失败: {result.Message}");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"搜索疾病分类失败: {ex.Message}");
        }
    }

    [RelayCommand]
    private Task SearchDiseaseNameAsync(string keyword)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                LoadSecondaryDiseases(SelectedDiseaseCategoryKey);
                return Task.CompletedTask;
            }

            DiseaseNameOptions.Clear();
            if (_diseaseCategoryMap.TryGetValue(SelectedDiseaseCategoryKey, out var diseases))
            {
                foreach (var disease in diseases
                    .Where(x => x.Key.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(x => x.SortOrder))
                {
                    DiseaseNameOptions.Add(new DictItemOption { Key = disease.Key, Display = disease.Key });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"搜索疾病名称失败: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    #endregion

    #region 身份证号搜索

    /// <summary>
    /// 根据身份证号跨表搜索人员信息
    /// </summary>
    [RelayCommand]
    private async Task SearchByIdCardAsync()
    {
        ApplicantIdCard = ApplicantIdCard?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(ApplicantIdCard) || ApplicantIdCard.Length != 18)
        {
            _logger.Warn("身份证号格式不正确，无法搜索");
            return;
        }

        try
        {
            IsBusy = true;
            LoadingMessage = "正在查询档案信息...";

            _logger.Info($"开始身份证号查询: {DataMasker.MaskIdCard(ApplicantIdCard)}");

            var result = await _personSearchService.SearchByIdCardAsync(ApplicantIdCard);

            if (result.IsFailure)
            {
                await _serviceProvider.GetRequiredService<IDialogService>()
                    .DisplayAlertAsync("查询失败", result.Message ?? "查询过程中出现错误", "确定");
                return;
            }

            if (result.Value == null || result.Value.Count == 0)
            {
                await _serviceProvider.GetRequiredService<IDialogService>()
                    .DisplayAlertAsync("提示", $"未找到身份证号 {DataMasker.MaskIdCard(ApplicantIdCard)} 的相关信息", "确定");
                return;
            }

            SearchResults.Clear();
            foreach (var item in result.Value)
                SearchResults.Add(item);

            if (result.Value.Count == 1)
                SelectedSearchResult = result.Value[0];

            IsSearchPopupVisible = true;
            IsBusy = false;
        }
        catch (Exception ex)
        {
            _logger.Error($"身份证号查询失败: {ex.Message}");
            if (_serviceProvider.GetService<IDialogService>() is { } ds)
                await ds.DisplayAlertAsync("错误", $"查询失败: {ex.Message}", "确定");
        }
        finally
        {
            if (!IsSearchPopupVisible)
                IsBusy = false;
        }
    }

    /// <summary>
    /// 确认选择搜索结果
    /// </summary>
    [RelayCommand]
    private async Task ConfirmSearchResultAsync()
    {
        _logger.Info("[SEARCH_POPUP] ConfirmSearchResultAsync 被调用");
        if (SelectedSearchResult != null)
        {
            await FillFormWithSearchResultAsync(SelectedSearchResult);
            IsSearchPopupVisible = false;
        }
    }

    /// <summary>
    /// 取消选择搜索结果
    /// </summary>
    [RelayCommand]
    private void CancelSearchResult()
    {
        _logger.Info("[SEARCH_POPUP] CancelSearchResult 被调用");
        IsSearchPopupVisible = false;
        SelectedSearchResult = null;
    }

    /// <summary>
    /// 填充表单（从人员库检索）
    /// </summary>
    private async Task FillFormWithSearchResultAsync(PersonSearchResult item)
    {
        ApplicantName = item.Name ?? string.Empty;
        ApplicantPhone = item.Phone ?? string.Empty;

        if (!string.IsNullOrEmpty(item.IdCard))
        {
            ApplicantIdCard = item.IdCard;
            DetectedAge = Helpers.IdCardValidator.ExtractAgeBasic(item.IdCard);
        }

        // 加载城市/区县/乡镇/村
        if (!string.IsNullOrEmpty(item.City) || !string.IsNullOrEmpty(item.District))
        {
            await LoadRegionDataForSearchAsync(item.City, item.District, item.Town, item.Village);
        }

        // 导入家庭成员
        if (item.FamilyMembers.Count > 0)
        {
            FamilyMembers.Clear();
            foreach (var member in item.FamilyMembers)
            {
                member.ApplicationId = _applicationId;

                // 设置地址默认值（如果为空）
                if (string.IsNullOrEmpty(member.HomeCity))
                    member.HomeCity = SelectedCity ?? string.Empty;
                if (string.IsNullOrEmpty(member.HomeDistrict))
                    member.HomeDistrict = SelectedDistrict ?? string.Empty;
                if (string.IsNullOrEmpty(member.HomeTown))
                    member.HomeTown = SelectedTown ?? string.Empty;
                if (string.IsNullOrEmpty(member.HomeVillage))
                    member.HomeVillage = SelectedVillage ?? string.Empty;
                if (string.IsNullOrEmpty(member.HomeAddress))
                    member.HomeAddress = Address ?? string.Empty;
                if (string.IsNullOrEmpty(member.HomeProvince))
                    member.HomeProvince = DefaultValuesConstants.HOME_PROVINCE;

                // 设置字典默认值（如果为空）
                if (string.IsNullOrEmpty(member.Ethnicity))
                    member.Ethnicity = EthnicityOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.ETHNICITY_KEY)?.Key
                        ?? (EthnicityOptions.Count > 0 ? EthnicityOptions[0].Key : string.Empty);
                if (string.IsNullOrEmpty(member.MaritalStatus))
                    member.MaritalStatus = MaritalStatusOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.MARITAL_STATUS_KEY)?.Key
                        ?? (MaritalStatusOptions.Count > 0 ? MaritalStatusOptions[0].Key : string.Empty);
                if (string.IsNullOrEmpty(member.EducationLevel))
                    member.EducationLevel = EducationLevelOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.EDUCATION_LEVEL_KEY)?.Key
                        ?? (EducationLevelOptions.Count > 0 ? EducationLevelOptions[0].Key : string.Empty);
                if (string.IsNullOrEmpty(member.PoliticalStatus))
                    member.PoliticalStatus = PoliticalStatusOptions.FirstOrDefault(o => o.Key == DefaultValuesConstants.POLITICAL_STATUS_KEY)?.Key
                        ?? (PoliticalStatusOptions.Count > 0 ? PoliticalStatusOptions[0].Key : string.Empty);

                // 从身份证推导性别和年龄
                if (string.IsNullOrEmpty(member.Gender) && !string.IsNullOrEmpty(member.IdCard))
                    member.Gender = Helpers.IdCardValidator.ExtractGender(member.IdCard) ?? string.Empty;
                if (member.Age <= 0 && !string.IsNullOrEmpty(member.IdCard))
                    member.Age = Helpers.IdCardValidator.ExtractAgeBasic(member.IdCard);

                member.SetRegionService(_regionService);
                await member.LoadInitialAddressOptionsAsync(
                    member.HomeCity, member.HomeDistrict, member.HomeTown);
                SyncMemberDictOptions(member);
                FamilyMembers.Add(member);
            }
            RefreshDerivedCollections();
            UpdateFamilySize();
        }

        _logger.Info($"身份证号查询成功: {DataMasker.MaskIdCard(ApplicantIdCard)}, 来源: {item.SourceDisplay}, 城市: {item.City} {item.District}");
    }

    /// <summary>
    /// 为身份证号搜索结果加载地区数据
    /// </summary>
    private async Task LoadRegionDataForSearchAsync(string? city, string? district, string? town, string? village)
    {
        try
        {
            _isLoadingDefaults = true;

            if (!string.IsNullOrEmpty(city) && CityOptions.Contains(city))
            {
                SelectedCity = city;
                await LoadDistrictsAsync(city);
            }

            if (!string.IsNullOrEmpty(district) && DistrictOptions.Contains(district))
            {
                SelectedDistrict = district;
                await LoadTownsForDistrictAsync(district);
            }

            if (!string.IsNullOrEmpty(town) && TownOptions.Contains(town))
            {
                SelectedTown = town;
                await LoadVillagesForTownAsync(town);
            }

            if (!string.IsNullOrEmpty(village) && VillageOptions.Contains(village))
            {
                SelectedVillage = village;
            }

            UpdateHukouAddress();

            _isLoadingDefaults = false;
        }
        catch (Exception ex)
        {
            _isLoadingDefaults = false;
            _logger.Error($"搜索结果地区加载失败: {ex.Message}");
        }
    }

    #endregion

    #region 表单验证方法

    /// <summary>
    /// 验证表单字段
    /// </summary>
    public bool ValidateFormFields()
    {
        var isValid = true;

        isValid &= ApplicantNameField.Validate();
        isValid &= ApplicantIdCardField.Validate();
        isValid &= DisabilityCardNoField.Validate();
        isValid &= PhoneField.Validate();
        isValid &= HukouAddressField.Validate();

        return isValid;
    }

    #endregion

    #region 土地确权归户表命令

    [RelayCommand]
    private void AddLandConfirmationGroup()
    {
        var group = new LandConfirmationGroup
        {
            GroupId = _nextGroupId++,
            ContractorName = ApplicantName ?? string.Empty
        };
        group.Records.CollectionChanged += (s, e) => CalculateLandConfirmationArea();
        group.Persons.CollectionChanged += (s, e) => CalculateLandConfirmationArea();
        LandConfirmationGroups.Add(group);

        // 自动从家庭成员填充人员
        InitializePersonsForAllGroups();
    }

    [RelayCommand]
    private async Task RemoveLandConfirmationGroup(LandConfirmationGroup? group)
    {
        if (group == null) return;
        // 让出 UI 帧后再移除行，规避 WinUI 移除含焦点控件的原生层崩溃
        await Task.Yield();
        await Task.Delay(30);
        LandConfirmationGroups.Remove(group);
        CalculateLandConfirmationArea();
    }

    [RelayCommand]
    private void AddLandConfirmationRecord(LandConfirmationGroup? group)
    {
        if (group == null) return;
        var record = new LandConfirmationRecord
        {
            MemberName = group.ContractorName,
            UnitPrice = GetUnitPriceByLandUsage(DictionaryConstants.LandUsage.SELF_FARM)
        };
        record.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(LandConfirmationRecord.LandUsage))
            {
                record.UnitPrice = GetUnitPriceByLandUsage(record.LandUsage);
                CalculateLandConfirmationArea();
            }
            else if (e.PropertyName == nameof(LandConfirmationRecord.LandArea)
                  || e.PropertyName == nameof(LandConfirmationRecord.UnitPrice))
            {
                CalculateLandConfirmationArea();
            }
        };
        group.Records.Add(record);
    }

    [RelayCommand]
    private async Task RemoveLandConfirmationRecord(LandConfirmationRecord? record)
    {
        if (record == null) return;
        // 让出 UI 帧后再移除行，规避 WinUI 移除含焦点控件的原生层崩溃
        await Task.Yield();
        await Task.Delay(30);
        foreach (var group in LandConfirmationGroups)
        {
            if (group.Records.Contains(record))
            {
                group.Records.Remove(record);
                break;
            }
        }
        CalculateLandConfirmationArea();
    }

    [RelayCommand]
    private void AddLandConfirmationPerson(LandConfirmationGroup? group)
    {
        if (group == null) return;
        var person = new LandConfirmationPerson
        {
            LandStatus = LandStatusConstants.LIVING_ENTITLED,
            SharesCount = 1
        };
        person.PropertyChanged += (s, e) => CalculateLandConfirmationArea();
        group.Persons.Add(person);
        CalculateLandConfirmationArea();
    }

    [RelayCommand]
    private async Task RemoveLandConfirmationPerson(LandConfirmationPerson? person)
    {
        if (person == null) return;
        // 让出 UI 帧后再移除行，规避 WinUI 移除含焦点控件的原生层崩溃
        await Task.Yield();
        await Task.Delay(30);
        foreach (var group in LandConfirmationGroups)
        {
            if (group.Persons.Contains(person))
            {
                group.Persons.Remove(person);
                break;
            }
        }
        CalculateLandConfirmationArea();
    }

    public decimal GetUnitPriceByLandUsage(string landUsage)
    {
        return landUsage switch
        {
            DictionaryConstants.LandUsage.SELF_FARM => SelfFarmUnitPrice,
            DictionaryConstants.LandUsage.SUBLEASE => SubleaseUnitPrice,
            DictionaryConstants.LandUsage.CONTRACT => ContractUnitPrice,
            _ => 0
        };
    }

    [RelayCommand]
    private async Task ImportLandConfirmationAsync()
    {
        try
        {
            IsBusy = true;
            _logger.LogBusiness("导入土地确权归户表");

        var names = new List<string>();
        if (!string.IsNullOrWhiteSpace(ApplicantName))
            names.Add(ApplicantName);
        foreach (var member in SharedLivingMembers)
        {
            if (!string.IsNullOrWhiteSpace(member.Name) && !names.Contains(member.Name))
                names.Add(member.Name);
        }

            var result = await _landContractService.GetRecordsByNamesAsync(names);
            if (result.IsSuccess && result.Value.Count > 0)
            {
                var recordsByContractor = result.Value
                    .GroupBy(r => r.MemberName)
                    .ToDictionary(g => g.Key, g => g.ToList());

                foreach (var kvp in recordsByContractor)
                {
                    var existingGroup = LandConfirmationGroups
                        .FirstOrDefault(g => g.ContractorName == kvp.Key);

                    if (existingGroup == null)
                    {
                        var newGroup = new LandConfirmationGroup
                        {
                            GroupId = _nextGroupId++,
                            ContractorName = kvp.Key
                        };

                        foreach (var record in kvp.Value)
                        {
                            record.IsImported = true;
                            record.UnitPrice = GetUnitPriceByLandUsage(record.LandUsage);
                            record.PropertyChanged += (s, e) =>
                            {
                                if (e.PropertyName == nameof(LandConfirmationRecord.LandUsage))
                                {
                                    record.UnitPrice = GetUnitPriceByLandUsage(record.LandUsage);
                                    CalculateLandConfirmationArea();
                                }
                                else if (e.PropertyName == nameof(LandConfirmationRecord.LandArea)
                                      || e.PropertyName == nameof(LandConfirmationRecord.UnitPrice))
                                {
                                    CalculateLandConfirmationArea();
                                }
                            };
                            newGroup.Records.Add(record);
                        }

                        newGroup.Records.CollectionChanged += (s, e) => CalculateLandConfirmationArea();
                        newGroup.Persons.CollectionChanged += (s, e) => CalculateLandConfirmationArea();
                        LandConfirmationGroups.Add(newGroup);
                    }
                    else
                    {
                        foreach (var record in kvp.Value)
                        {
                            var exists = existingGroup.Records.Any(r => r.PlotCode == record.PlotCode);
                            if (!exists)
                            {
                                record.IsImported = true;
                                record.UnitPrice = GetUnitPriceByLandUsage(record.LandUsage);
                                record.PropertyChanged += (s, e) =>
                                {
                                    if (e.PropertyName == nameof(LandConfirmationRecord.LandUsage))
                                    {
                                        record.UnitPrice = GetUnitPriceByLandUsage(record.LandUsage);
                                        CalculateLandConfirmationArea();
                                    }
                                    else if (e.PropertyName == nameof(LandConfirmationRecord.LandArea)
                                          || e.PropertyName == nameof(LandConfirmationRecord.UnitPrice))
                                    {
                                        CalculateLandConfirmationArea();
                                    }
                                };
                                existingGroup.Records.Add(record);
                            }
                        }
                    }
                }

                InitializePersonsForAllGroups();
                CalculateLandConfirmationArea();

                await _serviceProvider.GetRequiredService<IDialogService>()
                    .DisplayAlertAsync("成功", $"已导入{result.Value.Count}条记录，分为{recordsByContractor.Count}个归户表", "确定");
            }
            else
            {
                await _serviceProvider.GetRequiredService<IDialogService>()
                    .DisplayAlertAsync("提示", "未找到土地确权数据", "确定");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"导入土地数据失败: {ex.Message}");
            await _serviceProvider.GetRequiredService<IDialogService>()
                .DisplayAlertAsync("错误", $"导入失败: {ex.Message}", "确定");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 批量设置土地用途（全自种/全转包/全承包）
    /// </summary>
    [RelayCommand]
    private void BatchSetLandUsage(string landUsage)
    {
        if (LandConfirmationGroups.Count == 0) return;

        _logger.LogBusiness("批量设置土地用途", ("LandUsage", landUsage));

        // 根据用途设置默认单价
        decimal defaultPrice = landUsage switch
        {
            DictionaryConstants.LandUsage.SELF_FARM => SelfFarmUnitPrice,
            DictionaryConstants.LandUsage.SUBLEASE => SubleaseUnitPrice,
            DictionaryConstants.LandUsage.CONTRACT => ContractUnitPrice,
            _ => 0m
        };

        foreach (var group in LandConfirmationGroups)
        {
            foreach (var record in group.Records)
            {
                record.LandUsage = landUsage;
                record.UnitPrice = defaultPrice;
            }
        }
        CalculateLandConfirmationArea();
    }

    /// <summary>
    /// 计算土地确权汇总
    /// </summary>
    private void CalculateLandConfirmationArea()
    {
        if (LandConfirmationGroups.Count == 0)
        {
            TotalConfirmedLandArea = 0;
            TotalLandShares = 0;
            TotalLandPersonCount = 0;
            FamilyLandArea = 0;
            FamilyLandShares = 0;
            FamilyLandPersonCount = 0;
            SelfFarmedLandArea = 0;
            SubleasedLandArea = 0;
            ContractedLandArea = 0;
            LandIncomeTotal = 0;
            OnPropertyChanged(nameof(CalculatedPerPersonArea));
            OnPropertyChanged(nameof(LandCalculationFormula));
            RecalculateIncome();
            return;
        }

        TotalConfirmedLandArea = (decimal)LandConfirmationGroups.Sum(g => g.TotalArea);
        TotalLandShares = (decimal)LandConfirmationGroups.Sum(g => g.TotalShares);
        TotalLandPersonCount = LandConfirmationGroups.Sum(g => g.PersonCount);

        // 构建家庭成员姓名集（户主 + 共同居住人员）
        var familyNames = new HashSet<string>();
        if (!string.IsNullOrWhiteSpace(ApplicantName))
            familyNames.Add(ApplicantName);
        foreach (var member in FamilyMembers)
        {
            if (member.IsHouseholdHead || member.MemberCategory == MemberCategoryConstants.SHARED_LIVING)
            {
                if (!string.IsNullOrWhiteSpace(member.Name))
                    familyNames.Add(member.Name);
            }
        }

        // 按种植类型汇总面积和收入（全部记录）
        decimal selfFarmed = 0, subleased = 0, contracted = 0;
        decimal selfFarmedIncome = 0, subleasedIncome = 0, contractedIncome = 0;
        foreach (var group in LandConfirmationGroups)
        {
            foreach (var record in group.Records)
            {
                switch (record.LandUsage)
                {
                    case DictionaryConstants.LandUsage.SELF_FARM:
                        selfFarmed += record.LandArea;
                        selfFarmedIncome += record.LandValue;
                        break;
                    case DictionaryConstants.LandUsage.SUBLEASE:
                        subleased += record.LandArea;
                        subleasedIncome += record.LandValue;
                        break;
                    case DictionaryConstants.LandUsage.CONTRACT:
                        contracted += record.LandArea;
                        contractedIncome += record.LandValue;
                        break;
                }
            }
        }

        FamilyLandArea = 0;
        FamilyLandShares = 0;
        FamilyLandPersonCount = 0;

        // 按组计算家庭收入（家庭面积占比 × 该组全部收入）
        decimal familyIncome = 0;
        foreach (var group in LandConfirmationGroups)
        {
            var gArea = (decimal)group.TotalArea;
            var gShares = (decimal)group.TotalShares;
            if (gShares <= 0) continue;

            // 死亡继承份额转移等口径统一收敛到 LandShareCalculator（唯一权威实现）：
            // 本组内"死亡继承"人员的份额累加到指定继承人（LandInheritTo）名下，
            // 死亡人员本身不再单独计入家庭份额；未指定继承人/继承人不计份额时该份额不参与家庭计算。
            var effectiveShares = LandShareCalculator.ComputeEffectiveShares(group);

            decimal familyRatio = 0;
            foreach (var kv in effectiveShares)
            {
                if (familyNames.Contains(kv.Key))
                {
                    FamilyLandShares += kv.Value;
                    FamilyLandPersonCount++;
                    familyRatio += kv.Value;
                }
            }
            familyRatio = familyRatio / gShares;

            decimal groupIncome = 0;
            foreach (var record in group.Records)
                groupIncome += record.LandValue;

            familyIncome += Math.Round(groupIncome * familyRatio, 2);
        }

        FamilyLandArea = TotalLandShares > 0
            ? Math.Round(TotalConfirmedLandArea / TotalLandShares * FamilyLandShares, 2)
            : 0;

        // 按家庭份额折算三个子分类面积
        decimal familyRatioAll = TotalLandShares > 0 ? FamilyLandShares / TotalLandShares : 0;
        SelfFarmedLandArea = Math.Round(selfFarmed * familyRatioAll, 2);
        SubleasedLandArea = Math.Round(subleased * familyRatioAll, 2);
        ContractedLandArea = Math.Round(contracted * familyRatioAll, 2);

        LandIncomeTotal = Math.Round(familyIncome, 2);
        RecalculateIncome();

        OnPropertyChanged(nameof(CalculatedPerPersonArea));
        OnPropertyChanged(nameof(LandCalculationFormula));

        // 按组内份额计算每人的面积
        foreach (var group in LandConfirmationGroups)
        {
            var gArea = (decimal)group.TotalArea;
            var gShares = (decimal)group.TotalShares;
            foreach (var person in group.Persons)
            {
                if (gShares > 0 && LandStatusConstants.ShouldCount(person.LandStatus))
                    person.TotalLandArea = Math.Round((double)(gArea / gShares * (decimal)person.SharesCount), 2);
                else
                    person.TotalLandArea = 0;
            }
        }
    }

    /// <summary>
    /// 初始化归户表人员（仅对人员表为空的组补种户主 + 共同生活成员）。
    /// 已有人员的组（含用户手工增删过的）不再补种，避免把用户删除的人员自动加回；
    /// 赡养抚养扶养人（Support）不属于本户共同生活成员，不补种（否则会稀释家庭土地份额）。
    /// </summary>
    private void InitializePersonsForAllGroups()
    {
        foreach (var group in LandConfirmationGroups)
        {
            // 仅补种空人员表：已有保存/用户编辑结果时保持原样
            if (group.Persons.Count > 0) continue;

            if (!string.IsNullOrWhiteSpace(ApplicantName))
            {
                var head = new LandConfirmationPerson
                {
                    Name = ApplicantName,
                    IdCard = ApplicantIdCard ?? "",
                    LandStatus = LandStatusConstants.LIVING_ENTITLED,
                    SharesCount = 1
                };
                head.PropertyChanged += (s, e) => CalculateLandConfirmationArea();
                group.Persons.Add(head);
            }

            foreach (var member in SharedLivingMembers)
            {
                if (string.IsNullOrWhiteSpace(member.Name)) continue;

                var person = new LandConfirmationPerson
                {
                    Name = member.Name,
                    IdCard = member.IdCard ?? "",
                    LandStatus = LandStatusConstants.LIVING_ENTITLED,
                    SharesCount = 1
                };
                person.PropertyChanged += (s, e) => CalculateLandConfirmationArea();
                group.Persons.Add(person);
            }
        }
    }

    #endregion

    #region 事件处理方法（供 Page.xaml.cs 调用）

    public void OnIdCardChanged(FamilyMember member)
    {
        if (member == null) return;
        member.UpdateGenderAndAgeFromIdCard();
        string? diseaseKey = null;
        if (member.DiseaseCategory != null && _diseaseCategoryMap.ContainsKey(member.DiseaseCategory))
            diseaseKey = member.DiseaseCategory;
        member.UpdateHealthStatus(_noDiseaseKey, _severeLevelKeys, diseaseKey);
    }

    public void OnDisabilityCertificateChanged(FamilyMember member)
    {
        if (member == null) return;

        // 证号为空或长度不足2位 → 清空残疾信息（清除旧残留）
        if (string.IsNullOrWhiteSpace(member.DisabilityCertificateNo) || member.DisabilityCertificateNo.Length < 2)
        {
            member.ClearDisabilityInfo();
            // 更新健康状态
            string? diseaseKey = null;
            if (member.DiseaseCategory != null && _diseaseCategoryMap.ContainsKey(member.DiseaseCategory))
                diseaseKey = member.DiseaseCategory;
            member.UpdateHealthStatus(_noDiseaseKey, _severeLevelKeys, diseaseKey);
            return;
        }

        // 恰好2位时解析残疾证号（存字典 key，显示由 DisabilityTypeDisplay/DisabilityLevelDisplay 提供）
        member.ParseDisabilityCertificate(
            _disabilityTypeKeyMap,
            _disabilityLevelKeyMap);

        // 解析后同步更新健康状态
        string? diseaseKey2 = null;
        if (member.DiseaseCategory != null && _diseaseCategoryMap.ContainsKey(member.DiseaseCategory))
            diseaseKey2 = member.DiseaseCategory;
        member.UpdateHealthStatus(_noDiseaseKey, _severeLevelKeys, diseaseKey2);
    }

    public void OnDiseaseCategoryChanged(FamilyMember member)
    {
        if (member == null) return;
        // 先保存当前二级疾病值，防止 LoadSecondaryDiseases 的 Clear 触发级联清空
        var savedDiseaseName = member.DiseaseName;
        member.LoadSecondaryDiseases(_diseaseCategoryMap);
        // 手动切换一级分类时，旧二级可能不在新列表中（正确行为：清空）
        // 加载/同分类重触发时，旧值仍在新列表中（自动保留）
        if (!string.IsNullOrEmpty(savedDiseaseName) && member.SecondaryDiseaseOptions.Any(o => o.Key == savedDiseaseName))
            member.DiseaseName = savedDiseaseName;
        member.UpdateHealthStatus(_noDiseaseKey, _severeLevelKeys, member.DiseaseCategory);
    }

    public void OnSearchResultSelectionChanged(object? previousSelection, object? currentSelection)
    {
        _logger.Info($"搜索结果选中变化");
    }

    /// <summary>
    /// 同步家庭成员的字典选项属性（从 string Key → DictItemOption）
    /// </summary>
    private void SyncMemberDictOptions(FamilyMember member)
    {
        if (!string.IsNullOrEmpty(member.Ethnicity))
            member.SelectedEthnicity = EthnicityOptions.FirstOrDefault(o => o.Key == member.Ethnicity);

        if (!string.IsNullOrEmpty(member.MaritalStatus))
            member.SelectedMaritalStatus = MaritalStatusOptions.FirstOrDefault(o => o.Key == member.MaritalStatus);

        if (!string.IsNullOrEmpty(member.EducationLevel))
            member.SelectedEducationLevel = EducationLevelOptions.FirstOrDefault(o => o.Key == member.EducationLevel);

        if (!string.IsNullOrEmpty(member.PoliticalStatus))
            member.SelectedPoliticalStatus = PoliticalStatusOptions.FirstOrDefault(o => o.Key == member.PoliticalStatus);

        if (!string.IsNullOrEmpty(member.HukouType))
            member.SelectedHukouType = HukouTypeOptions.FirstOrDefault(o => o.Key == member.HukouType);

        if (!string.IsNullOrEmpty(member.EmploymentStatus))
            member.SelectedEmploymentStatus = EmploymentStatusOptions.FirstOrDefault(o => o.Key == member.EmploymentStatus);

        if (!string.IsNullOrEmpty(member.MainIncomeSource))
            member.SelectedIncomeSource = IncomeSourceOptions.FirstOrDefault(o => o.Key == member.MainIncomeSource);

        if (!string.IsNullOrEmpty(member.DiseaseCategory))
        {
            member.SelectedDiseaseCategory = DiseaseCategoryOptions.FirstOrDefault(o => o.Key == member.DiseaseCategory);
            // 先保存疾病名称，防止 LoadSecondaryDiseases 的 Clear 触发级联清空
            var savedDiseaseName = member.DiseaseName;
            member.LoadSecondaryDiseases(_diseaseCategoryMap);
            // 恢复二级疾病选中（加载时 LoadSecondaryDiseases 已触发 SelectionChanged 清空了 DiseaseName）
            if (!string.IsNullOrEmpty(savedDiseaseName))
                member.DiseaseName = savedDiseaseName;
            if (!string.IsNullOrEmpty(member.DiseaseName))
                member.SelectedDiseaseNameObj = member.SecondaryDiseaseOptions.FirstOrDefault(o => o.Key == member.DiseaseName);
        }

        if (!string.IsNullOrEmpty(member.RelationshipToHead))
            member.SelectedRelationshipToHead = MemberRelationOptions.FirstOrDefault(o => o.Key == member.RelationshipToHead);
    }

    #endregion

    #region 放弃申请

    /// <summary>
    /// 放弃申请：五步表单第一步原本没有任何退出途径（上一步在首步隐藏），
    /// 此命令提供带确认的放弃入口——确认后直接返回上一页，不保存已录入内容。
    /// </summary>
    [RelayCommand]
    private async Task AbandonAsync()
    {
        var dialog = _serviceProvider.GetRequiredService<IDialogService>();
        var confirm = await dialog.DisplayAlertAsync(
            "放弃申请",
            "确定要放弃本次操作吗？\n已录入但未保存的信息将会丢失。",
            "放弃并返回", "继续填写");
        if (!confirm)
            return;

        await GoBackAsync();
    }

    #endregion
}
