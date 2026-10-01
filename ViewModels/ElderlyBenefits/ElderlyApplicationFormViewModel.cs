using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Navigation;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.System;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;

namespace NewCosmos.ViewModels.ElderlyBenefits;

public partial class ElderlyApplicationFormViewModel : ViewModelBase
{
    private readonly IElderlyApplicationService _applicationService = null!;
    private readonly ILoggerService _logger = null!;
    private readonly IServiceProvider _serviceProvider = null!;
    private readonly IDialogService _dialogService = null!;
    private readonly IRegionService _regionService = null!;
    private readonly IUserService _userService = null!;
    private readonly IOrganizationService _organizationService = null!;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    private long _id;
    private bool _suppressEvaluate;

    /// <summary>保存后自动跳转到停发页（从导入库建档补全流程）</summary>
    public bool NavigateToStopAfterSave { get; private set; }

    /// <summary>待停发的档案ID（保存后跳转停发页用）</summary>
    public long PendingStopApplicationId { get; set; }

    /// <summary>导入库来源记录ID（source_type='ElderlySubsidyHistory' 时有效，补全保存后删除导入库用）</summary>
    private long? _importedHistoryId;

    /// <summary>设置 NavigateToStopAfterSave 并通知横幅属性刷新</summary>
    public void SetNavigateToStopAfterSave(bool value)
    {
        NavigateToStopAfterSave = value;
        OnPropertyChanged(nameof(IsModeBannerVisible));
        OnPropertyChanged(nameof(ModeBannerText));
    }

    #region 操作模式

    [ObservableProperty]
    private FormOperationMode _operationMode = FormOperationMode.Create;

    /// <summary>是否新建模式</summary>
    public bool IsCreateMode => OperationMode == FormOperationMode.Create;

    /// <summary>是否查看模式</summary>
    public bool IsViewMode => OperationMode == FormOperationMode.View;

    /// <summary>是否编辑模式</summary>
    public bool IsEditMode => OperationMode == FormOperationMode.Edit;

    /// <summary>是否可编辑（非查看模式）</summary>
    public bool IsEditable => OperationMode != FormOperationMode.View;

    /// <summary>是否可保存（编辑模式或新建，且非查看）</summary>
    public bool IsSavable => OperationMode != FormOperationMode.View;

    /// <summary>是否显示模式横幅（仅补全模式）</summary>
    public bool IsModeBannerVisible => NavigateToStopAfterSave;

    /// <summary>模式横幅文案</summary>
    public string ModeBannerText => NavigateToStopAfterSave
        ? "数据补全模式\n补全建档缺失信息，保存后进入停发登记"
        : string.Empty;

    #endregion

    #region 表单分区 Tab

    [ObservableProperty]
    private int _selectedSection;

    public bool IsSection0Selected => SelectedSection == 0;
    public bool IsSection1Selected => SelectedSection == 1;
    public bool IsSection2Selected => SelectedSection == 2;

    /// <summary>上一页可用（非第一个 Tab，XAML IsEnabled 绑定用）</summary>
    public bool CanGoPrevious => SelectedSection > 0;

    /// <summary>下一页可用（非最后一个 Tab，XAML IsEnabled 绑定用）</summary>
    public bool CanGoNext => SelectedSection < 2;

    /// <summary>保存并确认仅在最后一个 Tab（判定认证）显示</summary>
    public bool IsSaveAndConfirmVisible => IsSavable && IsSection2Selected;

