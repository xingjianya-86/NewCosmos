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

public partial class ElderlyApplicationFormViewModel
{
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

    /// <summary>是否显示模式横幅（补全模式：停发联动 / 复核前置补全）</summary>
    public bool IsModeBannerVisible => NavigateToStopAfterSave || ReturnToReview;

    /// <summary>模式横幅文案</summary>
    public string ModeBannerText => NavigateToStopAfterSave
        ? "数据补全模式\n补全建档缺失信息，保存后进入停发登记"
        : ReturnToReview
            ? "数据补全模式\n补全完成缺失信息，保存后返回复核页继续办理"
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

    // ── 2026-10-27 五级化 + 管辖范围限制 ──
    // 户籍组（受限）：账号绑定级别及以上锁定置灰、只留本单位一项；以下级开放。
    // 家庭住址组（居住地）：全国可选、不受范围限制（默认取本单位）。
    // 省不落库：省由市隐含（city_code 前2位），仅作级联入口，DB 仍是市/县/镇/村 ID 四列。

    [ObservableProperty]
    private ObservableCollection<string> _hukouProvinceOptions = new();

    [ObservableProperty]
    private string _selectedHukouProvince = string.Empty;

    [ObservableProperty]
    private ObservableCollection<string> _familyProvinceOptions = new();

    [ObservableProperty]
    private string _selectedFamilyProvince = string.Empty;

    /// <summary>省名 → 省ID（按省筛市；省表缺失时为空 → 市回退全量）</summary>
    private readonly Dictionary<string, int> _provinceIdMap = new();

    /// <summary>户籍级锁定（账号绑定级别及以上 → Picker 置灰禁点）</summary>
    public bool IsHukouProvinceLocked => _addressScope?.IsProvinceLocked == true;
    public bool IsHukouCityLocked => _addressScope?.IsCityLocked == true;
    public bool IsHukouCountyLocked => _addressScope?.IsCountyLocked == true;
    public bool IsHukouTownLocked => _addressScope?.IsTownLocked == true;
    public bool IsHukouVillageLocked => _addressScope?.IsVillageLocked == true;

    /// <summary>地址区是否可用（false=未绑区划 → 整个地址区禁用）</summary>
    public bool IsAddressScopeAvailable => _addressScope?.IsAvailable != false;

    /// <summary>户籍范围提示文案</summary>
    public string AddressScopeDescription => _addressScope?.ScopeDescription ?? string.Empty;

    /// <summary>范围解析后刷新锁定态绑定</summary>
    private void NotifyAddressScopeChanged()
    {
        OnPropertyChanged(nameof(IsHukouProvinceLocked));
        OnPropertyChanged(nameof(IsHukouCityLocked));
        OnPropertyChanged(nameof(IsHukouCountyLocked));
        OnPropertyChanged(nameof(IsHukouTownLocked));
        OnPropertyChanged(nameof(IsHukouVillageLocked));
        OnPropertyChanged(nameof(IsAddressScopeAvailable));
        OnPropertyChanged(nameof(AddressScopeDescription));
    }

    /// <summary>户籍市级：锁定时只留本单位市</summary>
    private bool AllowHukouCity(string cityName) =>
        _addressScope is not { IsCityLocked: true }
        || string.Equals(cityName, _addressScope.CityName, StringComparison.Ordinal);

    private bool AllowHukouCounty(string countyName) =>
        _addressScope is not { IsCountyLocked: true }
        || string.Equals(countyName, _addressScope.CountyName, StringComparison.Ordinal);

    private bool AllowHukouTown(string townName) =>
        _addressScope is not { IsTownLocked: true }
        || string.Equals(townName, _addressScope.TownName, StringComparison.Ordinal);

    private bool AllowHukouVillage(string villageName) =>
        _addressScope is not { IsVillageLocked: true }
        || string.Equals(villageName, _addressScope.VillageName, StringComparison.Ordinal);

    /// <summary>解析管辖范围 + 加载省列表（户籍/家庭住址两组共用一次；编辑与新建入口各调一次）</summary>
    private async Task EnsureAddressScopeAsync()
    {
        if (_addressScope == null)
        {
            var scopeResult = await _addressScopeService.ResolveAsync(CancellationToken);
            _addressScope = scopeResult.IsSuccess ? scopeResult.Value : null;
            if (_addressScope == null)
                _logger.Warn($"地址管辖范围解析失败（地址区将被禁用）: {scopeResult.Message}");
            NotifyAddressScopeChanged();
        }

        if (HukouProvinceOptions.Count == 0) await LoadHukouProvincesAsync();
    }

