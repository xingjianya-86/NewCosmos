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

}
