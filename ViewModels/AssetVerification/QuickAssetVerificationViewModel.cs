using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Media;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Domain.AssetVerification;
using NewCosmos.Models.NavigationData;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.Platform;
using NewCosmos.Services.System;
using NewCosmos.ViewModels.Base;
using NewCosmos.ViewModels.ArchiveManagement;
using NewCosmos.Helpers;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.AssetVerification;

public partial class QuickAssetVerificationViewModel : ViewModelBase
{
    private readonly IAssetVerificationService _verificationService;
    private readonly IUserService _userService;
    private readonly IOrganizationService _organizationService;
    private readonly IRegionService _regionService;
    private readonly IDictionaryService _dictionaryService;
    private readonly IDictCacheService _dictCacheService;
    private readonly IDialogService _dialogService;
    private readonly ILoggerService _logger;
    private readonly ITemplateService _templateService;
    private readonly IServiceProvider _serviceProvider;
    private readonly IIdentityReader _identityReader;

    private string _batchId = string.Empty;
    private int? _operatorTownId;
    private bool _isInitialized;
    private Dictionary<string, string> _idTypeCodeMap = new();
    private Dictionary<string, string> _relationshipCodeMap = new();
    private Dictionary<string, string> _applicationReasonCodeMap = new();
    private Dictionary<string, string> _villageCodeMap = new();

    #region 集合属性
    public ObservableCollection<AssetCheckItemDto> Applicants { get; } = new();
    public ObservableCollection<string> Relationships { get; } = new();
    public ObservableCollection<string> NonHeadRelationships { get; } = new();
    public ObservableCollection<string> IdTypes { get; } = new();
    public ObservableCollection<string> Villages { get; } = new();
    public ObservableCollection<string> ApplicationReasons { get; } = new();

    #endregion

    #region 申请信息

    [ObservableProperty]
    private DateTime _applicationDate = DateTime.Today;

    [ObservableProperty]
    private string _selectedVillage = string.Empty;

    [ObservableProperty]
    private string _familyAddressDetail = string.Empty;

    [ObservableProperty]
    private string _selectedApplicationReason = string.Empty;

    [ObservableProperty]
    private string _contactPhone = string.Empty;

    [ObservableProperty]
    private string _operatorTownName = string.Empty;

    [ObservableProperty]
    private bool _isTownConfigured;

    public string FamilyAddress => $"{OperatorTownName}{SelectedVillage}{FamilyAddressDetail}";

    #endregion

    #region 代理人信息
    [ObservableProperty]
    private bool _isAgent;

    [ObservableProperty]
    private string _agentName = string.Empty;

    [ObservableProperty]
    private string _agentIdCard = string.Empty;

    [ObservableProperty]
    private string _agentRelationship = string.Empty;

    [ObservableProperty]
    private string _agentIdType = IdTypeConstants.DefaultIdType;

    public bool IsAgentVisible => IsAgent;

    #endregion

    #region 状态属性
    [ObservableProperty]
    private bool _isSubmitting;

    #endregion