    /// <summary>加载 34 省（户籍+家庭住址两组同一份数据；省表缺失时回退单值占位）</summary>
    private async Task LoadHukouProvincesAsync()
    {
        var result = await _regionService.GetProvincesAsync(CancellationToken);

        HukouProvinceOptions.Clear();
        FamilyProvinceOptions.Clear();
        _provinceIdMap.Clear();

        if (result.IsSuccess && result.Value is { Count: > 0 })
        {
            foreach (var p in result.Value)
            {
                HukouProvinceOptions.Add(p.ProvinceName);
                FamilyProvinceOptions.Add(p.ProvinceName);
                _provinceIdMap[p.ProvinceName] = p.Id;
            }

            var preferred = _addressScope?.ProvinceName;
            if (string.IsNullOrWhiteSpace(preferred)) preferred = DefaultValuesConstants.HOME_PROVINCE;
            if (!string.IsNullOrEmpty(preferred) && HukouProvinceOptions.Contains(preferred))
            {
                SelectedHukouProvince = preferred;
                SelectedFamilyProvince = preferred;
            }
        }
        else
        {
            if (result.IsFailure) _logger.Warn($"省级选项加载失败（回退单值）: {result.Message}");
            HukouProvinceOptions.Add(DefaultValuesConstants.HOME_PROVINCE);
            FamilyProvinceOptions.Add(DefaultValuesConstants.HOME_PROVINCE);
        }
    }

    partial void OnSelectedHukouProvinceChanged(string value)
    {
        if (_suppressRegionCascade) return;
        if (value is null) return;
        SafeFireAndForget(async () => await LoadHukouCitiesAsync());
    }

    partial void OnSelectedFamilyProvinceChanged(string value)
    {
        if (_suppressRegionCascade) return;
        if (value is null) return;
        SafeFireAndForget(async () => await LoadFamilyCitiesAsync());
    }

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
        // 2026-10-27：按选中省筛市（省已可选全国）+ 户籍按管辖范围过滤
        var provinceId = 0;
        if (!string.IsNullOrEmpty(SelectedHukouProvince))
            _provinceIdMap.TryGetValue(SelectedHukouProvince, out provinceId);

