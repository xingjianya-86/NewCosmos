using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;
using NewCosmos.ViewModels.Base;
using NewCosmos.Helpers;

namespace NewCosmos.ViewModels.SocialAssistance;

public partial class SpouseInfoViewModel : ViewModelBase
{
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    public SpouseInfoViewModel(ILoggerService logger, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        Title = "配偶信息";
    }

    #region 开始
    [ObservableProperty]
    private bool _spouseEnabled = true;

    [ObservableProperty]
    private bool _healthEnabled = true;

    [ObservableProperty]
    private bool _disabilityEnabled = true;

    [ObservableProperty]
    private bool _employmentEnabled = true;

    #endregion

    #region 基本信息

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _idCard = string.Empty;

    [ObservableProperty]
    private string _gender = "";

    [ObservableProperty]
    private DateTime _birthDate;

    [ObservableProperty]
    private int _age;

    [ObservableProperty]
    private string _ethnicity = "汉族";

    [ObservableProperty]
    private string _maritalStatus = "已婚";

    [ObservableProperty]
    private string _phone = string.Empty;

    [ObservableProperty]
    private string _hukouType = "农村户口";

    [ObservableProperty]
    private string _hukouAddress = string.Empty;

    [ObservableProperty]
    private string _educationLevel = string.Empty;

    [ObservableProperty]
    private string _politicalStatus = "群众";

    #endregion

    #region 健康与劳动
    [ObservableProperty]
    private string _healthStatus = "Healthy";

    [ObservableProperty]
    private string _workCapacity = "有劳动能力";

    [ObservableProperty]
    private string _primaryDisease = string.Empty;

    [ObservableProperty]
    private bool _isSevereDisease;

    [ObservableProperty]
    private string _secondaryDisease = string.Empty;

    #endregion

    #region 残疾

    [ObservableProperty]
    private bool _isDisabled;

    [ObservableProperty]
    private string _disabilityCertificateNo = string.Empty;

    [ObservableProperty]
    private string _disabilityType = string.Empty;

    [ObservableProperty]
    private string _disabilityLevel = string.Empty;

    [ObservableProperty]
    private bool _isSevereDisability;

    #endregion

    #region 就业

    [ObservableProperty]
    private string _employmentStatus = string.Empty;

    [ObservableProperty]
    private string _workUnit = string.Empty;

    [ObservableProperty]
    private string _mainIncomeSource = string.Empty;

    [ObservableProperty]
    private decimal _annualIncome;

    [ObservableProperty]
    private string _selfCareAbility = "自理";

    #endregion

    #region Commands

    [RelayCommand]
    private async Task SaveAsync()
    {
        await Task.CompletedTask;
    }

    #endregion

    public FamilyMember ToEntity(long applicationId)
    {
        return new FamilyMember
        {
            ApplicationId = applicationId,
            Name = Name,
            IdCard = IdCard,
            Gender = PageDefaultValues.GetCode(PageDefaultValues.GenderOptions, PageDefaultValues.GenderCodes, Gender),
            Age = Age,
            BirthDate = BirthDate,
            Ethnicity = Ethnicity,
            Phone = Phone,
            HukouType = PageDefaultValues.GetCode(PageDefaultValues.HukouTypeOptions, PageDefaultValues.HukouTypeCodes, HukouType),
            HukouAddress = HukouAddress,
            MaritalStatus = MaritalStatus,
            EducationLevel = PageDefaultValues.GetCode(PageDefaultValues.EducationOptions, PageDefaultValues.EducationCodes, EducationLevel),
            PoliticalStatus = PoliticalStatus,
            RelationshipToHead = "配偶",
            HealthStatus = PageDefaultValues.GetCode(PageDefaultValues.HealthOptions, PageDefaultValues.HealthCodes, HealthStatus),
            WorkCapacity = PageDefaultValues.GetCode(PageDefaultValues.WorkCapacityOptions, PageDefaultValues.WorkCapacityCodes, WorkCapacity),
            DiseaseName = PrimaryDisease,
            IsSevereDisease = IsSevereDisease,
            SecondaryDisease = SecondaryDisease,
            IsDisabled = IsDisabled,
            DisabilityCertificateNo = DisabilityCertificateNo,
            DisabilityType = DisabilityType,
            DisabilityLevel = DisabilityLevel,
            IsSevereDisability = IsSevereDisability,
            EmploymentStatus = EmploymentStatus,
            WorkUnit = WorkUnit,
            MainIncomeSource = MainIncomeSource,
            AnnualIncome = AnnualIncome,
            SelfCareAbility = SelfCareAbility
        };
    }
}