    public QuickAssetVerificationViewModel(
        IAssetVerificationService verificationService,
        IUserService userService,
        IOrganizationService organizationService,
        IRegionService regionService,
        IDictionaryService dictionaryService,
        IDictCacheService dictCacheService,
        IDialogService dialogService,
        ILoggerService logger,
        ITemplateService templateService,
        IServiceProvider serviceProvider,
        IIdentityReader identityReader)
    {
        _verificationService = verificationService;
        _userService = userService;
        _organizationService = organizationService;
        _regionService = regionService;
        _dictionaryService = dictionaryService;
        _dictCacheService = dictCacheService;
        _dialogService = dialogService;
        _logger = logger;
        _templateService = templateService;
        _serviceProvider = serviceProvider;
        _identityReader = identityReader;

        Title = "快速资产核查";
    }

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    #region 初始化
    public async Task InitializeAsync()
    {
        if (_isInitialized) return;
        _isInitialized = true;

        try
        {
            _logger.LogBusiness("打开快速核查页面");

            InitializeApplicants();

            await LoadOperatorInfoAsync();
            await LoadRelationshipsAsync();
            await LoadApplicationReasonsAsync();
            await LoadIdTypesAsync();

            RefreshApplicantDefaults();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "快速核查页面初始化失败");
        }
    }

    private void InitializeApplicants()
    {
        Applicants.Clear();
        _batchId = Guid.NewGuid().ToString("N");

        Applicants.Add(new AssetCheckItemDto
        {
            Relationship = PickerConstants.Relationship.HeadDisplay,
            ApplicantIdType = IdTypeConstants.DefaultIdType,
            IsHead = true,
            BatchId = _batchId
        });
    }

    private void RefreshApplicantDefaults()
    {
        foreach (var applicant in Applicants)
        {
            if (string.IsNullOrWhiteSpace(applicant.ApplicantIdType))
                applicant.ApplicantIdType = IdTypes.FirstOrDefault() ?? IdTypeConstants.DefaultIdType;

            if (string.IsNullOrWhiteSpace(applicant.Relationship))
            {
                applicant.Relationship = applicant.IsHead
                    ? PickerConstants.Relationship.HeadDisplay
                    : PickerConstants.Relationship.SpouseDisplay;
            }
        }
    }

    private async Task LoadOperatorInfoAsync()
    {
        var userId = App.CurrentUserId;
        if (!userId.HasValue)
        {
            IsTownConfigured = false;
            return;
        }

        try
        {
            var userResult = await _userService.GetByIdAsync(userId.Value);
            if (userResult.IsFailure || userResult.Value == null)
            {
                IsTownConfigured = false;
                return;
            }

            var user = userResult.Value;
            if (!user.OrganizationId.HasValue)
            {
                IsTownConfigured = false;
                return;
            }

            var orgResult = await _organizationService.GetByIdAsync(user.OrganizationId.Value);
            if (orgResult.IsFailure || orgResult.Value == null || !orgResult.Value.TownId.HasValue)
            {
                IsTownConfigured = false;
                return;
            }

            _operatorTownId = orgResult.Value.TownId.Value;

            var townResult = await _regionService.GetTownByIdAsync(_operatorTownId.Value);
            if (townResult.IsSuccess && townResult.Value != null)
            {
                OperatorTownName = townResult.Value.TownName;
                await LoadVillagesAsync(_operatorTownId.Value);
                IsTownConfigured = true;
            }
            else
            {
                IsTownConfigured = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "失败");
            IsTownConfigured = false;
        }
    }

    private async Task LoadVillagesAsync(int townId)
    {
        try
        {
            Villages.Clear();
            _villageCodeMap.Clear();

            var result = await _regionService.GetVillagesByTownIdAsync(townId);
            if (result.IsSuccess && result.Value != null)
            {
                foreach (var village in result.Value.Where(v => v.IsActive).OrderBy(v => v.SortOrder))
                {
                    Villages.Add(village.VillageName);
                    _villageCodeMap[village.VillageName] = village.VillageCode ?? village.VillageName;
                }
            }

            SelectedVillage = Villages.FirstOrDefault() ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "失败");
        }
    }

    private async Task LoadRelationshipsAsync()
    {
        Relationships.Clear();
        NonHeadRelationships.Clear();
        _relationshipCodeMap.Clear();

        var cached = _dictCacheService.GetAllByCategory(DictionaryTypeCodes.FamilyRelationships);
        if (cached.Count > 0)
        {
            foreach (var kvp in cached)
            {
                Relationships.Add(kvp.Value);
                _relationshipCodeMap[kvp.Value] = kvp.Key;
            }

            foreach (var r in Relationships.Where(r => r != PickerConstants.Relationship.HeadDisplay))
                NonHeadRelationships.Add(r);
            return;
        }

        try
        {
            var result = await _dictionaryService.GetItemsByCategoryAsync(
                DictionaryTypeCodes.FamilyRelationships);

            if (result.IsSuccess && result.Value != null)
            {
                foreach (var item in result.Value.Where(i => i.IsActive).OrderBy(i => i.SortOrder))
                {
                    var displayValue = item.ItemValue ?? item.ItemKey;
                    Relationships.Add(displayValue);
                    _relationshipCodeMap[displayValue] = item.ItemKey;
                }
            }

            if (Relationships.Count == 0)
            {
                AddDefaultRelationships();
            }

            foreach (var r in Relationships.Where(r => r != PickerConstants.Relationship.HeadDisplay))
            {
                NonHeadRelationships.Add(r);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "失败");
            Relationships.Clear();
            NonHeadRelationships.Clear();
            _relationshipCodeMap.Clear();
            AddDefaultRelationships();
            foreach (var r in Relationships.Where(r => r != PickerConstants.Relationship.HeadDisplay))
            {
                NonHeadRelationships.Add(r);
            }
        }
    }

    private void AddDefaultRelationships()
    {
        var defaults = new (string code, string display)[]
        {
            (PickerConstants.Relationship.HeadCode, PickerConstants.Relationship.HeadDisplay),
            (PickerConstants.Relationship.SpouseCode, PickerConstants.Relationship.SpouseDisplay),
            (PickerConstants.Relationship.SonCode, PickerConstants.Relationship.SonDisplay),
            (PickerConstants.Relationship.DaughterCode, PickerConstants.Relationship.DaughterDisplay),
            (PickerConstants.Relationship.GrandchildCode, PickerConstants.Relationship.GrandchildDisplay),
            (PickerConstants.Relationship.ParentCode, PickerConstants.Relationship.ParentDisplay),
            (PickerConstants.Relationship.GrandparentCode, PickerConstants.Relationship.GrandparentDisplay),
            (PickerConstants.Relationship.SiblingCode, PickerConstants.Relationship.SiblingDisplay),
            (PickerConstants.Relationship.OtherCode, PickerConstants.Relationship.OtherDisplay)
        };
        foreach (var (code, display) in defaults)
        {
            if (!Relationships.Contains(display))
            {
                Relationships.Add(display);
                _relationshipCodeMap[display] = code;
            }
        }
    }

    private async Task LoadApplicationReasonsAsync()
    {
        ApplicationReasons.Clear();
        _applicationReasonCodeMap.Clear();

        var cached = _dictCacheService.GetAllByCategory(DictionaryTypeCodes.ApplicationReasons);
        if (cached.Count > 0)
        {
            foreach (var kvp in cached)
            {
                ApplicationReasons.Add(kvp.Value);
                _applicationReasonCodeMap[kvp.Value] = kvp.Key;
            }
            SelectedApplicationReason = ApplicationReasons.FirstOrDefault() ?? string.Empty;
            return;
        }

        try
        {
            var result = await _dictionaryService.GetItemsByCategoryAsync(
                DictionaryTypeCodes.ApplicationReasons);

            if (result.IsSuccess && result.Value != null && result.Value.Count > 0)
            {
                foreach (var item in result.Value.Where(i => i.IsActive).OrderBy(i => i.SortOrder))
                {
                    var displayValue = item.ItemValue ?? item.ItemKey;
                    ApplicationReasons.Add(displayValue);
                    _applicationReasonCodeMap[displayValue] = item.ItemKey;
                }
            }
            else
            {
                AddDefaultApplicationReasons();
            }
        }
        catch
        {
            AddDefaultApplicationReasons();
        }

        SelectedApplicationReason = ApplicationReasons.FirstOrDefault() ?? string.Empty;
    }

    private void AddDefaultApplicationReasons()
    {
        var defaults = new (string code, string display)[]
        {
            (PickerConstants.ApplicationReason.IllnessCode, PickerConstants.ApplicationReason.IllnessDisplay),
            (PickerConstants.ApplicationReason.DisasterCode, PickerConstants.ApplicationReason.DisasterDisplay),
            (PickerConstants.ApplicationReason.DisabilityCode, PickerConstants.ApplicationReason.DisabilityDisplay),
            (PickerConstants.ApplicationReason.EducationCode, PickerConstants.ApplicationReason.EducationDisplay),
            (PickerConstants.ApplicationReason.LowIncomeCode, PickerConstants.ApplicationReason.LowIncomeDisplay),
            (PickerConstants.ApplicationReason.UnemploymentCode, PickerConstants.ApplicationReason.UnemploymentDisplay),
            (PickerConstants.ApplicationReason.LandLossCode, PickerConstants.ApplicationReason.LandLossDisplay),
            (PickerConstants.ApplicationReason.AccidentCode, PickerConstants.ApplicationReason.AccidentDisplay),
            (PickerConstants.ApplicationReason.OtherCode, PickerConstants.ApplicationReason.OtherDisplay)
        };
        foreach (var (code, display) in defaults)
        {
            if (!ApplicationReasons.Contains(display))
            {
                ApplicationReasons.Add(display);
                _applicationReasonCodeMap[display] = code;
            }
        }
    }

    private async Task LoadIdTypesAsync()
    {
        var previousType = AgentIdType;

        IdTypes.Clear();
        _idTypeCodeMap.Clear();

        var cached = _dictCacheService.GetAllByCategory(DictionaryTypeCodes.IdTypes);
        if (cached.Count > 0)
        {
            foreach (var kvp in cached)
            {
                IdTypes.Add(kvp.Value);
                _idTypeCodeMap[kvp.Value] = kvp.Key;
            }
            RestoreAgentIdType(previousType);
            return;
        }

        try
        {
            var result = await _dictionaryService.GetItemsByCategoryAsync(
                DictionaryTypeCodes.IdTypes);

            if (result.IsSuccess && result.Value != null)
            {
                foreach (var item in result.Value.Where(i => i.IsActive).OrderBy(i => i.SortOrder))
                {
                    var displayValue = item.ItemValue ?? item.ItemKey;
                    IdTypes.Add(displayValue);
                    _idTypeCodeMap[displayValue] = item.ItemKey;
                }
            }

            if (IdTypes.Count == 0)
            {
                AddDefaultIdTypes();
            }

            RestoreAgentIdType(previousType);
        }
        catch
        {
            IdTypes.Clear();
            _idTypeCodeMap.Clear();
            AddDefaultIdTypes();
            RestoreAgentIdType(previousType);
        }
    }

    private void RestoreAgentIdType(string previousType)
    {
        if (!string.IsNullOrEmpty(previousType) && IdTypes.Contains(previousType))
            AgentIdType = previousType;
        else if (IdTypes.Contains(IdTypeConstants.ResidentIdCardDisplay))
            AgentIdType = IdTypeConstants.ResidentIdCardDisplay;
        else if (IdTypes.Count > 0)
            AgentIdType = IdTypes[0];
    }

    private void AddDefaultIdTypes()
    {
        var defaults = new (string code, string display)[]
        {
            (IdTypeConstants.ResidentIdCardCode, IdTypeConstants.ResidentIdCardDisplay),
            (IdTypeConstants.HouseholdRegisterCode, IdTypeConstants.HouseholdRegisterDisplay),
            (IdTypeConstants.PassportCode, IdTypeConstants.PassportDisplay),
            (IdTypeConstants.HKMacauPermitCode, IdTypeConstants.HKMacauPermitDisplay),
            (IdTypeConstants.TaiwanPermitCode, IdTypeConstants.TaiwanPermitDisplay),
            (IdTypeConstants.OtherCode, IdTypeConstants.OtherDisplay)
        };
        foreach (var (code, display) in defaults)
        {
            if (!IdTypes.Contains(display))
            {
                IdTypes.Add(display);
                _idTypeCodeMap[display] = code;
            }
        }
    }

    #endregion

    #region 属性变更回调
    partial void OnSelectedVillageChanged(string value)
    {
        OnPropertyChanged(nameof(FamilyAddress));
    }

    partial void OnFamilyAddressDetailChanged(string value)
    {
        OnPropertyChanged(nameof(FamilyAddress));
    }

    partial void OnIsAgentChanged(bool value)
    {
        foreach (var applicant in Applicants)
        {
            applicant.IsAgent = value;
        }
        OnPropertyChanged(nameof(IsAgentVisible));
    }

    partial void OnAgentNameChanged(string value)
    {
        foreach (var applicant in Applicants)
        {
            applicant.AgentName = value;
        }
    }

    partial void OnAgentIdCardChanged(string value)
    {
        foreach (var applicant in Applicants)
        {
            applicant.AgentIdCard = value;
        }
    }

    partial void OnAgentRelationshipChanged(string value)
    {
        foreach (var applicant in Applicants)
        {
            applicant.AgentRelationship = value;
        }
    }

    partial void OnAgentIdTypeChanged(string value)
    {
        foreach (var applicant in Applicants)
        {
            applicant.AgentIdType = value;
        }
    }

    #endregion

    #region Commands

    [RelayCommand]
    private void AddApplicant()
    {
        Applicants.Add(new AssetCheckItemDto
        {
            Relationship = PickerConstants.Relationship.SpouseDisplay,
            ApplicantIdType = IdTypes.FirstOrDefault() ?? IdTypeConstants.DefaultIdType,
            IsHead = false,
            BatchId = _batchId
        });
    }

    [RelayCommand]
    private void RemoveApplicant(AssetCheckItemDto item)
    {
        if (item != null && !item.IsHead)
        {
            Applicants.Remove(item);
        }
    }

    /// <summary>
    /// 扫描身份证（移动端专用）：跳转原生 Camera2 取景页（带实时对齐框）拍照 → 端侧离线 OCR
    /// → 回填户主姓名/身份证号。相机页通过 <see cref="IdCardScanParameter.OnCompleted"/> 回传结果。
    /// </summary>
    [RelayCommand]
    private async Task ScanIdCardAsync()
    {
        if (!_identityReader.IsSupported)
        {
            await _dialogService.DisplayAlertAsync("提示", "当前设备不支持身份证扫描，请在手机端使用", "确定");
            return;
        }

        try
        {
            var completion = new TaskCompletionSource<Result<IdCardInfo>?>();
            await NavigateToPageAsync<Pages.Mobile.MobileIdCardScanPage, IdCardScanParameter>(
                new IdCardScanParameter(result => completion.TrySetResult(result)));

            var scanResult = await completion.Task;
            if (scanResult is null)
                return; // 用户取消

            if (scanResult.IsFailure || scanResult.Value is null)
            {
                await _dialogService.DisplayAlertAsync("识别失败", scanResult.Message ?? "未能识别身份证信息", "确定");
                return;
            }

            var info = scanResult.Value;
            var target = Applicants.FirstOrDefault(a => a.IsHead) ?? Applicants.FirstOrDefault();
            if (target is null)
            {
                await _dialogService.DisplayAlertAsync("提示", "请先添加授权人信息行后再扫描", "确定");
                return;
            }

            if (!string.IsNullOrWhiteSpace(info.Name)) target.ApplicantName = info.Name;
            if (!string.IsNullOrWhiteSpace(info.IdCard)) target.ApplicantIdCard = info.IdCard;

            var rawText = info.RawLines.Count > 0 ? string.Join("\n", info.RawLines) : "（无）";
            var checksumHint = !string.IsNullOrWhiteSpace(info.IdCard) && !info.IdCardChecksumValid
                ? "\n（校验位不符，请人工核对身份证号）"
                : string.Empty;
            await _dialogService.DisplayAlertAsync("识别结果",
                $"姓名：{(string.IsNullOrWhiteSpace(info.Name) ? "未识别" : info.Name)}\n" +
                $"身份证号：{(string.IsNullOrWhiteSpace(info.IdCard) ? "未识别" : info.IdCard)}{checksumHint}\n\n" +
                $"原始识别行：\n{rawText}",
                "确定");

            _logger.LogBusiness("身份证 OCR 识别完成",
                ("Name", DataMasker.MaskName(info.Name)),
                ("IdCard", DataMasker.MaskIdCard(info.IdCard)),
                ("ChecksumValid", info.IdCardChecksumValid));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "身份证扫描失败");
            await _dialogService.DisplayAlertAsync("错误", $"扫描失败：{ex.Message}", "确定");
        }
    }

    [RelayCommand]
    private async Task QueryHistoryAsync(AssetCheckItemDto applicant)
    {
        if (applicant == null) return;

        var idCard = applicant.ApplicantIdCard;
        if (string.IsNullOrWhiteSpace(idCard) || idCard.Length != 18)
        {
            await _dialogService.DisplayAlertAsync("提示", "请输入完整的18位身份证号后查询", "确定");
            return;
        }

        try
        {
            IsBusy = true;
            LoadingMessage = "查询历史记录...";

            var result = await _verificationService.GetHistoryByIdCardAsync(idCard, CancellationToken);
            if (result == null)
            {
                await _dialogService.DisplayAlertAsync("提示", "未查询到该身份证号的历史记录", "确定");
                return;
            }

            var filledFields = new List<string>();

            // 1. 填充当前行个人信息
            applicant.ApplicantName = result.ApplicantName;
            filledFields.Add("申请人姓名");

            if (!string.IsNullOrWhiteSpace(result.ApplicantIdType))
            {
                applicant.ApplicantIdType = GetIdTypeDisplay(result.ApplicantIdType);
                filledFields.Add("身份证类型");
            }

            if (!string.IsNullOrWhiteSpace(result.Relationship))
            {
                var relDisplay = GetRelationshipDisplay(result.Relationship);
                var relIndex = Relationships.IndexOf(relDisplay);
                if (relIndex >= 0)
                {
                    applicant.RelationshipIndex = relIndex;
                    if (!filledFields.Contains("家庭关系"))
                        filledFields.Add("家庭关系");
                }
            }

            // 2. 填充表单级字段（仅在空白时填充）
            if (string.IsNullOrWhiteSpace(FamilyAddressDetail) && !string.IsNullOrWhiteSpace(result.FamilyAddress))
            {
                FamilyAddressDetail = result.FamilyAddress;
                filledFields.Add("详细地址");
            }

            if (string.IsNullOrWhiteSpace(ContactPhone) && !string.IsNullOrWhiteSpace(result.ContactPhone))
            {
                ContactPhone = result.ContactPhone;
                filledFields.Add("联系电话");
            }

            if (string.IsNullOrWhiteSpace(SelectedApplicationReason)
                && !string.IsNullOrWhiteSpace(result.ApplicationReason))
            {
                var reasonDisplay = GetApplicationReasonDisplay(result.ApplicationReason);
                if (ApplicationReasons.Contains(reasonDisplay))
                {
                    SelectedApplicationReason = reasonDisplay;
                    filledFields.Add("申请原因");
                }
            }

            if (!string.IsNullOrWhiteSpace(result.Village))
            {
                var villageDisplay = GetVillageDisplay(result.Village);
                if (Villages.Contains(villageDisplay))
                {
                    SelectedVillage = villageDisplay;
                    if (!filledFields.Contains("详细地址"))
                        filledFields.Add("村社");
                }
            }

            // 3. 重建家庭成员列表
            if (result.FamilyMembers.Count > 0)
            {
                Applicants.Clear();
                foreach (var member in result.FamilyMembers)
                {
                    var memberRelIndex = 0;
                    if (!string.IsNullOrWhiteSpace(member.Relationship))
                    {
                        var idx = Relationships.IndexOf(member.Relationship);
                        if (idx >= 0) memberRelIndex = idx;
                    }

                    Applicants.Add(new AssetCheckItemDto
                    {
                        ApplicantName = member.Name,
                        ApplicantIdType = string.IsNullOrWhiteSpace(member.IdType) ? IdTypeConstants.DefaultIdType : GetIdTypeDisplay(member.IdType),
                        ApplicantIdCard = member.IdCard,
                        RelationshipIndex = memberRelIndex,
                        IsHead = member.IsHead,
                        BatchId = _batchId
                    });
                }
                filledFields.Add($"{result.FamilyMembers.Count}名家庭成员");
            }

            // 4. 填充代理人信息
            if (result.HasAgent)
            {
                IsAgent = true;
                AgentName = result.AgentName;
                AgentIdCard = result.AgentIdCard;
                AgentRelationship = result.AgentRelationship;
                AgentIdType = string.IsNullOrWhiteSpace(result.AgentIdType) ? IdTypeConstants.DefaultIdType : GetIdTypeDisplay(result.AgentIdType);
                filledFields.Add("代理人信息");
            }

            // 5. 提示用户
            OnPropertyChanged(nameof(FamilyAddress));
            await _dialogService.DisplayAlertAsync("查询成功",
                $"已自动填充 {string.Join("、", filledFields)}", "确定");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "失败");
            await _dialogService.DisplayAlertAsync("错误", $"查询失败: {ex.Message}", "确定");
        }
        finally
        {
            IsBusy = false;
            LoadingMessage = string.Empty;
        }
    }

    [RelayCommand]
    private async Task SubmitAsync()
    {
        if (IsSubmitting || !IsTownConfigured)
        {
            if (!IsTownConfigured)
                await _dialogService.DisplayAlertAsync("提示", "请先完善个人信息的镇归属", "确定");
            return;
        }

        var validationError = ValidateSubmission();
        if (validationError != null)
        {
            await _dialogService.DisplayAlertAsync("提示", validationError, "确定");
            return;
        }

        IsSubmitting = true;
        try
        {
            var validApplicants = Applicants
                .Where(a => !string.IsNullOrWhiteSpace(a.ApplicantName) && !string.IsNullOrWhiteSpace(a.ApplicantIdCard))
                .ToList();

            var head = Applicants.First();
            var operatorInfo = await GetOperatorInfoAsync();

            foreach (var applicant in validApplicants)
            {
                PrepareApplicantForSubmission(applicant, head.ApplicantIdCard, operatorInfo);
            }

            var duplicateResult = await _verificationService.CheckDuplicateAsync(
                head.ApplicantIdCard, ApplicationDate.Year, ApplicationDate.Month);

            if (duplicateResult.IsSuccess && duplicateResult.Value)
            {
                var confirm = await _dialogService.DisplayAlertAsync(
                    "提示",
                    $"户主 {head.ApplicantName} 在本业务周期内已有核查记录\n\n是否覆盖提交",
                    "覆盖提交", "确定");
                if (!confirm) return;

                await _verificationService.DeleteByHeadIdCardAsync(
                    head.ApplicantIdCard, ApplicationDate.Year, ApplicationDate.Month);
            }

            IsBusy = true;
            LoadingMessage = "提交中...";

            var request = BuildSubmitRequest(validApplicants, operatorInfo);
            var submitResult = await _verificationService.SubmitQuickVerificationAsync(request, CancellationToken);

            // 先关闭蒙版，再显示对话框
            IsBusy = false;

            if (submitResult.IsSuccess)
            {
                _logger.LogBusiness("快速核查提交成功",
                    ("BatchId", _batchId),
                    ("HeadName", head.ApplicantName),
                    ("MemberCount", validApplicants.Count));

                await ProcessPrintResultAsync(validApplicants, head, operatorInfo);
            }
            else
            {
                var errorMsg = submitResult.Message ?? "提交失败，请查看日志";
                await _dialogService.DisplayAlertAsync("错误", errorMsg, "确定");
                _logger.LogBusiness("快速核查提交失败",
                    ("BatchId", _batchId),
                    ("ErrorCode", submitResult.ErrorCode));
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Info("用户取消操作");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "失败");
            await _dialogService.DisplayAlertAsync("错误", $"提交失败: {ex.Message}", "确定");
        }
        finally
        {
            IsBusy = false;
            IsSubmitting = false;
        }
    }

    [RelayCommand]
    private void Reset()
    {
        IsAgent = false;
        AgentName = string.Empty;
        AgentIdCard = string.Empty;
        AgentRelationship = string.Empty;
        AgentIdType = IdTypeConstants.DefaultIdType;
        ApplicationDate = DateTime.Today;
        SelectedVillage = Villages.FirstOrDefault() ?? string.Empty;
        FamilyAddressDetail = string.Empty;
        SelectedApplicationReason = ApplicationReasons.FirstOrDefault() ?? string.Empty;
        ContactPhone = string.Empty;
        _isInitialized = false;

        InitializeApplicants();

        OnPropertyChanged(nameof(FamilyAddress));
        OnPropertyChanged(nameof(IsAgentVisible));
    }

    #endregion

    #region 验证

    private string? ValidateSubmission()
    {
        var validApplicants = Applicants
            .Where(a => !string.IsNullOrWhiteSpace(a.ApplicantName) && !string.IsNullOrWhiteSpace(a.ApplicantIdCard))
            .ToList();

        if (validApplicants.Count == 0)
            return "请至少录入一个申请人信息";

        var head = Applicants.First(); // 上方已保证 validApplicants 非空，Applicants 必有元素
        if (string.IsNullOrWhiteSpace(head.ApplicantName))
            return "第一行必须填写户主信息";

        if (string.IsNullOrWhiteSpace(head.ApplicantIdCard))
            return "请填写户主行的身份证号";

        if (string.IsNullOrWhiteSpace(head.Relationship))
            head.Relationship = PickerConstants.Relationship.HeadDisplay;

        if (string.IsNullOrWhiteSpace(SelectedVillage))
            return "请选择村社";

        if (string.IsNullOrWhiteSpace(FamilyAddressDetail))
            return "请填写详细地址";

        if (string.IsNullOrWhiteSpace(SelectedApplicationReason))
            return "请选择申请原因";

        foreach (var applicant in validApplicants)
        {
            if (string.IsNullOrWhiteSpace(applicant.Relationship))
            {
                var rowNumber = Applicants.IndexOf(applicant) + 1;
                var displayName = string.IsNullOrWhiteSpace(applicant.ApplicantName) ? "未填姓名" : applicant.ApplicantName;
                return $"第{rowNumber}行{displayName}未选择家庭关系";
            }

            if (!string.IsNullOrWhiteSpace(applicant.ApplicantIdCard))
            {
                if (applicant.ApplicantIdCard.Length != 18)
                    return $"申请人{applicant.ApplicantName} 身份证号格式错误：必须为18位";
            }
        }

        return null;
    }

    #endregion

    #region 提交辅助方法

    private async Task<OperatorInfoDto> GetOperatorInfoAsync()
    {
        var userId = App.CurrentUserId;
        var operatorName = string.Empty;
        var operatorAccount = string.Empty;
        var operatorUnitName = string.Empty;

        if (userId.HasValue)
        {
            var userResult = await _userService.GetByIdAsync(userId.Value);
            if (userResult.IsSuccess && userResult.Value != null)
            {
                var user = userResult.Value;
                operatorName = user.FullName ?? user.Username;
                operatorAccount = user.Username;
                operatorUnitName = user.OrganizationName ?? string.Empty;
            }
        }

        return new OperatorInfoDto(userId, operatorName, operatorAccount, operatorUnitName);
    }

    private void PrepareApplicantForSubmission(AssetCheckItemDto applicant, string headIdCard, OperatorInfoDto operatorInfo)
    {
        applicant.BatchId = _batchId;
        applicant.ContactPhone = ContactPhone;
        applicant.IsAgent = IsAgent;

        if (IsAgent)
        {
            applicant.AgentName = AgentName;
            applicant.AgentIdCard = AgentIdCard;
            applicant.AgentRelationship = AgentRelationship;
        }
    }

    private string GetIdTypeCode(string displayValue)
    {
        if (string.IsNullOrWhiteSpace(displayValue))
            return IdTypeConstants.ResidentIdCardCode;
        return _idTypeCodeMap.TryGetValue(displayValue, out var code) ? code : IdTypeConstants.ResidentIdCardCode;
    }

    private string GetIdTypeDisplay(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return IdTypeConstants.DefaultIdType;
        return _dictCacheService.GetValue(DictionaryTypeCodes.IdTypes, code);
    }

    private string GetRelationshipCode(string displayValue)
    {
        if (string.IsNullOrWhiteSpace(displayValue))
            return string.Empty;
        return _relationshipCodeMap.TryGetValue(displayValue, out var code) ? code : displayValue;
    }

    private string GetRelationshipDisplay(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return PickerConstants.Relationship.HeadDisplay;
        return _dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, code);
    }

    private string GetApplicationReasonCode(string displayValue)
    {
        if (string.IsNullOrWhiteSpace(displayValue))
            return PickerConstants.ApplicationReason.OtherCode;
        return _applicationReasonCodeMap.TryGetValue(displayValue, out var code) ? code : displayValue;
    }

    private string GetApplicationReasonDisplay(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return PickerConstants.ApplicationReason.OtherDisplay;
        return _dictCacheService.GetValue(DictionaryTypeCodes.ApplicationReasons, code);
    }

    private string GetVillageDisplay(string code)
    {
        var defaultValue = Villages.FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(code))
            return defaultValue;
        return _villageCodeMap.FirstOrDefault(kvp => kvp.Value == code).Key ?? defaultValue;
    }

    private QuickAssetCheckSubmitRequest BuildSubmitRequest(List<AssetCheckItemDto> validApplicants, OperatorInfoDto operatorInfo)
    {
        return new QuickAssetCheckSubmitRequest
        {
            ApplicationDate = ApplicationDate,
            FamilyAddress = FamilyAddressDetail,
            ApplicationReason = GetApplicationReasonCode(SelectedApplicationReason),
            Community = _villageCodeMap.TryGetValue(SelectedVillage, out var code) ? code : SelectedVillage,
            ContactPhone = ContactPhone,
            Applicants = validApplicants.Select(a => new AssetCheckItemDto
            {
                BatchId = _batchId,
                ApplicantName = a.ApplicantName,
                ApplicantIdCard = a.ApplicantIdCard,
                ApplicantIdType = GetIdTypeCode(a.ApplicantIdType),
                Relationship = a.IsHead
                    ? PickerConstants.Relationship.HeadCode
                    : (GetRelationshipCode(a.Relationship) == PickerConstants.Relationship.HeadCode
                        ? PickerConstants.Relationship.SpouseCode
                        : GetRelationshipCode(a.Relationship)),
                IsHead = a.IsHead,
                ContactPhone = ContactPhone,
                IsAgent = IsAgent,
            }).ToList(),
            IsAgent = IsAgent,
            AgentName = AgentName,
            AgentIdCard = AgentIdCard,
            AgentRelationship = AgentRelationship,
            AgentIdType = GetIdTypeCode(AgentIdType),
            OperatorUserId = operatorInfo.UserId,
            OperatorName = operatorInfo.Name,
            OperatorAccount = operatorInfo.Account,
            OperatorUnitName = operatorInfo.UnitName,
            BatchId = _batchId,
            VerificationYear = ApplicationDate.Year,
            VerificationMonth = ApplicationDate.Month
        };
    }

    private async Task ProcessPrintResultAsync(
        List<AssetCheckItemDto> validApplicants,
        AssetCheckItemDto head,
        OperatorInfoDto operatorInfo)
    {
        try
        {
            var goToPrint = await _dialogService.DisplayAlertAsync(
                "提交成功", "申请已提交成功，是否立即制作档案",
                "制作档案", "确定");

            if (!goToPrint) return;

            _logger.LogBusiness("快速核查提交完成，准备档案输出",
                ("ApplicantName", head.ApplicantName),
                ("BatchId", _batchId));

            var civilAssistantName = await GetCivilAssistantNameAsync();
            var orgInfo = await GetOrganizationInfoAsync();

            var fields = new Dictionary<string, string>
            {
                [FieldKeys.APPLICANT_NAME] = head.ApplicantName,
                [FieldKeys.APPLICANT_ID_CARD] = head.ApplicantIdCard,
                [FieldKeys.FAMILY_ADDRESS] = FamilyAddress,
                [FieldKeys.FAMILY_VILLAGE] = SelectedVillage,
                [FieldKeys.FAMILY_DETAIL_ADDRESS] = FamilyAddressDetail,
                [FieldKeys.APPLICATION_REASON] = SelectedApplicationReason,
                [FieldKeys.APPLICATION_DATE] = ApplicationDate.ToString("yyyy-MM-dd"),
                [FieldKeys.CONTACT_PHONE] = ContactPhone,
                [FieldKeys.OPERATOR_NAME] = operatorInfo.Name,
                [FieldKeys.OPERATOR_UNIT] = operatorInfo.UnitName,
                [FieldKeys.APPLICANT_COUNT] = validApplicants.Count.ToString(),
                [FieldKeys.IS_AGENT] = IsAgent ? "是" : "否",
                [FieldKeys.AGENT_ID_TYPE] = AgentIdType,
                [FieldKeys.HEAD_FAMILY_SIZE] = validApplicants.Count.ToString(),
                [FieldKeys.REPORT_DATE] = DateTime.Now.ToString("yyyy-MM-dd"),
                [FieldKeys.CIVIL_ASSISTANT_NAME] = civilAssistantName,
                [FieldKeys.DISTRICT] = orgInfo.District,
                [FieldKeys.TOWN] = orgInfo.Town,
                [FieldKeys.NOTICE_NUMBER] = GenerateNoticeNumber(),
                [FieldKeys.DISTRICT_CIVIL_BUREAU] = orgInfo.DistrictCivilBureau,
                [FieldKeys.DISTRICT_CIVIL_BUREAU_PHONE] = orgInfo.DistrictCivilBureauPhone,
                [FieldKeys.OPERATOR_UNIT_PHONE] = orgInfo.OperatorUnitPhone
            };

            // 模板1（授权承诺书）家庭成员索引槽位：户主恒为第 1 位，其余成员依序（最多 6 人）
            var familySlots = new List<FamilyFieldBuilder.FamilySlot>
            {
                new(head.ApplicantName, head.ApplicantIdCard,
                    _dictCacheService.GetValue(DictionaryTypeCodes.IdTypes, head.ApplicantIdType),
                    PickerConstants.Relationship.HeadDisplay)
            };
            foreach (var applicant in validApplicants.Where(x => !x.IsHead))
            {
                familySlots.Add(new FamilyFieldBuilder.FamilySlot(
                    applicant.ApplicantName,
                    applicant.ApplicantIdCard,
                    _dictCacheService.GetValue(DictionaryTypeCodes.IdTypes, applicant.ApplicantIdType),
                    GetRelationshipDisplay(applicant.Relationship)));
            }
            FamilyFieldBuilder.Build(fields, familySlots, IsAgent, AgentName, AgentIdCard, AgentIdType, AgentRelationship);

            var tableRows = new List<Dictionary<string, string>>();
            foreach (var applicant in validApplicants)
            {
                tableRows.Add(new Dictionary<string, string>
                {
                    [FieldKeys.FAMILY_MEMBER_NAME] = applicant.ApplicantName,
                    [FieldKeys.FAMILY_MEMBER_ID_CARD] = applicant.ApplicantIdCard,
                    [FieldKeys.FAMILY_MEMBER_RELATION] = applicant.Relationship,
                    [FieldKeys.FAMILY_MEMBER_CERT_TYPE] = _dictCacheService.GetValue(DictionaryTypeCodes.IdTypes, applicant.ApplicantIdType),
                    [FieldKeys.FAMILY_MEMBER_ADDRESS] = FamilyAddress
                });
            }

            // BusinessId 显式置空：PrintNavigationData 是静态单例，快速核查提交不产生核查记录ID，
            // 不清空会残留上一流程（如人员搜索输出档案）的 checkId，导致一键打印误判"该家庭未上传核查报告"而拦截
            PrintNavigationData.BusinessId = null;
            // 整档入口：先清文书模式上下文，防上一次「仅出文书」的静态残留被继承
            PrintNavigationData.ClearDocumentMode();
            PrintNavigationData.BusinessType = "AssetVerification";
            PrintNavigationData.FieldData = fields;
            PrintNavigationData.TableData = tableRows;
            PrintNavigationData.Classification = ClassificationConstants.AssetVerification;

            await NavigateToPageAsync<NewCosmos.Pages.ArchiveManagement.ArchiveOutputPage>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "失败");
            await _dialogService.DisplayAlertAsync("提示",
                $"申请已提交成功，但打开打印预览失败\n{ex.GetType().Name}: {ex.Message}", "确定");
            Reset();
        }
    }

    private async Task<string> GetCivilAssistantNameAsync()
    {
        var orgId = App.CurrentUserOrganizationId;
        if (!orgId.HasValue)
            return "未配置";

        try
        {
            var usersResult = await _userService.GetByOrganizationAsync(orgId.Value);
            if (usersResult.IsFailure || usersResult.Value == null || usersResult.Value.Count == 0)
                return "未配置";

            var civilAssistant = usersResult.Value.FirstOrDefault(u => u.Position == "民政助理");
            return civilAssistant?.FullName ?? "未配置";
        }
        catch
        {
            return "未配置";
        }
    }

    private async Task<OrganizationInfoDto> GetOrganizationInfoAsync()
    {
        var orgId = App.CurrentUserOrganizationId;
        if (!orgId.HasValue)
            return new OrganizationInfoDto("-", "-", "-", "-", "-");

        try
        {
            var orgResult = await _organizationService.GetByIdAsync(orgId.Value);
            if (orgResult.IsFailure || orgResult.Value == null)
                return new OrganizationInfoDto("-", "-", "-", "-", "-");

            var org = orgResult.Value;
            var district = org.CountyName ?? "-";
            var town = org.TownName ?? "-";
            var unitPhone = org.Phone ?? "-";
            var parentName = org.ParentName ?? "-";
            var parentPhone = "-";

            if (org.ParentId.HasValue)
            {
                var parentResult = await _organizationService.GetByIdAsync(org.ParentId.Value);
                if (parentResult.IsSuccess && parentResult.Value != null)
                    parentPhone = parentResult.Value.Phone ?? "-";
            }

            return new OrganizationInfoDto(district, town, parentName, parentPhone, unitPhone);
        }
        catch
        {
            return new OrganizationInfoDto("-", "-", "-", "-", "-");
        }
    }

    private string GenerateNoticeNumber()
    {
        return $"告知-{DateTime.Now:yyyyMMdd}-{new Random().Next(1, 999):D3}";
    }

    #endregion

    private sealed record OperatorInfoDto(int? UserId, string Name, string Account, string UnitName);
    private sealed record OrganizationInfoDto(string District, string Town, string DistrictCivilBureau, string DistrictCivilBureauPhone, string OperatorUnitPhone);
}