        var result = provinceId > 0
            ? await _regionService.GetCitiesByProvinceIdAsync(provinceId, CancellationToken)
            : await _regionService.GetCitiesAsync(CancellationToken);
        if (result.IsFailure || result.Value == null) return;
        HukouCityOptions.Clear();
        foreach (var c in result.Value)
        {
            if (AllowHukouCity(c.CityName)) HukouCityOptions.Add(c.CityName);
        }
        if (string.IsNullOrEmpty(SelectedHukouCity) && HukouCityOptions.Count > 0)
        {
            SelectedHukouCity = HukouCityOptions[0];
        }
        // 户籍锁定时（账号绑到市级及以下）预选本单位市
        if (_addressScope is { IsCityLocked: true } && !string.IsNullOrEmpty(_addressScope.CityName)
            && HukouCityOptions.Contains(_addressScope.CityName))
        {
            SelectedHukouCity = _addressScope.CityName;
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
        foreach (var c in result.Value)
        {
            if (AllowHukouCounty(c.CountyName)) HukouCountyOptions.Add(c.CountyName);   // 管辖范围内
        }
        if (HukouCountyOptions.Count > 0) SelectedHukouCounty = HukouCountyOptions[0];
        if (_addressScope is { IsCountyLocked: true } && !string.IsNullOrEmpty(_addressScope.CountyName)
            && HukouCountyOptions.Contains(_addressScope.CountyName))
        {
            SelectedHukouCounty = _addressScope.CountyName;
        }
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
        foreach (var t in towns.Value)
        {
            if (AllowHukouTown(t.TownName)) HukouTownOptions.Add(t.TownName);   // 管辖范围内
        }
        if (HukouTownOptions.Count > 0) SelectedHukouTown = HukouTownOptions[0];
        if (_addressScope is { IsTownLocked: true } && !string.IsNullOrEmpty(_addressScope.TownName)
            && HukouTownOptions.Contains(_addressScope.TownName))
        {
            SelectedHukouTown = _addressScope.TownName;
        }
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
        foreach (var v in villages.Value)
        {
            if (AllowHukouVillage(v.VillageName)) HukouVillageOptions.Add(v.VillageName);   // 管辖范围内
        }
        if (HukouVillageOptions.Count > 0) SelectedHukouVillage = HukouVillageOptions[0];
        // 账号绑到村 → 村下拉只剩本村一项（=「其他全部禁用」）
        if (_addressScope is { IsVillageLocked: true } && !string.IsNullOrEmpty(_addressScope.VillageName)
            && HukouVillageOptions.Contains(_addressScope.VillageName))
        {
            SelectedHukouVillage = _addressScope.VillageName;
        }
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
        // 家庭住址=居住地：**全国可选、不限范围**，只按选中省筛市
        var provinceId = 0;
        if (!string.IsNullOrEmpty(SelectedFamilyProvince))
            _provinceIdMap.TryGetValue(SelectedFamilyProvince, out provinceId);

        var result = provinceId > 0
            ? await _regionService.GetCitiesByProvinceIdAsync(provinceId, CancellationToken)
            : await _regionService.GetCitiesAsync(CancellationToken);
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
    /// 特殊情况开关变化时：开启则可用手动起止月（进入手动模式）并立即同步补发；
    /// 关闭则恢复系统自动计算值
    /// </summary>
    partial void OnIsSpecialCaseChanged(bool value)
    {
        if (!value)
        {
            // 关闭特殊情况：恢复系统自动计算值（起止月赋值触发的联动因开关已关闭而跳过）
            PaybackStartMonth = AutoStartMonth;
            PaybackEndMonth = AutoEndMonth;
            SafeFireAndForget(() => RecalculateFromMonthsAsync());
        }
        else if (!_suppressEvaluate)
        {
            // 开启特殊情况：立即按当前起止月同步（起止月都为空则补发归零，不留评估旧值）
            ScheduleManualPaybackSync();
        }
    }

    /// <summary>
    /// 特殊情况下手动改动补发起算月/止算月：立即同步补发月数、金额与分段（输入即刷新，避免残留评估旧值）
    /// </summary>
    partial void OnPaybackStartMonthChanged(string value) => ScheduleManualPaybackSync();

    partial void OnPaybackEndMonthChanged(string value) => ScheduleManualPaybackSync();

    /// <summary>
    /// 仅特殊情况且非批量回填期间才触发手动同步（编辑加载由 _suppressEvaluate 抑制，避免覆盖库中已存分段）
    /// </summary>
    private void ScheduleManualPaybackSync()
    {
        if (!IsSpecialCase || _suppressEvaluate) return;
        SafeFireAndForget(() => RecalculateFromMonthsAsync());
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
            // 特殊情况：按当前手动起止月同步补发（起止月都留空 = 无补发）
            if (IsSpecialCase)
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

                // 数据补全并写入数据库后：标记名册行已并入当前库（Active→Stopped，保留历史发放金额）
                await MarkImportedHistoryMigratedIfNeededAsync();

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
            if (IsSpecialCase)
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

            // 数据补全并写入数据库后：标记名册行已并入当前库（Active→Stopped，保留历史发放金额）
            await MarkImportedHistoryMigratedIfNeededAsync();

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

            // 复核前置补全：保存+确认后返回复核页继续办理（复核页 OnAppearing 自动重评，不跳档案输出页）
            if (ReturnToReview)
            {
                await CloseAsync();
                return Result.Success();
            }

            // 正常流程：跳转档案输出页
            var savedResult = await _applicationService.GetByIdAsync(_id, CancellationToken);
            if (savedResult.IsSuccess && savedResult.Value != null)
            {
                var fields = ElderlyPrintDataBuilder.BuildSingleFields(savedResult.Value);
                // 整档入口：先清文书模式上下文，防上一次「仅出文书」的静态残留被继承
                PrintNavigationData.ClearDocumentMode();
                PrintNavigationData.BusinessType = "ElderlyBenefits";
                PrintNavigationData.BusinessId = _id;
                PrintNavigationData.Classification = "New";
                PrintNavigationData.FieldData = fields;
                PrintNavigationData.TableData = new();
                PrintNavigationData.SupporterTableData = null;

                // 按 Mobile{桌面页名} 约定解析：Android 走手机端「推送打印工作台」，桌面走原档案输出页
                var page = ResolvePage<Pages.ArchiveManagement.ArchiveOutputPage>();
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

}