    partial void OnSelectedSectionChanged(int value)
    {
        OnPropertyChanged(nameof(IsSection0Selected));
        OnPropertyChanged(nameof(IsSection1Selected));
        OnPropertyChanged(nameof(IsSection2Selected));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(IsSaveAndConfirmVisible));
        PreviousSectionCommand.NotifyCanExecuteChanged();
        NextSectionCommand.NotifyCanExecuteChanged();
    }

    // CanExecute 用方法（CommunityToolkit 惯例；属性仅供 XAML IsEnabled 绑定）
    private bool CanGoPreviousMethod() => SelectedSection > 0;

    private bool CanGoNextMethod() => SelectedSection < 2;

    [RelayCommand(CanExecute = nameof(CanGoPreviousMethod))]
    private void PreviousSection() => SelectedSection = Math.Max(0, SelectedSection - 1);

    [RelayCommand(CanExecute = nameof(CanGoNextMethod))]
    private void NextSection() => SelectedSection = Math.Min(2, SelectedSection + 1);

    [RelayCommand]
    private void SwitchSection(string? sectionIndex)
    {
        if (int.TryParse(sectionIndex, out var idx))
        {
            SelectedSection = idx;
        }
    }

    #endregion

    #region 户籍地区级联（遵循低收入认定流程）

    /// <summary>级联抑制标志：默认值设置时抑制 OnChanged 异步级联，避免与手动 await 交错产生竞态</summary>
    private bool _suppressRegionCascade;

    [ObservableProperty]
    private ObservableCollection<string> _hukouCityOptions = new();

    [ObservableProperty]
    private ObservableCollection<string> _hukouCountyOptions = new();

    [ObservableProperty]
    private ObservableCollection<string> _hukouTownOptions = new();

    [ObservableProperty]
    private ObservableCollection<string> _hukouVillageOptions = new();

    [ObservableProperty]
    private string _selectedHukouCity = string.Empty;

    [ObservableProperty]
    private string _selectedHukouCounty = string.Empty;

    [ObservableProperty]
    private string _selectedHukouTown = string.Empty;

    [ObservableProperty]
    private string _selectedHukouVillage = string.Empty;

    [ObservableProperty]
    private string _hukouDetailAddress = string.Empty;

    partial void OnSelectedHukouCityChanged(string value)
    {
        if (_suppressRegionCascade) return;
        SafeFireAndForget(async () => await LoadHukouCountiesAsync());
    }

    partial void OnSelectedHukouCountyChanged(string value)
    {
        if (_suppressRegionCascade) return;
        SafeFireAndForget(async () => await LoadHukouTownsAsync());
    }

    partial void OnSelectedHukouTownChanged(string value)
    {
        if (_suppressRegionCascade) return;
        SafeFireAndForget(async () => await LoadHukouVillagesAsync());
    }

    private async Task LoadHukouCitiesAsync()
    {
        var result = await _regionService.GetCitiesAsync(CancellationToken);
        if (result.IsFailure || result.Value == null) return;
        HukouCityOptions.Clear();
        foreach (var c in result.Value) HukouCityOptions.Add(c.CityName);
        if (string.IsNullOrEmpty(SelectedHukouCity) && HukouCityOptions.Count > 0)
        {
            SelectedHukouCity = HukouCityOptions[0];
        }
    }

    private async Task LoadHukouCountiesAsync()
    {
        HukouCountyOptions.Clear();
        HukouTownOptions.Clear();
        HukouVillageOptions.Clear();
        if (string.IsNullOrEmpty(SelectedHukouCity)) return;
        var result = await _regionService.GetCountiesByCityAsync(SelectedHukouCity, CancellationToken);
        if (result.IsFailure || result.Value == null) return;
        foreach (var c in result.Value) HukouCountyOptions.Add(c.CountyName);
        if (HukouCountyOptions.Count > 0) SelectedHukouCounty = HukouCountyOptions[0];
    }

    private async Task LoadHukouTownsAsync()
    {
        HukouTownOptions.Clear();
        HukouVillageOptions.Clear();
        if (string.IsNullOrEmpty(SelectedHukouCity) || string.IsNullOrEmpty(SelectedHukouCounty)) return;
        var counties = await _regionService.GetCountiesByCityAsync(SelectedHukouCity, CancellationToken);
        if (counties.IsFailure || counties.Value == null) return;
        var county = counties.Value.FirstOrDefault(c => c.CountyName == SelectedHukouCounty);
        if (county == null) return;
        var towns = await _regionService.GetTownsByCountyIdAsync(county.Id, CancellationToken);
        if (towns.IsFailure || towns.Value == null) return;
        foreach (var t in towns.Value) HukouTownOptions.Add(t.TownName);
        if (HukouTownOptions.Count > 0) SelectedHukouTown = HukouTownOptions[0];
    }

    private async Task LoadHukouVillagesAsync()
    {
        HukouVillageOptions.Clear();
        if (string.IsNullOrEmpty(SelectedHukouCity) || string.IsNullOrEmpty(SelectedHukouCounty)) return;
        var counties = await _regionService.GetCountiesByCityAsync(SelectedHukouCity, CancellationToken);
        if (counties.IsFailure || counties.Value == null) return;
        var county = counties.Value.FirstOrDefault(c => c.CountyName == SelectedHukouCounty);
        if (county == null) return;
        var towns = await _regionService.GetTownsByCountyIdAsync(county.Id, CancellationToken);
        if (towns.IsFailure || towns.Value == null) return;
        var town = towns.Value.FirstOrDefault(t => t.TownName == SelectedHukouTown);
        if (town == null) return;
        var villages = await _regionService.GetVillagesByTownIdAsync(town.Id, CancellationToken);
        if (villages.IsFailure || villages.Value == null) return;
        foreach (var v in villages.Value) HukouVillageOptions.Add(v.VillageName);
        if (HukouVillageOptions.Count > 0) SelectedHukouVillage = HukouVillageOptions[0];
    }

    #endregion

    #region 家庭地区级联

    [ObservableProperty]
    private ObservableCollection<string> _familyCityOptions = new();

    [ObservableProperty]
    private ObservableCollection<string> _familyCountyOptions = new();

    [ObservableProperty]
    private ObservableCollection<string> _familyTownOptions = new();

    [ObservableProperty]
    private ObservableCollection<string> _familyVillageOptions = new();

    [ObservableProperty]
    private string _selectedFamilyCity = string.Empty;

    [ObservableProperty]
    private string _selectedFamilyCounty = string.Empty;

    [ObservableProperty]
    private string _selectedFamilyTown = string.Empty;

    [ObservableProperty]
    private string _selectedFamilyVillage = string.Empty;

    [ObservableProperty]
    private string _detailAddress = string.Empty;

    partial void OnSelectedFamilyCityChanged(string value) => SafeFireAndForget(async () => await LoadFamilyCountiesAsync());

    partial void OnSelectedFamilyCountyChanged(string value) => SafeFireAndForget(async () => await LoadFamilyTownsAsync());

    partial void OnSelectedFamilyTownChanged(string value) => SafeFireAndForget(async () => await LoadFamilyVillagesAsync());

    private async Task LoadFamilyCitiesAsync()
    {
        var result = await _regionService.GetCitiesAsync(CancellationToken);
        if (result.IsFailure || result.Value == null) return;
        FamilyCityOptions.Clear();
        foreach (var c in result.Value) FamilyCityOptions.Add(c.CityName);
        if (string.IsNullOrEmpty(SelectedFamilyCity) && FamilyCityOptions.Count > 0)
        {
            SelectedFamilyCity = FamilyCityOptions[0];
        }
    }

    private async Task LoadFamilyCountiesAsync()
    {
        FamilyCountyOptions.Clear();
        FamilyTownOptions.Clear();
        FamilyVillageOptions.Clear();
        if (string.IsNullOrEmpty(SelectedFamilyCity)) return;
        var result = await _regionService.GetCountiesByCityAsync(SelectedFamilyCity, CancellationToken);
        if (result.IsFailure || result.Value == null) return;
        foreach (var c in result.Value) FamilyCountyOptions.Add(c.CountyName);
        if (FamilyCountyOptions.Count > 0) SelectedFamilyCounty = FamilyCountyOptions[0];
    }

    private async Task LoadFamilyTownsAsync()
    {
        FamilyTownOptions.Clear();
        FamilyVillageOptions.Clear();
        if (string.IsNullOrEmpty(SelectedFamilyCity) || string.IsNullOrEmpty(SelectedFamilyCounty)) return;
        var counties = await _regionService.GetCountiesByCityAsync(SelectedFamilyCity, CancellationToken);
        if (counties.IsFailure || counties.Value == null) return;
        var county = counties.Value.FirstOrDefault(c => c.CountyName == SelectedFamilyCounty);
        if (county == null) return;
        var towns = await _regionService.GetTownsByCountyIdAsync(county.Id, CancellationToken);
        if (towns.IsFailure || towns.Value == null) return;
        foreach (var t in towns.Value) FamilyTownOptions.Add(t.TownName);
        if (FamilyTownOptions.Count > 0) SelectedFamilyTown = FamilyTownOptions[0];
    }

    private async Task LoadFamilyVillagesAsync()
    {
        FamilyVillageOptions.Clear();
        if (string.IsNullOrEmpty(SelectedFamilyCity) || string.IsNullOrEmpty(SelectedFamilyCounty)) return;
        var counties = await _regionService.GetCountiesByCityAsync(SelectedFamilyCity, CancellationToken);
        if (counties.IsFailure || counties.Value == null) return;
        var county = counties.Value.FirstOrDefault(c => c.CountyName == SelectedFamilyCounty);
        if (county == null) return;
        var towns = await _regionService.GetTownsByCountyIdAsync(county.Id, CancellationToken);
        if (towns.IsFailure || towns.Value == null) return;
        var town = towns.Value.FirstOrDefault(t => t.TownName == SelectedFamilyTown);
        if (town == null) return;
        var villages = await _regionService.GetVillagesByTownIdAsync(town.Id, CancellationToken);
        if (villages.IsFailure || villages.Value == null) return;
        foreach (var v in villages.Value) FamilyVillageOptions.Add(v.VillageName);
        if (FamilyVillageOptions.Count > 0) SelectedFamilyVillage = FamilyVillageOptions[0];
    }

    #endregion

    #region 申请人信息

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private string _idCard = string.Empty;

    [ObservableProperty]
    private string _gender = string.Empty;

    [ObservableProperty]
    private DateTime? _birthDate;

    [ObservableProperty]
    private int? _age;

    [ObservableProperty]
    private string _phone = string.Empty;

    [ObservableProperty]
    private string _hukouAddress = string.Empty;

    [ObservableProperty]
    private string _hukouVillage = string.Empty;

    [ObservableProperty]
    private string _familyAddress = string.Empty;

    [ObservableProperty]
    private string _bankName = string.Empty;

    [ObservableProperty]
    private string _bankAccount = string.Empty;

    [ObservableProperty]
    private string _agentName = string.Empty;

    [ObservableProperty]
    private string _agentRelation = string.Empty;

    [ObservableProperty]
    private string _agentReceiveName = string.Empty;

    [ObservableProperty]
    private string _agentReceiveRelation = string.Empty;

    [ObservableProperty]
    private string _agentReceiveBankName = string.Empty;

    [ObservableProperty]
    private string _agentReceiveBankAccount = string.Empty;

    [ObservableProperty]
    private string _agentReceiveReason = string.Empty;

    #endregion

    #region 享受类别与补发

    [ObservableProperty]
    private string _category = string.Empty;

    [ObservableProperty]
    private string _categoryName = string.Empty;

    [ObservableProperty]
    private string _identityFlag = string.Empty;

    [ObservableProperty]
    private string _identitySource = string.Empty;

    [ObservableProperty]
    private bool _hasEvaluateResult;

    [ObservableProperty]
    private string _evaluateMessage = string.Empty;

    [ObservableProperty]
    private decimal _monthlyAmount;

    [ObservableProperty]
    private string _issueStartMonth = string.Empty;

    [ObservableProperty]
    private decimal _issueAmount;

    [ObservableProperty]
    private string _paybackStartMonth = string.Empty;

    [ObservableProperty]
    private string _paybackEndMonth = string.Empty;

    [ObservableProperty]
    private string _autoStartMonth = string.Empty;

    [ObservableProperty]
    private string _autoEndMonth = string.Empty;

    [ObservableProperty]
    private int _paybackMonths;

    [ObservableProperty]
    private decimal _paybackAmount;

    /// <summary>补发原因（未及时登记/查找不到本人/短暂失联/其他，可选）</summary>
    [ObservableProperty]
    private string _paybackReason = string.Empty;

    [ObservableProperty]
    private bool _isPaybackReasonMissed;

    [ObservableProperty]
    private bool _isPaybackReasonNotLocated;

    [ObservableProperty]
    private bool _isPaybackReasonLostContact;

    [ObservableProperty]
    private bool _isPaybackReasonOther;

    partial void OnIsPaybackReasonMissedChanged(bool value)
    {
        if (value) PaybackReason = ElderlyBenefitConstants.PaybackReasonMissedRegistration;
    }

    partial void OnIsPaybackReasonNotLocatedChanged(bool value)
    {
        if (value) PaybackReason = ElderlyBenefitConstants.PaybackReasonNotLocated;
    }

    partial void OnIsPaybackReasonLostContactChanged(bool value)
    {
        if (value) PaybackReason = ElderlyBenefitConstants.PaybackReasonLostContact;
    }

    partial void OnIsPaybackReasonOtherChanged(bool value)
    {
        if (value) PaybackReason = ElderlyBenefitConstants.PaybackReasonOther;
    }

    private void ApplyPaybackReasonSelection(string reason)
    {
        IsPaybackReasonMissed = reason == ElderlyBenefitConstants.PaybackReasonMissedRegistration;
        IsPaybackReasonNotLocated = reason == ElderlyBenefitConstants.PaybackReasonNotLocated;
        IsPaybackReasonLostContact = reason == ElderlyBenefitConstants.PaybackReasonLostContact;
        IsPaybackReasonOther = reason == ElderlyBenefitConstants.PaybackReasonOther;
    }
    [ObservableProperty]
    private ObservableCollection<ElderlyPaybackSegment> _segments = new();

    [ObservableProperty]
    private ObservableCollection<CategoryDisplayItem> _categoryItems = new();

    /// <summary>补发起算月显示（2025年1月）</summary>
    public string PaybackStartDisplay => FormatMonth(PaybackStartMonth);

    /// <summary>补发止算月显示（2025年1月）</summary>
    public string PaybackEndDisplay => FormatMonth(PaybackEndMonth);

    /// <summary>计发年月显示（2026年8月）</summary>
    public string IssueStartMonthDisplay => FormatMonth(IssueStartMonth);

    /// <summary>补发金额显示（100元）</summary>
    public string PaybackAmountDisplay => $"{PaybackAmount:F2}元";

    /// <summary>计发金额显示（25元/月）</summary>
    public string IssueAmountDisplay => $"{IssueAmount:F2}元/月";

    /// <summary>月补贴标准显示（25元/月）</summary>
    public string MonthlyAmountDisplay => $"{MonthlyAmount:F2}元/月";

    private static string FormatMonth(string month)
    {
        if (string.IsNullOrWhiteSpace(month)) return string.Empty;
        if (DateTime.TryParseExact(month, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dt))
        {
            return $"{dt.Year}年{dt.Month}月";
        }
        return month;
    }

    #endregion

    #region 特殊情况

    [ObservableProperty]
    private bool _isSpecialCase;

    [ObservableProperty]
    private string _specialReason = string.Empty;

    #endregion

    #region 受理日期

    [ObservableProperty]
    private DateTime _applyDate = DateTime.Today;

    #endregion

    public ElderlyApplicationFormViewModel(
        IElderlyApplicationService applicationService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        IDialogService dialogService,
        IRegionService regionService,
        IUserService userService,
        IOrganizationService organizationService)
    {
        _applicationService = applicationService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _dialogService = dialogService;
        _regionService = regionService;
        _userService = userService;
        _organizationService = organizationService;
    }

    partial void OnOperationModeChanged(FormOperationMode value)
    {
        OnPropertyChanged(nameof(IsCreateMode));
        OnPropertyChanged(nameof(IsViewMode));
        OnPropertyChanged(nameof(IsEditMode));
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(IsSavable));
        OnPropertyChanged(nameof(IsSaveAndConfirmVisible));
    }

    /// <summary>
    /// 身份证变化时自动判类 + 补发计算（仅新建/编辑且非查看时触发）
    /// </summary>
    partial void OnIdCardChanged(string value)
    {
        if (_suppressEvaluate) return;
        if (OperationMode == FormOperationMode.View) return;
        SafeFireAndForget(async () => await EvaluateAsync());
    }

    /// <summary>
    /// 特殊情况开关变化时：开启则可用手动起止月（进入手动模式）
    /// </summary>
    partial void OnIsSpecialCaseChanged(bool value)
    {
        if (!value)
        {
            // 关闭特殊情况：恢复系统自动计算值
            PaybackStartMonth = AutoStartMonth;
            PaybackEndMonth = AutoEndMonth;
            SafeFireAndForget(() => RecalculateFromMonthsAsync());
        }
    }

    public async Task InitializeAsync()
    {
        if (OperationMode == FormOperationMode.Create)
        {
            ApplyDate = DateTime.Today;
            _id = 0;
            await LoadInitialRegionAsync();
            await ApplyOperatorRegionDefaultsAsync();
        }
    }

    /// <summary>
    /// 新建模式：按当前登录用户所在组织对应的区划预选户籍/家庭住址默认值
    /// （户籍与家庭住址默认一致，均可手动修改；组织未配置区划时保持原默认第一个城市）
    /// </summary>
    private async Task ApplyOperatorRegionDefaultsAsync()
    {
        if (App.CurrentUserId == null) return;

        var userResult = await _userService.GetByIdAsync(App.CurrentUserId.Value, CancellationToken);
        if (userResult.IsFailure || userResult.Value == null || !userResult.Value.OrganizationId.HasValue)
            return;

        var orgResult = await _organizationService.GetByIdAsync(userResult.Value.OrganizationId.Value, CancellationToken);
        if (orgResult.IsFailure || orgResult.Value == null)
            return;

        var org = orgResult.Value;

        // 城市
        var cityName = string.Empty;
        if (org.CityId.HasValue)
        {
            var city = await _regionService.GetCityByIdAsync(org.CityId.Value, CancellationToken);
            if (city.IsSuccess && city.Value != null && HukouCityOptions.Contains(city.Value.CityName))
                cityName = city.Value.CityName;
        }
        if (string.IsNullOrEmpty(cityName)) return;

        // 设置默认值期间抑制 OnChanged 异步级联，手动顺序 await，避免并行竞态覆盖选项
        _suppressRegionCascade = true;
        try
        {
            // 户籍（逐级设置并等待级联加载完成，避免异步乱序覆盖）
            SelectedHukouCity = cityName;
            await LoadHukouCountiesAsync();
            if (org.CountyId.HasValue && HukouCountyOptions.Contains(org.CountyName))
            {
                SelectedHukouCounty = org.CountyName;
                await LoadHukouTownsAsync();
                if (org.TownId.HasValue && HukouTownOptions.Contains(org.TownName))
                {
                    SelectedHukouTown = org.TownName;
                    await LoadHukouVillagesAsync();
                    if (org.VillageId.HasValue && HukouVillageOptions.Contains(org.VillageName))
                        SelectedHukouVillage = org.VillageName;
                }
            }

            // 家庭住址跟随户籍（默认一致，可改）
            SelectedFamilyCity = cityName;
            await LoadFamilyCountiesAsync();
            if (org.CountyId.HasValue && FamilyCountyOptions.Contains(org.CountyName))
            {
                SelectedFamilyCounty = org.CountyName;
                await LoadFamilyTownsAsync();
                if (org.TownId.HasValue && FamilyTownOptions.Contains(org.TownName))
                {
                    SelectedFamilyTown = org.TownName;
                    await LoadFamilyVillagesAsync();
                    if (org.VillageId.HasValue && FamilyVillageOptions.Contains(org.VillageName))
                        SelectedFamilyVillage = org.VillageName;
                }
            }
        }
        finally
        {
            _suppressRegionCascade = false;
        }
    }

    /// <summary>
    /// 加载已有登记（编辑/查看模式）
    /// </summary>
    public async Task LoadAsync(long id)
    {
        _id = id;
        _suppressEvaluate = true;
        await ExecuteAsync(async () =>
        {
            var result = await _applicationService.GetByIdAsync(id, CancellationToken);
            if (result.IsFailure || result.Value == null)
                return Result.Failure(ErrorCodes.RECORD_NOT_FOUND, "登记记录不存在");

            var app = result.Value;
            Name = app.Name;
            IdCard = app.IdCard;
            Gender = app.Gender;
            BirthDate = app.BirthDate;
            Age = app.Age;
            Phone = app.Phone;
            HukouAddress = app.HukouAddress;
            HukouVillage = app.HukouVillage;
            HukouDetailAddress = app.HukouDetailAddress;
            FamilyAddress = app.FamilyAddress;
            DetailAddress = app.DetailAddress;
            BankName = app.BankName;
            BankAccount = app.BankAccount;
            AgentName = app.AgentName;
            AgentRelation = app.AgentRelation;
            AgentReceiveName = app.AgentReceiveName;
            AgentReceiveRelation = app.AgentReceiveRelation;
            AgentReceiveBankName = app.AgentReceiveBankName;
            AgentReceiveBankAccount = app.AgentReceiveBankAccount;
            AgentReceiveReason = app.AgentReceiveReason;
            Category = app.Category;
            CategoryName = app.CategoryName;
            IdentityFlag = app.IdentityFlag;
            IdentitySource = app.IdentitySource;
            MonthlyAmount = string.IsNullOrEmpty(app.Category) ? 0m : await GetCategoryMonthlyAmountAsync(app.Category, CancellationToken);
            IssueStartMonth = app.IssueStartMonth;
            IssueAmount = app.IssueAmount;
            PaybackStartMonth = app.PaybackStartMonth;
            PaybackEndMonth = app.PaybackEndMonth;
            AutoStartMonth = app.AutoStartMonth;
            AutoEndMonth = app.AutoEndMonth;
            PaybackMonths = app.PaybackMonths;
            PaybackAmount = app.PaybackAmount;
            PaybackReason = app.PaybackReason;
            ApplyPaybackReasonSelection(app.PaybackReason);
            IsSpecialCase = app.IsSpecialCase;
            SpecialReason = app.SpecialReason;
            ApplyDate = app.ApplyDate ?? DateTime.Today;

            // 记录导入库来源ID：数据补全（从导入库建档补全信息）保存后删除导入库记录
            _importedHistoryId = string.Equals(app.SourceType, "ElderlySubsidyHistory", StringComparison.OrdinalIgnoreCase)
                ? app.SourceId
                : null;

            HasEvaluateResult = true;
            EvaluateMessage = $"已匹配到：{CategoryName}";
            RefreshCategoryItems(Category);
            OnPropertyChanged(nameof(PaybackStartDisplay));
            OnPropertyChanged(nameof(PaybackEndDisplay));
            OnPropertyChanged(nameof(IssueStartMonthDisplay));
            OnPropertyChanged(nameof(PaybackAmountDisplay));
            OnPropertyChanged(nameof(IssueAmountDisplay));
            OnPropertyChanged(nameof(MonthlyAmountDisplay));

            // 回填补发分段明细（编辑/查看草稿时保留分段，避免保存时被清空）
            var segmentsResult = await _applicationService.GetSegmentsAsync(id, CancellationToken);
            if (segmentsResult.IsSuccess && segmentsResult.Value != null)
            {
                Segments.Clear();
                foreach (var segment in segmentsResult.Value)
                {
                    Segments.Add(segment);
                }
            }

            await LoadRegionCascadesForEditAsync(app);

            return Result.Success();
        }, "加载登记信息...");
        _suppressEvaluate = false;
    }

    /// <summary>
    /// 编辑/查看模式：按已存地区 id 逐级回显户籍与家庭级联
    /// </summary>
    private async Task LoadRegionCascadesForEditAsync(Models.Entities.ElderlyApplication app)
    {
        await LoadHukouCitiesAsync();
        await LoadFamilyCitiesAsync();

        if (app.HukouCityId.HasValue)
        {
            var city = await _regionService.GetCityByIdAsync(app.HukouCityId.Value, CancellationToken);
            if (city.IsSuccess && city.Value != null && HukouCityOptions.Contains(city.Value.CityName))
            {
                SelectedHukouCity = city.Value.CityName;
                await LoadHukouCountiesAsync();
                if (app.HukouCountyId.HasValue)
                {
                    var counties = await _regionService.GetCountiesByCityAsync(SelectedHukouCity, CancellationToken);
                    var county = counties.IsSuccess ? counties.Value?.FirstOrDefault(c => c.Id == app.HukouCountyId) : null;
                    if (county != null)
                    {
                        SelectedHukouCounty = county.CountyName;
                        await LoadHukouTownsAsync();
                        if (app.HukouTownId.HasValue)
                        {
                            var towns = await _regionService.GetTownsByCountyIdAsync(app.HukouCountyId.Value, CancellationToken);
                            var town = towns.IsSuccess ? towns.Value?.FirstOrDefault(t => t.Id == app.HukouTownId) : null;
                            if (town != null)
                            {
                                SelectedHukouTown = town.TownName;
                                await LoadHukouVillagesAsync();
                                if (app.HukouVillageId.HasValue)
                                {
                                    var villages = await _regionService.GetVillagesByTownIdAsync(app.HukouTownId.Value, CancellationToken);
                                    var village = villages.IsSuccess ? villages.Value?.FirstOrDefault(v => v.Id == app.HukouVillageId) : null;
                                    if (village != null) SelectedHukouVillage = village.VillageName;
                                }
                            }
                        }
                    }
                }
            }
        }

        if (app.FamilyCityId.HasValue)
        {
            var city = await _regionService.GetCityByIdAsync(app.FamilyCityId.Value, CancellationToken);
            if (city.IsSuccess && city.Value != null && FamilyCityOptions.Contains(city.Value.CityName))
            {
                SelectedFamilyCity = city.Value.CityName;
                await LoadFamilyCountiesAsync();
                if (app.FamilyCountyId.HasValue)
                {
                    var counties = await _regionService.GetCountiesByCityAsync(SelectedFamilyCity, CancellationToken);
                    var county = counties.IsSuccess ? counties.Value?.FirstOrDefault(c => c.Id == app.FamilyCountyId) : null;
                    if (county != null)
                    {
                        SelectedFamilyCounty = county.CountyName;
                        await LoadFamilyTownsAsync();
                        if (app.FamilyTownId.HasValue)
                        {
                            var towns = await _regionService.GetTownsByCountyIdAsync(app.FamilyCountyId.Value, CancellationToken);
                            var town = towns.IsSuccess ? towns.Value?.FirstOrDefault(t => t.Id == app.FamilyTownId) : null;
                            if (town != null)
                            {
                                SelectedFamilyTown = town.TownName;
                                await LoadFamilyVillagesAsync();
                                if (app.FamilyVillageId.HasValue)
                                {
                                    var villages = await _regionService.GetVillagesByTownIdAsync(app.FamilyTownId.Value, CancellationToken);
                                    var village = villages.IsSuccess ? villages.Value?.FirstOrDefault(v => v.Id == app.FamilyVillageId) : null;
                                    if (village != null) SelectedFamilyVillage = village.VillageName;
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// 新建模式：初始化城市选项
    /// </summary>
    public async Task LoadInitialRegionAsync()
    {
        await LoadHukouCitiesAsync();
        await LoadFamilyCitiesAsync();
    }

    /// <summary>
    /// 身份证联动：判类 + 补发分段计算
    /// </summary>
    private async Task EvaluateAsync()
    {
        if (string.IsNullOrWhiteSpace(IdCard)) return;

        await ExecuteAsync(async () =>
        {
            var result = await _applicationService.EvaluateAsync(IdCard, ApplyDate, CancellationToken);
            if (result.IsFailure)
            {
                ResetEvaluateResult();
                EvaluateMessage = result.Message ?? "评估失败";
                return result;
            }

            var eval = result.Value;
            Gender = eval.Gender;
            BirthDate = eval.BirthDate;
            Age = eval.Age;
            Category = eval.Category;
            CategoryName = eval.CategoryName;
            IdentityFlag = eval.IdentityFlag;
            IdentitySource = eval.IdentitySource;
            MonthlyAmount = eval.MonthlyAmount;
            IssueStartMonth = eval.IssueStartMonth;
            IssueAmount = eval.IssueAmount;
            ApplyEvalPayback(eval.Payback);
            RefreshCategoryItems(eval.Category);
            OnPropertyChanged(nameof(PaybackStartDisplay));
            OnPropertyChanged(nameof(PaybackEndDisplay));
            OnPropertyChanged(nameof(IssueStartMonthDisplay));
            OnPropertyChanged(nameof(PaybackAmountDisplay));
            OnPropertyChanged(nameof(IssueAmountDisplay));
            OnPropertyChanged(nameof(MonthlyAmountDisplay));

            HasEvaluateResult = true;
            EvaluateMessage = $"当前年龄 {eval.Age} 周岁，享受类别：{eval.CategoryName}，月标准 {eval.MonthlyAmount:F2} 元/月";
            if (!string.IsNullOrEmpty(eval.IdentitySource))
                EvaluateMessage += $"；已匹配导入库身份（{eval.IdentityFlag}）";

            // 输入身份证后从导入台账自动带出人员信息（直接填充，可改）
            var imported = await _applicationService.GetImportedByIdCardAsync(IdCard, CancellationToken);
            if (imported.IsSuccess && imported.Value != null)
            {
                Name = imported.Value.Name ?? string.Empty;
                Phone = imported.Value.Phone ?? string.Empty;
                BankAccount = imported.Value.BankAccount ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(imported.Value.Address))
                {
                    HukouAddress = imported.Value.Address;
                    HukouDetailAddress = imported.Value.Address;
                }
                EvaluateMessage += $"；已从导入台账自动填入[{imported.Value.Name}]基本信息";
            }

            _logger.LogBusiness("普惠高龄身份证评估",
                ("Age", eval.Age), ("Category", eval.Category), ("PaybackAmount", eval.Payback.TotalAmount));
            return result;
        }, "评估享受类别与补发...");
    }

    private void ApplyEvalPayback(ElderlyPaybackResult payback)
    {
        PaybackStartMonth = payback.StartMonth;
        PaybackEndMonth = payback.EndMonth;
        AutoStartMonth = payback.StartMonth;
        AutoEndMonth = payback.EndMonth;
        PaybackMonths = payback.TotalMonths;
        PaybackAmount = payback.TotalAmount;

        Segments.Clear();
        foreach (var segment in payback.Segments)
        {
            Segments.Add(segment);
        }
    }

    /// <summary>
    /// 刷新享受类别展示项（只读判定结果：命中 ☑ 高亮，未命中 □）
    /// </summary>
    private void RefreshCategoryItems(string category)
    {
        CategoryItems.Clear();
        var codes = new[]
        {
            ElderlyBenefitConstants.CatLowSubsidy,
            ElderlyBenefitConstants.CatOtherElderly,
            ElderlyBenefitConstants.Cat90To99,
            ElderlyBenefitConstants.Cat100Plus
        };
        foreach (var code in codes)
        {
            CategoryItems.Add(new CategoryDisplayItem(
                ElderlyBenefitConstants.GetCategoryName(code),
                code == category));
        }
    }

    private void ResetEvaluateResult()
    {
        HasEvaluateResult = false;
        Gender = string.Empty;
        BirthDate = null;
        Age = null;
        Category = string.Empty;
        CategoryName = string.Empty;
        IdentityFlag = string.Empty;
        IdentitySource = string.Empty;
        MonthlyAmount = 0;
        IssueStartMonth = string.Empty;
        IssueAmount = 0;
        PaybackStartMonth = string.Empty;
        PaybackEndMonth = string.Empty;
        AutoStartMonth = string.Empty;
        AutoEndMonth = string.Empty;
        PaybackMonths = 0;
        PaybackAmount = 0;
        Segments.Clear();
    }

    [RelayCommand]
    private async Task RecalculateAsync()
    {
        if (string.IsNullOrWhiteSpace(IdCard)) return;
        await ExecuteAsync(async () =>
        {
            var result = await _applicationService.EvaluateAsync(IdCard, ApplyDate, CancellationToken);
            if (result.IsFailure)
                return result;

            var eval = result.Value;
            Gender = eval.Gender;
            BirthDate = eval.BirthDate;
            Age = eval.Age;
            Category = eval.Category;
            CategoryName = eval.CategoryName;
            IdentityFlag = eval.IdentityFlag;
            IdentitySource = eval.IdentitySource;
            MonthlyAmount = eval.MonthlyAmount;
            IssueStartMonth = eval.IssueStartMonth;
            IssueAmount = eval.IssueAmount;
            ApplyEvalPayback(eval.Payback);
            HasEvaluateResult = true;
            EvaluateMessage = $"当前年龄 {eval.Age} 周岁，享受类别：{eval.CategoryName}，月标准 {eval.MonthlyAmount:F2} 元/月";
            return result;
        }, "重新评估...");
    }

    /// <summary>
    /// 判定按钮：手动触发判类 + 补发计算（身份证自动评估之外的手动判定入口）
    /// </summary>
    [RelayCommand]
    private async Task Evaluate()
    {
        if (string.IsNullOrWhiteSpace(IdCard))
        {
            await _dialogService.DisplayAlertAsync("提示", "请先填写申请人身份证号码", "确定");
            return;
        }

        await EvaluateAsync();
    }

    /// <summary>
    /// 特殊情况：按手动起止月重算分段与金额
    /// </summary>
    private async Task RecalculateFromMonthsAsync()
    {
        if (string.IsNullOrWhiteSpace(PaybackStartMonth) || string.IsNullOrWhiteSpace(PaybackEndMonth)) return;
        if (BirthDate == null) return;

        var result = await _applicationService.GetMonthlyAmountAsync(Category, CancellationToken);
        if (result.IsFailure) return;

        var category = Category;
        var identityFlag = IdentityFlag;

        // 手动起算月可能被 calc 重置（calc 内部取 max(政策,满80)）；此处强制使用用户输入的起止月
        if (TryParseMonth(PaybackStartMonth, out var start) && TryParseMonth(PaybackEndMonth, out var end) && start <= end)
        {
            // 循环外预取各档标准（委托内纯内存查表，禁止逐段 sync-over-async 死锁 UI 线程）
            var standardMap = await LoadStandardMapAsync();
            // 按手动起止月重算分段（逐月按年龄判断档位）
            var manualSegments = BuildSegmentsFromRange(start, end, identityFlag, standardMap);
            PaybackMonths = manualSegments.Sum(s => s.Months);
            PaybackAmount = manualSegments.Sum(s => s.SegmentAmount);
            Segments.Clear();
            foreach (var segment in manualSegments)
            {
                Segments.Add(segment);
            }
        }
    }

    /// <summary>
    /// 预取各年龄档月标准（一次性 await，供分段计算内存查表）
    /// </summary>
    private async Task<IReadOnlyDictionary<string, decimal>> LoadStandardMapAsync()
    {
        var map = new Dictionary<string, decimal>();
        foreach (var cat in new[]
                 {
                     ElderlyBenefitConstants.CatOtherElderly,
                     ElderlyBenefitConstants.CatLowSubsidy,
                     ElderlyBenefitConstants.Cat90To99,
                     ElderlyBenefitConstants.Cat100Plus
                 })
        {
            var r = await _applicationService.GetMonthlyAmountAsync(cat, CancellationToken);
            map[cat] = r.IsSuccess ? r.Value : 0m;
        }
        return map;
    }

    /// <summary>
    /// 按指定起止月逐月分段计算（特殊情况手动范围用）
    /// </summary>
    private List<ElderlyPaybackSegment> BuildSegmentsFromRange(DateTime start, DateTime end, string identityFlag, IReadOnlyDictionary<string, decimal> standardMap)
    {
        var segments = new List<ElderlyPaybackSegment>();
        ElderlyPaybackSegment? current = null;
        var cursor = start;
        var birth = BirthDate!.Value;

        while (cursor <= end)
        {
            var age = ElderlyPaybackCalculator.GetAgeAtMonth(birth, cursor);
            var cat = ElderlyPaybackCalculator.GetCategoryForAge(age, identityFlag);
            var amount = standardMap.GetValueOrDefault(cat);

            if (current == null || current.CategoryCode != cat || current.MonthlyAmount != amount)
            {
                current = new ElderlyPaybackSegment
                {
                    CategoryCode = cat,
                    MonthlyAmount = amount,
                    SegmentStartMonth = cursor.ToString("yyyy-MM")
                };
                segments.Add(current);
            }

            current.SegmentEndMonth = cursor.ToString("yyyy-MM");
            current.Months++;
            current.SegmentAmount = current.MonthlyAmount * current.Months;
            cursor = cursor.AddMonths(1);
        }

        return segments;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        _logger.LogBusiness("[保存草稿] 命令触发",
            ("IsBusy", IsBusy), ("OperationMode", OperationMode.ToString()), ("HasEvaluate", HasEvaluateResult),
            ("HukouCity", SelectedHukouCity), ("HukouVillage", SelectedHukouVillage));
        var validation = Validate();
        _logger.LogBusiness("[保存草稿] Validate", ("Result", validation ?? "OK"));
        if (validation != null)
        {
            await _dialogService.DisplayAlertAsync("提示", validation, "确定");
            return;
        }

        // 保存前若未评估或已改身份证，先评估一次
        if (!HasEvaluateResult)
        {
            await EvaluateAsync();
            if (!HasEvaluateResult)
            {
                await _dialogService.DisplayAlertAsync("提示", EvaluateMessage, "确定");
                return;
            }
        }

        await ExecuteAsync(async () =>
        {
            // 特殊情况：使用手动起止月
            if (IsSpecialCase && !string.IsNullOrEmpty(PaybackStartMonth) && !string.IsNullOrEmpty(PaybackEndMonth))
            {
                await RecalculateFromMonthsAsync();
            }

            var app = BuildApplication();
            await ResolveRegionIdsAsync(app);
            var segmentList = Segments.ToList();

            if (OperationMode == FormOperationMode.Create)
            {
                var result = await _applicationService.CreateAsync(app, segmentList, CancellationToken);
                if (!result.IsSuccess)
                {
                    _logger.Error($"普惠高龄登记保存失败: {result.Message}");
                    await _dialogService.DisplayAlertAsync("保存失败", result.Message ?? "保存失败，请重试", "确定");
                    return result;
                }
                _id = result.Value;
                _logger.LogBusiness("普惠高龄登记保存成功", ("ApplicationId", _id));
                await _dialogService.ShowSnackBarAsync("保存成功");
                await CloseAsync();
                return Result.Success();
            }
            else if (OperationMode == FormOperationMode.Edit)
            {
                var result = await _applicationService.UpdateAsync(app, segmentList, CancellationToken);
                if (!result.IsSuccess)
                {
                    _logger.Error($"普惠高龄登记更新失败: {result.Message}");
                    await _dialogService.DisplayAlertAsync("保存失败", result.Message ?? "保存失败，请重试", "确定");
                    return result;
                }
                _logger.LogBusiness("普惠高龄登记更新成功", ("ApplicationId", _id));
                await _dialogService.ShowSnackBarAsync("保存成功");

                // 数据补全并写入数据库后：删除对应的导入库记录（防止重复建档/重复导入）
                await DeleteImportedHistoryIfNeededAsync();

                // 从导入库补全后自动跳转停发页（跳过档案输出）
                if (NavigateToStopAfterSave && PendingStopApplicationId > 0)
                {
                    var loadResult = await _applicationService.GetByIdAsync(PendingStopApplicationId, CancellationToken);
                    if (loadResult.IsSuccess && loadResult.Value != null)
                    {
                        await NavigateToPageAsync<Pages.ElderlyBenefits.ElderlyStopPage, Models.Entities.ElderlyApplication>(loadResult.Value);
                        return Result.Success();
                    }
                }

                await CloseAsync();
                return Result.Success();
            }

            return Result.Success();
        }, "保存登记...");
    }

    [RelayCommand]
    private async Task SaveAndConfirmAsync()
    {
        _logger.LogBusiness("[保存并确认] 命令触发",
            ("IsBusy", IsBusy), ("IsSavable", IsSavable), ("OperationMode", OperationMode.ToString()), ("HasEvaluate", HasEvaluateResult),
            ("HukouCity", SelectedHukouCity), ("HukouVillage", SelectedHukouVillage));
        if (!IsSavable)
        {
            _logger.LogBusiness("[保存并确认] 非可保存模式，直接返回");
            return;
        }

        var validation = Validate();
        _logger.LogBusiness("[保存并确认] Validate", ("Result", validation ?? "OK"));
        if (validation != null)
        {
            await _dialogService.DisplayAlertAsync("提示", validation, "确定");
            return;
        }

        if (!HasEvaluateResult)
        {
            await EvaluateAsync();
            if (!HasEvaluateResult)
            {
                await _dialogService.DisplayAlertAsync("提示", EvaluateMessage, "确定");
                return;
            }
        }

        await ExecuteAsync(async () =>
        {
            if (IsSpecialCase && !string.IsNullOrEmpty(PaybackStartMonth) && !string.IsNullOrEmpty(PaybackEndMonth))
            {
                await RecalculateFromMonthsAsync();
            }

            var app = BuildApplication();
            await ResolveRegionIdsAsync(app);
            var segmentList = Segments.ToList();

            if (OperationMode == FormOperationMode.Create)
            {
                var result = await _applicationService.CreateAsync(app, segmentList, CancellationToken);
                if (!result.IsSuccess)
                {
                    _logger.Error($"普惠高龄登记保存失败: {result.Message}");
                    await _dialogService.DisplayAlertAsync("保存失败", result.Message ?? "保存失败，请重试", "确定");
                    return result;
                }
                _id = result.Value;
            }
            else
            {
                var updateResult = await _applicationService.UpdateAsync(app, segmentList, CancellationToken);
                if (!updateResult.IsSuccess)
                {
                    _logger.Error($"普惠高龄登记更新失败: {updateResult.Message}");
                    await _dialogService.DisplayAlertAsync("保存失败", updateResult.Message ?? "保存失败，请重试", "确定");
                    return updateResult;
                }
            }

            var confirmResult = await _applicationService.ConfirmAsync(_id, App.CurrentUserName, CancellationToken);
            if (!confirmResult.IsSuccess)
            {
                _logger.Error($"普惠高龄登记确认失败: {confirmResult.Message}");
                await _dialogService.DisplayAlertAsync("确认失败", confirmResult.Message ?? "确认失败，请重试", "确定");
                return confirmResult;
            }

            _logger.LogBusiness("普惠高龄登记保存并确认成功", ("ApplicationId", _id));
            await _dialogService.ShowSnackBarAsync("保存并确认成功");

            // 数据补全并写入数据库后：删除对应的导入库记录（防止重复建档/重复导入）
            await DeleteImportedHistoryIfNeededAsync();

            // 从导入库补全后自动跳转停发页（跳过档案输出）
            if (NavigateToStopAfterSave && PendingStopApplicationId > 0)
            {
                var loadResult = await _applicationService.GetByIdAsync(PendingStopApplicationId, CancellationToken);
                if (loadResult.IsSuccess && loadResult.Value != null)
                {
                    await NavigateToPageAsync<Pages.ElderlyBenefits.ElderlyStopPage, Models.Entities.ElderlyApplication>(loadResult.Value);
                    return Result.Success();
                }
            }

            // 正常流程：跳转档案输出页
            var savedResult = await _applicationService.GetByIdAsync(_id, CancellationToken);
            if (savedResult.IsSuccess && savedResult.Value != null)
            {
                var fields = ElderlyPrintDataBuilder.BuildSingleFields(savedResult.Value);
                PrintNavigationData.BusinessType = "ElderlyBenefits";
                PrintNavigationData.BusinessId = _id;
                PrintNavigationData.Classification = "New";
                PrintNavigationData.FieldData = fields;
                PrintNavigationData.TableData = new();
                PrintNavigationData.SupporterTableData = null;

                var page = _serviceProvider.GetRequiredService<Pages.ArchiveManagement.ArchiveOutputPage>();
                _serviceProvider.GetRequiredService<IWindowTitleService>()?.Register(page);
                await Helpers.WindowNavigator.CurrentPage!.Navigation.PushAsync(page);
            }
            else
            {
                await CloseAsync();
            }
            return Result.Success();
        }, "保存并确认...");
    }

    [RelayCommand]
    private async Task CancelAsync()
    {
        await CloseAsync();
    }

    /// <summary>
    /// 解析选中的地区名称到 id，并拼接地址文本（户籍 + 家庭）
    /// </summary>
    private async Task ResolveRegionIdsAsync(Models.Entities.ElderlyApplication app)
    {
        // 户籍
        if (!string.IsNullOrEmpty(SelectedHukouCity))
        {
            var cities = await _regionService.GetCitiesAsync(CancellationToken);
            var city = cities.IsSuccess ? cities.Value?.FirstOrDefault(c => c.CityName == SelectedHukouCity) : null;
            if (city != null)
            {
                app.HukouCityId = city.Id;
                var counties = await _regionService.GetCountiesByCityAsync(SelectedHukouCity, CancellationToken);
                var county = counties.IsSuccess && !string.IsNullOrEmpty(SelectedHukouCounty)
                    ? counties.Value?.FirstOrDefault(c => c.CountyName == SelectedHukouCounty) : null;
                if (county != null)
                {
                    app.HukouCountyId = county.Id;
                    var towns = await _regionService.GetTownsByCountyIdAsync(county.Id, CancellationToken);
                    var town = towns.IsSuccess && !string.IsNullOrEmpty(SelectedHukouTown)
                        ? towns.Value?.FirstOrDefault(t => t.TownName == SelectedHukouTown) : null;
                    if (town != null)
                    {
                        app.HukouTownId = town.Id;
                        var villages = await _regionService.GetVillagesByTownIdAsync(town.Id, CancellationToken);
                        var village = villages.IsSuccess && !string.IsNullOrEmpty(SelectedHukouVillage)
                            ? villages.Value?.FirstOrDefault(v => v.VillageName == SelectedHukouVillage) : null;
                        if (village != null) app.HukouVillageId = village.Id;
                    }
                }
            }
            app.HukouAddress = $"{SelectedHukouCity}{SelectedHukouCounty}{SelectedHukouTown}{SelectedHukouVillage}".Trim();
            app.HukouVillage = SelectedHukouVillage;
        }

        // 家庭
        if (!string.IsNullOrEmpty(SelectedFamilyCity))
        {
            var cities = await _regionService.GetCitiesAsync(CancellationToken);
            var city = cities.IsSuccess ? cities.Value?.FirstOrDefault(c => c.CityName == SelectedFamilyCity) : null;
            if (city != null)
            {
                app.FamilyCityId = city.Id;
                var counties = await _regionService.GetCountiesByCityAsync(SelectedFamilyCity, CancellationToken);
                var county = counties.IsSuccess && !string.IsNullOrEmpty(SelectedFamilyCounty)
                    ? counties.Value?.FirstOrDefault(c => c.CountyName == SelectedFamilyCounty) : null;
                if (county != null)
                {
                    app.FamilyCountyId = county.Id;
                    var towns = await _regionService.GetTownsByCountyIdAsync(county.Id, CancellationToken);
                    var town = towns.IsSuccess && !string.IsNullOrEmpty(SelectedFamilyTown)
                        ? towns.Value?.FirstOrDefault(t => t.TownName == SelectedFamilyTown) : null;
                    if (town != null)
                    {
                        app.FamilyTownId = town.Id;
                        var villages = await _regionService.GetVillagesByTownIdAsync(town.Id, CancellationToken);
                        var village = villages.IsSuccess && !string.IsNullOrEmpty(SelectedFamilyVillage)
                            ? villages.Value?.FirstOrDefault(v => v.VillageName == SelectedFamilyVillage) : null;
                        if (village != null) app.FamilyVillageId = village.Id;
                    }
                }
            }
            app.FamilyAddress = $"{SelectedFamilyCity}{SelectedFamilyCounty}{SelectedFamilyTown}{SelectedFamilyVillage}{DetailAddress}".Trim();
        }
    }

    private ElderlyApplication BuildApplication()
    {
        return new ElderlyApplication
        {
            Id = _id,
            Name = Name?.Trim() ?? string.Empty,
            IdCard = IdCard?.Trim() ?? string.Empty,
            Gender = Gender ?? string.Empty,
            BirthDate = BirthDate,
            Phone = Phone?.Trim() ?? string.Empty,
            HukouAddress = HukouAddress?.Trim() ?? string.Empty,
            HukouVillage = HukouVillage?.Trim() ?? string.Empty,
            HukouDetailAddress = HukouDetailAddress?.Trim() ?? string.Empty,
            FamilyAddress = FamilyAddress?.Trim() ?? string.Empty,
            DetailAddress = DetailAddress?.Trim() ?? string.Empty,
            BankName = BankName?.Trim() ?? string.Empty,
            BankAccount = BankAccount?.Trim() ?? string.Empty,
            AgentName = AgentName?.Trim() ?? string.Empty,
            AgentRelation = AgentRelation?.Trim() ?? string.Empty,
            AgentReceiveName = AgentReceiveName?.Trim() ?? string.Empty,
            AgentReceiveRelation = AgentReceiveRelation?.Trim() ?? string.Empty,
            AgentReceiveBankName = AgentReceiveBankName?.Trim() ?? string.Empty,
            AgentReceiveBankAccount = AgentReceiveBankAccount?.Trim() ?? string.Empty,
            AgentReceiveReason = AgentReceiveReason?.Trim() ?? string.Empty,
            Category = Category ?? string.Empty,
            IdentityFlag = IdentityFlag ?? string.Empty,
            IdentitySource = IdentitySource ?? string.Empty,
            IsCategoryManual = false,
            IssueStartMonth = IssueStartMonth ?? string.Empty,
            IssueAmount = IssueAmount,
            PaybackStartMonth = PaybackStartMonth ?? string.Empty,
            PaybackEndMonth = PaybackEndMonth ?? string.Empty,
            AutoStartMonth = AutoStartMonth ?? string.Empty,
            AutoEndMonth = AutoEndMonth ?? string.Empty,
            PaybackMonths = PaybackMonths,
            PaybackAmount = PaybackAmount,
            PaybackReason = PaybackReason?.Trim() ?? string.Empty,
            IsSpecialCase = IsSpecialCase,
            SpecialReason = SpecialReason?.Trim() ?? string.Empty,
            ApplyDate = ApplyDate,
            CreatedBy = App.CurrentUserName,
            UpdatedBy = App.CurrentUserName
        };
    }

    private string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Name)) return "请填写申请人姓名";
        if (string.IsNullOrWhiteSpace(IdCard)) return "请填写身份证号码";
        if (Helpers.IdCardValidator.IsValid(IdCard.Trim()) == false) return "身份证号码无效";
        if (string.IsNullOrWhiteSpace(Phone)) return "请填写联系电话";
        if (string.IsNullOrWhiteSpace(SelectedHukouCity)) return "请选择户籍城市";
        if (string.IsNullOrWhiteSpace(SelectedHukouCounty)) return "请选择户籍区县";
        if (string.IsNullOrWhiteSpace(SelectedHukouTown)) return "请选择户籍乡镇";
        if (string.IsNullOrWhiteSpace(SelectedHukouVillage)) return "请选择户籍村/社区";
        if (string.IsNullOrWhiteSpace(BankName)) return "请填写社保卡开户行";
        if (string.IsNullOrWhiteSpace(BankAccount)) return "请填写社保卡账号";
        if (!HasEvaluateResult) return "请先通过身份证评估享受类别与补发金额";

        if (IsSpecialCase && string.IsNullOrWhiteSpace(SpecialReason))
            return "请填写特殊情况原因说明";

        return null;
    }

    private async Task<decimal> GetCategoryMonthlyAmountAsync(string category, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(category)) return 0m;
        var result = await _applicationService.GetMonthlyAmountAsync(category, ct);
        return result.IsSuccess ? result.Value : 0m;
    }

    private static bool TryParseMonth(string month, out DateTime value)
    {
        if (DateTime.TryParseExact(month, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var parsed))
        {
            value = new DateTime(parsed.Year, parsed.Month, 1);
            return true;
        }
        value = default;
        return false;
    }

    private async Task CloseAsync()
    {
        if (Helpers.WindowNavigator.CurrentPage?.Navigation != null)
        {
            await Helpers.WindowNavigator.CurrentPage.Navigation.PopAsync();
            RestoreWindowTitleFromNavigation();
        }
    }

    /// <summary>
    /// 数据补全保存成功后：若本登记源自高龄导入库（nc_biz_elderly_subsidy_history），
    /// 删除对应导入库记录，避免重复建档/重复导入。删除失败仅记录日志，不阻断保存流程。
    /// </summary>
    private async Task DeleteImportedHistoryIfNeededAsync()
    {
        if (_importedHistoryId is not > 0) return;

        var result = await _applicationService.DeleteHistoryAsync(_importedHistoryId.Value, CancellationToken.None);
        if (result.IsSuccess)
        {
            _logger.LogBusiness("数据补全后删除高龄导入库记录", ("HistoryId", _importedHistoryId.Value));
        }
        else
        {
            _logger.Warn($"数据补全后删除高龄导入库记录失败: {result.ErrorCode} {result.Message}");
        }
        _importedHistoryId = null;
    }
}

/// <summary>
/// 享受类别展示项（只读判定结果：命中 ☑ 高亮，未命中 □）
/// </summary>
public sealed record CategoryDisplayItem(string Label, bool Matched)
{
    /// <summary>勾选标记：命中 ☑，未命中 □</summary>
    public string Mark => Matched ? "☑" : "□";
}
