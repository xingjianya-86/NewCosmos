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
    /// 收入小计（年·毛）= 月项×12 + 赡养年值 + 土地年值 + 补贴年值（不含刚性支出扣减）。
    /// 公式走 IncomeCalculationService 单点实现（§9.4 禁止 VM 重抄）；无日志版供绑定热路径调用。
    /// </summary>
    public decimal IncomeSubtotal => _incomeCalculationService.CalculateGrossAnnualFamilyIncome(
        WorkIncomeTotal, BusinessIncomeTotal, PropertyIncomeTotal,
        TransferIncomeTotal, OtherIncomeTotal, AlimonyIncome, LandIncomeTotal, SubsidyTotal);

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

    /// <summary>
    /// 农业补贴计算比例（百分比）= 家庭份数 ÷ 总份数 × 100，供补贴区显示与「更新计算比例」按钮取值。
    /// 无有效份数时为 0（按钮据此提示）。
    /// </summary>
    public decimal SubsidyShareRatioPercent =>
        TotalLandShares > 0 ? Math.Round(FamilyLandShares / TotalLandShares * 100, 2) : 0;

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
    /// 变更链上游档案的原月保障金额（户主死亡等停旧建新：渐退封顶比较用"原有享受额度"，
    /// 口径=户月保障金，不含分类施保，与 ApplyGraceCapAsync 文档一致）
    /// </summary>
    private decimal? _originalGuaranteeContext;

    /// <summary>
    /// 变更链上游档案ID（0=非链档案；渐退期内分类施保按原分类待遇重算的适用条件判定用）
    /// </summary>
    private long _originalApplicationId;

    /// <summary>
    /// 变更链上游档案的分类施保金额（渐退保存提醒/渐退审批表"分类施保由X调整为Y"取原值用）
    /// </summary>
    private decimal? _originalClassifiedContext;

    /// <summary>
    /// 变更链上游档案的户主姓名（渐退减发分类施保话术中"减去原户主X享受的份额"用）
    /// </summary>
    private string? _originalHeadNameContext;

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

}
