using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Enums;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Domain.AssetVerification;
using NewCosmos.Services.Domain.ChangeManagement;
using NewCosmos.Services.Domain.NearRelative;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.Domain.SpecialApproval;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.System;
using NewCosmos.Services.Utilities;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;
using System.Text;
using System.Text.Json;
using ApplicationEntity = NewCosmos.Models.Entities.Application;

namespace NewCosmos.ViewModels.ArchiveManagement;

/// <summary>
/// 档案制作中心 ViewModel
/// 接收业务来源（从工作流页面传入），查询数据，构建字段，右侧显示信息边栏
/// </summary>
public partial class ArchiveProductionViewModel : ViewModelBase
{
    private readonly IAssetVerificationService _verificationService;
    private readonly IApplicationService _applicationService;
    private readonly IUserService _userService;
    private readonly IOrganizationService _organizationService = null!;
    private readonly IDictCacheService _dictCacheService = null!;
    private readonly IDialogService _dialogService = null!;
    private readonly ILoggerService _logger = null!;
    private readonly IServiceProvider _serviceProvider = null!;
    private readonly AddressResolver _addressResolver = null!;
    private readonly IFamilyMemberService _familyMemberService = null!;
    private readonly ISupporterService _supporterService = null!;
    private readonly ICaregiverService _caregiverService = null!;
    private readonly IHouseholdSurveyService _householdSurveyService = null!;
    private readonly IEconomicDetailService _economicDetailService = null!;
    private readonly IGracePeriodService _gracePeriodService = null!;
    private readonly IBusinessTimelineService _timelineService = null!;
    private readonly ICapabilityAssessmentService _capabilityAssessmentService = null!;
    private readonly IChangeService _changeService = null!;
    private readonly IStandardConfigService _standardConfigService = null!;

    public ArchiveProductionViewModel(
        IAssetVerificationService verificationService,
        IApplicationService applicationService,
        IUserService userService,
        IOrganizationService organizationService,
        IDictCacheService dictCacheService,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        AddressResolver addressResolver,
        IFamilyMemberService familyMemberService,
        ISupporterService supporterService,
        ICaregiverService caregiverService,
        IHouseholdSurveyService householdSurveyService,
        IEconomicDetailService economicDetailService,
        IGracePeriodService gracePeriodService,
        IBusinessTimelineService timelineService,
        ICapabilityAssessmentService capabilityAssessmentService,
        IChangeService changeService,
        IStandardConfigService standardConfigService)
    {
        _verificationService = verificationService;
        _applicationService = applicationService;
        _userService = userService;
        _organizationService = organizationService;
        _dictCacheService = dictCacheService;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _addressResolver = addressResolver;
        _familyMemberService = familyMemberService;
        _supporterService = supporterService;
        _caregiverService = caregiverService;
        _householdSurveyService = householdSurveyService;
        _economicDetailService = economicDetailService;
        _gracePeriodService = gracePeriodService;
        _timelineService = timelineService;
        _capabilityAssessmentService = capabilityAssessmentService;
        _changeService = changeService;
        _standardConfigService = standardConfigService;
        Title = "档案制作中心";
    }

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    private bool _isInitialized;

    // ---- 业务信息展示 ----
    [ObservableProperty]
    private string _businessTypeDisplay = string.Empty;

    [ObservableProperty]
    private string _applicantName = string.Empty;

    [ObservableProperty]
    private string _applicantIdCard = string.Empty;

    [ObservableProperty]
    private string _familyInfo = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private bool _hasBusinessData;

    [ObservableProperty]
    private bool _isReady;

    // ---- 侧边栏 ----
    [ObservableProperty]
    private ObservableCollection<SidebarGroup> _sidebarGroups = new();

    [ObservableProperty]
    private string _dataSourceType = string.Empty;

    [ObservableProperty]
    private int _autoFilledCount;

    [ObservableProperty]
    private int _sidebarTotalCount;

    // ---- 内部数据 ----
    private string _currentBusinessType = string.Empty;
    private long? _currentBusinessId;
    private string _currentClassification = string.Empty;

    /// <summary>当前档案状态（用于档案输出判定停保/变更告知书）</summary>
    private string _currentStatus = string.Empty;
    private Dictionary<string, string> _fieldData = new();
    private List<Dictionary<string, string>> _tableData = new();
    private List<Dictionary<string, string>> _supporterTableData = new();
    private List<Dictionary<string, string>> _nearRelativePairs = new();

    // ---- 分步加载缓存 ----
    private ApplicationEntity? _cachedApplication;
    private List<FamilyMember>? _cachedFamilyMembers;
    private List<Supporter>? _cachedSupporters;
    private List<Caregiver>? _cachedCaregivers;
    private HouseholdSurvey? _cachedHouseholdSurvey;
    private EconomicDetailData? _cachedEconomicDetails;
    private CapabilityAssessment? _cachedCapabilityAssessment;

    public ArchiveOutputViewModel OutputViewModel { get; private set; } = null!;

    public Task InitializeAsync()
    {
        if (_isInitialized) return Task.CompletedTask;
        _isInitialized = true;

        var outputVm = _serviceProvider.GetRequiredService<ArchiveOutputViewModel>();
        OutputViewModel = outputVm;
        OnPropertyChanged(nameof(OutputViewModel));
        return Task.CompletedTask;
    }

    #region 从外部页面初始化（工作流页面调用）

    /// <summary>
    /// 从资产核查记录初始化（工作流页面 Tab0/Tab2 点击调用）
    /// </summary>
    public async Task InitializeFromAssetCheckAsync(long verificationId)
    {
        IsBusy = true;
        StatusText = "正在加载经济核对数据...";

        try
        {
            var success = await LoadAssetVerificationDataAsync(verificationId, null);
            if (!success)
            {
                StatusText = "加载经济核对数据失败";
                await _dialogService.DisplayAlertAsync("提示", "未找到对应的经济核对记录", "确定");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 从申请记录初始化（工作流页面 Tab1 点击调用）
    /// </summary>
    public async Task InitializeFromApplicationAsync(long applicationId)
    {
        _logger.LogBusiness("开始从申请记录初始化", ("ApplicationId", applicationId.ToString()));
        IsBusy = true;
        StatusText = "正在初始化...";

        try
        {
            _logger.LogBusiness("步骤1: 初始化服务", ("ApplicationId", applicationId.ToString()));
            StatusText = "正在初始化服务...";
            await InitializeAsync();

            _logger.LogBusiness("步骤2: 加载申请数据", ("ApplicationId", applicationId.ToString()));
            StatusText = "正在加载申请数据...";
            var success = await LoadApplicationDataAsync(applicationId);

            if (!success)
            {
                _logger.LogBusiness("申请数据加载失败", ("ApplicationId", applicationId.ToString()));
                StatusText = "加载申请数据失败";
                await _dialogService.DisplayAlertAsync("提示", "未找到对应的申请记录", "确定");
            }
            else
            {
                _logger.LogBusiness("申请数据加载成功", ("ApplicationId", applicationId.ToString()), ("ApplicantName", ApplicantName));
                StatusText = $"已加载 {ApplicantName}";
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"加载申请数据异常: {ex.Message}\n{ex.StackTrace}");
            StatusText = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _logger.LogBusiness("InitializeFromApplicationAsync 完成", ("ApplicationId", applicationId.ToString()));
        }
    }

    /// <summary>
    /// 从申请记录初始化（复核/成员变更归档：加载变更后档案数据，按 EconomicReview 分类输出）。
    /// businessTitle 仅控制展示文案（经济复核/成员变更），模板分类与打印权限仍走 EconomicReview。
    /// </summary>
    public async Task InitializeFromApplicationReviewAsync(long applicationId, string businessTitle = "经济复核")
    {
        _logger.LogBusiness("开始从申请记录初始化（复核归档）", ("ApplicationId", applicationId.ToString()), ("BusinessTitle", businessTitle));
        IsBusy = true;
        StatusText = "正在初始化...";

        try
        {
            _logger.LogBusiness("步骤1: 初始化服务", ("ApplicationId", applicationId.ToString()));
            StatusText = "正在初始化服务...";
            await InitializeAsync();

            _logger.LogBusiness("步骤2: 加载申请数据", ("ApplicationId", applicationId.ToString()));
            StatusText = "正在加载申请数据...";
            var success = await LoadApplicationDataAsync(applicationId);

            if (!success)
            {
                _logger.LogBusiness("申请数据加载失败", ("ApplicationId", applicationId.ToString()));
                StatusText = "加载申请数据失败";
                await _dialogService.DisplayAlertAsync("提示", "未找到对应的申请记录", "确定");
                return;
            }

            // 切到复核业务类型：模板分类走 [分类代码, 经济复核]
            _currentBusinessType = "EconomicReview";
            BusinessTypeDisplay = businessTitle;
            DataSourceType = businessTitle;

            _logger.LogBusiness("申请数据加载成功（复核归档）",
                ("ApplicationId", applicationId.ToString()),
                ("ApplicantName", DataMasker.MaskName(ApplicantName)),
                ("Classification", _currentClassification));

            // 填 PrintNavigationData 并推入档案输出页
            await PrepareAndNavigateAsync();
        }
        catch (Exception ex)
        {
            _logger.Error($"加载申请数据异常: {ex.Message}\n{ex.StackTrace}");
            StatusText = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            _logger.LogBusiness("InitializeFromApplicationReviewAsync 完成", ("ApplicationId", applicationId.ToString()));
        }
    }

    /// <summary>
    /// 从申请记录初始化（带进度弹窗）
    /// </summary>
    public async Task InitializeFromApplicationWithProgressAsync(long applicationId)
    {
        var progressService = _serviceProvider.GetRequiredService<ILoadingProgressService>();
        var success = await Pages.Shared.LoadingProgressDialog.ShowAsync(_serviceProvider, async ct =>
        {
            progressService.UpdateProgress(LoadingSteps.Initialize, "正在初始化...");
            await InitializeAsync();

            progressService.UpdateProgress(LoadingSteps.LoadApplication, "正在加载申请信息...");
            var loadSuccess = await LoadApplicationBasicInfoAsync(applicationId);
            if (!loadSuccess) throw new Exception("未找到对应的申请记录");

            progressService.UpdateProgress(LoadingSteps.LoadFamilyMembers, "正在加载家庭成员...");
            await LoadFamilyMembersDataAsync(applicationId);

            progressService.UpdateProgress(LoadingSteps.LoadSupporters, "正在加载赡养人...");
            await LoadSupportersDataAsync(applicationId);

            progressService.UpdateProgress(LoadingSteps.LoadCaregivers, "正在加载照料人...");
            await LoadCaregiversDataAsync(applicationId);

            progressService.UpdateProgress(LoadingSteps.LoadHouseholdSurvey, "正在加载入户调查...");
            await LoadHouseholdSurveyDataAsync(applicationId);

            progressService.UpdateProgress(LoadingSteps.LoadEconomicDetails, "正在加载经济明细...");
            await LoadEconomicDetailsDataAsync(applicationId);

            progressService.UpdateProgress(LoadingSteps.LoadCapabilityAssessment, "正在加载能力鉴定...");
            await LoadCapabilityAssessmentDataAsync(applicationId);

            progressService.UpdateProgress(LoadingSteps.BuildFieldData, "正在构建字段数据...");
            await BuildFieldDataFromCacheAsync();
        });

        if (success)
        {
            StatusText = $"已加载 {ApplicantName}";
        }
        else
        {
            StatusText = "加载失败或已取消";
        }
    }

    /// <summary>
    /// 构建打印数据（历史补打专用）：复用 LoadApplicationDataAsync 全套字段构建逻辑，
    /// 返回构建结果供调用方填充 PrintNavigationData 后进入预览/打印流程。
    /// </summary>
    public async Task<ApplicationPrintData?> BuildPrintDataAsync(long applicationId)
    {
        var success = await LoadApplicationDataAsync(applicationId);
        if (!success) return null;

        return new ApplicationPrintData(
            _currentBusinessType,
            _currentBusinessId,
            _currentClassification,
            new Dictionary<string, string>(_fieldData, StringComparer.Ordinal),
            _tableData.ToList(),
            _supporterTableData?.ToList(),
            ApplicantName);
    }

    /// <summary>
    /// 从外部页面传入业务数据（快速核查、月报表等提交后直接调用）
    /// </summary>
    public async Task LoadFromExternalAsync(string businessType, long businessId, string idCard = null)
    {
        _currentBusinessType = businessType;
        _currentBusinessId = businessId;

        StatusText = "正在加载业务数据...";
        IsBusy = true;

        try
        {
            await InitializeAsync();
            var success = await LoadBusinessDataAsync(businessType, businessId, idCard);
            if (success)
            {
                await PrepareAndNavigateAsync();
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion

    #region 跳转到档案输出页面

    [RelayCommand]
    private async Task NavigateToOutputAsync()
    {
        if (!HasBusinessData || _fieldData == null)
        {
            await _dialogService.DisplayAlertAsync("提示", "请先加载业务数据", "确定");
            return;
        }

        await PrepareAndNavigateAsync();
    }

    private async Task PrepareAndNavigateAsync()
    {
        PrintNavigationData.BusinessType = _currentBusinessType;
        PrintNavigationData.BusinessId = _currentBusinessId;
        PrintNavigationData.Classification = _currentClassification;
        PrintNavigationData.Status = _currentStatus;
        PrintNavigationData.FieldData = _fieldData;
        PrintNavigationData.TableData = _tableData;
        PrintNavigationData.SupporterTableData = _supporterTableData;
        PrintNavigationData.NearRelativePairs = _nearRelativePairs;
        // 注意：不清理 OutputCategories / PrefilterTemplateNames / OperationOverride / TemplateFilter
        // —— 输出文书入口在导航到本页前已写入，进入 Output 时按其收窄/预勾选。

        _logger.LogBusiness("档案制作中心 - 准备导航到档案输出",
            ("BusinessType", _currentBusinessType),
            ("BusinessId", _currentBusinessId?.ToString() ?? "null"),
            ("ApplicantName", DataMasker.MaskName(ApplicantName)),
            ("DocMode", PrintNavigationData.OutputCategories != null || PrintNavigationData.PrefilterTemplateNames != null));

        await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveOutputPage>();
    }

    #endregion

    #region 复制侧边栏数据

    /// <summary>
    /// 复制单行数据到剪贴板
    /// </summary>
    [RelayCommand]
    private async Task CopyFieldAsync(SidebarItem? item)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Value)) return;

        try
        {
            await Clipboard.SetTextAsync(item.Value);
            _logger.LogBusiness("复制侧边栏数据", ("Label", item.Label));
            StatusText = $"已复制: {item.Label}";
        }
        catch (Exception ex)
        {
            _logger.Error("复制到剪贴板失败: " + ex.Message);
        }
    }

    #endregion

    #region 返回

    // 返回首页：归档完成后直接回到社会救助首页，跳过中间页面
    public override async Task GoBackAsync()
    {
        try
        {
            var navigation = Helpers.WindowNavigator.CurrentNavigation;
            if (navigation == null)
            {
                Logger.Warn("返回失败：无导航栈");
                return;
            }

            await navigation.PopToRootAsync();
            // PopToRoot 直接回到主首页，必须恢复窗口标题（历史 BUG：标题残留档案生成页）
            RestoreWindowTitleFromNavigation();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "返回首页失败");
        }
    }

    #endregion

    // ========================
    //  业务数据加载（按类型分发）
    // ========================

    private async Task<bool> LoadBusinessDataAsync(string businessType, long? businessId, string idCard)
    {
        return businessType switch
        {
            "AssetVerification" => await LoadAssetVerificationDataAsync(businessId, idCard),
            "AssetVerificationMonthlyReport" => await LoadMonthlyReportDataAsync(businessId),
            _ => await LoadGenericDataAsync(businessType, businessId)
        };
    }

    private async Task<bool> LoadAssetVerificationDataAsync(long? businessId, string idCard)
    {
        AssetVerificationDetail? detail = null;

        if (businessId.HasValue)
        {
            var result = await _verificationService.GetDetailByIdAsync(businessId.Value);
            if (result.IsFailure || result.Value == null) return false;
            detail = result.Value;
        }
        else if (!string.IsNullOrWhiteSpace(idCard))
        {
            var searchResult = await _verificationService.SearchByNameOrIdCardAsync(idCard);
            if (searchResult.IsFailure || searchResult.Value == null || searchResult.Value.Count == 0) return false;

            var task = searchResult.Value.First();
            var detailResult = await _verificationService.GetDetailByIdAsync(task.Id);
            if (detailResult.IsFailure || detailResult.Value == null) return false;
            detail = detailResult.Value;
        }
        else
        {
            return false;
        }

        var civilAssistantName = await GetCivilAssistantNameAsync();
        var orgInfo = await GetOrganizationInfoAsync();

        _currentBusinessType = "AssetVerification";
        _currentBusinessId = detail.Id;
        _currentClassification = ClassificationConstants.AssetVerification;

        BusinessTypeDisplay = "家庭经济状况核对";
        DataSourceType = "家庭经济状况核对";
        ApplicantName = detail.ApplicantName;
        ApplicantIdCard = DataMasker.MaskIdCard(detail.ApplicantIdCard);
        FamilyInfo = $"家庭成员 {detail.FamilyMembers.Count} 人";
        HasBusinessData = true;
        IsReady = true;
        StatusText = $"已加载 {detail.ApplicantName}";

        // 构建地址信息（10秒超时）
        using var addressCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var fullAddress = await _addressResolver.BuildAddressAsync(detail.Community, detail.FamilyAddress, CancellationToken);

        _fieldData = new Dictionary<string, string>
        {
            [FieldKeys.APPLICANT_NAME] = detail.ApplicantName,
            [FieldKeys.APPLICANT_ID_CARD] = detail.ApplicantIdCard,
            [FieldKeys.APPLICANT_ID_TYPE] = GetIdTypeDisplay(detail.ApplicantIdType),
            [FieldKeys.FAMILY_ADDRESS] = fullAddress,
            [FieldKeys.COMMUNITY] = detail.Community,
            [FieldKeys.APPLICATION_REASON] = GetApplicationReasonDisplay(detail.ApplicationReason),
            [FieldKeys.APPLICATION_DATE] = detail.ApplicationDate.ToString("yyyy-MM-dd"),
            [FieldKeys.CONTACT_PHONE] = detail.ContactPhone,
            [FieldKeys.STATUS] = detail.StatusDisplay,
            [FieldKeys.OPERATOR_NAME] = detail.OperatorName,
            [FieldKeys.OPERATOR_UNIT] = detail.OperatorUnitName,
            [FieldKeys.IS_AGENT] = detail.HasAgent ? "是" : "否",
            [FieldKeys.AGENT_NAME] = detail.AgentName,
            [FieldKeys.AGENT_ID_CARD] = detail.AgentIdCard,
            [FieldKeys.AGENT_RELATION] = detail.AgentRelationship,
            [FieldKeys.AGENT_CERT_TYPE] = GetIdTypeDisplay(detail.AgentIdType),
            [FieldKeys.APPLICANT_COUNT] = detail.FamilyMembers.Count.ToString(),
            [FieldKeys.HEAD_FAMILY_SIZE] = detail.FamilyMembers.Count.ToString(),
            [FieldKeys.REPORT_DATE] = DateTime.Now.ToString("yyyy-MM-dd"),
            [FieldKeys.CIVIL_ASSISTANT_NAME] = civilAssistantName,
            [FieldKeys.DISTRICT] = orgInfo.District,
            [FieldKeys.TOWN] = orgInfo.Town,
            [FieldKeys.NOTICE_NUMBER] = GenerateNoticeNumber(),
            [FieldKeys.DISTRICT_CIVIL_BUREAU] = orgInfo.DistrictCivilBureau,
            [FieldKeys.DISTRICT_CIVIL_BUREAU_PHONE] = orgInfo.DistrictCivilBureauPhone,
            [FieldKeys.OPERATOR_UNIT_PHONE] = orgInfo.OperatorUnitPhone,
            [FieldKeys.HEAD_ID_CARD] = detail.HeadIdCard,
            [FieldKeys.APPLICATION_NO] = "",
            [FieldKeys.HUKOU_ADDRESS] = fullAddress,
            ["FAMILY_MEMBER_INFO"] = BuildAssetCheckFamilyMemberInfo(detail),
        };

        // 构建 _tableData：户主始终在第 1 位
        var headEntry = BuildHeadMemberEntry(detail.ApplicantName, detail.ApplicantIdCard, fullAddress);
        _tableData = new List<Dictionary<string, string>> { headEntry };

        // 添加其他家庭成员（跳过户主）
        foreach (var m in detail.FamilyMembers)
        {
            if (m.IsHead || m.IdCard == detail.ApplicantIdCard)
                continue;

            _tableData.Add(BuildMemberEntry(m.Name, m.IdCard, m.Relationship, fullAddress, detail.HeadIdCard));
        }

        BuildSidebarFromAssetCheck(detail, fullAddress, civilAssistantName, orgInfo);

        return true;
    }

    private async Task<bool> LoadApplicationDataAsync(long applicationId)
    {
        _logger.LogBusiness("LoadApplicationDataAsync 开始", ("ApplicationId", applicationId.ToString()));
        StatusText = "正在查询申请记录...";

        // 10秒超时兜底，防止查询挂起卡死页面
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await _applicationService.GetByIdAsync(applicationId, cts.Token);
        if (result.IsFailure || result.Value == null)
        {
            _logger.LogBusiness("申请记录查询失败", ("ApplicationId", applicationId.ToString()), ("IsFailure", result.IsFailure.ToString()));
            return false;
        }

        _logger.LogBusiness("申请记录查询成功", ("ApplicationId", applicationId.ToString()), ("ApplicantName", result.Value.ApplicantName));
        var app = result.Value;

        StatusText = "正在获取经办人信息...";
        var civilAssistantName = await GetCivilAssistantNameAsync();
        _logger.LogBusiness("经办人信息获取完成", ("CivilAssistantName", civilAssistantName));

        StatusText = "正在获取机构信息...";
        var orgInfo = await GetOrganizationInfoAsync();
        _logger.LogBusiness("机构信息获取完成", ("OperatorUnitName", orgInfo.OperatorUnitName));

        _currentBusinessType = "FamilyApplication";
        _currentBusinessId = app.Id;
        _currentClassification = app.ClassificationResult ?? "";
        _currentStatus = app.Status ?? "";

        BusinessTypeDisplay = "社会救助申请";
        DataSourceType = "社会救助申请";
        ApplicantName = app.ApplicantName;
        ApplicantIdCard = DataMasker.MaskIdCard(app.ApplicantIdCard);
        FamilyInfo = $"家庭成员 {app.FamilySize} 人";
        HasBusinessData = true;
        IsReady = true;

        // 构建地址信息
        StatusText = "正在构建地址信息...";
        _logger.LogBusiness("构建地址信息", ("Community", app.Community ?? ""));
        var fullAddress = $"{app.Town ?? ""}{app.Community ?? ""}{app.Address ?? ""}";
        _logger.LogBusiness("地址信息构建完成", ("FullAddress", fullAddress));

        StatusText = "正在构建字段数据...";
        _logger.LogBusiness("开始构建字段数据", ("ApplicationId", applicationId.ToString()));
        var landIncomeSituation = await BuildLandIncomeSituationAsync(app);
        var landIncomeSituationV2 = await BuildLandIncomeSituationAsync(app, includeLead: false);

        // 预加载经济明细（用于单人保、财产豁免、申请理由等字段）
        Services.Domain.SocialAssistance.EconomicDetailData? economicDetail = null;
        try
        {
            var econService = _serviceProvider.GetRequiredService<IEconomicDetailService>();
            var detailResult = await econService.LoadAllAsync(app.Id, CancellationToken.None);
            if (detailResult.IsSuccess)
                economicDetail = detailResult.Value;
        }
        catch (Exception ex)
        {
            _logger.Warn($"预加载经济明细失败: {ex.Message}");
        }

        // 预加载入户调查（用于政府意见）
        if (_cachedHouseholdSurvey == null)
        {
            try
            {
                var surveyResult = await _householdSurveyService.GetByApplicationIdAsync(applicationId, CancellationToken.None);
                if (surveyResult.IsSuccess) _cachedHouseholdSurvey = surveyResult.Value;
            }
            catch { }
        }

        // B线时间轴：公示起止/审核确认日/档案生效/卷号/入户调查日期（统一走 BusinessTimelineService）
        // 锚定该档案自身业务日期（补全完成→首次审批→更新→创建），保证补打时编号/卷号/日期稳定，不随当天漂移
        var archiveAnchor = ResolveArchiveAnchorDate(app);
        var tl = _timelineService.GetTimelineForDate(archiveAnchor, TimelineType.BusinessProcess);
        var publicityStart = tl.PublicityStartDate.ToString("yyyy-MM-dd");
        var publicityEnd = tl.PublicityEndDate.ToString("yyyy-MM-dd");
        var auditDate = tl.AuditDate.ToString("yyyy-MM-dd");
        var archiveEffective = new DateTime(tl.CycleEndDate.Year, tl.CycleEndDate.Month, 1).AddMonths(1).ToString("yyyy-MM-dd");
        var volumeDate = new DateTime(tl.CycleEndDate.Year, tl.CycleEndDate.Month, 1).AddMonths(1).ToString("yyyy-M-d");
        var reviewEffective = new DateTime(tl.CycleEndDate.Year, tl.CycleEndDate.Month, 1).AddMonths(1).ToString("M月d日");
        var surveyDate = ResolveSurveyDate(tl, _cachedHouseholdSurvey?.SurveyDate);
        var hukouAddress = await ResolveHukouAddressAsync(app);

        _fieldData = new Dictionary<string, string>
        {
            [FieldKeys.APPLICANT_NAME] = app.ApplicantName,
            [FieldKeys.APPLICANT_ID_CARD] = app.ApplicantIdCard,
            [FieldKeys.APPLICANT_ID_TYPE] = "居民身份证",
            [FieldKeys.FAMILY_ADDRESS] = fullAddress,
            [FieldKeys.COMMUNITY] = app.Community ?? "",
            [FieldKeys.APPLICATION_REASON] = GetApplicationReasonDisplay(app.ApplicationReason),
            [FieldKeys.APPLICATION_DATE] = ResolveApplicationDate(app).ToString("yyyy-MM-dd"),
            [FieldKeys.CONTACT_PHONE] = app.ApplicantPhone,
            [FieldKeys.STATUS] = ApplicationStatusExtensions.FromCode(app.Status ?? "").GetDescription(),
            [FieldKeys.OPERATOR_NAME] = App.CurrentUserFullName,
            [FieldKeys.OPERATOR_UNIT] = orgInfo.OperatorUnitName,
            [FieldKeys.IS_AGENT] = "否",
            [FieldKeys.AGENT_NAME] = "",
            [FieldKeys.AGENT_ID_CARD] = "",
            [FieldKeys.AGENT_RELATION] = "",
            [FieldKeys.AGENT_CERT_TYPE] = "",
            [FieldKeys.APPLICANT_COUNT] = app.FamilySize.ToString(),
            [FieldKeys.HEAD_FAMILY_SIZE] = $"{app.FamilySize}口人",
            [FieldKeys.REPORT_DATE] = DateTime.Now.ToString("yyyy-MM-dd"),
            [FieldKeys.CIVIL_ASSISTANT_NAME] = civilAssistantName,
            [FieldKeys.DISTRICT] = orgInfo.District,
            [FieldKeys.TOWN] = orgInfo.Town,
            [FieldKeys.NOTICE_NUMBER] = GenerateNoticeNumber(),
            [FieldKeys.DISTRICT_CIVIL_BUREAU] = orgInfo.DistrictCivilBureau,
            [FieldKeys.DISTRICT_CIVIL_BUREAU_PHONE] = orgInfo.DistrictCivilBureauPhone,
            [FieldKeys.OPERATOR_UNIT_PHONE] = orgInfo.OperatorUnitPhone,
            [FieldKeys.HEAD_ID_CARD] = app.ApplicantIdCard,
            [FieldKeys.APPLICATION_NO] = app.ApplicationNo ?? "",
            [FieldKeys.CLASSIFICATION_RESULT] = ClassificationConstants.ConvertToFullName(app.ClassificationResult ?? ""),
            [FieldKeys.HUKOU_ADDRESS] = hukouAddress,
            [FieldKeys.SURVEY_DATE] = surveyDate,
            [FieldKeys.SURVEY_TIME] = DateTime.Now.ToString("yyyy年M月d日 HH:mm"),
            [FieldKeys.APPLICATION_REASON_DETAIL] = "-",  // 模板10其它困难原因：需求要求固定留空为"-"
            [FieldKeys.TOTAL_FAMILY_INCOME_ANNUAL] = FormatDecimal(app.TotalAnnualIncome),
            [FieldKeys.PER_CAPITA_INCOME_ANNUAL] = FormatDecimal(app.PerCapitaAnnualIncome),
            [FieldKeys.TOTAL_FAMILY_INCOME_MONTHLY] = FormatDecimal(app.TotalFamilyIncome),
            [FieldKeys.PER_CAPITA_INCOME_MONTHLY] = FormatDecimal(app.PerCapitaIncome),
            [FieldKeys.RIGID_EXPENDITURE_DETAIL] = BuildRigidExpenditureDisplay(app, economicDetail),
            [FieldKeys.LAND_INCOME_SELF_FARM] = "0",
            [FieldKeys.LAND_INCOME_SUBLEASE] = "0",
            [FieldKeys.LAND_INCOME_CONTRACT] = "0",
            [FieldKeys.LAND_INCOME_SITUATION] = landIncomeSituation,
            [FieldKeys.LAND_INCOME_SITUATION_V2] = landIncomeSituationV2,
            [FieldKeys.SINGLE_RESCUE_SITUATION] = BuildSingleRescueSituation(app),
            [FieldKeys.PROPERTY_EXEMPTION_SITUATION] = BuildPropertyExemptionSituation(app, economicDetail),
            [FieldKeys.KINSHIP_FILING_SITUATION] = BuildKinshipFilingSituation(),
            [FieldKeys.SURVEY_VISIT_SITUATION] = BuildSurveyVisitSituation(app),

            // 模板10 需要的字段
            ["APPLICANT_GENDER"] = AddressResolver.ExtractGenderFromIdCard(app.ApplicantIdCard),
            ["APPLICANT_AGE"] = AddressResolver.ExtractAgeFromIdCard(app.ApplicantIdCard),
            ["INCOME_SITUATION"] = BuildIncomeSituationText(app, economicDetail),
            ["FAMILY_MEMBER_INFO"] = "",  // 延迟到加载家庭成员后填充
            // 模板10（最低生活保障申请书）专用：简称，避免与模板16的CLASSIFICATION_RESULT冲突
            [FieldKeys.APPLY_CLASSIFICATION] = ClassificationConstants.ConvertFromCode(app.ClassificationResult ?? ""),
            // 定期复核审批表：享受类别缩写（低保/低保边缘/特困/刚性支出）
            [FieldKeys.CLASSIFICATION_MAJOR] = ClassificationConstants.ConvertToMajorCategoryName(app.ClassificationResult ?? ""),

            // 模板8（邻里访问调查表）需要的字段
            ["INCOME_SOURCE"] = BuildIncomeSourceSummary(app, economicDetail),
            ["INCOME_BREEDING_DESC"] = BuildBreedingIncomeDesc(economicDetail),
            ["INCOME_BUSINESS_DESC"] = BuildBusinessIncomeDesc(economicDetail),
            ["INCOME_LABOR_DESC"] = BuildLaborIncomeDesc(economicDetail),
            ["PROPERTY_FARM_EQUIPMENT"] = BuildFarmEquipmentDesc(economicDetail),
            ["PROPERTY_VEHICLE"] = BuildVehicleDesc(economicDetail),
            ["PROPERTY_HOUSE_DESC"] = BuildHouseDesc(economicDetail),
            ["APPLICANT_LIFE_SITUATION"] = "",  // 延迟到加载家庭成员后填充
            // 模板8（邻里访问调查表）专用：简称，避免与模板16的CLASSIFICATION_RESULT冲突
            [FieldKeys.SURVEY_CLASSIFICATION] = ClassificationConstants.ConvertFromCode(app.ClassificationResult ?? ""),

            // 模板13（公示文件）需要的字段
            ["GUARANTEE_TYPE_DISPLAY"] = BuildGuaranteeTypeDisplay(app.ClassificationResult ?? ""),
            [FieldKeys.GUARANTEE_AMOUNT] = app.TotalGuaranteeAmount.ToString("N2"),
            [FieldKeys.PUBLICITY_START_DATE] = publicityStart,
            [FieldKeys.PUBLICITY_END_DATE] = publicityEnd,
            // 模板13（公示文件）专用：避免与模板16的CLASSIFICATION_RESULT冲突
            [FieldKeys.PUBLICITY_CLASSIFICATION] = ClassificationConstants.ConvertToFullName(app.ClassificationResult ?? ""),

            // 模板14（低保审核确认表）需要的字段
            [FieldKeys.CONFIRM_AMOUNT] = app.HouseholdMonthlyGuaranteeAmount > 0 ? $"{app.HouseholdMonthlyGuaranteeAmount:N2}元" : "",
            [FieldKeys.APPLICANT_GENDER] = AddressResolver.ExtractGenderFromIdCard(app.ApplicantIdCard),
            [FieldKeys.EMPLOYMENT_STATUS] = _dictCacheService.GetValue(DictionaryTypeCodes.EmploymentStatuses, app.EmploymentStatus ?? ""),
            [FieldKeys.HOUSING_CATEGORY] = "",
            [FieldKeys.HEALTH_STATUS] = _dictCacheService.GetValue(DictionaryTypeCodes.HealthStatuses, app.HealthStatus ?? ""),
            [FieldKeys.MARITAL_STATUS] = _dictCacheService.GetValue(DictionaryTypeCodes.MaritalStatuses, app.MaritalStatus ?? ""),
            [FieldKeys.URBAN_MONTHLY_INCOME] = ClassificationConstants.HukouType.IsHukouUrban(app.HukouType) ? FormatDecimal(app.TotalFamilyIncome) : "",
            [FieldKeys.RURAL_ANNUAL_INCOME] = ClassificationConstants.HukouType.IsHukouRural(app.HukouType) ? FormatDecimal(app.TotalAnnualIncome) : "",
            [FieldKeys.ENJOY_CLASSIFIED_SUBSIDY] = BuildCheckboxWithLabel(app.ClassifiedSubsidyAmount > 0, "享受分类施保加发"),
            [FieldKeys.CLASSIFIED_SUBSIDY_AMOUNT] = app.ClassifiedSubsidyAmount > 0 ? $"{FormatDecimal(app.ClassifiedSubsidyAmount)}元" : "",
            [FieldKeys.ENJOY_DISABILITY_ALLOWANCE] = BuildCheckboxWithLabel(false, "享受失能老人津贴"),
            [FieldKeys.DISABILITY_ALLOWANCE_AMOUNT] = "",
            [FieldKeys.ENJOY_ELDERLY_ALLOWANCE] = BuildCheckboxWithLabel(false, "享受高龄老人津贴"),
            [FieldKeys.ELDERLY_ALLOWANCE_AMOUNT] = "",
            [FieldKeys.SINGLE_PERSON_GUARANTEE] = BuildYesNoCheckbox(ClassificationConstants.IsCodeSingleRescue(app.ClassificationResult ?? "")),
            [FieldKeys.GUARANTEE_FAMILY_SIZE] = $"{app.FamilySize}口人",
            [FieldKeys.RIGID_EXPENDITURE] = BuildYesNoCheckbox(app.RigidExpenditure > 0),
            [FieldKeys.KINSHIP_FILING] = BuildYesNoCheckbox(false),
            [FieldKeys.PROPERTY_EXEMPTION] = BuildYesNoCheckbox(false),
            [FieldKeys.PUBLICITY_STATUS] = "已公示、无异议",
            [FieldKeys.GOVERNMENT_OPINION] = BuildGovernmentOpinion(_cachedHouseholdSurvey),
            [FieldKeys.AUDIT_DATE] = auditDate,
            [FieldKeys.VERIFIED_BENEFIT] = BuildVerifiedBenefitText(app),
[FieldKeys.ARCHIVE_EFFECTIVE_DATE] = archiveEffective,
            // 定期复核审批表：审核次月日期（M月d日）
            [FieldKeys.REVIEW_EFFECTIVE_DATE] = reviewEffective,
            // 定期复核审批表：复核组默认值（真实值由 ApplyReviewGroupAsync 覆写；无变更记录时打 "-" 而非报未映射）
            [FieldKeys.REVIEW_SITUATION] = "",
            [FieldKeys.REVIEW_TIME] = "",
            [FieldKeys.REVIEW_CLASSIFICATION_CHANGE] = "",
            [FieldKeys.REVIEW_AMOUNT_CHANGE] = "",
            // 模板14（低保审核确认表）专用：简称，单人保显示为"单人保"
            [FieldKeys.AUDIT_CLASSIFICATION] = ClassificationConstants.ConvertToShortName(app.ClassificationResult ?? ""),

            // 模板15（分类施保调整表）需要的字段
            [FieldKeys.APPLICANT_BIRTH_DATE] = IdCardValidator.ExtractBirthDate(app.ApplicantIdCard)?.ToString("yyyy-MM-dd") ?? "",
            [FieldKeys.NATIONALITY] = _dictCacheService.GetValue(DictionaryTypeCodes.Ethnicities, app.Ethnicity ?? ""),
            [FieldKeys.CLASSIFIED_SUBSIDY_TYPE] = "",  // 延迟到加载家庭成员后填充
            [FieldKeys.CLASSIFIED_TOTAL_COUNT] = "",  // 延迟到加载家庭成员后填充
            [FieldKeys.CLASSIFIED_TOTAL_AMOUNT] = $"经 {orgInfo.Town}（{orgInfo.OperatorUnitName}）批准加发 {FormatDecimal(app.ClassifiedSubsidyAmount)} 元",
            [FieldKeys.AUDIT_OPINION] = "",  // 延迟到加载家庭成员后填充
            [FieldKeys.AUDIT_DATE] = auditDate,

            // 渐退期审批表字段（初始空值，侧栏加载时填充）
            [FieldKeys.GP_HEAD_NAME] = "",
            [FieldKeys.GP_HEAD_GENDER] = "",
            [FieldKeys.GP_HEAD_ID_CARD] = "",
            [FieldKeys.GP_HEAD_AGE] = "",
            [FieldKeys.GP_FAMILY_SIZE] = "",
            [FieldKeys.GP_CLASSIFICATION] = "",
            [FieldKeys.GP_ADDRESS] = "",
            [FieldKeys.GP_PHONE] = "",
            [FieldKeys.GP_CHANGE_DETAIL] = "",
            [FieldKeys.GP_PERIOD_MONTHS] = "",
            [FieldKeys.GP_START] = "",
            [FieldKeys.GP_END] = "",
            [FieldKeys.GP_PERIOD_RANGE] = "",
            [FieldKeys.GP_EXIT_SITUATION] = "",
            [FieldKeys.GP_AUDIT_TIME] = "",

            // 档案_保障金减少字段（初始空值，侧栏渐退封顶时填充）
            [FieldKeys.GR_HEAD_NAME] = "",
            [FieldKeys.GR_ID_CARD] = "",
            [FieldKeys.GR_FAMILY_SIZE] = "",
            [FieldKeys.GR_OLD_AMOUNT] = "",
            [FieldKeys.GR_NEW_AMOUNT] = "",
            [FieldKeys.GR_DECREASE_AMOUNT] = "",
            [FieldKeys.GR_CAP_BASIS] = "",
            [FieldKeys.GR_GRACE_START] = "",
            [FieldKeys.GR_GRACE_END] = "",
            [FieldKeys.GR_DEATH_DATE] = "",
            [FieldKeys.GR_REASON] = "",
            [FieldKeys.GR_AUDIT_TIME] = "",

            // 增减员调整表字段（初始空值，侧栏加载时填充）
            [FieldKeys.ADJ_FILL_UNIT] = "",
            [FieldKeys.ADJ_HEAD_NAME] = "",
            [FieldKeys.ADJ_HEAD_GENDER] = "",
            [FieldKeys.ADJ_HEAD_BIRTH] = "",
            [FieldKeys.ADJ_HEAD_NATION] = "",
            [FieldKeys.ADJ_HEAD_FAMILY_SIZE] = "",
            [FieldKeys.ADJ_HEAD_FAMILY_TYPE] = "",
            [FieldKeys.ADJ_HEAD_ADDRESS] = "",
            [FieldKeys.ADJ_HEAD_CLASSIFICATION] = "",
            [FieldKeys.ADJ_HEAD_HUKOU] = "",
            [FieldKeys.ADJ_HEAD_ID_CARD] = "",
            [FieldKeys.ADJ_ADD_NAME_1] = "", [FieldKeys.ADJ_ADD_GENDER_1] = "", [FieldKeys.ADJ_ADD_ID_CARD_1] = "",
            [FieldKeys.ADJ_ADD_RELATION_1] = "", [FieldKeys.ADJ_ADD_HEALTH_1] = "", [FieldKeys.ADJ_ADD_WORKPLACE_1] = "",
            [FieldKeys.ADJ_ADD_REASON_1] = "", [FieldKeys.ADJ_ADD_INCOME_1] = "",
            [FieldKeys.ADJ_ADD_NAME_2] = "", [FieldKeys.ADJ_ADD_GENDER_2] = "", [FieldKeys.ADJ_ADD_ID_CARD_2] = "",
            [FieldKeys.ADJ_ADD_RELATION_2] = "", [FieldKeys.ADJ_ADD_HEALTH_2] = "", [FieldKeys.ADJ_ADD_WORKPLACE_2] = "",
            [FieldKeys.ADJ_ADD_REASON_2] = "", [FieldKeys.ADJ_ADD_INCOME_2] = "",
            [FieldKeys.ADJ_ADD_NAME_3] = "", [FieldKeys.ADJ_ADD_GENDER_3] = "", [FieldKeys.ADJ_ADD_ID_CARD_3] = "",
            [FieldKeys.ADJ_ADD_RELATION_3] = "", [FieldKeys.ADJ_ADD_HEALTH_3] = "", [FieldKeys.ADJ_ADD_WORKPLACE_3] = "",
            [FieldKeys.ADJ_ADD_REASON_3] = "", [FieldKeys.ADJ_ADD_INCOME_3] = "",
            [FieldKeys.ADJ_REMOVE_NAME_1] = "", [FieldKeys.ADJ_REMOVE_GENDER_1] = "", [FieldKeys.ADJ_REMOVE_ID_CARD_1] = "",
            [FieldKeys.ADJ_REMOVE_RELATION_1] = "", [FieldKeys.ADJ_REMOVE_HEALTH_1] = "", [FieldKeys.ADJ_REMOVE_WORKPLACE_1] = "",
            [FieldKeys.ADJ_REMOVE_REASON_1] = "", [FieldKeys.ADJ_REMOVE_INCOME_1] = "",
            [FieldKeys.ADJ_REMOVE_NAME_2] = "", [FieldKeys.ADJ_REMOVE_GENDER_2] = "", [FieldKeys.ADJ_REMOVE_ID_CARD_2] = "",
            [FieldKeys.ADJ_REMOVE_RELATION_2] = "", [FieldKeys.ADJ_REMOVE_HEALTH_2] = "", [FieldKeys.ADJ_REMOVE_WORKPLACE_2] = "",
            [FieldKeys.ADJ_REMOVE_REASON_2] = "", [FieldKeys.ADJ_REMOVE_INCOME_2] = "",
            [FieldKeys.ADJ_REMOVE_NAME_3] = "", [FieldKeys.ADJ_REMOVE_GENDER_3] = "", [FieldKeys.ADJ_REMOVE_ID_CARD_3] = "",
            [FieldKeys.ADJ_REMOVE_RELATION_3] = "", [FieldKeys.ADJ_REMOVE_HEALTH_3] = "", [FieldKeys.ADJ_REMOVE_WORKPLACE_3] = "",
            [FieldKeys.ADJ_REMOVE_REASON_3] = "", [FieldKeys.ADJ_REMOVE_INCOME_3] = "",
            [FieldKeys.ADJ_FAMILY_DETAIL] = "", [FieldKeys.ADJ_CHANGE_SUMMARY] = "",
            [FieldKeys.ADJ_AUDIT_TIME] = "", [FieldKeys.ADJ_INCREASE_AMOUNT] = "",
            [FieldKeys.ADJ_DECREASE_AMOUNT] = "", [FieldKeys.ADJ_EFFECTIVE_DATE] = "",
            // 增/减人数：无人员变动时也须有值，否则打印端记「未映射的字段」并输出 "-"
            [FieldKeys.ADJ_ADD_COUNT] = "0", [FieldKeys.ADJ_REMOVE_COUNT] = "0",

            // 模板16（经济财产声明书）需要的字段 — 从 economicDetail 填充
            [FieldKeys.DECLARATION_CLASSIFICATION] = BuildClassificationCheckbox(app.ClassificationResult ?? ""),
            [FieldKeys.LAND_STATUS] = BuildLandStatusCheckbox(app),
            [FieldKeys.LAND_AREA] = BuildLandAreaDetail(app),
            [FieldKeys.HAS_PENSION] = "否",
            [FieldKeys.PENSION_AMOUNT] = "",
            [FieldKeys.HAS_UNEMPLOYMENT] = "否",
            [FieldKeys.UNEMPLOYMENT_AMOUNT] = "",
            [FieldKeys.HAS_ODD_JOB] = "",
            [FieldKeys.ODD_JOB_AMOUNT] = "",
            [FieldKeys.HAS_BUSINESS] = economicDetail?.BusinessIncomes?.Count > 0 ? "是" : "否",
            [FieldKeys.BUSINESS_AMOUNT] = FormatDecimal(economicDetail?.BusinessIncomes?.Sum(b => b.AnnualIncome) ?? 0) + "元",
            [FieldKeys.OTHER_INCOME] = "",
            [FieldKeys.HAS_CASH] = economicDetail?.FinancialAssets?.Any(f => f.HasCash) == true ? "是" : "否",
            [FieldKeys.CASH_AMOUNT] = FormatDecimal(economicDetail?.FinancialAssets?.FirstOrDefault(f => f.HasCash)?.CashAmount ?? 0),
            [FieldKeys.HAS_BANK_DEPOSIT] = economicDetail?.FinancialAssets?.Any(f => f.HasBankDeposit) == true ? "是" : "否",
            [FieldKeys.BANK_DEPOSIT_AMOUNT] = FormatDecimal(economicDetail?.FinancialAssets?.FirstOrDefault(f => f.HasBankDeposit)?.BankDepositAmount ?? 0),
            [FieldKeys.BANK_DEPOSIT_NAME] = "",
            [FieldKeys.HAS_STOCK] = economicDetail?.FinancialAssets?.Any(f => f.HasSecurities) == true ? "是" : "否",
            [FieldKeys.STOCK_AMOUNT] = FormatDecimal(economicDetail?.FinancialAssets?.FirstOrDefault(f => f.HasSecurities)?.SecuritiesAmount ?? 0),
            [FieldKeys.STOCK_BANK_NAME] = "",
            [FieldKeys.HAS_HOUSE] = economicDetail?.FamilyProperties?.Count > 0 ? "是" : "否",
            [FieldKeys.LIVING_AREA] = FormatDecimal(economicDetail?.FamilyProperties?.Sum(p => p.Area) ?? 0),
            [FieldKeys.HAS_VEHICLE] = economicDetail?.Vehicles?.Count > 0 ? "是" : "否",
            [FieldKeys.LICENSE_PLATE] = string.Join("/", economicDetail?.Vehicles?.Where(v => !string.IsNullOrEmpty(v.LicensePlate)).Select(v => v.LicensePlate) ?? Array.Empty<string>()),
            [FieldKeys.HAS_INSURANCE] = economicDetail?.FinancialAssets?.Any(f => f.HasCommercialInsurance) == true ? "是" : "否",
            [FieldKeys.INSURANCE_AMOUNT] = FormatDecimal(economicDetail?.FinancialAssets?.FirstOrDefault(f => f.HasCommercialInsurance)?.CommercialInsuranceAmount ?? 0) + "元",

            // 模板17/18（土地证明）需要的字段
            [FieldKeys.SUBSIDY_TYPE_CONTENT] = BuildSubsidyTypeContent(app, economicDetail),
            [FieldKeys.LAND_DETAIL_INFO] = "",  // 模板17/18村级土地说明：延迟到家庭成员加载后填充（需本户成员名单匹配台账人员）

            // 模板19（赡养费承诺书）需要的字段 — 默认户主+第一赡养人，实际打印时按人迭代覆盖
            [FieldKeys.HEAD_INFO] = $"{app.ApplicantName} {AddressResolver.ExtractGenderFromIdCard(app.ApplicantIdCard)} {AddressResolver.ExtractAgeFromIdCard(app.ApplicantIdCard)}",
            [FieldKeys.SPOUSE_INFO] = "",  // 延迟到加载家庭成员后填充
            [FieldKeys.SUPPORTER_INFO] = "",
            [FieldKeys.SUPPORT_FEE] = "",
            [FieldKeys.SUPPORT_FEE_CHINESE] = "",
            [FieldKeys.AUDIT_DATE] = auditDate,

            // 模板23（特困入户调查表）需要的字段
            [FieldKeys.DESTITUTE_SUPPORT_TYPE] = BuildDestituteSupportTypeDisplay(app),
            [FieldKeys.DESTITUTE_CAREGIVER_SITUATION] = BuildDestituteCaregiverSituation(app, null),  // 延迟到加载照料人后填充
            [FieldKeys.DESTITUTE_HEALTH_ASSESSMENT] = BuildDestituteHealthAssessment(app, null),  // 延迟到加载家庭成员后填充
            [FieldKeys.DESTITUTE_LIVING_CONDITION] = BuildDestituteLivingCondition(app, economicDetail),
            [FieldKeys.DESTITUTE_DAILY_CARE] = BuildDestituteDailyCare(app, null),  // 延迟到加载照料人后填充
            [FieldKeys.DESTITUTE_MEDICAL_SITUATION] = BuildDestituteMedicalSituation(app),
            [FieldKeys.DESTITUTE_FAMILY_STATUS] = BuildDestituteFamilyStatus(app, null),  // 延迟到加载家庭成员后填充
            [FieldKeys.DESTITUTE_PROPERTY_SITUATION] = BuildDestitutePropertySituation(app, economicDetail),
            [FieldKeys.DESTITUTE_SURVEY_CONCLUSION] = BuildDestituteSurveyConclusion(app),
            [FieldKeys.DESTITUTE_INSTITUTION_INFO] = BuildDestituteInstitutionInfo(app),
            [FieldKeys.DESTITUTE_SELF_CARE_ABILITY] = BuildSelfCareAbility(app),
            [FieldKeys.DESTITUTE_CARE_LEVEL] = BuildCareLevel(app),

            // 特困审核确认表（模板75）专用字段
            [FieldKeys.DESTITUTE_AUDIT_CATEGORY] = BuildPersonCategory(app),
            [FieldKeys.DESTITUTE_AUDIT_CLASSIFICATION] = ClassificationConstants.ConvertToFullName(app.ClassificationResult ?? ""),
            [FieldKeys.DESTITUTE_DISABILITY] = BuildDisabilityDisplay(app),

            // 户口性质（特困审核确认表/分散供养协议共用）
            ["HUKOU_TYPE_DISPLAY"] = app.HukouType?.ToLowerInvariant() switch
            {
                "rural" => "农村户口",
                "urban" => "城市户口",
                _ => app.HukouType ?? ""
            },
            [FieldKeys.FAMILY_VILLAGE] = app.Community ?? "",
            [FieldKeys.COPYRIGHT_INFO] = CopyrightHelper.BuildCopyrightInfo(app.ApplicantIdCard),

            // 能力鉴定字段
            [FieldKeys.CAPABILITY_SELF_CARE_LEVEL] = "",  // 延迟到加载能力鉴定后填充
            [FieldKeys.CAPABILITY_COMPLETED_ITEMS] = "",  // 延迟到加载能力鉴定后填充
            [FieldKeys.CAPABILITY_ASSESSMENT_DATE] = "",  // 延迟到加载能力鉴定后填充
            [FieldKeys.CAPABILITY_ASSESSOR] = "",  // 延迟到加载能力鉴定后填充

            // 集中供养协议字段
            ["INSTITUTION_NAME"] = "",  // 延迟到加载机构后填充
            ["INSTITUTION_ADDRESS"] = "",  // 延迟到加载机构后填充
            ["INSTITUTION_PHONE"] = "",  // 延迟到加载机构后填充
            ["INSTITUTION_TOTAL_FEE"] = "",  // 延迟到加载机构后填充
            ["SUPPORT_START_DATE"] = DateTime.Now.ToString("yyyy-MM-dd"),
            ["SUPPORT_END_DATE"] = DateTime.Now.AddYears(1).ToString("yyyy-MM-dd"),

            // 照料服务协议字段
            ["CAREGIVER_NAME"] = "",  // 延迟到加载照料人后填充
            ["CAREGIVER_ID_CARD"] = "",  // 延迟到加载照料人后填充
            ["CAREGIVER_RELATIONSHIP"] = "",  // 延迟到加载照料人后填充
            ["CAREGIVER_PHONE"] = "",  // 延迟到加载照料人后填充
            ["CAREGIVER_GENDER"] = "",  // 延迟到加载照料人后填充（分散供养协议丙方性别）
            ["CAREGIVER_AGE"] = "",  // 延迟到加载照料人后填充（丙方年龄）
            ["CAREGIVER_ADDRESS"] = "",  // 延迟到加载照料人后填充（丙方/监护人家庭住址）
            ["CAREGIVER_WORK_UNIT"] = "",  // 延迟到加载照料人后填充（丙方单位名称）
            ["CARE_CONTENT"] = "日常生活照料",
            ["CARE_FREQUENCY"] = "每日",
            ["CAREGIVER_SUBSIDY"] = "",  // 延迟到计算后填充（照料补贴金额，带"元"）
            ["CAREGIVER_SUBSIDY_AMOUNT"] = "",  // 延迟到计算后填充（照料护理费纯数字）
            ["CAREGIVER_SUBSIDY_UPPER"] = "",  // 延迟到计算后填充（照料护理费中文大写）
            ["AGREEMENT_PERIOD"] = "1年",
            // 照料服务协议：无数据源字段留空（占位符替换为空，避免残留）
            ["OPERATOR_TITLE"] = "",
            ["OPERATOR_ID_CARD"] = "",
            ["CAREGIVER_BANK_NAME"] = "",
            ["CAREGIVER_BANK_ACCOUNT"] = "",
            ["CAREGIVER_ORG_LEGAL"] = "",
            ["CAREGIVER_ORG_ADDRESS"] = "",
            ["CAREGIVER_ORG_POSTAL"] = "",
            ["CAREGIVER_ORG_PHONE"] = "",

            // 档案封面专用字段（避免与模板16的CLASSIFICATION_RESULT冲突）
            [FieldKeys.COVER_CLASSIFICATION] = ClassificationConstants.ConvertToFullName(app.ClassificationResult ?? ""),
            // 定期复核审批表：档案分类 A类/B类（享受待遇4族=A，停保/其他=B）
            [FieldKeys.ARCHIVE_CLASS] = ClassificationConstants.ArchiveCategoryCodes.Contains(app.ClassificationResult ?? "") ? "A类" : "B类",
            [FieldKeys.ARCHIVE_NUMBER] = volumeDate,

            // 档案认定结果告知书专用字段（避免与模板16的CLASSIFICATION_RESULT冲突）
            [FieldKeys.NOTICE_CLASSIFICATION_RESULT] = ClassificationConstants.ConvertToFullName(app.ClassificationResult ?? ""),
        };

        // ── 林口县社会救助告知书（停保/变更告知）字段装配 ──
        // 不按 app.Status 设门槛：成员变更停旧建新后输出的是新档案（Draft），停保事实由变更记录承载；
        // 记录匹配（application_id OR new_application_id）同时兼容经济复核输出旧档案与成员变更输出新档案
        try
        {
            var stopRecordResult = await _changeService.GetLatestTriggeredStopRecordAsync(
                applicationId, ClassificationConstants.StopCategoryCodes, CancellationToken.None);
            var stopRecord = stopRecordResult.IsSuccess ? stopRecordResult.Value : null;

            if (stopRecord != null)
            {
                var newClassification = stopRecord.NewClassification;

                // 判断是"停止告知"还是"不予认定告知"
                // 停止告知：原先在低收入人口认定范围内（原分类为有效分类），因收入超标被停保
                // 不予认定告知：首次申请即被拒（原分类为 IncomeExceeded/Ineligible 等停保类）
                // 原分类取变更记录：成员变更输出的是停保类新档案，直接取 app 分类会误判为"不予认定"
                var originalClassification = string.IsNullOrEmpty(stopRecord.OldClassification)
                    ? (app.ClassificationResult ?? "")
                    : stopRecord.OldClassification;
                bool wasInScope = !ClassificationConstants.IsCodeStop(originalClassification);

                _fieldData[FieldKeys.NOTICE_BUSINESS_CATEGORY] = wasInScope ? "停止" : "不予认定";
                _fieldData[FieldKeys.NOTICE_TOWN] = app.Town ?? "";
                _fieldData[FieldKeys.NOTICE_VILLAGE] = app.Community ?? "";
                _fieldData[FieldKeys.NOTICE_FAMILY_SIZE_TEXT] = $"{app.FamilySize}人";

                // 承办分类：显示具体救助类型
                _fieldData[FieldKeys.NOTICE_HANDLING_CATEGORY] = ClassificationConstants.ConvertToFullName(originalClassification);

                _fieldData[FieldKeys.NOTICE_BENEFIT_TYPE] = wasInScope ? "停止享受" : "不予认定";
                _fieldData[FieldKeys.NOTICE_BENEFIT_RESULT] = "社会救助保障待遇";

                // 自动生成详细经济理由（H4：标准读配置，缺失显式失败，禁止 632/832 硬编码回退）
                _fieldData[FieldKeys.NOTICE_DETAILED_REASON] =
                    await BuildIncomeExceededReasonAsync(app, newClassification);
                _fieldData[FieldKeys.NOTICE_CUSTOM_REASON] = "";
                _fieldData[FieldKeys.NOTICE_DELIVERER] = App.CurrentUserFullName ?? "";
                _fieldData[FieldKeys.NOTICE_ORG_UNIT] = orgInfo.OperatorUnitName ?? "";
                _fieldData[FieldKeys.NOTICE_DATE] = DateTime.Now.ToString("yyyy年MM月dd日");

                _logger.LogBusiness("停保告知书字段已装配",
                    ("ApplicationId", applicationId.ToString()),
                    ("BusinessCategory", _fieldData[FieldKeys.NOTICE_BUSINESS_CATEGORY]),
                    ("OriginalClassification", originalClassification),
                    ("NewClassification", newClassification ?? "null"));
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"停保告知书字段装配失败: {ex.Message}");
        }

        // 一事一议申报表字段装配（申请通过一事一议时，供"档案_乡镇社会救助一事一议申报表"模板使用）
        if (app.IsSpecialApproval)
        {
            try
            {
                var specialApprovalFormService = _serviceProvider.GetRequiredService<ISpecialApprovalFormService>();
                var spForm = await specialApprovalFormService.GetByApplicationIdAsync(applicationId, CancellationToken.None);
                if (spForm != null && spForm.Id > 0)
                {
                    _fieldData[FieldKeys.SPECIAL_APPROVAL_BASIC_SITUATION] = spForm.BasicSituation;
                    _fieldData[FieldKeys.SPECIAL_APPROVAL_MATTERS] = spForm.SpecialMatters;
                    _fieldData[FieldKeys.SPECIAL_APPROVAL_AUDIT_RESULT] = spForm.AuditResult;
                    _fieldData[FieldKeys.SPECIAL_APPROVAL_AUDIT_DATE] = spForm.AuditDate?.ToString("yyyy-MM-dd") ?? "";
                    _fieldData[FieldKeys.APPLICANT_ID_CARD] = app.ApplicantIdCard;
                    _fieldData[FieldKeys.CONTACT_PHONE] = app.ApplicantPhone;
                    _fieldData[FieldKeys.HEAD_FAMILY_SIZE] = $"{app.FamilySize}口人";
                    _fieldData[FieldKeys.OPERATOR_UNIT] = spForm.ReportUnit;
                    _logger.LogBusiness("一事一议申报表字段已装配", ("ApplicationId", applicationId.ToString()));
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"一事一议申报表字段装配失败: {ex.Message}");
            }
        }

        // 近亲属备案字段装配（档案近亲属两表：工作人员关联信息表/救助对象信息表；按申请检索关联备案，每对一页）
        try
        {
            var nearRelativeService = _serviceProvider.GetRequiredService<INearRelativeService>();
            var pairsResult = await nearRelativeService.GetPairsByApplicationIdAsync(applicationId, CancellationToken.None);
            if (pairsResult.IsSuccess && pairsResult.Value != null && pairsResult.Value.Count > 0)
            {
                _fieldData[FieldKeys.NEAR_RELATIVE_HAS_DATA] = "1";
                _nearRelativePairs = pairsResult.Value
                    .Select(p => NearRelativePrintDataBuilder.BuildArchivePairFields(p, auditDate))
                    .ToList();
                _logger.LogBusiness("近亲属备案字段已装配", ("ApplicationId", applicationId.ToString()), ("Pairs", _nearRelativePairs.Count.ToString()));
            }
        }
        catch (Exception ex)
        {
            _logger.Warn($"近亲属备案字段装配失败: {ex.Message}");
        }

        // 从经济明细补充住房类别
        if (economicDetail?.FamilyProperties != null && economicDetail.FamilyProperties.Count > 0)
        {
            var housingNatures = economicDetail.FamilyProperties
                .Where(p => !string.IsNullOrEmpty(p.HousingNature))
                .Select(p => p.HousingNature)
                .Distinct()
                .ToList();
            if (housingNatures.Count > 0)
                _fieldData[FieldKeys.HOUSING_CATEGORY] = string.Join("/", housingNatures);
        }

        StatusText = "正在构建表格数据...";
        _logger.LogBusiness("开始构建表格数据", ("ApplicationId", applicationId.ToString()));
        // 构建 _tableData：户主始终在第 1 位
        var headEntry = BuildHeadMemberEntry(app.ApplicantName, app.ApplicantIdCard, fullAddress);
        _tableData = new List<Dictionary<string, string>> { headEntry };
        _logger.LogBusiness("户主条目构建完成");

        // 添加其他家庭成员（跳过户主）
        StatusText = "正在加载家庭成员...";
        _logger.LogBusiness("开始加载家庭成员", ("ApplicationId", applicationId.ToString()));
        using var memberCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var membersResult = await _familyMemberService.GetByApplicationIdAsync(applicationId, memberCts.Token);
        _logger.LogBusiness("家庭成员查询完成", ("IsSuccess", membersResult.IsSuccess.ToString()), ("Count", membersResult.Value?.Count.ToString() ?? "0"));
if (membersResult.IsSuccess && membersResult.Value != null)
        {
            _logger.Info($"家庭成员总数: {membersResult.Value.Count}");
            foreach (var m in membersResult.Value)
            {
                _logger.Info($"成员: Name={m.Name}, MemberCategory={m.MemberCategory}, IsHouseholdHead={m.IsHouseholdHead}, IdCard={m.IdCard}");
            }

            // 查询已死亡登记成员（历史数据未软删，生成档案时须统一过滤）
            var deathIdCards = new List<string>();
            var deathResult = await _familyMemberService.GetDeadIdCardsByApplicationIdAsync(applicationId, CancellationToken.None);
            if (deathResult.IsSuccess && deathResult.Value != null)
                deathIdCards = deathResult.Value;

            // 共同生活成员（索引字段，不含户主本人、不含已死亡成员）
            var sharedMembers = membersResult.Value
                .Where(m => m.MemberCategory == MemberCategoryConstants.SHARED_LIVING && !m.IsHouseholdHead && m.IdCard != app.ApplicantIdCard && !deathIdCards.Contains(m.IdCard ?? ""))
                .ToList();
            _logger.Info($"共同生活成员数: {sharedMembers.Count}");

            // 户主（申请人本人）始终作为家庭成员索引第 1 位（模板1授权承诺书/模板15/16/74）
            var headGender = AddressResolver.ExtractGenderFromIdCard(app.ApplicantIdCard);
            var headAge = AddressResolver.ExtractAgeFromIdCard(app.ApplicantIdCard);
            var headBirthDate = IdCardValidator.ExtractBirthDate(app.ApplicantIdCard)?.ToString("yyyy-MM-dd") ?? "";
            var headHealthDisplay = _dictCacheService.GetValue(DictionaryTypeCodes.HealthStatuses, app.HealthStatus ?? "");
            var headEmploymentDisplay = _dictCacheService.GetValue(DictionaryTypeCodes.EmploymentStatuses, app.EmploymentStatus ?? "");
            var headHukouDisplay = _dictCacheService.GetValue(DictionaryTypeCodes.HukouTypes, app.HukouType ?? "");
            var headIncomeAnnual = app.TotalAnnualIncome > 0 ? FormatDecimal(app.TotalAnnualIncome) : "";

            _fieldData[$"{FieldKeys.SHARED_MEMBER_NAME}_1"] = app.ApplicantName;
            _fieldData[$"{FieldKeys.SHARED_MEMBER_GENDER}_1"] = headGender;
            _fieldData[$"{FieldKeys.SHARED_MEMBER_AGE}_1"] = headAge;
            _fieldData[$"{FieldKeys.SHARED_MEMBER_RELATION}_1"] = "本人/户主";
            _fieldData[$"{FieldKeys.SHARED_MEMBER_EMPLOYMENT}_1"] = _dictCacheService.GetValue(DictionaryTypeCodes.EmploymentStatuses, app.EmploymentStatus ?? "");

            // 模板15（分类施保调整表）户主索引
            _fieldData["FAMILY_MEMBER_NAME_1"] = app.ApplicantName;
            _fieldData["FAMILY_MEMBER_GENDER_1"] = headGender;
            _fieldData["FAMILY_MEMBER_BIRTH_DATE_1"] = headBirthDate;
            _fieldData["FAMILY_MEMBER_RELATION_1"] = "本人/户主";
            _fieldData["FAMILY_MEMBER_HEALTH_1"] = headHealthDisplay;
            _fieldData["FAMILY_MEMBER_INCOME_1"] = headIncomeAnnual;

            // 模板74（低收入审核确认表）户主索引
            _fieldData[$"{FieldKeys.FAMILY_MEMBER_AGE}_1"] = headAge;
            _fieldData[$"{FieldKeys.FAMILY_MEMBER_HUKOU}_1"] = headHukouDisplay;
            _fieldData[$"{FieldKeys.FAMILY_MEMBER_EMPLOYMENT}_1"] = headEmploymentDisplay;

            // 模板1（授权承诺书）户主索引
            _fieldData["FAMILY_MEMBER_ID_CARD_1"] = app.ApplicantIdCard;
            _fieldData["FAMILY_MEMBER_CERT_TYPE_1"] = _dictCacheService.GetValue(DictionaryTypeCodes.IdTypes, "ResidentIdCard");

            // 模板16（经济财产声明书）户主索引
            _fieldData["SHARED_NAME_1"] = app.ApplicantName;
            _fieldData["SHARED_GENDER_1"] = headGender;
            _fieldData["SHARED_NATIONALITY_1"] = _dictCacheService.GetValue(DictionaryTypeCodes.Ethnicities, app.Ethnicity ?? "");
            _fieldData["SHARED_MARITAL_1"] = _dictCacheService.GetValue(DictionaryTypeCodes.MaritalStatuses, app.MaritalStatus ?? "");
            _fieldData["SHARED_HEALTH_1"] = headHealthDisplay;
            _fieldData["SHARED_ADDRESS_1"] = "-";
            _fieldData["SHARED_RELATION_1"] = "本人/户主";

            // 共同生活成员从第 2 位起填充（第 1 位已被户主占用，最多 5 人）
            for (int i = 0; i < sharedMembers.Count && i < 5; i++)
            {
                var m = sharedMembers[i];
                var suffix = i + 2;
                _fieldData[$"{FieldKeys.SHARED_MEMBER_NAME}_{suffix}"] = m.Name;
                _fieldData[$"{FieldKeys.SHARED_MEMBER_GENDER}_{suffix}"] = m.Gender ?? "";
                _fieldData[$"{FieldKeys.SHARED_MEMBER_AGE}_{suffix}"] = m.Age?.ToString() ?? "";
                _fieldData[$"{FieldKeys.SHARED_MEMBER_RELATION}_{suffix}"] = _dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, m.RelationshipToHead);
                _fieldData[$"{FieldKeys.SHARED_MEMBER_EMPLOYMENT}_{suffix}"] = _dictCacheService.GetValue(DictionaryTypeCodes.EmploymentStatuses, m.EmploymentStatus ?? "");

                // 模板15（分类施保调整表）家庭成员索引
                _fieldData[$"FAMILY_MEMBER_NAME_{suffix}"] = m.Name;
                _fieldData[$"FAMILY_MEMBER_GENDER_{suffix}"] = m.Gender ?? "";
                _fieldData[$"FAMILY_MEMBER_BIRTH_DATE_{suffix}"] = IdCardValidator.ExtractBirthDate(m.IdCard)?.ToString("yyyy-MM-dd") ?? "";
                _fieldData[$"FAMILY_MEMBER_RELATION_{suffix}"] = _dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, m.RelationshipToHead);
                _fieldData[$"FAMILY_MEMBER_HEALTH_{suffix}"] = _dictCacheService.GetValue(DictionaryTypeCodes.HealthStatuses, m.HealthStatus ?? "");
                _fieldData[$"FAMILY_MEMBER_INCOME_{suffix}"] = m.AnnualIncome > 0 ? FormatDecimal(m.AnnualIncome) : "";

                // 模板74（低收入审核确认表）成员索引
                _fieldData[$"{FieldKeys.FAMILY_MEMBER_AGE}_{suffix}"] = m.Age?.ToString() ?? "";
                _fieldData[$"{FieldKeys.FAMILY_MEMBER_HUKOU}_{suffix}"] = _dictCacheService.GetValue(DictionaryTypeCodes.HukouTypes, m.HukouType ?? "");
                _fieldData[$"{FieldKeys.FAMILY_MEMBER_EMPLOYMENT}_{suffix}"] = _dictCacheService.GetValue(DictionaryTypeCodes.EmploymentStatuses, m.EmploymentStatus ?? "");

                // 模板1（授权承诺书）家庭成员索引
                _fieldData[$"FAMILY_MEMBER_ID_CARD_{suffix}"] = m.IdCard ?? "";
                _fieldData[$"FAMILY_MEMBER_CERT_TYPE_{suffix}"] = _dictCacheService.GetValue(DictionaryTypeCodes.IdTypes, "ResidentIdCard");

                // 模板16（经济财产声明书）共同生活成员索引
                _fieldData[$"SHARED_NAME_{suffix}"] = m.Name;
                _fieldData[$"SHARED_GENDER_{suffix}"] = m.Gender ?? "";
                _fieldData[$"SHARED_NATIONALITY_{suffix}"] = _dictCacheService.GetValue(DictionaryTypeCodes.Ethnicities, m.Ethnicity ?? "");
                _fieldData[$"SHARED_MARITAL_{suffix}"] = _dictCacheService.GetValue(DictionaryTypeCodes.MaritalStatuses, m.MaritalStatus ?? "");
                _fieldData[$"SHARED_HEALTH_{suffix}"] = _dictCacheService.GetValue(DictionaryTypeCodes.HealthStatuses, m.HealthStatus ?? "");
                _fieldData[$"SHARED_ADDRESS_{suffix}"] = "-";
                _fieldData[$"SHARED_RELATION_{suffix}"] = _dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, m.RelationshipToHead);

// 同时添加到 _tableData
                _tableData.Add(BuildMemberEntry(m.Name, m.IdCard ?? "", m.RelationshipToHead, fullAddress, app.ApplicantIdCard));
            }

            // 模板15（分类施保调整表）家庭成员索引：不含户主本人（户主信息已在上方单独填写），从第 1 位起，最多 6 人
            for (int i = 0; i < sharedMembers.Count && i < 6; i++)
            {
                var m = sharedMembers[i];
                var csIdx = i + 1;
                _fieldData[$"CLASSIFIED_MEMBER_NAME_{csIdx}"] = m.Name;
                _fieldData[$"CLASSIFIED_MEMBER_GENDER_{csIdx}"] = m.Gender ?? "";
                _fieldData[$"CLASSIFIED_MEMBER_BIRTH_DATE_{csIdx}"] = IdCardValidator.ExtractBirthDate(m.IdCard)?.ToString("yyyy-MM-dd") ?? "";
                _fieldData[$"CLASSIFIED_MEMBER_RELATION_{csIdx}"] = _dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, m.RelationshipToHead);
                _fieldData[$"CLASSIFIED_MEMBER_HEALTH_{csIdx}"] = _dictCacheService.GetValue(DictionaryTypeCodes.HealthStatuses, m.HealthStatus ?? "");
                _fieldData[$"CLASSIFIED_MEMBER_INCOME_{csIdx}"] = m.AnnualIncome > 0 ? FormatDecimal(m.AnnualIncome) : "";
            }

            // 填充模板10的户主及家庭成员状况
            _fieldData["FAMILY_MEMBER_INFO"] = BuildFamilyMemberInfoText(app, membersResult.Value);

            // 模板17/18（村级土地说明）{土地详细信息}：延迟填充。
            // 登记人数/本户人数/总台账面积从确权台账实时汇总
            //（主表 total_confirmed_land_area / confirmed_person_count 为无人维护的死列，不再读取）；
            // 本户成员口径 = 户主 + 共同生活成员，与申请表单 CalculateLandConfirmationArea 一致。
            if (economicDetail?.LandConfirmationGroups is { Count: > 0 })
            {
                var landFamilyNames = new HashSet<string>(StringComparer.Ordinal);
                if (!string.IsNullOrWhiteSpace(app.ApplicantName))
                    landFamilyNames.Add(app.ApplicantName);
                foreach (var m in sharedMembers)
                {
                    if (!string.IsNullOrWhiteSpace(m.Name))
                        landFamilyNames.Add(m.Name.Trim());
                }
                _fieldData[FieldKeys.LAND_DETAIL_INFO] = BuildLandDetailInfo(app, economicDetail, landFamilyNames);
            }

            // 模板15：修正家庭人口 + 分类施保享受类型 + 分类施保共计人数 + 审核确认意见
            // 家庭人口 = 主表 family_size（与其他模板 HEAD_FAMILY_SIZE 口径一致；权威 ClassificationService 亦优先取主表值）
            _fieldData[FieldKeys.FAMILY_SIZE] = app.FamilySize.ToString();

            // 分类施保判定集合 = 户主（主表）+ 共同生活成员（sharedMembers 已排除赡养义务人/已死亡/非共同生活）
            _fieldData[FieldKeys.CLASSIFIED_SUBSIDY_TYPE] = BuildClassifiedSubsidyTypeDisplay(app, sharedMembers);
            var classifiedCount = BuildClassifiedCount(app, sharedMembers);
            _fieldData[FieldKeys.CLASSIFIED_TOTAL_COUNT] = classifiedCount.text;
            _fieldData[FieldKeys.AUDIT_OPINION] = BuildAuditOpinion(app, sharedMembers, orgInfo);
        }

        // 赡养人（索引字段）— 从家庭成员表中筛选 member_category=赡养抚养扶养
        StatusText = "正在加载赡养人...";
        _logger.LogBusiness("开始加载赡养人", ("ApplicationId", applicationId.ToString()));
        var supporters = membersResult.IsSuccess && membersResult.Value != null
            ? membersResult.Value.Where(m => m.MemberCategory == MemberCategoryConstants.SUPPORT).ToList()
            : new List<FamilyMember>();
        _logger.LogBusiness("赡养人加载完成", ("Count", supporters.Count.ToString()));

        for (int i = 0; i < supporters.Count && i < 6; i++)
        {
            var s = supporters[i];
            var suffix = i + 1;
            _fieldData[$"{FieldKeys.SUPPORTER_NAME}_{suffix}"] = s.Name;
            _fieldData[$"{FieldKeys.SUPPORTER_GENDER}_{suffix}"] = s.Gender ?? "";
            _fieldData[$"{FieldKeys.SUPPORTER_AGE}_{suffix}"] = s.Age?.ToString() ?? "";
            _fieldData[$"{FieldKeys.SUPPORTER_RELATION}_{suffix}"] = _dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, s.RelationshipToHead);
            _fieldData[$"{FieldKeys.SUPPORTER_AMOUNT}_{suffix}"] = FormatDecimal(s.AnnualSupportFee);
            _fieldData[$"{FieldKeys.SUPPORTER_ABILITY}_{suffix}"] = s.IsSupportAbility ? "有" : "无";
            _fieldData[$"{FieldKeys.SUPPORTER_WORK}_{suffix}"] = s.WorkUnit ?? "";
            _fieldData[$"{FieldKeys.SUPPORTER_INCOME_SOURCE}_{suffix}"] = _dictCacheService.GetValue(DictionaryTypeCodes.IncomeSources, s.MainIncomeSource ?? "");
            _fieldData[$"{FieldKeys.SUPPORTER_INCOME}_{suffix}"] = s.MonthlyIncomeCapacity > 0 ? FormatDecimal(s.MonthlyIncomeCapacity) : "";

            // 模板74（低收入审核确认表）赡养义务人索引
            _fieldData[$"{FieldKeys.SUPPORTER_FAMILY_SIZE}_{suffix}"] = s.FamilySize?.ToString() ?? "";
            _fieldData[$"{FieldKeys.SUPPORTER_OCCUPATION}_{suffix}"] = _dictCacheService.GetValue(DictionaryTypeCodes.EmploymentStatuses, s.EmploymentStatus ?? "");
            _fieldData[$"{FieldKeys.SUPPORTER_MONTHLY_INCOME}_{suffix}"] = s.MonthlyIncomeCapacity > 0 ? FormatDecimal(s.MonthlyIncomeCapacity) : "";

            // 模板16（经济财产声明书）赡扶抚人索引
            _fieldData[$"SUPPORTER_NAME_{suffix}"] = s.Name;
            _fieldData[$"SUPPORTER_GENDER_{suffix}"] = s.Gender ?? "";
            _fieldData[$"SUPPORTER_AGE_{suffix}"] = s.Age?.ToString() ?? "";
            _fieldData[$"SUPPORTER_RELATION_{suffix}"] = _dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, s.RelationshipToHead);
            _fieldData[$"SUPPORTER_NATIONALITY_{suffix}"] = _dictCacheService.GetValue(DictionaryTypeCodes.Ethnicities, s.Ethnicity ?? "");
            _fieldData[$"SUPPORTER_MARITAL_{suffix}"] = _dictCacheService.GetValue(DictionaryTypeCodes.MaritalStatuses, s.MaritalStatus ?? "");
            _fieldData[$"SUPPORTER_HEALTH_{suffix}"] = _dictCacheService.GetValue(DictionaryTypeCodes.HealthStatuses, s.HealthStatus ?? "");
            _fieldData[$"SUPPORTER_OCCUPATION_{suffix}"] = _dictCacheService.GetValue(DictionaryTypeCodes.EmploymentStatuses, s.EmploymentStatus ?? "");
            _fieldData[$"SUPPORTER_WORKPLACE_{suffix}"] = s.WorkUnit ?? "";
            _fieldData[$"SUPPORTER_WORK_{suffix}"] = s.WorkUnit ?? "";
            _fieldData[$"SUPPORTER_INCOME_SOURCE_{suffix}"] = _dictCacheService.GetValue(DictionaryTypeCodes.IncomeSources, s.MainIncomeSource ?? "");
            _fieldData[$"SUPPORTER_INCOME_{suffix}"] = s.MonthlyIncomeCapacity > 0 ? FormatDecimal(s.MonthlyIncomeCapacity) : "";
            _fieldData[$"SUPPORTER_ID_CARD_{suffix}"] = s.IdCard ?? "";
            _fieldData[$"SUPPORTER_PHONE_{suffix}"] = s.Phone ?? "";
            _fieldData[$"SUPPORTER_AMOUNT_{suffix}"] = FormatDecimal(s.AnnualSupportFee);
        }

        // 模板74（低收入审核确认表）赡养义务人合计：只统计赡养抚养扶养义务人
        // 使用专用键，避免覆盖模板15 的家庭人口 FAMILY_SIZE（原实现误用 FAMILY_SIZE 导致模板15 家庭人口恒为赡养义务人合计）
        var supporterTotalSize = supporters.Sum(s => s.FamilySize ?? 0);
        var supporterTotalIncome = supporters.Sum(s => s.MonthlyIncomeCapacity);
        _fieldData[FieldKeys.SUPPORTER_TOTAL_FAMILY_SIZE] = supporterTotalSize.ToString();
        _fieldData[FieldKeys.SUPPORTER_TOTAL_INCOME_MONTHLY] = FormatDecimal(supporterTotalIncome);
        _fieldData[FieldKeys.SUPPORTER_PER_CAPITA_INCOME_MONTHLY] = supporterTotalSize > 0
            ? FormatDecimal(supporterTotalIncome / supporterTotalSize)
            : "0.00";
        _logger.LogBusiness("模板74赡养义务人合计",
            ("TotalSize", supporterTotalSize.ToString()),
            ("TotalIncome", supporterTotalIncome.ToString()),
            ("PerCapita", _fieldData[FieldKeys.SUPPORTER_PER_CAPITA_INCOME_MONTHLY]));

        // 加载照料人 + 能力鉴定（特困分散供养协议/照料服务协议/特困审核确认表/入户调查表用）
        StatusText = "正在加载照料人及能力鉴定...";
        List<Caregiver>? caregivers = null;
        using var careCts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var caregiversResult = await _caregiverService.GetByApplicationIdAsync(applicationId, careCts.Token);
        if (caregiversResult.IsSuccess && caregiversResult.Value != null)
        {
            caregivers = caregiversResult.Value;
            _cachedCaregivers = caregivers;
        }

        CapabilityAssessment? capability = null;
        var capResult = await _capabilityAssessmentService.GetByApplicationIdAsync(applicationId, careCts.Token);
        if (capResult.IsSuccess && capResult.Value != null)
        {
            capability = capResult.Value;
            _cachedCapabilityAssessment = capability;
        }

        // 特困入户调查表：照料情况/健康评估/日常照料/家庭状况（此前传 null 导致永远走默认文案）
        _fieldData[FieldKeys.DESTITUTE_CAREGIVER_SITUATION] = BuildDestituteCaregiverSituation(app, caregivers);
        _fieldData[FieldKeys.DESTITUTE_DAILY_CARE] = BuildDestituteDailyCare(app, caregivers);
        _fieldData[FieldKeys.DESTITUTE_HEALTH_ASSESSMENT] = BuildDestituteHealthAssessment(app, membersResult.Value);
        _fieldData[FieldKeys.DESTITUTE_FAMILY_STATUS] = BuildDestituteFamilyStatus(app, membersResult.Value);

        // 自理能力三档（优先使用能力鉴定结论）
        _fieldData[FieldKeys.DESTITUTE_SELF_CARE_ABILITY] = BuildSelfCareAbility(app, capability);
        _fieldData[FieldKeys.DESTITUTE_CARE_LEVEL] = BuildCareLevel(app, capability);

        // 能力鉴定字段（特困审核确认表"生活自理情况"）
        _fieldData[FieldKeys.CAPABILITY_SELF_CARE_LEVEL] = BuildSelfCareAbility(app, capability);
        _fieldData[FieldKeys.CAPABILITY_COMPLETED_ITEMS] = capability != null ? capability.CompletedItems.ToString() : "";
        _fieldData[FieldKeys.CAPABILITY_ASSESSMENT_DATE] = capability != null ? capability.AssessmentDate.ToString("yyyy-MM-dd") : "";
        _fieldData[FieldKeys.CAPABILITY_ASSESSOR] = capability?.AssessorName ?? "";

        // 照料服务协议（模板77）：取第一位照料人
        if (caregivers != null && caregivers.Count > 0)
        {
            var cg = caregivers[0];
            _fieldData["CAREGIVER_NAME"] = cg.Name ?? "";
            _fieldData["CAREGIVER_ID_CARD"] = cg.IdCard ?? "";
            _fieldData["CAREGIVER_RELATIONSHIP"] = _dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, cg.Relationship ?? "");
            _fieldData["CAREGIVER_PHONE"] = cg.Phone ?? "";
            _fieldData["CAREGIVER_GENDER"] = _dictCacheService.GetValue(DictionaryTypeCodes.Gender, cg.Gender ?? "") ?? cg.Gender ?? "";
            _fieldData["CAREGIVER_AGE"] = cg.Age > 0 ? cg.Age.ToString() : "";
            _fieldData["CAREGIVER_ADDRESS"] = cg.Address ?? "";
            _fieldData["CAREGIVER_WORK_UNIT"] = cg.WorkUnit ?? "";
            _fieldData["CAREGIVER_SUBSIDY"] = app.CaregiverSubsidyAmount > 0 ? $"{FormatDecimal(app.CaregiverSubsidyAmount)}元" : "";
            _fieldData["CAREGIVER_SUBSIDY_AMOUNT"] = app.CaregiverSubsidyAmount > 0 ? FormatDecimal(app.CaregiverSubsidyAmount) : "";
            _fieldData["CAREGIVER_SUBSIDY_UPPER"] = app.CaregiverSubsidyAmount > 0 ? ConvertToChineseAmount(app.CaregiverSubsidyAmount) : "";
        }
        _fieldData["APPLICANT_PHONE"] = app.ApplicantPhone ?? "";

        // 声明类模板专用字段：配偶信息、户主所在市、当前日期
        _fieldData[FieldKeys.SPOUSE_NAME] = "";
        _fieldData[FieldKeys.SPOUSE_GENDER] = "";
        _fieldData[FieldKeys.SPOUSE_ID_CARD] = "";
        _fieldData[FieldKeys.HEAD_CITY] = app.City ?? "";
        _fieldData[FieldKeys.CURRENT_DATE] = DateTime.Now.ToString("yyyy年M月d日");
        if (membersResult.IsSuccess && membersResult.Value != null)
        {
            var spouse = membersResult.Value.FirstOrDefault(m =>
                m.RelationshipToHead == "Spouse" && !m.IsHouseholdHead);
            if (spouse != null)
            {
                _fieldData[FieldKeys.SPOUSE_NAME] = spouse.Name ?? "";
                _fieldData[FieldKeys.SPOUSE_GENDER] = AddressResolver.ExtractGenderFromIdCard(spouse.IdCard);
                _fieldData[FieldKeys.SPOUSE_ID_CARD] = spouse.IdCard ?? "";
            }
        }

        // 构建赡养人表格数据（用于赡养费承诺书模板按人迭代）
        _supporterTableData.Clear();
        if (supporters.Count > 0)
        {
            // 查找配偶信息
            string spouseInfo = "";
            if (_cachedFamilyMembers != null)
            {
                var spouse = _cachedFamilyMembers.FirstOrDefault(m =>
                    m.RelationshipToHead == "Spouse" && !m.IsHouseholdHead);
                if (spouse != null)
                {
                    var spouseGender = AddressResolver.ExtractGenderFromIdCard(spouse.IdCard);
                    var spouseAge = AddressResolver.ExtractAgeFromIdCard(spouse.IdCard);
                    var spouseRel = _dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, spouse.RelationshipToHead ?? "");
                    spouseInfo = $"{spouse.Name} {spouseGender} {spouseAge}岁 {spouseRel}";
                }
            }

            foreach (var s in supporters)
            {
                var supporterRel = _dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, s.RelationshipToHead);
                var supporterInfo = $"{s.Name} {s.Gender ?? ""} {(s.Age ?? 0)}岁 {supporterRel}";

                _supporterTableData.Add(new Dictionary<string, string>
                {
                    ["SUPPORTER_NAME"] = s.Name,
                    ["SUPPORTER_GENDER"] = s.Gender ?? "",
                    ["SUPPORTER_AGE"] = s.Age?.ToString() ?? "",
                    ["SUPPORTER_RELATION"] = supporterRel,
                    ["SUPPORTER_INFO"] = supporterInfo,
                    ["SPOUSE_INFO"] = spouseInfo,
                    ["SUPPORT_FEE"] = s.MonthlySupportFee > 0 ? FormatDecimal(s.MonthlySupportFee) : "",
                    ["SUPPORT_FEE_CHINESE"] = s.MonthlySupportFee > 0 ? ConvertToChineseAmount(s.MonthlySupportFee) : "",
                    ["ANNUAL_SUPPORT_FEE"] = s.AnnualSupportFee > 0 ? FormatDecimal(s.AnnualSupportFee) : "",
                });
            }
        }

        // 加载经济明细数据（务工详情、财产详情等）
        StatusText = "正在加载经济明细...";
        _logger.LogBusiness("开始加载经济明细", ("ApplicationId", applicationId.ToString()));
        var economicDetailService = _serviceProvider.GetRequiredService<IEconomicDetailService>();
        using var economicCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        // 用 LongRunning 强制创建专用线程，避免被 MAUI SynchronizationContext 干扰
        var economicResult = await Task.Factory.StartNew(
            async () => await economicDetailService.LoadAllAsync(applicationId, economicCts.Token),
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
        _logger.LogBusiness("经济明细查询完成", ("IsSuccess", economicResult.IsSuccess.ToString()));

        if (economicResult.IsSuccess && economicResult.Value != null)
        {
            var economic = economicResult.Value;
            _logger.LogBusiness("经济明细数据",
                ("LaborIncomes", (economic.LaborIncomes?.Count ?? 0).ToString()),
                ("BreedingIncomes", (economic.BreedingIncomes?.Count ?? 0).ToString()),
                ("FamilyProperties", (economic.FamilyProperties?.Count ?? 0).ToString()),
                ("Vehicles", (economic.Vehicles?.Count ?? 0).ToString()),
                ("FinancialAssets", (economic.FinancialAssets?.Count ?? 0).ToString()));

            // 养殖业收入
            var breedingIncome = economic.BreedingIncomes?.Sum(x => x.AnnualIncome) ?? 0m;
            _fieldData[FieldKeys.INCOME_BREEDING] = FormatDecimal(breedingIncome) + "元";

            // 务工收入详情（索引字段，最多2条）
            if (economic.LaborIncomes != null)
            {
                for (int i = 0; i < economic.LaborIncomes.Count && i < 2; i++)
                {
                    var labor = economic.LaborIncomes[i];
                    var suffix = i + 1;
                    _fieldData[$"{FieldKeys.LABOR_LOCATION}_{suffix}"] = labor.WorkUnit ?? "";
                    _fieldData[$"{FieldKeys.LABOR_TYPE}_{suffix}"] = labor.IncomeSubType ?? "";
                }
            }

            // 房产信息
            if (economic.FamilyProperties != null && economic.FamilyProperties.Count > 0)
            {
                var property = economic.FamilyProperties[0];
                _fieldData[FieldKeys.PROPERTY_RENTAL] = property.HousingNature?.Contains("租赁") == true ? "是" : "否";
                _fieldData[FieldKeys.PROPERTY_BUILDING_AREA] = FormatDecimal(property.Area);
                _fieldData[FieldKeys.PROPERTY_PRIVATE] = property.HousingNature?.Contains("自有") == true ? "是" : "否";
                _fieldData[FieldKeys.PROPERTY_STRUCTURE] = property.HousingStructure ?? "";
            }
            else
            {
                _fieldData[FieldKeys.PROPERTY_RENTAL] = "否";
                _fieldData[FieldKeys.PROPERTY_BUILDING_AREA] = "0";
                _fieldData[FieldKeys.PROPERTY_PRIVATE] = "否";
                _fieldData[FieldKeys.PROPERTY_STRUCTURE] = "";
            }

            // 车辆信息（使用格式化描述，不覆盖模板8的描述）
            if (economic.Vehicles != null && economic.Vehicles.Count > 0)
            {
                // PROPERTY_VEHICLE 已在字典初始化时由 BuildVehicleDesc 设置，此处不再覆盖
                // 仅更新其他车辆相关字段
            }
            else
            {
                _fieldData[FieldKeys.PROPERTY_VEHICLE] = "";
            }

            // 金融资产
            if (economic.FinancialAssets != null && economic.FinancialAssets.Count > 0)
            {
                var financial = economic.FinancialAssets[0];
                _fieldData[FieldKeys.PROPERTY_DEPOSIT] = FormatDecimal(financial.BankDepositAmount);
                _fieldData[FieldKeys.PROPERTY_SECURITY] = FormatDecimal(financial.SecuritiesAmount);
                _fieldData[FieldKeys.PROPERTY_FUND] = "0";
                _fieldData[FieldKeys.PROPERTY_INSURANCE] = FormatDecimal(financial.CommercialInsuranceAmount);
                _fieldData[FieldKeys.PROPERTY_BUSINESS_REG] = "0";
                _fieldData[FieldKeys.PROPERTY_BOND] = "0";
                _fieldData[FieldKeys.PROPERTY_OTHER] = "0";
            }
            else
            {
                _fieldData[FieldKeys.PROPERTY_DEPOSIT] = "0";
                _fieldData[FieldKeys.PROPERTY_SECURITY] = "0";
                _fieldData[FieldKeys.PROPERTY_FUND] = "0";
                _fieldData[FieldKeys.PROPERTY_INSURANCE] = "0";
                _fieldData[FieldKeys.PROPERTY_BUSINESS_REG] = "0";
                _fieldData[FieldKeys.PROPERTY_BOND] = "0";
                _fieldData[FieldKeys.PROPERTY_OTHER] = "0";
            }

            // 土地确权信息
            if (economic.LandConfirmationGroups != null && economic.LandConfirmationGroups.Count > 0)
            {
                var totalConfirmedArea = economic.LandConfirmationGroups.Sum(g => g.TotalArea);
                _fieldData[FieldKeys.TOTAL_CONFIRMED_LAND_AREA] = FormatDecimal((decimal)totalConfirmedArea);
            }

            // 档案_定期复核审批表：副业种类/数量（经营类明细汇总）+ 财产性收入明细叙述
            var businessTypes = economic.BusinessIncomes?
                .Select(x => x.VendorType)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct()
                .ToList() ?? new List<string>();
            _fieldData[FieldKeys.SIDEBUSINESS_TYPE] = string.Join("、", businessTypes);
            _fieldData[FieldKeys.SIDEBUSINESS_COUNT] = (economic.BusinessIncomes?.Count ?? 0).ToString();
            _fieldData[FieldKeys.INCOME_PROPERTY_DESC] = economic.PropertyIncomes is { Count: > 0 }
                ? string.Join("；", economic.PropertyIncomes.Select(p =>
                    $"{(string.IsNullOrWhiteSpace(p.IncomeType) ? "财产性" : p.IncomeType)}"
                    + $"{(string.IsNullOrWhiteSpace(p.PropertyDescription) ? "" : $"({p.PropertyDescription})")}"
                    + $"{FormatDecimal(p.Amount)}元"))
                : "";
        }

        // 收入状况（以 Application 表汇总字段为准，明细表仅用于详情补充）
        // 【年值基准】月值项（务工/经营/财产/转移/赡养/其他/刚性）主表存月，农村打印×12 年化；
        // 年值项（赡养/土地/补贴）主表存年，直接显示（城市打印时赡养÷12 转月）。
        StatusText = "正在计算收入数据...";
        _logger.LogBusiness("开始计算收入数据", ("HukouType", app.HukouType ?? ""));
        var isRural = app.HukouType != "Urban";
        var monthToDisplay = isRural ? 12m : 1m;

        // 档案_定期复核审批表收入行：标签在单元格文字里已含"收入"，值格统一补"元"单位
        _fieldData[FieldKeys.INCOME_LABOR] = FormatDecimal(app.WorkIncomeTotal * monthToDisplay) + "元";
        _fieldData[FieldKeys.INCOME_BUSINESS] = FormatDecimal(app.BusinessIncomeTotal * monthToDisplay) + "元";
        _fieldData[FieldKeys.INCOME_TRANSFER] = FormatDecimal(app.TransferIncomeTotal * monthToDisplay);
        // 财产性收入 = 月财产性收入(×12) + 年土地收入 + 年补贴
        _fieldData[FieldKeys.INCOME_PROPERTY] = FormatDecimal(
            app.PropertyIncomeTotal * monthToDisplay
            + app.LandIncomeTotal + app.SubsidyTotal) + "元";
        _fieldData[FieldKeys.INCOME_ALIMONY] = FormatDecimal(
            isRural ? app.AlimonyIncome : Math.Round(app.AlimonyIncome / 12m, 2)) + "元";
        _fieldData[FieldKeys.INCOME_OTHER] = FormatDecimal(app.OtherIncomeTotal * monthToDisplay) + "元";
        _fieldData[FieldKeys.INCOME_SUBSIDY] = FormatDecimal(app.SubsidyTotal);  // 年收入直接显示
        _fieldData[FieldKeys.INCOME_LAND] = FormatDecimal(app.LandIncomeTotal);  // 年收入直接显示

        // 总收入：农村打印年值权威口径（TotalAnnualIncome），城市打印月值（年÷12）
        var annualTotal = app.TotalAnnualIncome;
        _fieldData[FieldKeys.INCOME_TOTAL] = FormatDecimal(
            isRural ? annualTotal : Math.Round(annualTotal / 12m, 2));
        _fieldData[FieldKeys.INCOME_RIGID_EXPENDITURE] = BuildRigidExpenditureDisplay(app, economicDetail);

        _logger.LogBusiness("收入数据计算完成",
            ("WorkIncome", app.WorkIncomeTotal.ToString()),
            ("BusinessIncome", app.BusinessIncomeTotal.ToString()),
            ("TotalAnnualIncome", annualTotal.ToString()));

        // 土地面积信息（从申请表获取）
        StatusText = "正在处理土地信息...";
        _fieldData[FieldKeys.FAMILY_LAND_AREA] = app.FamilyLandArea > 0 ? FormatDecimal(app.FamilyLandArea) : "0";
        _fieldData[FieldKeys.SELF_FARMED_LAND_AREA] = app.SelfFarmedLandArea > 0 ? FormatDecimal(app.SelfFarmedLandArea) : "0";
        _fieldData[FieldKeys.SUBLEASED_LAND_AREA] = app.SubleasedLandArea > 0 ? FormatDecimal(app.SubleasedLandArea) : "0";
        _fieldData[FieldKeys.CONTRACTED_LAND_AREA] = app.ContractedLandArea > 0 ? FormatDecimal(app.ContractedLandArea) : "0";
        _fieldData[FieldKeys.LAND_INCOME_TOTAL] = app.LandIncomeTotal > 0 ? FormatDecimal(app.LandIncomeTotal) : "0";

        // 档案_定期复核审批表：刚性支出+财产豁免（两段拼接）、复核组（最近一条复核/变更记录，含死亡与成员变更流程）
        _fieldData[FieldKeys.RIGID_AND_EXEMPTION] =
            $"{_fieldData.GetValueOrDefault(FieldKeys.RIGID_EXPENDITURE_DETAIL, "")}；{_fieldData.GetValueOrDefault(FieldKeys.PROPERTY_EXEMPTION_SITUATION, "")}";

        // 复核组：{定期复核情况}/{复核时间}/{待遇变化分类}/{待遇变化金额}（按流程分支，见 ApplyReviewGroupAsync）
        await ApplyReviewGroupAsync(app, applicationId);

        StatusText = "正在构建侧边栏...";
        _logger.LogBusiness("开始构建侧边栏");
        await BuildSidebarFromApplicationAsync(app, civilAssistantName, orgInfo, app.TotalFamilyIncome, app.WorkIncomeTotal, app.BusinessIncomeTotal, app.PropertyIncomeTotal, app.TransferIncomeTotal, app.AlimonyIncome, app.OtherIncomeTotal, app.RigidExpenditure);
        _logger.LogBusiness("侧边栏构建完成");

        _logger.LogBusiness("LoadApplicationDataAsync 完成", ("ApplicationId", applicationId.ToString()), ("FieldCount", _fieldData.Count.ToString()));
        return true;
    }

    // ---- 分步加载方法（用于进度弹窗） ----

    private async Task<bool> LoadApplicationBasicInfoAsync(long applicationId)
    {
        var result = await _applicationService.GetByIdAsync(applicationId);
        if (result.IsFailure || result.Value == null) return false;

        _cachedApplication = result.Value;

        var orgInfo = await GetOrganizationInfoAsync();
        _currentBusinessType = "FamilyApplication";
        _currentBusinessId = applicationId;
        _currentClassification = _cachedApplication.ClassificationResult ?? "";
        _currentStatus = _cachedApplication.Status ?? "";

        BusinessTypeDisplay = "社会救助申请";
        DataSourceType = "社会救助申请";
        ApplicantName = _cachedApplication.ApplicantName;
        ApplicantIdCard = DataMasker.MaskIdCard(_cachedApplication.ApplicantIdCard);
        FamilyInfo = $"家庭成员 {_cachedApplication.FamilySize} 人";
        HasBusinessData = true;
        IsReady = true;
        StatusText = $"已加载 {_cachedApplication.ApplicantName}";

        return true;
    }

    private async Task LoadFamilyMembersDataAsync(long applicationId)
    {
        _cachedFamilyMembers = (await _familyMemberService.GetByApplicationIdAsync(applicationId, CancellationToken.None)).Value ?? new();
    }

    private async Task LoadSupportersDataAsync(long applicationId)
    {
        _cachedSupporters = (await _supporterService.GetByApplicationIdAsync(applicationId, CancellationToken.None)).Value ?? new();
    }

    private async Task LoadCaregiversDataAsync(long applicationId)
    {
        _cachedCaregivers = (await _caregiverService.GetByApplicationIdAsync(applicationId, CancellationToken.None)).Value ?? new();
    }

    private async Task LoadHouseholdSurveyDataAsync(long applicationId)
    {
        _cachedHouseholdSurvey = (await _householdSurveyService.GetByApplicationIdAsync(applicationId, CancellationToken.None)).Value;
    }

    private async Task LoadCapabilityAssessmentDataAsync(long applicationId)
    {
        var result = await _capabilityAssessmentService.GetByApplicationIdAsync(applicationId, CancellationToken.None);
        if (result.IsSuccess) _cachedCapabilityAssessment = result.Value;
    }

    private async Task LoadEconomicDetailsDataAsync(long applicationId)
    {
        _cachedEconomicDetails = (await _economicDetailService.LoadAllAsync(applicationId, CancellationToken.None)).Value;
    }

    private async Task BuildFieldDataFromCacheAsync()
    {
        if (_cachedApplication == null) return;

        var app = _cachedApplication;

        var civilAssistantName = await GetCivilAssistantNameAsync();
        var orgInfo = await GetOrganizationInfoAsync();

        var fullAddress = await _addressResolver.BuildAddressAsync(app.Community, app.Address, CancellationToken);

        var landIncomeSituation = await BuildLandIncomeSituationAsync(app);

        // 预加载经济明细
        Services.Domain.SocialAssistance.EconomicDetailData? economicDetail = null;
        try
        {
            var econService = _serviceProvider.GetRequiredService<IEconomicDetailService>();
            var detailResult = await econService.LoadAllAsync(app.Id, CancellationToken.None);
            if (detailResult.IsSuccess) economicDetail = detailResult.Value;
        }
        catch { }

        // B线时间轴：公示起止/审核确认日/卷号/入户调查日期（统一走 BusinessTimelineService）
        // 锚定该档案自身业务日期，保证补打时编号/卷号/日期稳定，不随当天漂移
        var archiveAnchor = ResolveArchiveAnchorDate(app);
        var tl = _timelineService.GetTimelineForDate(archiveAnchor, TimelineType.BusinessProcess);
        var publicityStart = tl.PublicityStartDate.ToString("yyyy-MM-dd");
        var publicityEnd = tl.PublicityEndDate.ToString("yyyy-MM-dd");
        var auditDate = tl.AuditDate.ToString("yyyy-MM-dd");
        var volumeDate = new DateTime(tl.CycleEndDate.Year, tl.CycleEndDate.Month, 1).AddMonths(1).ToString("yyyy-M-d");
        var surveyDate = ResolveSurveyDate(tl, _cachedHouseholdSurvey?.SurveyDate);
        var reviewEffective = new DateTime(tl.CycleEndDate.Year, tl.CycleEndDate.Month, 1).AddMonths(1).ToString("M月d日");
        var hukouAddress = await ResolveHukouAddressAsync(app);

        _fieldData = new Dictionary<string, string>
        {
            [FieldKeys.APPLICANT_NAME] = app.ApplicantName,
            [FieldKeys.APPLICANT_ID_CARD] = app.ApplicantIdCard,
            [FieldKeys.APPLICANT_ID_TYPE] = "居民身份证",
            [FieldKeys.FAMILY_ADDRESS] = fullAddress,
            [FieldKeys.COMMUNITY] = app.Community ?? "",
            [FieldKeys.APPLICATION_REASON] = app.ApplicationReason ?? "",
            [FieldKeys.APPLICATION_DATE] = ResolveApplicationDate(app).ToString("yyyy-MM-dd"),
            [FieldKeys.CONTACT_PHONE] = app.ApplicantPhone ?? "",
            [FieldKeys.STATUS] = "草稿",
            [FieldKeys.OPERATOR_NAME] = App.CurrentUserFullName,
            [FieldKeys.OPERATOR_UNIT] = orgInfo.OperatorUnitName,
            [FieldKeys.APPLICANT_COUNT] = app.FamilySize.ToString(),
            [FieldKeys.HEAD_FAMILY_SIZE] = $"{app.FamilySize}口人",
            [FieldKeys.REPORT_DATE] = DateTime.Now.ToString("yyyy-MM-dd"),
            [FieldKeys.CIVIL_ASSISTANT_NAME] = civilAssistantName,
            [FieldKeys.DISTRICT] = orgInfo.District,
            [FieldKeys.TOWN] = orgInfo.Town,
            [FieldKeys.HEAD_ID_CARD] = app.ApplicantIdCard,
            [FieldKeys.CLASSIFICATION_RESULT] = ClassificationConstants.ConvertToFullName(app.ClassificationResult ?? ""),
            [FieldKeys.HUKOU_ADDRESS] = hukouAddress,
            [FieldKeys.SURVEY_DATE] = surveyDate,
            [FieldKeys.SURVEY_TIME] = DateTime.Now.ToString("yyyy年M月d日 HH:mm"),
            [FieldKeys.APPLICATION_REASON_DETAIL] = "-",  // 模板10其它困难原因：需求要求固定留空为"-"
            [FieldKeys.TOTAL_FAMILY_INCOME_ANNUAL] = FormatDecimal(app.TotalAnnualIncome),
            [FieldKeys.PER_CAPITA_INCOME_ANNUAL] = FormatDecimal(app.PerCapitaAnnualIncome),
            [FieldKeys.TOTAL_FAMILY_INCOME_MONTHLY] = FormatDecimal(app.TotalFamilyIncome),
            [FieldKeys.PER_CAPITA_INCOME_MONTHLY] = FormatDecimal(app.PerCapitaIncome),
            [FieldKeys.RIGID_EXPENDITURE_DETAIL] = BuildRigidExpenditureDisplay(app, economicDetail),
            [FieldKeys.LAND_INCOME_SITUATION] = landIncomeSituation,
            [FieldKeys.SINGLE_RESCUE_SITUATION] = BuildSingleRescueSituation(app),
            [FieldKeys.PROPERTY_EXEMPTION_SITUATION] = BuildPropertyExemptionSituation(app, economicDetail),
            [FieldKeys.KINSHIP_FILING_SITUATION] = BuildKinshipFilingSituation(),
            [FieldKeys.SURVEY_VISIT_SITUATION] = BuildSurveyVisitSituation(app),
            // 模板8（邻里访问调查表）需要的字段
            ["INCOME_SOURCE"] = BuildIncomeSourceSummary(app, economicDetail),
            ["INCOME_BREEDING_DESC"] = BuildBreedingIncomeDesc(economicDetail),
            ["INCOME_BUSINESS_DESC"] = BuildBusinessIncomeDesc(economicDetail),
            ["INCOME_LABOR_DESC"] = BuildLaborIncomeDesc(economicDetail),
            ["PROPERTY_FARM_EQUIPMENT"] = BuildFarmEquipmentDesc(economicDetail),
            ["PROPERTY_VEHICLE"] = BuildVehicleDesc(economicDetail),
            ["PROPERTY_HOUSE_DESC"] = BuildHouseDesc(economicDetail),
            ["APPLICANT_LIFE_SITUATION"] = "",  // 延迟到加载经济明细/入户调查后填充
            // 模板8（邻里访问调查表）专用：简称，避免与模板16的CLASSIFICATION_RESULT冲突
            [FieldKeys.SURVEY_CLASSIFICATION] = ClassificationConstants.ConvertFromCode(app.ClassificationResult ?? ""),
            // 模板10（最低生活保障申请书）专用：简称，避免与模板16的CLASSIFICATION_RESULT冲突
            [FieldKeys.APPLY_CLASSIFICATION] = ClassificationConstants.ConvertFromCode(app.ClassificationResult ?? ""),

            // 公示文件字段
            [FieldKeys.GUARANTEE_AMOUNT] = app.TotalGuaranteeAmount.ToString("N2"),
            [FieldKeys.GUARANTEE_TYPE_DISPLAY] = BuildGuaranteeTypeDisplay(app.ClassificationResult ?? ""),
            [FieldKeys.OPERATOR_UNIT_PHONE] = orgInfo.OperatorUnitPhone,
            [FieldKeys.PUBLICITY_START_DATE] = publicityStart,
            [FieldKeys.PUBLICITY_END_DATE] = publicityEnd,
            // 模板13（公示文件）专用：避免与模板16的CLASSIFICATION_RESULT冲突
            [FieldKeys.PUBLICITY_CLASSIFICATION] = ClassificationConstants.ConvertToFullName(app.ClassificationResult ?? ""),

            // 模板14（低保审核确认表）
            [FieldKeys.CONFIRM_AMOUNT] = app.HouseholdMonthlyGuaranteeAmount > 0 ? $"{app.HouseholdMonthlyGuaranteeAmount:N2}元" : "",
            [FieldKeys.APPLICANT_GENDER] = AddressResolver.ExtractGenderFromIdCard(app.ApplicantIdCard),
            [FieldKeys.HEALTH_STATUS] = _dictCacheService.GetValue(DictionaryTypeCodes.HealthStatuses, app.HealthStatus ?? ""),
            [FieldKeys.RIGID_EXPENDITURE] = BuildYesNoCheckbox(app.RigidExpenditure > 0),
            [FieldKeys.GUARANTEE_FAMILY_SIZE] = app.FamilySize.ToString(),
            [FieldKeys.SINGLE_PERSON_GUARANTEE] = BuildYesNoCheckbox(ClassificationConstants.IsCodeSingleRescue(app.ClassificationResult ?? "")),
            [FieldKeys.AUDIT_DATE] = auditDate,
            [FieldKeys.VERIFIED_BENEFIT] = BuildVerifiedBenefitText(app),
            [FieldKeys.PUBLICITY_STATUS] = "已公示、无异议",
            [FieldKeys.GOVERNMENT_OPINION] = BuildGovernmentOpinion(_cachedHouseholdSurvey),
            [FieldKeys.URBAN_MONTHLY_INCOME] = ClassificationConstants.HukouType.IsHukouUrban(app.HukouType) ? FormatDecimal(app.TotalFamilyIncome) : "",
[FieldKeys.RURAL_ANNUAL_INCOME] = ClassificationConstants.HukouType.IsHukouRural(app.HukouType) ? FormatDecimal(app.TotalAnnualIncome) : "",
// 模板14（低保审核确认表）专用：简称，单人保显示为"单人保"
            [FieldKeys.AUDIT_CLASSIFICATION] = ClassificationConstants.ConvertToShortName(app.ClassificationResult ?? ""),

            // 定期复核审批表：审核次月日期 + 复核组默认值（真实值由 ApplyReviewGroupAsync 覆写；无变更记录时打 "-" 而非报未映射）
            [FieldKeys.REVIEW_EFFECTIVE_DATE] = reviewEffective,
            [FieldKeys.REVIEW_SITUATION] = "",
            [FieldKeys.REVIEW_TIME] = "",
            [FieldKeys.REVIEW_CLASSIFICATION_CHANGE] = "",
            [FieldKeys.REVIEW_AMOUNT_CHANGE] = "",

            // 档案封面专用字段（避免与模板16的CLASSIFICATION_RESULT冲突）
            [FieldKeys.COVER_CLASSIFICATION] = ClassificationConstants.ConvertToFullName(app.ClassificationResult ?? ""),
            [FieldKeys.ARCHIVE_NUMBER] = volumeDate,

            // 档案认定结果告知书专用字段（避免与模板16的CLASSIFICATION_RESULT冲突）
            [FieldKeys.NOTICE_CLASSIFICATION_RESULT] = ClassificationConstants.ConvertToFullName(app.ClassificationResult ?? ""),

            // 特困审核确认表（模板75）专用字段
            [FieldKeys.DESTITUTE_AUDIT_CATEGORY] = BuildPersonCategory(app),
            [FieldKeys.DESTITUTE_AUDIT_CLASSIFICATION] = ClassificationConstants.ConvertToFullName(app.ClassificationResult ?? ""),
            [FieldKeys.DESTITUTE_DISABILITY] = BuildDisabilityDisplay(app),

            // 户口性质（特困审核确认表/分散供养协议共用）
            ["HUKOU_TYPE_DISPLAY"] = app.HukouType?.ToLowerInvariant() switch
            {
                "rural" => "农村户口",
                "urban" => "城市户口",
                _ => app.HukouType ?? ""
            },
            [FieldKeys.FAMILY_VILLAGE] = app.Community ?? "",
            [FieldKeys.COPYRIGHT_INFO] = CopyrightHelper.BuildCopyrightInfo(app.ApplicantIdCard),
        };

        // 分步路径：装配能力鉴定/照料人/特困字段（复用已加载缓存）
        if (_cachedCapabilityAssessment != null)
        {
            _fieldData[FieldKeys.DESTITUTE_SELF_CARE_ABILITY] = BuildSelfCareAbility(app, _cachedCapabilityAssessment);
            _fieldData[FieldKeys.DESTITUTE_CARE_LEVEL] = BuildCareLevel(app, _cachedCapabilityAssessment);
            _fieldData[FieldKeys.CAPABILITY_SELF_CARE_LEVEL] = BuildSelfCareAbility(app, _cachedCapabilityAssessment);
            _fieldData[FieldKeys.CAPABILITY_COMPLETED_ITEMS] = _cachedCapabilityAssessment.CompletedItems.ToString();
            _fieldData[FieldKeys.CAPABILITY_ASSESSMENT_DATE] = _cachedCapabilityAssessment.AssessmentDate.ToString("yyyy-MM-dd");
            _fieldData[FieldKeys.CAPABILITY_ASSESSOR] = _cachedCapabilityAssessment.AssessorName ?? "";
        }
        if (_cachedCaregivers != null && _cachedCaregivers.Count > 0)
        {
            var cg = _cachedCaregivers[0];
            _fieldData["CAREGIVER_NAME"] = cg.Name ?? "";
            _fieldData["CAREGIVER_ID_CARD"] = cg.IdCard ?? "";
            _fieldData["CAREGIVER_RELATIONSHIP"] = _dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, cg.Relationship ?? "");
            _fieldData["CAREGIVER_PHONE"] = cg.Phone ?? "";
            _fieldData["CAREGIVER_GENDER"] = _dictCacheService.GetValue(DictionaryTypeCodes.Gender, cg.Gender ?? "") ?? cg.Gender ?? "";
            _fieldData["CAREGIVER_AGE"] = cg.Age > 0 ? cg.Age.ToString() : "";
            _fieldData["CAREGIVER_ADDRESS"] = cg.Address ?? "";
            _fieldData["CAREGIVER_WORK_UNIT"] = cg.WorkUnit ?? "";
            _fieldData["CAREGIVER_SUBSIDY"] = app.CaregiverSubsidyAmount > 0 ? $"{FormatDecimal(app.CaregiverSubsidyAmount)}元" : "";
            _fieldData["CAREGIVER_SUBSIDY_AMOUNT"] = app.CaregiverSubsidyAmount > 0 ? FormatDecimal(app.CaregiverSubsidyAmount) : "";
            _fieldData["CAREGIVER_SUBSIDY_UPPER"] = app.CaregiverSubsidyAmount > 0 ? ConvertToChineseAmount(app.CaregiverSubsidyAmount) : "";
            _fieldData[FieldKeys.DESTITUTE_CAREGIVER_SITUATION] = BuildDestituteCaregiverSituation(app, _cachedCaregivers);
            _fieldData[FieldKeys.DESTITUTE_DAILY_CARE] = BuildDestituteDailyCare(app, _cachedCaregivers);
        }
        _fieldData["APPLICANT_PHONE"] = app.ApplicantPhone ?? "";
        if (_cachedFamilyMembers != null)
        {
            _fieldData[FieldKeys.DESTITUTE_HEALTH_ASSESSMENT] = BuildDestituteHealthAssessment(app, _cachedFamilyMembers);
            _fieldData[FieldKeys.DESTITUTE_FAMILY_STATUS] = BuildDestituteFamilyStatus(app, _cachedFamilyMembers);
        }

        // 声明类模板专用字段：配偶信息、户主所在市、当前日期
        _fieldData[FieldKeys.SPOUSE_NAME] = "";
        _fieldData[FieldKeys.SPOUSE_GENDER] = "";
        _fieldData[FieldKeys.SPOUSE_ID_CARD] = "";
        _fieldData[FieldKeys.HEAD_CITY] = app.City ?? "";
        _fieldData[FieldKeys.CURRENT_DATE] = DateTime.Now.ToString("yyyy年M月d日");
        if (_cachedFamilyMembers != null)
        {
            var spouse = _cachedFamilyMembers.FirstOrDefault(m =>
                m.RelationshipToHead == "Spouse" && !m.IsHouseholdHead);
            if (spouse != null)
            {
                _fieldData[FieldKeys.SPOUSE_NAME] = spouse.Name ?? "";
                _fieldData[FieldKeys.SPOUSE_GENDER] = AddressResolver.ExtractGenderFromIdCard(spouse.IdCard);
                _fieldData[FieldKeys.SPOUSE_ID_CARD] = spouse.IdCard ?? "";
            }
        }

// 构建表格数据
        _tableData = new List<Dictionary<string, string>>();
        if (_cachedFamilyMembers != null)
        {
            // 查询已死亡登记成员（历史数据未软删，生成档案时须统一过滤）
            var deathIdCards = new List<string>();
            var deathResult = await _familyMemberService.GetDeadIdCardsByApplicationIdAsync(app.Id, CancellationToken.None);
            if (deathResult.IsSuccess && deathResult.Value != null)
                deathIdCards = deathResult.Value;

            var sortedMembers = _cachedFamilyMembers
                .Where(m => !deathIdCards.Contains(m.IdCard ?? ""))
                .OrderBy(m => m.IsHouseholdHead ? 0 : m.RelationshipToHead == "Spouse" ? 1 : 2)
                .ToList();
            foreach (var member in sortedMembers)
            {
                _tableData.Add(new Dictionary<string, string>
                {
                    [FieldKeys.FAMILY_MEMBER_NAME] = member.Name,
                    [FieldKeys.FAMILY_MEMBER_ID_CARD] = member.IdCard ?? "",
                    [FieldKeys.FAMILY_MEMBER_RELATION] = _dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, member.RelationshipToHead ?? "")
                });

                // 模板15（分类施保调整表）家庭成员索引
                var idx = sortedMembers.IndexOf(member) + 1;
                if (idx <= 6)
                {
                    _fieldData[$"FAMILY_MEMBER_NAME_{idx}"] = member.Name;
                    _fieldData[$"FAMILY_MEMBER_GENDER_{idx}"] = member.Gender ?? "";
                    _fieldData[$"FAMILY_MEMBER_BIRTH_DATE_{idx}"] = IdCardValidator.ExtractBirthDate(member.IdCard ?? "")?.ToString("yyyy-MM-dd") ?? "";
                    _fieldData[$"FAMILY_MEMBER_RELATION_{idx}"] = _dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, member.RelationshipToHead ?? "");
                    _fieldData[$"FAMILY_MEMBER_HEALTH_{idx}"] = _dictCacheService.GetValue(DictionaryTypeCodes.HealthStatuses, member.HealthStatus ?? "");
                    _fieldData[$"FAMILY_MEMBER_INCOME_{idx}"] = member.AnnualIncome > 0 ? FormatDecimal(member.AnnualIncome) : "";

                    // 模板1（授权承诺书）家庭成员索引
                    _fieldData[$"FAMILY_MEMBER_ID_CARD_{idx}"] = member.IdCard ?? "";
                    _fieldData[$"FAMILY_MEMBER_CERT_TYPE_{idx}"] = _dictCacheService.GetValue(DictionaryTypeCodes.IdTypes, "ResidentIdCard");

                    // 模板16（经济财产声明书）共同生活成员索引
                    _fieldData[$"SHARED_NAME_{idx}"] = member.Name;
                    _fieldData[$"SHARED_GENDER_{idx}"] = member.Gender ?? "";
                    _fieldData[$"SHARED_NATIONALITY_{idx}"] = _dictCacheService.GetValue(DictionaryTypeCodes.Ethnicities, member.Ethnicity ?? "");
                    _fieldData[$"SHARED_MARITAL_{idx}"] = _dictCacheService.GetValue(DictionaryTypeCodes.MaritalStatuses, member.MaritalStatus ?? "");
                    _fieldData[$"SHARED_HEALTH_{idx}"] = _dictCacheService.GetValue(DictionaryTypeCodes.HealthStatuses, member.HealthStatus ?? "");
                    _fieldData[$"SHARED_ADDRESS_{idx}"] = "-";
_fieldData[$"SHARED_RELATION_{idx}"] = _dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, member.RelationshipToHead ?? "");
                }
            }

            // 模板15（分类施保调整表）家庭成员索引：不含户主本人（户主信息已在上方单独填写），从第 1 位起，最多 6 人
            var csMembers = sortedMembers
                .Where(m => m.MemberCategory == MemberCategoryConstants.SHARED_LIVING && !m.IsHouseholdHead && m.IdCard != app.ApplicantIdCard)
                .ToList();
            for (int i = 0; i < csMembers.Count && i < 6; i++)
            {
                var m = csMembers[i];
                var csIdx = i + 1;
                _fieldData[$"CLASSIFIED_MEMBER_NAME_{csIdx}"] = m.Name;
                _fieldData[$"CLASSIFIED_MEMBER_GENDER_{csIdx}"] = m.Gender ?? "";
                _fieldData[$"CLASSIFIED_MEMBER_BIRTH_DATE_{csIdx}"] = IdCardValidator.ExtractBirthDate(m.IdCard ?? "")?.ToString("yyyy-MM-dd") ?? "";
                _fieldData[$"CLASSIFIED_MEMBER_RELATION_{csIdx}"] = _dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, m.RelationshipToHead ?? "");
                _fieldData[$"CLASSIFIED_MEMBER_HEALTH_{csIdx}"] = _dictCacheService.GetValue(DictionaryTypeCodes.HealthStatuses, m.HealthStatus ?? "");
                _fieldData[$"CLASSIFIED_MEMBER_INCOME_{csIdx}"] = m.AnnualIncome > 0 ? FormatDecimal(m.AnnualIncome) : "";
            }

            // 模板15：家庭人口 + 分类施保享受类型 + 分类施保共计人数 + 审核确认意见 + 加发额度
            _fieldData[FieldKeys.APPLICANT_BIRTH_DATE] = IdCardValidator.ExtractBirthDate(app.ApplicantIdCard ?? "")?.ToString("yyyy-MM-dd") ?? "";
            _fieldData[FieldKeys.NATIONALITY] = _dictCacheService.GetValue(DictionaryTypeCodes.Ethnicities, app.Ethnicity ?? "");
            _fieldData[FieldKeys.FAMILY_SIZE] = app.FamilySize.ToString();
            _fieldData[FieldKeys.CLASSIFIED_SUBSIDY_TYPE] = BuildClassifiedSubsidyTypeDisplay(app, csMembers);
            var csClassifiedCount = BuildClassifiedCount(app, csMembers);
            _fieldData[FieldKeys.CLASSIFIED_TOTAL_COUNT] = csClassifiedCount.text;
            _fieldData[FieldKeys.AUDIT_OPINION] = BuildAuditOpinion(app, csMembers, orgInfo);
            _fieldData[FieldKeys.CLASSIFIED_TOTAL_AMOUNT] = $"经 {orgInfo.Town}（{orgInfo.OperatorUnitName}）批准加发 {FormatDecimal(app.ClassifiedSubsidyAmount)} 元";
        }

        // 构建赡养人表格数据（用于赡养费承诺书模板按人迭代）
        _supporterTableData.Clear();
        var cachedSupporters = _cachedFamilyMembers?.Where(m => m.MemberCategory == MemberCategoryConstants.SUPPORT).ToList() ?? new List<FamilyMember>();
        if (cachedSupporters.Count > 0)
        {
            // 查找配偶信息
            string spouseInfo = "";
            if (_cachedFamilyMembers != null)
            {
                var spouse = _cachedFamilyMembers.FirstOrDefault(m =>
                    m.RelationshipToHead == "Spouse" && !m.IsHouseholdHead);
                if (spouse != null)
                {
                    var spouseGender = AddressResolver.ExtractGenderFromIdCard(spouse.IdCard);
                    var spouseAge = AddressResolver.ExtractAgeFromIdCard(spouse.IdCard);
                    var spouseRel = _dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, spouse.RelationshipToHead ?? "");
                    spouseInfo = $"{spouse.Name} {spouseGender} {spouseAge}岁 {spouseRel}";
                }
            }

            foreach (var s in cachedSupporters)
            {
                var supporterRel = _dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, s.RelationshipToHead);
                var supporterInfo = $"{s.Name} {s.Gender ?? ""} {(s.Age ?? 0)}岁 {supporterRel}";

                _supporterTableData.Add(new Dictionary<string, string>
                {
                    ["SUPPORTER_NAME"] = s.Name,
                    ["SUPPORTER_GENDER"] = s.Gender ?? "",
                    ["SUPPORTER_AGE"] = s.Age?.ToString() ?? "",
                    ["SUPPORTER_RELATION"] = supporterRel,
                    ["SUPPORTER_INFO"] = supporterInfo,
                    ["SPOUSE_INFO"] = spouseInfo,
                    ["SUPPORT_FEE"] = s.MonthlySupportFee > 0 ? FormatDecimal(s.MonthlySupportFee) : "",
                    ["SUPPORT_FEE_CHINESE"] = s.MonthlySupportFee > 0 ? ConvertToChineseAmount(s.MonthlySupportFee) : "",
                    ["ANNUAL_SUPPORT_FEE"] = s.AnnualSupportFee > 0 ? FormatDecimal(s.AnnualSupportFee) : "",
                });

                // 模板16（经济财产声明书）赡养人索引字段
                var idx = cachedSupporters.IndexOf(s) + 1;
                if (idx <= 6)
                {
                    _fieldData[$"SUPPORTER_NAME_{idx}"] = s.Name;
                    _fieldData[$"SUPPORTER_GENDER_{idx}"] = s.Gender ?? "";
                    _fieldData[$"SUPPORTER_AGE_{idx}"] = s.Age?.ToString() ?? "";
                    _fieldData[$"SUPPORTER_RELATION_{idx}"] = supporterRel;
                    _fieldData[$"SUPPORTER_NATIONALITY_{idx}"] = _dictCacheService.GetValue(DictionaryTypeCodes.Ethnicities, s.Ethnicity ?? "");
                    _fieldData[$"SUPPORTER_MARITAL_{idx}"] = _dictCacheService.GetValue(DictionaryTypeCodes.MaritalStatuses, s.MaritalStatus ?? "");
                    _fieldData[$"SUPPORTER_HEALTH_{idx}"] = _dictCacheService.GetValue(DictionaryTypeCodes.HealthStatuses, s.HealthStatus ?? "");
                    _fieldData[$"SUPPORTER_OCCUPATION_{idx}"] = _dictCacheService.GetValue(DictionaryTypeCodes.EmploymentStatuses, s.EmploymentStatus ?? "");
                    _fieldData[$"SUPPORTER_WORKPLACE_{idx}"] = s.WorkUnit ?? "";
                    _fieldData[$"SUPPORTER_ID_CARD_{idx}"] = s.IdCard ?? "";
                    _fieldData[$"SUPPORTER_PHONE_{idx}"] = s.Phone ?? "";
                    _fieldData[$"SUPPORTER_AMOUNT_{idx}"] = FormatDecimal(s.AnnualSupportFee);
                    _fieldData[$"SUPPORTER_ABILITY_{idx}"] = s.IsSupportAbility ? "有" : "无";
                }
            }
        }

        // 复核组：{定期复核情况}/{复核时间}/{待遇变化分类}/{待遇变化金额}（分步路径同样装配，避免该表复核组为空）
        await ApplyReviewGroupAsync(app, app.Id);

        // 构建侧边栏（月值口径：总收入取主表月值权威口径 TotalFamilyIncome，赡养费年值由侧边栏÷12 显示）
        await BuildSidebarFromApplicationAsync(app, civilAssistantName, orgInfo,
            app.TotalFamilyIncome, app.WorkIncomeTotal, app.BusinessIncomeTotal, app.PropertyIncomeTotal,
            app.TransferIncomeTotal, app.AlimonyIncome, app.OtherIncomeTotal, app.RigidExpenditure);
    }

    private Task<bool> LoadMonthlyReportDataAsync(long? businessId)
    {
        if (!businessId.HasValue) return Task.FromResult(false);

        var yearMonth = businessId.Value.ToString();
        if (yearMonth.Length != 6) return Task.FromResult(false);

        var year = int.Parse(yearMonth[..4]);
        var month = int.Parse(yearMonth[4..]);

        _currentBusinessType = "AssetVerificationMonthlyReport";
        _currentBusinessId = businessId;
        _currentClassification = ClassificationConstants.AssetVerification;

        BusinessTypeDisplay = "经济核对月报表";
        DataSourceType = "经济核对月报表";
        ApplicantName = $"{year}年{month}月";
        ApplicantIdCard = "-";
        FamilyInfo = "月度汇总";
        HasBusinessData = true;
        IsReady = true;
        StatusText = $"已加载 {year}年{month}月报表";

        _fieldData = new Dictionary<string, string>
        {
            ["REPORT_PERIOD"] = $"{year}年{month}月",
            ["YEAR"] = year.ToString(),
            ["MONTH"] = month.ToString(),
            ["GENERATED_AT"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            ["GENERATED_BY"] = App.CurrentUserName ?? "System"
        };

        _tableData = new List<Dictionary<string, string>>();

        SidebarGroups.Clear();
        AutoFilledCount = 0;
        SidebarTotalCount = 0;

        return Task.FromResult(true);
    }

    private Task<bool> LoadGenericDataAsync(string businessType, long? businessId)
    {
        _currentBusinessType = businessType;
        _currentBusinessId = businessId;
        _currentClassification = businessType;

        BusinessTypeDisplay = OutputPathHelper.GetCategoryDisplayName(businessType);
        DataSourceType = BusinessTypeDisplay;
        ApplicantName = businessId?.ToString() ?? "-";
        ApplicantIdCard = "-";
        FamilyInfo = "-";
        HasBusinessData = true;
        IsReady = true;
        StatusText = $"已加载 {BusinessTypeDisplay}";

        _fieldData = new Dictionary<string, string>
        {
            ["BUSINESS_TYPE"] = businessType,
            ["BUSINESS_ID"] = businessId?.ToString() ?? ""
        };

        _tableData = new List<Dictionary<string, string>>();

        SidebarGroups.Clear();
        AutoFilledCount = 0;
        SidebarTotalCount = 0;

        return Task.FromResult(true);
    }

    // ========================
    //  侧边栏构建 + 自动填充
    // ========================

    private void BuildSidebarFromAssetCheck(AssetVerificationDetail detail, string fullAddress, string civilAssistantName, OrganizationInfoDto orgInfo)
    {
        SidebarGroups.Clear();
        AutoFilledCount = 0;

        var fieldMapping = new Dictionary<string, string>
        {
            ["申请人姓名"] = FieldKeys.APPLICANT_NAME,
            ["身份证号"] = FieldKeys.APPLICANT_ID_CARD,
            ["证件类型"] = FieldKeys.APPLICANT_ID_TYPE,
            ["联系电话"] = FieldKeys.CONTACT_PHONE,
            ["家庭地址"] = FieldKeys.FAMILY_ADDRESS,
            ["社区"] = FieldKeys.COMMUNITY,
            ["申请原因"] = FieldKeys.APPLICATION_REASON,
            ["申请日期"] = FieldKeys.APPLICATION_DATE,
            ["经办人"] = FieldKeys.OPERATOR_NAME,
            ["经办单位"] = FieldKeys.OPERATOR_UNIT,
            ["代理人姓名"] = FieldKeys.AGENT_NAME,
            ["代理人身份证"] = FieldKeys.AGENT_ID_CARD,
            ["代理关系"] = FieldKeys.AGENT_RELATION,
            ["代理人证件类型"] = FieldKeys.AGENT_CERT_TYPE,
            ["是否委托代理"] = FieldKeys.IS_AGENT,
            ["区县"] = FieldKeys.DISTRICT,
            ["乡镇"] = FieldKeys.TOWN,
            ["民政助理"] = FieldKeys.CIVIL_ASSISTANT_NAME,
            ["区县民政局"] = FieldKeys.DISTRICT_CIVIL_BUREAU,
            ["区县民政局电话"] = FieldKeys.DISTRICT_CIVIL_BUREAU_PHONE,
            ["经办单位电话"] = FieldKeys.OPERATOR_UNIT_PHONE
        };

        // 基本信息组
        var basicGroup = new SidebarGroup("基本信息");
        basicGroup.Items.Add(CreateSidebarItem("申请人姓名", detail.ApplicantName, fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("身份证号", detail.ApplicantIdCard, fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("证件类型", GetIdTypeDisplay(detail.ApplicantIdType), fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("联系电话", detail.ContactPhone, fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("申请原因", GetApplicationReasonDisplay(detail.ApplicationReason), fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("申请日期", detail.ApplicationDate.ToString("yyyy-MM-dd"), fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("核查状态", detail.StatusDisplay, fieldMapping));
        SidebarGroups.Add(basicGroup);

        // 地址信息组
        var addressGroup = new SidebarGroup("地址信息");
        addressGroup.Items.Add(CreateSidebarItem("家庭地址", fullAddress, fieldMapping));
        addressGroup.Items.Add(CreateSidebarItem("社区", detail.Community, fieldMapping));
        SidebarGroups.Add(addressGroup);

        // 代理人信息组
        if (detail.HasAgent)
        {
            var agentGroup = new SidebarGroup("代理人信息");
            agentGroup.Items.Add(CreateSidebarItem("是否委托代理", "是", fieldMapping));
            agentGroup.Items.Add(CreateSidebarItem("代理人姓名", detail.AgentName, fieldMapping));
            agentGroup.Items.Add(CreateSidebarItem("代理人身份证", detail.AgentIdCard, fieldMapping));
            agentGroup.Items.Add(CreateSidebarItem("代理关系", detail.AgentRelationship, fieldMapping));
            agentGroup.Items.Add(CreateSidebarItem("代理人证件类型", GetIdTypeDisplay(detail.AgentIdType), fieldMapping));
            SidebarGroups.Add(agentGroup);
        }

        // 经办人信息组
        var operatorGroup = new SidebarGroup("经办人信息");
        operatorGroup.Items.Add(CreateSidebarItem("经办人", detail.OperatorName, fieldMapping));
        operatorGroup.Items.Add(CreateSidebarItem("经办单位", detail.OperatorUnitName, fieldMapping));
        operatorGroup.Items.Add(CreateSidebarItem("民政助理", civilAssistantName, fieldMapping));
        operatorGroup.Items.Add(CreateSidebarItem("区县", orgInfo.District, fieldMapping));
        operatorGroup.Items.Add(CreateSidebarItem("乡镇", orgInfo.Town, fieldMapping));
        operatorGroup.Items.Add(CreateSidebarItem("区县民政局", orgInfo.DistrictCivilBureau, fieldMapping));
        operatorGroup.Items.Add(CreateSidebarItem("区县民政局电话", orgInfo.DistrictCivilBureauPhone, fieldMapping));
        operatorGroup.Items.Add(CreateSidebarItem("经办单位电话", orgInfo.OperatorUnitPhone, fieldMapping));
        SidebarGroups.Add(operatorGroup);

        // 家庭成员组
        if (detail.FamilyMembers.Count > 0)
        {
            var memberGroup = new SidebarGroup($"家庭成员 ({detail.FamilyMembers.Count}人)");
            for (int i = 0; i < detail.FamilyMembers.Count; i++)
            {
                var m = detail.FamilyMembers[i];
                var prefix = $"{i + 1}. {m.Name}";
                memberGroup.Items.Add(new SidebarItem
                {
                    Label = $"{prefix} 身份证",
                    Value = m.IdCard,
                    FieldKey = "",
                    IsMatchable = false,
                    IsAutoFilled = false
                });
                memberGroup.Items.Add(new SidebarItem
                {
                    Label = $"{prefix} 关系",
                    Value = m.Relationship,
                    FieldKey = "",
                    IsMatchable = false,
                    IsAutoFilled = false
                });
            }
            SidebarGroups.Add(memberGroup);
        }

        SidebarTotalCount = SidebarGroups.Sum(g => g.Items.Count);
    }

    private async Task BuildSidebarFromApplicationAsync(ApplicationEntity app, string civilAssistantName, OrganizationInfoDto orgInfo,
        decimal totalIncome, decimal workIncome, decimal businessIncome, decimal propertyIncome,
        decimal transferIncome, decimal alimonyIncome, decimal otherIncome, decimal rigidExpenditure)
    {
        SidebarGroups.Clear();
        AutoFilledCount = 0;

        var fieldMapping = new Dictionary<string, string>
        {
            ["申请人姓名"] = FieldKeys.APPLICANT_NAME,
            ["身份证号"] = FieldKeys.APPLICANT_ID_CARD,
            ["联系电话"] = FieldKeys.CONTACT_PHONE,
            ["家庭地址"] = FieldKeys.FAMILY_ADDRESS,
            ["社区"] = FieldKeys.COMMUNITY,
            ["申请原因"] = FieldKeys.APPLICATION_REASON,
            ["经办人"] = FieldKeys.OPERATOR_NAME,
            ["区县"] = FieldKeys.DISTRICT,
            ["乡镇"] = FieldKeys.TOWN,
            ["民政助理"] = FieldKeys.CIVIL_ASSISTANT_NAME,
            ["户籍类型"] = FieldKeys.CLASSIFICATION_RESULT,
            ["性别"] = "GENDER",
            ["民族"] = "ETHNICITY",
            ["婚姻状况"] = "MARITAL_STATUS",
            ["文化程度"] = "EDUCATION_LEVEL",
            ["申请状态"] = FieldKeys.STATUS,
            ["家庭地址"] = FieldKeys.FAMILY_ADDRESS,
            ["户籍地址"] = FieldKeys.HUKOU_ADDRESS,
            ["家庭人数"] = FieldKeys.HEAD_FAMILY_SIZE,
            ["家庭总收入"] = FieldKeys.INCOME_TOTAL,
            ["人均收入"] = "PER_CAPITA_INCOME",
            ["务工收入"] = FieldKeys.INCOME_LABOR,
            ["经营收入"] = FieldKeys.INCOME_BUSINESS,
            ["财产性收入"] = FieldKeys.INCOME_PROPERTY,
            ["转移性收入"] = FieldKeys.INCOME_TRANSFER,
            ["农业补贴"] = FieldKeys.INCOME_SUBSIDY,
            ["土地收入"] = FieldKeys.INCOME_LAND,
            ["刚性支出"] = FieldKeys.INCOME_RIGID_EXPENDITURE
        };

        // 基本信息组
        var basicGroup = new SidebarGroup("基本信息");
        basicGroup.Items.Add(CreateSidebarItem("申请人姓名", app.ApplicantName, fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("身份证号", app.ApplicantIdCard, fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("联系电话", app.ApplicantPhone, fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("申请原因", app.ApplicationReason, fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("户籍类型", app.HukouType, fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("性别", app.Gender, fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("民族", app.Ethnicity, fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("婚姻状况", app.MaritalStatus, fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("文化程度", app.EducationLevel, fieldMapping));
        basicGroup.Items.Add(CreateSidebarItem("申请状态", app.Status, fieldMapping));
        SidebarGroups.Add(basicGroup);

        // 地址信息组
        var addressGroup = new SidebarGroup("地址信息");
        addressGroup.Items.Add(CreateSidebarItem("家庭地址", app.Address, fieldMapping));
        addressGroup.Items.Add(CreateSidebarItem("社区", app.Community, fieldMapping));
        addressGroup.Items.Add(CreateSidebarItem("乡镇", app.Town, fieldMapping));
        addressGroup.Items.Add(CreateSidebarItem("区县", app.District, fieldMapping));
        addressGroup.Items.Add(CreateSidebarItem("户籍地址", app.HukouAddress, fieldMapping));
        SidebarGroups.Add(addressGroup);

        // 经济信息组（使用从经济明细计算的值，而非 Application 表汇总字段）
        var economicGroup = new SidebarGroup("经济信息");
        var perCapitaIncome = app.FamilySize > 0 ? Math.Round(totalIncome / app.FamilySize, 2) : 0;
        economicGroup.Items.Add(new SidebarItem { Label = "家庭人数", Value = app.FamilySize.ToString(), IsMatchable = false });
        economicGroup.Items.Add(new SidebarItem { Label = "家庭总收入", Value = FormatDecimal(totalIncome), IsMatchable = false });
        economicGroup.Items.Add(new SidebarItem { Label = "人均收入", Value = FormatDecimal(perCapitaIncome), IsMatchable = false });
        economicGroup.Items.Add(new SidebarItem { Label = "务工收入", Value = FormatDecimal(workIncome), IsMatchable = false });
        economicGroup.Items.Add(new SidebarItem { Label = "经营收入", Value = FormatDecimal(businessIncome), IsMatchable = false });
        economicGroup.Items.Add(new SidebarItem { Label = "财产性收入", Value = FormatDecimal(propertyIncome), IsMatchable = false });
        economicGroup.Items.Add(new SidebarItem { Label = "转移性收入", Value = FormatDecimal(transferIncome), IsMatchable = false });
        economicGroup.Items.Add(new SidebarItem { Label = "农业补贴", Value = FormatDecimal(app.SubsidyTotal), IsMatchable = false });
        economicGroup.Items.Add(new SidebarItem { Label = "刚性支出", Value = FormatDecimal(rigidExpenditure), IsMatchable = false });
        economicGroup.Items.Add(new SidebarItem { Label = "赡养费收入", Value = FormatDecimal(Math.Round(alimonyIncome / 12m, 2)), IsMatchable = false });
        SidebarGroups.Add(economicGroup);

        // 分类结果组
        if (!string.IsNullOrEmpty(app.ClassificationResult))
        {
            var classGroup = new SidebarGroup("分类结果");
            classGroup.Items.Add(new SidebarItem { Label = "分类结果", Value = ClassificationConstants.ConvertToFullName(app.ClassificationResult ?? ""), IsMatchable = false });
            classGroup.Items.Add(new SidebarItem { Label = "补贴类型", Value = app.ClassifiedSubsidyType, IsMatchable = false });
            classGroup.Items.Add(new SidebarItem { Label = "补贴金额", Value = FormatDecimal(app.ClassifiedSubsidyAmount), IsMatchable = false });
            classGroup.Items.Add(new SidebarItem { Label = "保障金总额", Value = FormatDecimal(app.TotalGuaranteeAmount), IsMatchable = false });
            // 渐退期审批表基础 8 项：只依赖主档，不依赖渐退行（无行时也有姓名/证号等）
            _fieldData[FieldKeys.GP_HEAD_NAME] = app.ApplicantName ?? "";
            _fieldData[FieldKeys.GP_HEAD_GENDER] = app.Gender ?? "";
            _fieldData[FieldKeys.GP_HEAD_ID_CARD] = app.ApplicantIdCard ?? "";
            _fieldData[FieldKeys.GP_HEAD_AGE] = (IdCardValidator.ExtractAge(app.ApplicantIdCard ?? "") ?? 0).ToString();
            _fieldData[FieldKeys.GP_FAMILY_SIZE] = app.FamilySize.ToString();
            _fieldData[FieldKeys.GP_CLASSIFICATION] = ClassificationConstants.ConvertToFullName(app.ClassificationResult ?? "");
            _fieldData[FieldKeys.GP_ADDRESS] = app.Address ?? "";
            _fieldData[FieldKeys.GP_PHONE] = app.ApplicantPhone ?? "";
            _fieldData[FieldKeys.GP_AUDIT_TIME] = DateTime.Now.ToString("yyyy年M月d日");

            // 渐退取最近一条（含已退出）：退出后补打审批表仍需起止/退出说明
            var graceRes = await _gracePeriodService.GetLatestAsync(app.Id, CancellationToken.None);
            if (graceRes.IsSuccess && graceRes.Value != null)
            {
                if (graceRes.Value.IsActive)
                {
                    classGroup.Items.Add(new SidebarItem { Label = "渐退期", Value = $"{graceRes.Value.GracePeriodMonths}个月", IsMatchable = false });
                    classGroup.Items.Add(new SidebarItem { Label = "渐退开始", Value = graceRes.Value.StartDate?.ToString("yyyy-MM-dd") ?? "", IsMatchable = false });
                    classGroup.Items.Add(new SidebarItem { Label = "渐退结束", Value = graceRes.Value.EndDate?.ToString("yyyy-MM-dd") ?? "", IsMatchable = false });
                }

                // 渐退期审批表：依赖渐退行的字段（变动说明/月数/起止/退出句）
                _fieldData[FieldKeys.GP_CHANGE_DETAIL] = await BuildGraceChangeDetailAsync(app, graceRes.Value);
                _fieldData[FieldKeys.GP_PERIOD_MONTHS] = graceRes.Value.GracePeriodMonths?.ToString() ?? "";
                _fieldData[FieldKeys.GP_START] = graceRes.Value.StartDate?.ToString("yyyy年M月d日") ?? "";
                _fieldData[FieldKeys.GP_END] = graceRes.Value.EndDate?.ToString("yyyy年M月d日") ?? "";
                var gpStartText = graceRes.Value.StartDate?.ToString("yyyy年M月d日") ?? "";
                var gpEndText = graceRes.Value.EndDate?.ToString("yyyy年M月d日") ?? "";
                _fieldData[FieldKeys.GP_PERIOD_RANGE] =
                    string.IsNullOrEmpty(gpStartText) && string.IsNullOrEmpty(gpEndText) ? ""
                    : string.IsNullOrEmpty(gpStartText) ? $"渐退期至{gpEndText}为止"
                    : string.IsNullOrEmpty(gpEndText) ? $"渐退期自{gpStartText}起"
                    : $"渐退期自{gpStartText}起，至{gpEndText}为止";
                _fieldData[FieldKeys.GP_EXIT_SITUATION] = await BuildGraceExitSituationAsync(app, graceRes.Value);

                // 档案_保障金减少：渐退封顶减发时填充（原额 > 现应发额）
                var graceOriginal = graceRes.Value.OriginalGuaranteeAmount ?? 0;
                var graceCurrent = graceRes.Value.GraceGrantAmount ?? app.HouseholdMonthlyGuaranteeAmount;
                if (graceOriginal > 0 && graceCurrent < graceOriginal)
                {
                    var decrease = graceOriginal - graceCurrent;
                    var capBasis = $"{(app.HukouType?.Contains("Urban") == true ? "城市" : "农村")}低保标准×{app.FamilySize}人";
                    _fieldData[FieldKeys.GR_HEAD_NAME] = app.ApplicantName ?? "";
                    _fieldData[FieldKeys.GR_ID_CARD] = app.ApplicantIdCard ?? "";
                    _fieldData[FieldKeys.GR_FAMILY_SIZE] = app.FamilySize.ToString();
                    _fieldData[FieldKeys.GR_OLD_AMOUNT] = FormatDecimal(graceOriginal);
                    _fieldData[FieldKeys.GR_NEW_AMOUNT] = FormatDecimal(graceCurrent);
                    _fieldData[FieldKeys.GR_DECREASE_AMOUNT] = FormatDecimal(decrease);
                    _fieldData[FieldKeys.GR_CAP_BASIS] = capBasis;
                    _fieldData[FieldKeys.GR_GRACE_START] = graceRes.Value.StartDate?.ToString("yyyy年M月d日") ?? "";
                    _fieldData[FieldKeys.GR_GRACE_END] = graceRes.Value.EndDate?.ToString("yyyy年M月d日") ?? "";
                    _fieldData[FieldKeys.GR_REASON] = "原保障金超过本户口类型最低保障额上限，渐退期内封顶减发";
                    _fieldData[FieldKeys.GR_AUDIT_TIME] = DateTime.Now.ToString("yyyy年M月d日");
                }
            }

            // 增减员调整表：人员变动类变更（MemberChange 类别 + HouseholdDeath 等类型兜底；
            // 记录可挂在旧档，取数经 new_application_id 关联到新档）
            var changeRes = await _changeService.GetMemberChangeRecordsAsync(app.Id, 5, CancellationToken.None);

            // 头部公共字段不依赖变更记录：即使本户无增减员，表头也必须有值
            //（性别按身份证号推导，与模板1/模板10 同口径，登记值仅兜底）
            _fieldData[FieldKeys.ADJ_HEAD_NAME] = app.ApplicantName ?? "";
            _fieldData[FieldKeys.ADJ_HEAD_GENDER] = ResolveGender(app.ApplicantIdCard, app.Gender);
            _fieldData[FieldKeys.ADJ_HEAD_BIRTH] = IdCardValidator.ExtractBirthDate(app.ApplicantIdCard ?? "")?.ToString("yyyy-MM-dd") ?? "";
            _fieldData[FieldKeys.ADJ_HEAD_NATION] = _dictCacheService.GetValue(DictionaryTypeCodes.Ethnicities, app.Ethnicity ?? "");
            _fieldData[FieldKeys.ADJ_HEAD_FAMILY_SIZE] = app.FamilySize.ToString();
            _fieldData[FieldKeys.ADJ_HEAD_FAMILY_TYPE] = app.HukouType ?? "";
            _fieldData[FieldKeys.ADJ_HEAD_ADDRESS] = app.Address ?? "";
            _fieldData[FieldKeys.ADJ_HEAD_CLASSIFICATION] = app.ClassificationResult ?? "";
            _fieldData[FieldKeys.ADJ_HEAD_HUKOU] = await ResolveHukouAddressAsync(app);
            _fieldData[FieldKeys.ADJ_HEAD_ID_CARD] = app.ApplicantIdCard ?? "";
            var adjAuditTime = DateTime.Now;
            _fieldData[FieldKeys.ADJ_AUDIT_TIME] = adjAuditTime.ToString("yyyy年M月d日");
            // 执行时间 = 审批时间的次月 1 号（与模板1 复核组 {审核次月日期} 同口径）
            _fieldData[FieldKeys.ADJ_EFFECTIVE_DATE] =
                new DateTime(adjAuditTime.Year, adjAuditTime.Month, 1).AddMonths(1).ToString("yyyy年M月1日");

            if (changeRes.IsSuccess && changeRes.Value != null && changeRes.Value.Count > 0)
            {
                // 统计增员/减员人数和金额差
                int addCount = 0, removeCount = 0;
                decimal amountDiff = 0;

                static bool IsRemoveType(string t) =>
                    t is "MemberDeath" or "MemberRemove" or "HouseholdDeath";

                foreach (var ch in changeRes.Value)
                {
                    string chType = ch.ChangeType ?? "";
                    DateTime chDate = ch.ChangeDate;
                    decimal oldAmt = ch.OldGuaranteeAmount ?? 0;
                    decimal newAmt = ch.NewGuaranteeAmount ?? 0;

                    if (IsRemoveType(chType))
                        removeCount++;
                    else if (chType is "MemberAdd" or "MemberModify" or "HouseholdHeadChange")
                        addCount++;

                    amountDiff += (newAmt - oldAmt);
                }

                // 增减金额（差额）；渐退期内待遇固定，增减员不产生增发/减发，两格都固定 0.00
                var inGracePeriod = graceRes.IsSuccess && graceRes.Value != null && graceRes.Value.IsActive;
                if (inGracePeriod)
                {
                    _fieldData[FieldKeys.ADJ_INCREASE_AMOUNT] = "0.00";
                    _fieldData[FieldKeys.ADJ_DECREASE_AMOUNT] = "0.00";
                }
                else if (amountDiff > 0)
                {
                    _fieldData[FieldKeys.ADJ_INCREASE_AMOUNT] = amountDiff.ToString("N2");
                    _fieldData[FieldKeys.ADJ_DECREASE_AMOUNT] = "0.00";
                }
                else
                {
                    _fieldData[FieldKeys.ADJ_INCREASE_AMOUNT] = "0.00";
                    _fieldData[FieldKeys.ADJ_DECREASE_AMOUNT] = Math.Abs(amountDiff).ToString("N2");
                }

                // ── 逐人增减明细（增员/减员槽）──
                // 权威源 nc_biz_change_details（成员增减流程逐人登记）；
                // 成员死亡/户主死亡流程无明细，服务按 change_reason 的"动词: 姓名(身份证)"解析兜底，
                // 并回查成员表补齐性别/关系/身体状况/工作单位/收入（减员行可能只剩软删除历史行）
                var adjustEntries = await GetMemberAdjustEntriesSafeAsync(app.Id);

                var addAll = adjustEntries
                    .Where(e => string.Equals(e.Direction, DictionaryConstants.ChangeType.MEMBER_ADD, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var removeAll = adjustEntries
                    .Where(e => string.Equals(e.Direction, DictionaryConstants.ChangeType.MEMBER_REMOVE, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                // 人数口径 = 逐人明细人数（一次变更可含多人，按变更记录数会少算）；无明细时回退按记录数
                if (addAll.Count > 0) addCount = addAll.Count;
                if (removeAll.Count > 0) removeCount = removeAll.Count;

                // 增员槽（最多 3 人，按变更日期倒序）
                for (var slot = 0; slot < addAll.Count && slot < 3; slot++)
                    FillAdjustSlot(isAdd: true, slot + 1, addAll[slot]);

                // 减员槽（最多 3 人，按变更日期倒序）
                for (var slot = 0; slot < removeAll.Count && slot < 3; slot++)
                    FillAdjustSlot(isAdd: false, slot + 1, removeAll[slot]);

                // 镇政府意见-家庭具体情况 = 逐人增减摘要 + 定期复核情况全文（变化情况+核算过程，含中文类别全称）
                // 复核组在 ApplyReviewGroupAsync 已先行装配（LoadApplicationDataAsync/分步路径均先于侧边栏）；
                // 无复核记录时回退中文描述，禁止拼 ClassificationResult 原始码（曾打出 RuralLowIncome 英文）
                var summaryParts = new List<string>();
                foreach (var e in removeAll.Take(3))
                    summaryParts.Add($"减员：{e.Name}（{ResolveAdjustShortReason(e)}）");
                foreach (var e in addAll.Take(3))
                    summaryParts.Add($"增员：{e.Name}（{ResolveAdjustShortReason(e)}）");
                var adjustSummary = string.Join("；", summaryParts);
                var reviewSituation = _fieldData.GetValueOrDefault(FieldKeys.REVIEW_SITUATION, "");
                var familyFallback = $"{app.ApplicantName}户，家庭人口{app.FamilySize}人，" +
                    $"{ClassificationConstants.ConvertToFullName(app.ClassificationResult ?? "")}，月保障金{FormatDecimal(app.TotalGuaranteeAmount)}元";
                _fieldData[FieldKeys.ADJ_FAMILY_DETAIL] = string.IsNullOrWhiteSpace(reviewSituation)
                    ? (string.IsNullOrWhiteSpace(adjustSummary) ? familyFallback : $"{adjustSummary}。{familyFallback}")
                    : string.IsNullOrWhiteSpace(adjustSummary) ? reviewSituation : $"{adjustSummary}。{reviewSituation}";
                _fieldData[FieldKeys.ADJ_CHANGE_SUMMARY] = $"增员{addCount}人，减员{removeCount}人";
                _fieldData[FieldKeys.ADJ_ADD_COUNT] = addCount.ToString();
                _fieldData[FieldKeys.ADJ_REMOVE_COUNT] = removeCount.ToString();
            }

            SidebarGroups.Add(classGroup);
        }

        // 经办人信息组
        var operatorGroup = new SidebarGroup("经办人信息");
        operatorGroup.Items.Add(CreateSidebarItem("经办人", app.UpdatedBy, fieldMapping));
        operatorGroup.Items.Add(CreateSidebarItem("民政助理", civilAssistantName, fieldMapping));
        operatorGroup.Items.Add(CreateSidebarItem("区县", orgInfo.District, fieldMapping));
        operatorGroup.Items.Add(CreateSidebarItem("乡镇", orgInfo.Town, fieldMapping));
        SidebarGroups.Add(operatorGroup);

        SidebarTotalCount = SidebarGroups.Sum(g => g.Items.Count);
    }

    /// <summary>
    /// 取逐人增/减员明细（增减员调整表槽位用）。
    /// 加载失败记错误日志并返回空列表：槽位留空、人数回退按变更记录数，绝不伪造数据。
    /// </summary>
    private async Task<List<MemberAdjustEntry>> GetMemberAdjustEntriesSafeAsync(long applicationId)
    {
        var result = await _changeService.GetMemberAdjustEntriesAsync(applicationId, 5, CancellationToken.None);
        if (result.IsSuccess && result.Value != null)
            return result.Value;

        _logger.Error($"增减员逐人明细加载失败: ApplicationId={applicationId}, ErrorCode={result.ErrorCode}, Message={result.Message}");
        return new List<MemberAdjustEntry>();
    }

    /// <summary>
    /// 增/减员槽位填充（槽位 1..3）：
    /// 关系与身体状况走字典显示值、性别按身份证号推导、收入按年值展示——与模板1/模板10 同口径。
    /// 增减原因逐行独立（ADJ_ADD_REASON_N/ADJ_REMOVE_REASON_N），写该行人员的短原因（≤6 字）。
    /// </summary>
    private void FillAdjustSlot(bool isAdd, int slot, MemberAdjustEntry entry)
    {
        var name = entry.Name ?? "";
        var idCard = entry.IdCard ?? "";
        var gender = ResolveGender(idCard, entry.Gender);
        var relation = _dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, entry.RelationshipToHead ?? "");
        var health = _dictCacheService.GetValue(DictionaryTypeCodes.HealthStatuses, entry.HealthStatus ?? "");
        var workplace = entry.WorkUnit ?? "";
        var income = entry.AnnualIncome > 0 ? $"{entry.AnnualIncome:N2}元/年" : "";
        var reason = ResolveAdjustShortReason(entry);

        if (isAdd)
        {
            switch (slot)
            {
                case 1:
                    _fieldData[FieldKeys.ADJ_ADD_NAME_1] = name;
                    _fieldData[FieldKeys.ADJ_ADD_ID_CARD_1] = idCard;
                    _fieldData[FieldKeys.ADJ_ADD_GENDER_1] = gender;
                    _fieldData[FieldKeys.ADJ_ADD_RELATION_1] = relation;
                    _fieldData[FieldKeys.ADJ_ADD_HEALTH_1] = health;
                    _fieldData[FieldKeys.ADJ_ADD_WORKPLACE_1] = workplace;
                    _fieldData[FieldKeys.ADJ_ADD_REASON_1] = reason;
                    _fieldData[FieldKeys.ADJ_ADD_INCOME_1] = income;
                    break;
                case 2:
                    _fieldData[FieldKeys.ADJ_ADD_NAME_2] = name;
                    _fieldData[FieldKeys.ADJ_ADD_ID_CARD_2] = idCard;
                    _fieldData[FieldKeys.ADJ_ADD_GENDER_2] = gender;
                    _fieldData[FieldKeys.ADJ_ADD_RELATION_2] = relation;
                    _fieldData[FieldKeys.ADJ_ADD_HEALTH_2] = health;
                    _fieldData[FieldKeys.ADJ_ADD_WORKPLACE_2] = workplace;
                    _fieldData[FieldKeys.ADJ_ADD_REASON_2] = reason;
                    _fieldData[FieldKeys.ADJ_ADD_INCOME_2] = income;
                    break;
                case 3:
                    _fieldData[FieldKeys.ADJ_ADD_NAME_3] = name;
                    _fieldData[FieldKeys.ADJ_ADD_ID_CARD_3] = idCard;
                    _fieldData[FieldKeys.ADJ_ADD_GENDER_3] = gender;
                    _fieldData[FieldKeys.ADJ_ADD_RELATION_3] = relation;
                    _fieldData[FieldKeys.ADJ_ADD_HEALTH_3] = health;
                    _fieldData[FieldKeys.ADJ_ADD_WORKPLACE_3] = workplace;
                    _fieldData[FieldKeys.ADJ_ADD_REASON_3] = reason;
                    _fieldData[FieldKeys.ADJ_ADD_INCOME_3] = income;
                    break;
            }
            return;
        }

        switch (slot)
        {
            case 1:
                _fieldData[FieldKeys.ADJ_REMOVE_NAME_1] = name;
                _fieldData[FieldKeys.ADJ_REMOVE_ID_CARD_1] = idCard;
                _fieldData[FieldKeys.ADJ_REMOVE_GENDER_1] = gender;
                _fieldData[FieldKeys.ADJ_REMOVE_RELATION_1] = relation;
                _fieldData[FieldKeys.ADJ_REMOVE_HEALTH_1] = health;
                _fieldData[FieldKeys.ADJ_REMOVE_WORKPLACE_1] = workplace;
                _fieldData[FieldKeys.ADJ_REMOVE_REASON_1] = reason;
                _fieldData[FieldKeys.ADJ_REMOVE_INCOME_1] = income;
                break;
            case 2:
                _fieldData[FieldKeys.ADJ_REMOVE_NAME_2] = name;
                _fieldData[FieldKeys.ADJ_REMOVE_ID_CARD_2] = idCard;
                _fieldData[FieldKeys.ADJ_REMOVE_GENDER_2] = gender;
                _fieldData[FieldKeys.ADJ_REMOVE_RELATION_2] = relation;
                _fieldData[FieldKeys.ADJ_REMOVE_HEALTH_2] = health;
                _fieldData[FieldKeys.ADJ_REMOVE_WORKPLACE_2] = workplace;
                _fieldData[FieldKeys.ADJ_REMOVE_REASON_2] = reason;
                _fieldData[FieldKeys.ADJ_REMOVE_INCOME_2] = income;
                break;
            case 3:
                _fieldData[FieldKeys.ADJ_REMOVE_NAME_3] = name;
                _fieldData[FieldKeys.ADJ_REMOVE_ID_CARD_3] = idCard;
                _fieldData[FieldKeys.ADJ_REMOVE_GENDER_3] = gender;
                _fieldData[FieldKeys.ADJ_REMOVE_RELATION_3] = relation;
                _fieldData[FieldKeys.ADJ_REMOVE_HEALTH_3] = health;
                _fieldData[FieldKeys.ADJ_REMOVE_WORKPLACE_3] = workplace;
                _fieldData[FieldKeys.ADJ_REMOVE_REASON_3] = reason;
                _fieldData[FieldKeys.ADJ_REMOVE_INCOME_3] = income;
                break;
        }
    }

    /// <summary>
    /// 增减员槽位短原因（模板格子仅 6 字，禁止写 change_reason 原文长句）：
    /// 优先逐人登记的固定选项短语（成员增减流程，均 ≤6 字）；死亡类流程无登记值，
    /// 按变更类型兜底；其余留空（打印端显示 "-"，不编造原因）。
    /// </summary>
    private static string ResolveAdjustShortReason(MemberAdjustEntry entry)
    {
        var reason = entry.ReasonName?.Trim() ?? "";
        if (reason.Length == 0)
        {
            reason = entry.ChangeType switch
            {
                DictionaryConstants.ChangeType.HOUSEHOLD_DEATH => "原户主死亡",
                DictionaryConstants.ChangeType.MEMBER_DEATH => "人员死亡",
                _ => ""
            };
        }
        return reason.Length > 6 ? reason[..6] : reason;
    }

    /// <summary>性别展示值：身份证号推导优先，无有效证件号时回退登记值</summary>
    private static string ResolveGender(string? idCard, string? fallback)
    {
        var gender = AddressResolver.ExtractGenderFromIdCard(idCard);
        return gender == "-" ? (fallback ?? "") : gender;
    }

    private SidebarItem CreateSidebarItem(string label, string value, Dictionary<string, string> fieldMapping)
    {
        var fieldKey = fieldMapping.GetValueOrDefault(label, "");
        var isMatchable = !string.IsNullOrEmpty(fieldKey);
        var isAutoFilled = isMatchable && !string.IsNullOrWhiteSpace(value);

        if (isAutoFilled)
            AutoFilledCount++;

        return new SidebarItem
        {
            Label = label,
            Value = value ?? string.Empty,
            FieldKey = fieldKey,
            IsMatchable = isMatchable,
            IsAutoFilled = isAutoFilled
        };
    }

    // ========================
    //  辅助方法
    // ========================

    private async Task<string> GetCivilAssistantNameAsync()
    {
        try
        {
            var userId = App.CurrentUserId;
            if (!userId.HasValue) return "未配";

            var userService = _serviceProvider.GetRequiredService<IUserService>();
            var userResult = await userService.GetByIdAsync(userId.Value);
            if (userResult.IsSuccess && userResult.Value != null)
                return userResult.Value.FullName ?? "未配";

            return "未配";
        }
        catch
        {
            return "未配";
        }
    }

    private async Task<OrganizationInfoDto> GetOrganizationInfoAsync()
    {
        var orgId = App.CurrentUserOrganizationId;
        if (!orgId.HasValue)
            return new OrganizationInfoDto("-", "-", "-", "-", "-", "-");

        try
        {
            var orgResult = await _organizationService.GetByIdAsync(orgId.Value);
            if (orgResult.IsFailure || orgResult.Value == null)
                return new OrganizationInfoDto("-", "-", "-", "-", "-", "-");

            var org = orgResult.Value;
            var district = org.CountyName ?? "-";
            var town = org.TownName ?? "-";
            var unitName = org.Name ?? "-";
            var unitPhone = org.Phone ?? "-";
            var parentName = org.ParentName ?? "-";
            var parentPhone = "-";

            if (org.ParentId.HasValue)
            {
                var parentResult = await _organizationService.GetByIdAsync(org.ParentId.Value);
                if (parentResult.IsSuccess && parentResult.Value != null)
                    parentPhone = parentResult.Value.Phone ?? "-";
            }

            return new OrganizationInfoDto(district, town, parentName, parentPhone, unitName, unitPhone);
        }
        catch
        {
            return new OrganizationInfoDto("-", "-", "-", "-", "-", "-");
        }
    }

    private string GenerateNoticeNumber()
    {
        var nextMonth = DateTime.Now.AddMonths(1);
        return $"{nextMonth.Year}-{nextMonth.Month}-1";
    }

    private string GetIdTypeDisplay(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return IdTypeConstants.DefaultIdType;
        return _dictCacheService.GetValue(DictionaryTypeCodes.IdTypes, code);
    }

    private string GetApplicationReasonDisplay(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return PickerConstants.ApplicationReason.OtherDisplay;
        return _dictCacheService.GetValue(DictionaryTypeCodes.ApplicationReasons, code);
    }

    /// <summary>
    /// 构建户主条目（用于 _tableData 第 1 位）
    /// </summary>
    private Dictionary<string, string> BuildHeadMemberEntry(string name, string idCard, string address)
    {
        return new Dictionary<string, string>
        {
            [FieldKeys.FAMILY_MEMBER_NAME] = name,
            [FieldKeys.FAMILY_MEMBER_ID_CARD] = idCard,
            [FieldKeys.FAMILY_MEMBER_CERT_TYPE] = "居民身份证",
            [FieldKeys.FAMILY_MEMBER_RELATION] = "本人/户主",
            [FieldKeys.FAMILY_MEMBER_ADDRESS] = address,
            [FieldKeys.HEAD_ID_CARD] = idCard
        };
    }

    /// <summary>
    /// 构建家庭成员条目
    /// </summary>
    private Dictionary<string, string> BuildMemberEntry(string name, string idCard, string relationship, string address, string headIdCard)
    {
        return new Dictionary<string, string>
        {
            [FieldKeys.FAMILY_MEMBER_NAME] = name,
            [FieldKeys.FAMILY_MEMBER_ID_CARD] = idCard,
            [FieldKeys.FAMILY_MEMBER_CERT_TYPE] = _dictCacheService.GetValue(DictionaryTypeCodes.IdTypes, "ResidentIdCard"),
            [FieldKeys.FAMILY_MEMBER_RELATION] = _dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, relationship),
            [FieldKeys.FAMILY_MEMBER_ADDRESS] = address,
            [FieldKeys.HEAD_ID_CARD] = headIdCard
        };
    }

    /// <summary>
    /// 格式化数值：始终输出带两位小数的格式
    /// </summary>
    private static string FormatDecimal(decimal value)
    {
        return value.ToString("N2");
    }

    /// <summary>
    /// 构建土地收入情况（复合字段）
    /// 格式：本人申请{享受类别}待遇，由{所在村屯}出具土地收入证明...
    /// includeLead=false 时跳过"本人申请…口人。"引言段（定期复核审批表专用，从"土地情况"开始）。
    /// </summary>
    private async Task<string> BuildLandIncomeSituationAsync(ApplicationEntity app, bool includeLead = true)
    {
        var classificationDesc = ClassificationConstants.ConvertFromCode(app.ClassificationResult ?? "");
        var village = app.Community ?? app.Town ?? "";
        var headName = app.ApplicantName;
        var familySize = app.FamilySize;

        // 土地面积
        var selfFarmedArea = app.SelfFarmedLandArea;
        var subleasedArea = app.SubleasedLandArea;
        var contractedArea = app.ContractedLandArea;

        // 土地作物收入：直接从数据库读取（已由经济明细核算）
        var cropIncome = app.LandIncomeTotal;

        // 补贴金额（从经济明细查询）
        decimal landFertilityAmount = 0;   // 地力补贴
        decimal soybeanAmount = 0;         // 大豆补贴
        decimal rotationAmount = 0;        // 轮作补贴
        decimal riceAmount = 0;            // 种植补贴

        try
        {
            var economicDetailService = _serviceProvider.GetRequiredService<IEconomicDetailService>();
            var detailResult = await economicDetailService.LoadAllAsync(app.Id, CancellationToken.None);
            if (detailResult.IsSuccess && detailResult.Value?.Subsidies != null)
            {
                foreach (var subsidy in detailResult.Value.Subsidies)
                {
                    switch (subsidy.SubsidyType)
                    {
                        case "地力补贴": landFertilityAmount += subsidy.Amount; break;
                        case "大豆补贴": soybeanAmount += subsidy.Amount; break;
                        case "轮作补贴": rotationAmount += subsidy.Amount; break;
                        case "地表水水稻":
                        case "地下水水稻":
                        case "玉米补贴": riceAmount += subsidy.Amount; break;
                    }
                }
            }
        }
        catch { }

        // 全家土地总计收入 = 作物收入（数据库）+ 补贴
        var totalLandIncome = cropIncome + landFertilityAmount + soybeanAmount + rotationAmount + riceAmount;

        var sb = new System.Text.StringBuilder();
        if (includeLead)
        {
            sb.Append($"本人申请{classificationDesc}待遇，由{village}（村/居民委员会）出具土地收入证明，");
            sb.Append($"户主{headName}申请享受{familySize}口人。");
            sb.AppendLine();
        }
        sb.Append($"土地情况：自种{selfFarmedArea:F2}亩；转包{subleasedArea:F2}亩；承包{contractedArea:F2}亩。");
        sb.AppendLine();
        sb.Append($"土地收入情况：土地作物收入：{FormatDecimal(cropIncome)}元；");
        sb.Append($"粮食补贴:{FormatDecimal(riceAmount)}元；");
        sb.Append($"土地轮作补贴：{FormatDecimal(rotationAmount)}元；");
        sb.Append($"高脂高油补贴：{FormatDecimal(soybeanAmount)}元；");
        sb.Append($"地力补贴：{FormatDecimal(landFertilityAmount)}元。");
        sb.AppendLine();
        sb.Append($"全家土地总计收入为{FormatDecimal(totalLandIncome)}元。（标准亩666.7方）—特此证明。");

        return sb.ToString();
    }

    /// <summary>
    /// 定期复核审批表复核组装配：{待遇变化分类}/{待遇变化金额}/{复核时间}/{定期复核情况}。
    /// 取最近一条复核/变更记录（经济复核、户主死亡、成员变更等完整流程均覆盖）；
    /// 无记录时保持初始化器默认值 ""（打印端按占位符缺省显示 "-"，不会报未映射）。
    /// </summary>
    private async Task ApplyReviewGroupAsync(ApplicationEntity app, long applicationId)
    {
        var reviewRes = await _changeService.GetLatestReviewChangeRecordAsync(applicationId, CancellationToken.None);
        if (reviewRes.IsSuccess && reviewRes.Value != null)
        {
            var rv = reviewRes.Value;
            var (action, amountTail) = BuildReviewAmountChange(app, rv);
            _fieldData[FieldKeys.REVIEW_CLASSIFICATION_CHANGE] = action;
            _fieldData[FieldKeys.REVIEW_AMOUNT_CHANGE] = amountTail;
            _fieldData[FieldKeys.REVIEW_TIME] = rv.ChangeDate?.ToString("yyyy年M月d日") ?? "";

            // 定期复核情况 = 复核原因 + 变化内容（人口/月人均收入/类别，只列变化项）+ 核算过程
            _fieldData[FieldKeys.REVIEW_SITUATION] = await BuildReviewChangeDetailAsync(app, rv);
        }
        else if (reviewRes.IsFailure)
        {
            _logger.Warn($"定期复核审批表取复核记录失败: ApplicationId={applicationId}, {reviewRes.Message}");
        }
    }

    /// <summary>
    /// 待遇变化句：返回 {待遇变化分类} 动作词 + {待遇变化金额} 句尾（以「，」起头）。
    /// 户主死亡流程只留两个金额（原户主金额停发 + 本档案现保障金）——姓名在「户主姓名」与「定期复核情况」里已有，
    /// 类别在「保障类型」里已有，句尾不再重复；死亡记录的旧→新描述的是旧档停保，现保障金必须取被打印档案。
    /// 经济复核等按旧→新金额写增发/减发/保持/认定/停发。
    /// </summary>
    private (string Action, string AmountTail) BuildReviewAmountChange(ApplicationEntity app, ReviewChangeRecord rv)
    {
        var oldClsFull = ClassificationConstants.ConvertToFullName(rv.OldClassification ?? "");
        var newClsFull = ClassificationConstants.ConvertToFullName(rv.NewClassification ?? "");
        var oldAmt = rv.OldGuaranteeAmount;
        var newAmt = rv.NewGuaranteeAmount;

        if (rv.ChangeType == "HouseholdDeath" || rv.ChangeReasonType == "户主死亡")
        {
            var tail = oldAmt.HasValue ? $"，原户主{FormatDecimal(oldAmt.Value)}元停发" : "，原户主待遇停发";
            if (!ClassificationConstants.IsCodeStop(app.ClassificationResult ?? ""))
            {
                tail += $"，现保障金{FormatDecimal(app.HouseholdMonthlyGuaranteeAmount)}元";
            }
            return ("停发", tail);
        }

        string action;
        string amountTail;
        if (ClassificationConstants.IsCodeStop(rv.NewClassification ?? ""))
        {
            action = "停发";
            amountTail = oldAmt.HasValue ? $"，原月保障金{FormatDecimal(oldAmt.Value)}元自该月起停止发放" : "";
        }
        else if (oldAmt.HasValue && newAmt.HasValue && newAmt.Value > oldAmt.Value)
        {
            action = "增发";
            amountTail = $"，月保障金由{FormatDecimal(oldAmt.Value)}元调整为{FormatDecimal(newAmt.Value)}元（增发{FormatDecimal(newAmt.Value - oldAmt.Value)}元）";
        }
        else if (oldAmt.HasValue && newAmt.HasValue && newAmt.Value < oldAmt.Value)
        {
            action = "减发";
            amountTail = $"，月保障金由{FormatDecimal(oldAmt.Value)}元调整为{FormatDecimal(newAmt.Value)}元（减发{FormatDecimal(oldAmt.Value - newAmt.Value)}元）";
        }
        else if (oldAmt.HasValue && newAmt.HasValue)
        {
            action = "保持";
            amountTail = $"，月保障金维持{FormatDecimal(newAmt.Value)}元";
        }
        else if (newAmt.HasValue)
        {
            // 类别增类（停旧与建新各一条记录，本条只带新档金额）
            action = "认定";
            amountTail = $"，月保障金{FormatDecimal(newAmt.Value)}元";
        }
        else
        {
            _logger.Warn($"待遇变化句缺值降级: ApplicationId={app.Id}, ChangeType={rv.ChangeType}, OldAmt={(oldAmt.HasValue ? oldAmt.Value.ToString() : "null")}, NewAmt={(newAmt.HasValue ? newAmt.Value.ToString() : "null")}");
            action = "保持";
            amountTail = "，不增不减";
        }

        if (!string.IsNullOrEmpty(oldClsFull) && !string.IsNullOrEmpty(newClsFull) && oldClsFull != newClsFull)
        {
            amountTail += $"，享受类别由{oldClsFull}调整为{newClsFull}";
        }
        else if (string.IsNullOrEmpty(oldClsFull) && !string.IsNullOrEmpty(newClsFull))
        {
            amountTail += $"，类别认定为{newClsFull}";
        }

        return (action, amountTail);
    }

    /// <summary>
    /// 读取变更快照 JSON 的整型键（缺键/非数字/坏 JSON 返回 null，不抛）。
    /// </summary>
    private static int? ReadSnapshotInt(string? json, string key)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(key, out var v)
                && v.ValueKind == JsonValueKind.Number
                && v.TryGetInt32(out var i))
            {
                return i;
            }
        }
        catch (JsonException) { }
        return null;
    }

    /// <summary>
    /// 读取变更快照 JSON 的字符串键（缺键/非字符串/坏 JSON 返回 null，不抛）。
    /// </summary>
    private static string? ReadSnapshotString(string? json, string key)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty(key, out var v)
                && v.ValueKind == JsonValueKind.String)
            {
                return v.GetString();
            }
        }
        catch (JsonException) { }
        return null;
    }

    /// <summary>
    /// 户主户籍地址：优先取主表 HukouAddress 原文；为空时按户籍区域ID经 IRegionService 逐级取名拼「市+县+镇+村」
    /// （与既有数据格式一致，不含省）；ID 也为空返回 ""（打印端按占位符缺省显示 "-"）。
    /// 只补户籍侧、绝不回退现住地址成分；查询失败记 Warn 后返回 ""。
    /// </summary>
    private async Task<string> ResolveHukouAddressAsync(ApplicationEntity app)
    {
        var direct = app.HukouAddress?.Trim();
        if (!string.IsNullOrEmpty(direct)) return direct;

        var hasAnyId = app.HukouCityId is > 0 || app.HukouCountyId is > 0 || app.HukouTownId is > 0 || app.HukouVillageId is > 0;
        if (!hasAnyId) return "";

        try
        {
            var regionService = _serviceProvider.GetRequiredService<IRegionService>();
            var sb = new StringBuilder(64);

            if (app.HukouCityId is > 0)
            {
                var r = await regionService.GetCityByIdAsync(app.HukouCityId.Value);
                if (r.IsSuccess && r.Value != null) sb.Append(r.Value.CityName);
                else if (r.IsFailure) _logger.Warn($"户籍地址-市级区域名查询失败（ApplicationId={app.Id}, CityId={app.HukouCityId}, {r.Message}）");
            }
            if (app.HukouCountyId is > 0)
            {
                var r = await regionService.GetCountyByIdAsync(app.HukouCountyId.Value);
                if (r.IsSuccess && r.Value != null) sb.Append(r.Value.CountyName);
                else if (r.IsFailure) _logger.Warn($"户籍地址-县级区域名查询失败（ApplicationId={app.Id}, CountyId={app.HukouCountyId}, {r.Message}）");
            }
            if (app.HukouTownId is > 0)
            {
                var r = await regionService.GetTownByIdAsync(app.HukouTownId.Value);
                if (r.IsSuccess && r.Value != null) sb.Append(r.Value.TownName);
                else if (r.IsFailure) _logger.Warn($"户籍地址-乡镇区域名查询失败（ApplicationId={app.Id}, TownId={app.HukouTownId}, {r.Message}）");
            }
            if (app.HukouVillageId is > 0)
            {
                var r = await regionService.GetVillageByIdAsync(app.HukouVillageId.Value);
                if (r.IsSuccess && r.Value != null) sb.Append(r.Value.VillageName);
                else if (r.IsFailure) _logger.Warn($"户籍地址-村级区域名查询失败（ApplicationId={app.Id}, VillageId={app.HukouVillageId}, {r.Message}）");
            }

            return sb.ToString();
        }
        catch (Exception ex)
        {
            _logger.Warn($"户籍地址区域名拼接失败（ApplicationId={app.Id}）: {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// 定期复核审批表：变化内容拼句 = 复核原因。变化情况：人口/月人均收入/类别（只列变化项，全无变化则"无明显变化"）。
    /// 快照挂 change_record.application_id：停旧建新链在旧档（OriginalApplicationId），同档复核在本档；收入对比双挂新旧档。
    /// 查询失败显式降级为原因原文并记业务日志（不吞异常、不返回空串）。
    /// </summary>
    private async Task<string> BuildReviewChangeDetailAsync(ApplicationEntity app, ReviewChangeRecord review)
    {
        var reason = string.IsNullOrWhiteSpace(review.ChangeReason) ? "定期复核" : review.ChangeReason;
        var parts = new List<string>();
        string core;

        try
        {
            var snapOwner = app.OriginalApplicationId > 0 ? app.OriginalApplicationId : app.Id;
            var snapRes = await _changeService.GetLatestBeforeSnapshotAsync(snapOwner, CancellationToken.None);
            var snap = snapRes.IsSuccess ? snapRes.Value : null;
            if (snapRes.IsFailure)
            {
                _logger.LogBusiness("定期复核变化内容-快照查询失败降级", ("ApplicationId", app.Id.ToString()), ("ErrorCode", snapRes.ErrorCode ?? ""));
            }
            else if (snap != null && snap.OldFamilySize is > 0 && snap.OldFamilySize != app.FamilySize)
            {
                parts.Add($"家庭人口由{snap.OldFamilySize}人变为{app.FamilySize}人");
            }

            // Before/After 快照：家庭人口与死亡原因（死亡/成员变更链的快照无 Components 键，
            // GetLatestBeforeSnapshotAsync 取不到，须按 change_id 另取）
            var hasFamilySizePart = parts.Any(p => p.StartsWith("家庭人口"));
            var pairRes = await _changeService.GetChangeSnapshotsAsync(review.Id, CancellationToken.None);
            if (pairRes.IsFailure)
            {
                _logger.LogBusiness("定期复核变化内容-变更快照查询失败降级", ("ApplicationId", app.Id.ToString()), ("ErrorCode", pairRes.ErrorCode ?? ""));
            }
            else
            {
                if (pairRes.Value is null && app.OriginalApplicationId > 0)
                {
                    // 停旧建新链兜底：当前复核记录可能是无快照的 CategoryAdd（经济复核跨类行/Step5 跨类补写行），
                    // 死亡原因与家庭人口快照在链上发起记录（如户主死亡记录）——按本档案关联记录回退取一次
                    var linkedRes = await _changeService.GetLinkedChangeSnapshotsAsync(app.Id, CancellationToken.None);
                    if (linkedRes.IsFailure)
                        _logger.LogBusiness("定期复核变化内容-链上快照兜底查询失败降级", ("ApplicationId", app.Id.ToString()), ("ErrorCode", linkedRes.ErrorCode ?? ""));
                    else
                        pairRes = linkedRes;
                }

                if (pairRes.Value is { } pair)
                {
                    var beforeSize = ReadSnapshotInt(pair.BeforeJson, "FamilySize");
                    if (!hasFamilySizePart && beforeSize is > 0 && beforeSize != app.FamilySize)
                    {
                        parts.Add($"家庭人口由{beforeSize}人变为{app.FamilySize}人");
                    }

                    var deathReason = ReadSnapshotString(pair.AfterJson, "DeathReason");
                    if (!string.IsNullOrEmpty(deathReason))
                    {
                        parts.Add($"死亡原因：{deathReason}");
                    }
                }
            }

            var compRes = await _changeService.GetLatestIncomeComparisonAsync(app.Id, CancellationToken.None);
            if (compRes.IsFailure)
            {
                _logger.LogBusiness("定期复核变化内容-收入对比查询失败降级", ("ApplicationId", app.Id.ToString()), ("ErrorCode", compRes.ErrorCode ?? ""));
            }
            else if (compRes.Value is { } comp && comp.OldPerCapitaIncome.HasValue && comp.NewPerCapitaIncome.HasValue
                     && comp.OldPerCapitaIncome.Value != comp.NewPerCapitaIncome.Value)
            {
                parts.Add($"月人均收入由{FormatDecimal(comp.OldPerCapitaIncome.Value)}元变为{FormatDecimal(comp.NewPerCapitaIncome.Value)}元");
            }

            var oldCls = ClassificationConstants.ConvertToFullName(review.OldClassification ?? "");
            var newCls = ClassificationConstants.ConvertToFullName(review.NewClassification ?? "");
            if (!string.IsNullOrEmpty(oldCls) && !string.IsNullOrEmpty(newCls) && oldCls != newCls)
            {
                parts.Add($"类别由{oldCls}变为{newCls}");
            }
            else if (!string.IsNullOrEmpty(oldCls) && string.IsNullOrEmpty(newCls))
            {
                // 死亡/停保链：新类别为空表示旧档待遇终止
                parts.Add($"原{oldCls}待遇停止");
            }

            core = parts.Count > 0
                ? $"{reason}。变化情况：{string.Join("；", parts)}。"
                : $"{reason}。家庭情况较上期无明显变化。";
        }
        catch (Exception ex)
        {
            _logger.Warn($"定期复核变化内容拼句降级: ApplicationId={app.Id}, {ex.Message}");
            core = reason;
        }

        return core + await BuildReviewCalculationAsync(app);
    }

    /// <summary>
    /// 定期复核审批表：核算过程（年值口径单点展示，只展示不重算——保障金属审批结果，见 AGENTS §7.5）。
    /// 低保标准单点读 IStandardConfigService；缺失/无效时 LogWarn 并省略该子句，禁止硬编码回退值。
    /// 补差算式仅在现类别为低保（Subsistence）时展示（标准×人数−月收入 进一取整，与 ClassificationService 同式），
    /// 与现保障金不等时标注"经审批核定"；低保边缘/特困等类别只展示标准与现保障金，避免出现自相矛盾的算式。
    /// </summary>
    private async Task<string> BuildReviewCalculationAsync(ApplicationEntity app)
    {
        try
        {
            var familySize = app.FamilySize > 0 ? app.FamilySize : 1;
            var isRural = ClassificationConstants.HukouType.IsHukouRural(app.HukouType);
            var currentCls = ClassificationConstants.ConvertToFullName(app.ClassificationResult ?? "");
            var sb = new StringBuilder(256);

            sb.Append($"核算过程：家庭年收入{FormatDecimal(app.TotalAnnualIncome)}元"
                + "（务工/经营/财产/转移/其他按月×12，赡养、土地、补贴按年，减刚性支出×12），"
                + $"人均年收入{FormatDecimal(app.PerCapitaAnnualIncome)}元＝{FormatDecimal(app.TotalAnnualIncome)}÷{familySize}人，"
                + $"折人均月收入{FormatDecimal(app.PerCapitaIncome)}元；");

            var stdType = isRural ? "RuralSubsistenceStandard" : "UrbanSubsistenceStandard";
            var stdHukou = isRural ? "Rural" : "Urban";
            var stdResult = await _standardConfigService.GetStandardValueAsync(stdType, stdHukou);
            if (stdResult.IsSuccess && stdResult.Value > 0)
            {
                var standard = stdResult.Value;
                var threshold = standard * ClassificationConstants.LowIncomeMultiplier;
                sb.Append($"{(isRural ? "农村" : "城市")}低保标准{FormatDecimal(standard)}元/月"
                    + $"（低收入认定为其1.5倍{FormatDecimal(threshold)}元）；");

                if (ClassificationConstants.IsCodeSubsistence(app.ClassificationResult ?? ""))
                {
                    var gap = Math.Ceiling(standard * familySize - app.TotalFamilyIncome);
                    var guarantee = app.HouseholdMonthlyGuaranteeAmount;
                    sb.Append($"按低保标准×家庭人数−家庭月收入＝{FormatDecimal(standard)}×{familySize}−{FormatDecimal(app.TotalFamilyIncome)}"
                        + $"＝{FormatDecimal(gap)}元（进一取整到元），");
                    sb.Append(guarantee == gap
                        ? $"核定月保障金{FormatDecimal(guarantee)}元；"
                        : $"经审批核定月保障金{FormatDecimal(guarantee)}元；");
                }
            }
            else
            {
                _logger.Warn($"定期复核核算过程-低保标准缺失或无效，省略标准子句（{stdType}/{stdHukou}）: {stdResult.Message}");
            }

            sb.Append($"该家庭现类别{currentCls}，家庭月总收入{FormatDecimal(app.TotalFamilyIncome)}元，"
                + $"月保障金{FormatDecimal(app.HouseholdMonthlyGuaranteeAmount)}元。");
            return " " + sb;
        }
        catch (Exception ex)
        {
            _logger.Warn($"定期复核核算过程拼句降级: ApplicationId={app.Id}, {ex.Message}");
            return "";
        }
    }

    /// <summary>
    /// 构建单人保情况
    /// </summary>
    private string BuildSingleRescueSituation(ApplicationEntity app)
    {
        if (app.ClassificationResult?.Contains("Single") == true)
        {
            var amount = FormatDecimal(app.HouseholdMonthlyGuaranteeAmount);
            return $"该家庭成员因存在重度残疾、重大疾病或三级智力/精神残疾等特殊情况，符合单人保相关政策，纳入单人保保障范围，月保障金额{amount}元。";
        }
        return "-";
    }

    /// <summary>
    /// 构建家庭财产豁免情况（列出具体豁免财产明细）
    /// </summary>
    private string BuildPropertyExemptionSituation(ApplicationEntity app, EconomicDetailData? economicDetail)
    {
        try
        {
            if (economicDetail == null)
                return "-";

            var exemptions = new List<string>();

            // 1. 车辆豁免
            if (economicDetail.Vehicles != null && economicDetail.Vehicles.Count > 0)
            {
                foreach (var v in economicDetail.Vehicles)
                {
                    var value = FormatDecimal(v.EstimatedValue);
                    exemptions.Add($"车辆：{v.Brand} {v.Model}（{v.LicensePlate}），估值{value}元，因生活必需用途予以豁免");
                }
            }

            // 2. 金融资产豁免
            if (economicDetail.FinancialAssets != null && economicDetail.FinancialAssets.Count > 0)
            {
                var fa = economicDetail.FinancialAssets[0];
                if (fa.HasBankDeposit && fa.BankDepositAmount > 0)
                {
                    exemptions.Add($"银行存款：{FormatDecimal(fa.BankDepositAmount)}元，因符合低保/低收入存款限额标准予以豁免");
                }
                if (fa.HasSecurities && fa.SecuritiesAmount > 0)
                {
                    exemptions.Add($"有价证券：{FormatDecimal(fa.SecuritiesAmount)}元，因符合低保/低收入证券限额标准予以豁免");
                }
            }

            // 3. 房产豁免
            if (economicDetail.FamilyProperties != null && economicDetail.FamilyProperties.Count > 0)
            {
                foreach (var p in economicDetail.FamilyProperties)
                {
                    var area = FormatDecimal(p.Area);
                    exemptions.Add($"房产：{p.Address}（{p.HousingStructure}，{area}㎡），因属唯一住房且面积符合标准予以豁免");
                }
            }

            if (exemptions.Count > 0)
            {
                return $"经核查，该家庭以下财产符合豁免条件：\n{string.Join("\n", exemptions.Select((e, i) => $"  {i + 1}. {e}"))}。";
            }

            return "-";
        }
        catch
        {
            return "-";
        }
    }

    /// <summary>
    /// 构建近亲属备案情况
    /// </summary>
    private string BuildKinshipFilingSituation()
    {
        return "";
    }

    // ========================
    //  特困入户调查表专用字段构建方法
    // ========================

    /// <summary>
    /// 构建特困照料人情况
    /// </summary>
    private string BuildDestituteCaregiverSituation(ApplicationEntity app, List<Caregiver>? caregivers)
    {
        if (app.DestituteSupportType == "Centralized" || app.DestituteSupportType == ClassificationConstants.SupportMode.CENTRALIZED)
        {
            return "集中供养，由专业机构提供照料服务";
        }

        if (caregivers != null && caregivers.Count > 0)
        {
            var caregiverNames = string.Join("、", caregivers.Select(c => c.Name));
            return $"分散供养，由{caregiverNames}提供日常照料";
        }

        return "分散供养，由亲属提供日常照料";
    }

    /// <summary>
    /// 构建特困健康评估
    /// </summary>
    private string BuildDestituteHealthAssessment(ApplicationEntity app, List<FamilyMember>? members)
    {
        var sb = new System.Text.StringBuilder();

        // 户主健康状况
        sb.AppendLine($"户主自理能力: {BuildSelfCareAbility(app)}");
        sb.AppendLine($"健康状况: {app.HealthStatus ?? "未填写"}");

        if (!string.IsNullOrEmpty(app.DisabilityType))
        {
            sb.AppendLine($"残疾类型: {app.DisabilityType} {app.DisabilityLevel}");
        }

        if (!string.IsNullOrEmpty(app.DiseaseName))
        {
            sb.AppendLine($"疾病情况: {app.DiseaseName}");
        }

        // 家庭成员健康概况
        if (members != null && members.Count > 0)
        {
            var disabledCount = members.Count(m => m.IsDisabled);
            var diseasedCount = members.Count(m => !string.IsNullOrEmpty(m.DiseaseName));

            if (disabledCount > 0 || diseasedCount > 0)
            {
                sb.AppendLine($"家庭成员健康概况: 残疾{disabledCount}人, 患病{diseasedCount}人");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 构建特困居住条件
    /// </summary>
    private string BuildDestituteLivingCondition(ApplicationEntity app, EconomicDetailData? economicDetail)
    {
        var sb = new System.Text.StringBuilder();

        if (economicDetail?.FamilyProperties != null && economicDetail.FamilyProperties.Count > 0)
        {
            var property = economicDetail.FamilyProperties[0];
            sb.AppendLine($"住房类型: {property.HousingStructure ?? "未知"}");
            sb.AppendLine($"住房面积: {FormatDecimal(property.Area)}㎡");
            sb.AppendLine($"住房性质: {property.HousingNature ?? "未知"}");
        }
        else
        {
            sb.AppendLine("住房情况: 未填写");
        }

        sb.AppendLine($"家庭地址: {app.Address ?? "未填写"}");

        return sb.ToString();
    }

    /// <summary>
    /// 构建特困日常照料情况
    /// </summary>
    private string BuildDestituteDailyCare(ApplicationEntity app, List<Caregiver>? caregivers)
    {
        if (app.DestituteSupportType == "Centralized" || app.DestituteSupportType == ClassificationConstants.SupportMode.CENTRALIZED)
        {
            return "由供养机构提供日常照料，包括饮食起居、清洁卫生、医疗护理等服务";
        }

        if (caregivers != null && caregivers.Count > 0)
        {
            var caregiverInfo = string.Join("；", caregivers.Select(c =>
                $"{c.Name}（{(string.IsNullOrWhiteSpace(c.Relationship) ? "亲属" : DictDisplayHelper.GetFamilyRelationshipDisplay(c.Relationship))}）"));
            return $"由{caregiverInfo}提供日常照料";
        }

        return "由亲属提供日常照料";
    }

    /// <summary>
    /// 构建特困医疗情况
    /// </summary>
    private string BuildDestituteMedicalSituation(ApplicationEntity app)
    {
        var sb = new System.Text.StringBuilder();

        if (!string.IsNullOrEmpty(app.DiseaseName))
        {
            sb.AppendLine($"主要疾病: {app.DiseaseName}");
        }

        if (!string.IsNullOrEmpty(app.DisabilityType))
        {
            sb.AppendLine($"残疾情况: {app.DisabilityType} {app.DisabilityLevel}");
        }

        sb.AppendLine("医疗保障: 城乡居民基本医疗保险");

        return sb.ToString();
    }

    /// <summary>
    /// 构建特困家庭状况
    /// </summary>
    private string BuildDestituteFamilyStatus(ApplicationEntity app, List<FamilyMember>? members)
    {
        var sb = new System.Text.StringBuilder();

        sb.AppendLine($"家庭人口: {app.FamilySize}人");
        sb.AppendLine($"户主姓名: {app.ApplicantName}");
        var hukouType = ClassificationConstants.HukouType.IsHukouRural(app.HukouType) ? "农村" : "城市";
        sb.AppendLine($"户籍性质: {hukouType}");

        if (members != null && members.Count > 0)
        {
            var ages = members.Select(m => m.Age).ToList();
            var avgAge = ages.Average();
            sb.AppendLine($"家庭成员平均年龄: {avgAge:F0}岁");

            var elderlyCount = ages.Count(a => a >= 60);
            if (elderlyCount > 0)
            {
                sb.AppendLine($"60岁以上成员: {elderlyCount}人");
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 构建特困财产状况
    /// </summary>
    private string BuildDestitutePropertySituation(ApplicationEntity app, EconomicDetailData? economicDetail)
    {
        var sb = new System.Text.StringBuilder();

        // 车辆
        if (economicDetail?.Vehicles != null && economicDetail.Vehicles.Count > 0)
        {
            var vehicles = string.Join("、", economicDetail.Vehicles.Select(v => $"{v.Brand} {v.Model}"));
            sb.AppendLine($"车辆: {vehicles}");
        }
        else
        {
            sb.AppendLine("车辆: 无");
        }

        // 金融资产
        if (economicDetail?.FinancialAssets != null && economicDetail.FinancialAssets.Count > 0)
        {
            var financial = economicDetail.FinancialAssets[0];
            sb.AppendLine($"银行存款: {FormatDecimal(financial.BankDepositAmount)}元");
        }
        else
        {
            sb.AppendLine("银行存款: 无");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 构建特困调查结论
    /// </summary>
    private string BuildDestituteSurveyConclusion(ApplicationEntity app)
    {
        var supportType = app.DestituteSupportType == "Centralized" || app.DestituteSupportType == ClassificationConstants.SupportMode.CENTRALIZED
            ? "集中供养"
            : "分散供养";

        return $"经入户调查，{app.ApplicantName}符合特困人员认定条件，建议纳入{supportType}保障范围。";
    }

    /// <summary>
    /// 构建特困供养机构信息
    /// </summary>
    private string BuildDestituteInstitutionInfo(ApplicationEntity app)
    {
        if (app.DestituteSupportType != "Centralized" && app.DestituteSupportType != ClassificationConstants.SupportMode.CENTRALIZED)
        {
            return "非集中供养";
        }

        if (app.SupportInstitutionId > 0)
        {
            return $"供养机构ID: {app.SupportInstitutionId}";
        }

        return "待指定供养机构";
    }

    /// <summary>
    /// 构建自理能力评估（三档：自理/半自理/不能自理）。优先使用能力鉴定表结论。
    /// </summary>
    private string BuildSelfCareAbility(ApplicationEntity app, CapabilityAssessment? capability = null)
    {
        if (capability != null && !string.IsNullOrEmpty(capability.SelfCareLevel))
        {
            return DictionaryConstants.CapabilityLevel.ToSelfCareDisplay(capability.SelfCareLevel);
        }

        // 兜底：按健康状况和残疾情况判断
        var healthStatus = app.HealthStatus ?? "";
        var disabilityType = app.DisabilityType ?? "";

        if (!string.IsNullOrEmpty(disabilityType))
        {
            if (disabilityType.Contains("一级") || disabilityType.Contains("重度"))
                return "不能自理";
            if (disabilityType.Contains("二级") || disabilityType.Contains("中度"))
                return "半自理";
            return "自理";
        }

        if (healthStatus.Contains("较差"))
            return "半自理";

        return "自理";
    }

    /// <summary>
    /// 构建残疾类别（两行：第一行类别、第二行等级）
    /// </summary>
    private string BuildDisabilityDisplay(ApplicationEntity app)
    {
        var typeKey = app.DisabilityType ?? "";
        var levelKey = app.DisabilityLevel ?? "";
        if (string.IsNullOrEmpty(typeKey) && string.IsNullOrEmpty(levelKey)) return "无";

        var type = _dictCacheService.GetValue(DictionaryTypeCodes.DisabilityTypes, typeKey);
        var level = _dictCacheService.GetValue(DictionaryTypeCodes.DisabilityLevels, levelKey);
        if (string.IsNullOrEmpty(type)) type = typeKey;
        if (string.IsNullOrEmpty(level)) level = levelKey;

        var lines = new List<string>();
        if (!string.IsNullOrEmpty(type)) lines.Add(type);
        if (!string.IsNullOrEmpty(level)) lines.Add(level);
        return string.Join("\n", lines);
    }

    /// <summary>
    /// 构建照料等级（随自理能力三档）
    /// </summary>
    private string BuildCareLevel(ApplicationEntity app, CapabilityAssessment? capability = null)
    {
        var selfCare = BuildSelfCareAbility(app, capability);
        if (selfCare == "不能自理")
            return "特级照料";
        if (selfCare == "半自理")
            return "一级照料";
        return "自理";
    }

    /// <summary>
    /// 构建人员类别（可组合：老年人/残疾人/未成年人）
    /// </summary>
    private string BuildPersonCategory(ApplicationEntity app)
    {
        var parts = new List<string>();
        var ageText = AddressResolver.ExtractAgeFromIdCard(app.ApplicantIdCard);
        int.TryParse(ageText, out var age);
        if (age >= 60) parts.Add("老年人");
        if (!string.IsNullOrEmpty(app.DisabilityType)) parts.Add("残疾人");
        if (age >= 0 && age < 18) parts.Add("未成年人");
        return parts.Count > 0 ? string.Join("、", parts) : "-";
    }

    /// <summary>
    /// 构建供养方式显示文本
    /// </summary>
    private string BuildDestituteSupportTypeDisplay(ApplicationEntity app)
    {
        if (app.DestituteSupportType == "Centralized" || app.DestituteSupportType == ClassificationConstants.SupportMode.CENTRALIZED)
            return "集中供养";
        if (app.DestituteSupportType == "Scattered" || app.DestituteSupportType == ClassificationConstants.SupportMode.SCATTERED)
            return "分散供养";
        return app.DestituteSupportType ?? "未确定";
    }

    /// <summary>
    /// 构建刚性支出扣减明细
    /// </summary>
    private string BuildRigidExpenditureDisplay(ApplicationEntity app, EconomicDetailData? economicDetail)
    {
        if (economicDetail?.RigidExpenditures == null || economicDetail.RigidExpenditures.Count == 0)
            return "-";

        var parts = new List<string>();
        foreach (var item in economicDetail.RigidExpenditures)
        {
            var typeName = RigidExpenditureConstants.GetDescription(item.ExpenditureType);
            parts.Add($"{typeName}{item.PersonDescription}{FormatDecimal(item.Amount * 12)}元{(!string.IsNullOrEmpty(item.Remark) ? $"（{item.Remark}）" : "")}");
        }
        return string.Join("；", parts) + "。";
    }

    /// <summary>
    /// 构建调查走访情况
    /// </summary>
    private string BuildSurveyVisitSituation(ApplicationEntity app)
    {
        return $"经入户调查，该家庭确属困难家庭，符合{(ClassificationConstants.ConvertFromCode(app.ClassificationResult ?? ""))}条件。";
    }

    /// <summary>
    /// 生成勾选框+标签："申请家庭（☑是 □否）xxx"
    /// </summary>
    private static string BuildCheckboxWithLabel(bool isChecked, string label)
    {
        var yes = isChecked ? "☑是" : "□是";
        var no = isChecked ? "□否" : "☑否";
        return $"申请家庭（{yes} {no}）{label}";
    }

    /// <summary>
    /// 生成简单勾选框："☑是 □否" 或 "□是 ☑否"
    /// </summary>
    private static string BuildYesNoCheckbox(bool value)
    {
        return value ? "☑是 □否" : "□是 ☑否";
    }

    /// <summary>
    /// 构建政府意见：根据入户调查结论输出
    /// </summary>
    private static string BuildGovernmentOpinion(HouseholdSurvey? survey)
    {
        if (survey == null) return "";
        var conclusion = survey.SurveyConclusion ?? "";
        if (conclusion == "属实") return "经入户调查，该家庭情况属实。";
        if (conclusion == "不属实" || conclusion == "部分属实")
        {
            var reason = survey.SurveyNotes ?? "";
            return string.IsNullOrEmpty(reason)
                ? $"经入户调查，{conclusion}。"
                : $"经入户调查，{conclusion}。{reason}";
        }
        return conclusion;
    }

    /// <summary>
    /// 入户调查日期：优先用实际入户调查日；
    /// 无调查记录（补录/未录入）时回退 B 线受理窗口起点（上月15日）
    /// </summary>
    private static string ResolveSurveyDate(TimelineResult tl, DateTime? actualSurvey)
    {
        var windowStart = tl.CycleStartDate.Date;
        var baseDate = actualSurvey.HasValue && actualSurvey.Value.Date != default
            ? actualSurvey.Value
            : windowStart;
        return baseDate.ToString("yyyy年M月d日");
    }

    /// <summary>
    /// 档案时间锚定日期：数据补全完成 → 首次审批 → 更新 → 创建。
    /// 用于按该档案自身业务时间取 B 线周期（档案编号/卷号/公示/审核/生效/调查日期），
    /// 避免档案补打时按“当天”重算导致编号/卷号漂移。
    /// </summary>
    private static DateTime ResolveArchiveAnchorDate(ApplicationEntity app)
    {
        if (app.DataCompletedAt is { } completed && completed != default) return completed;
        if (app.FirstApprovedAt is { } approved && approved != default) return approved;
        if (app.UpdatedAt != default) return app.UpdatedAt;
        return ResolveApplicationDate(app);
    }

    /// <summary>
    /// 申请日期（业务受理时间）解析（稳定口径）：
    /// 申请编号内嵌日期（SA+yyyyMMdd，创建时生成）→ 创建时间 → 首次审批 → 补全完成 → 更新 → 今天。
    /// 记录 created_at 为 NULL 时实体初值会被填成加载时刻（UtcNow），直接打印会导致每次补打都漂移，
    /// 故以申请编号日期优先。
    /// </summary>
    private static DateTime ResolveApplicationDate(ApplicationEntity app)
    {
        var no = app.ApplicationNo;
        if (!string.IsNullOrEmpty(no) && no.Length >= 10
            && no.StartsWith("SA", StringComparison.Ordinal)
            && DateTime.TryParseExact(no.Substring(2, 8), "yyyyMMdd", null,
                System.Globalization.DateTimeStyles.None, out var fromNo))
        {
            return fromNo;
        }
        if (app.CreatedAt != default) return app.CreatedAt;
        if (app.FirstApprovedAt is { } approved && approved != default) return approved;
        if (app.DataCompletedAt is { } completed && completed != default) return completed;
        if (app.UpdatedAt != default) return app.UpdatedAt;
        return DateTime.Today;
    }

    /// <summary>
    /// 将金额转换为中文大写（如 200.50 → "贰佰元伍角"）
    /// </summary>
    private static string ConvertToChineseAmount(decimal amount)
    {
        if (amount == 0) return "零元整";

        string[] cnDigits = { "零", "壹", "贰", "叁", "肆", "伍", "陆", "柒", "捌", "玖" };
        string[] cnIntUnits = { "", "拾", "佰", "仟" };
        string[] cnBigUnits = { "", "万", "亿", "兆" };

        var parts = amount.ToString("F2").Split('.');
        var intPart = long.Parse(parts[0]);
        var decPart = int.Parse(parts[1]);

        var result = "";

        if (intPart > 0)
        {
            var intStr = intPart.ToString();
            var len = intStr.Length;
            for (int i = 0; i < len; i++)
            {
                var n = intStr[i] - '0';
                var pos = len - i - 1;
                var bigUnit = pos / 4;
                var smallUnit = pos % 4;

                if (n == 0)
                {
                    if (result.Length > 0 && result[^1] != '零')
                        result += "零";
                }
                else
                {
                    result += cnDigits[n] + cnIntUnits[smallUnit];
                }

                if (smallUnit == 0 && bigUnit > 0 && result.Length > 0 && result[^1] != '零')
                    result += cnBigUnits[bigUnit];
            }
            result += "元";
        }

        var jiao = decPart / 10;
        var fen = decPart % 10;

        if (jiao > 0)
            result += cnDigits[jiao] + "角";
        if (fen > 0)
            result += cnDigits[fen] + "分";
        if (jiao == 0 && fen == 0)
            result += "整";

        return result;
    }

    /// <summary>
    /// 户主分类施保判定（权威口径：ClassificationService.CalculateClassifiedSubsidyAsync）
    /// 重病=主表重病标记或健康状态；重残=健康状态或残疾等级重度（一、二级任意类型；三级智力/精神）；高龄/未成年按身份证年龄
    /// </summary>
    private static (bool SevereIllness, bool SevereDisability, bool Elderly, bool Minor) EvaluateHeadFlags(ApplicationEntity app)
    {
        var headAge = IdCardValidator.ExtractAgeBasic(app.ApplicantIdCard ?? "") ?? 0;
        return (
            app.IsSevereDisease || DictionaryConstants.HealthStatus.HasSevereDisease(app.HealthStatus ?? ""),
            DictionaryConstants.HealthStatus.HasSevereDisability(app.HealthStatus ?? "")
                || ClassificationConstants.DisabilityLevel.IsSevereForAssistance(app.DisabilityLevel ?? "", app.DisabilityType),
            headAge >= AgeConstants.ELDERLY_THRESHOLD,
            headAge > 0 && headAge < AgeConstants.MINOR_THRESHOLD);
    }

    /// <summary>
    /// 成员分类施保判定（权威口径）：重病走 IsSevereDisease 标记 + 健康状态双通道；
    /// 重残走 IsSevereDisability + 健康状态 + 残疾等级（含三级智力/精神）
    /// </summary>
    private static (bool SevereIllness, bool SevereDisability, bool Elderly, bool Minor) EvaluateMemberFlags(FamilyMember m)
    {
        var age = m.Age ?? 0;
        return (
            m.IsSevereDisease || DictionaryConstants.HealthStatus.HasSevereDisease(m.HealthStatus ?? ""),
            m.IsSevereDisability
                || DictionaryConstants.HealthStatus.HasSevereDisability(m.HealthStatus ?? "")
                || ClassificationConstants.DisabilityLevel.IsSevereForAssistance(
                    m.DisabilityLevelKeyResolved, m.DisabilityType),
            age >= AgeConstants.ELDERLY_THRESHOLD,
            age > 0 && age < AgeConstants.MINOR_THRESHOLD);
    }

    private static bool IsClassifiedMember(FamilyMember m)
    {
        var flags = EvaluateMemberFlags(m);
        return flags.SevereIllness || flags.SevereDisability || flags.Elderly || flags.Minor;
    }

    /// <summary>
    /// 分类施保共计人数：判定集合 = 户主（主表）+ 共同生活成员（已排除赡养义务人/已死亡）。
    /// count 每人只计 1 次（与权威 count 语义一致）；分项可叠加展示（与权威 Types 叠加语义一致）。
    /// </summary>
    private static (string text, int count) BuildClassifiedCount(ApplicationEntity app, List<FamilyMember> members)
    {
        var head = EvaluateHeadFlags(app);
        bool headEligible = head.SevereIllness || head.SevereDisability || head.Elderly || head.Minor;
        var eligible = members.Where(IsClassifiedMember).ToList();
        int count = (headEligible ? 1 : 0) + eligible.Count;
        if (count == 0) return ("共计 0 人", 0);

        // 类型列表（去重，一人可同时符合多项；count 为去重后人数）
        var types = new List<string>();
        if (head.SevereIllness || eligible.Any(m => EvaluateMemberFlags(m).SevereIllness)) types.Add("重病");
        if (head.SevereDisability || eligible.Any(m => EvaluateMemberFlags(m).SevereDisability)) types.Add("重残");
        if (head.Elderly || eligible.Any(m => EvaluateMemberFlags(m).Elderly)) types.Add("60周岁以上老人");
        if (head.Minor || eligible.Any(m => EvaluateMemberFlags(m).Minor)) types.Add("18周岁以下未成年人");

        return ($"共计 {count} 人，其中 {string.Join("、", types)}", count);
    }

    /// <summary>
    /// 分类施保享受类型（复选框格式）：判定集合 = 户主 + 共同生活成员
    /// </summary>
    private static string BuildClassifiedSubsidyTypeDisplay(ApplicationEntity app, List<FamilyMember> members)
    {
        var head = EvaluateHeadFlags(app);
        bool hasSevereIllness = head.SevereIllness || members.Any(m => EvaluateMemberFlags(m).SevereIllness);
        bool hasSevereDisability = head.SevereDisability || members.Any(m => EvaluateMemberFlags(m).SevereDisability);
        bool hasElderly = head.Elderly || members.Any(m => EvaluateMemberFlags(m).Elderly);
        bool hasMinor = head.Minor || members.Any(m => EvaluateMemberFlags(m).Minor);

        return $"{(hasSevereIllness ? "☑" : "□")}重病 {(hasSevereDisability ? "☑" : "□")}重残 {(hasElderly ? "☑" : "□")}60周岁以上老人 {(hasMinor ? "☑" : "□")}18周岁以下未成年人";
    }

    /// <summary>
    /// 审核确认意见：户主 + 共同生活成员逐人列出符合的分类施保加发条件
    /// </summary>
    private static string BuildAuditOpinion(ApplicationEntity app, List<FamilyMember> members, OrganizationInfoDto orgInfo)
    {
        var lines = new List<string>();
        int seq = 0;

        var head = EvaluateHeadFlags(app);
        if (head.SevereIllness || head.SevereDisability || head.Elderly || head.Minor)
        {
            var headReasons = new List<string>();
            var headAge = IdCardValidator.ExtractAgeBasic(app.ApplicantIdCard ?? "") ?? 0;
            if (head.SevereIllness) headReasons.Add("重病");
            if (head.SevereDisability) headReasons.Add("重残");
            if (head.Elderly) headReasons.Add($"{headAge}周岁以上老人");
            if (head.Minor) headReasons.Add($"{headAge}周岁以下未成年人");
            var headReasonText = string.Join("、", headReasons);
            var headSuffix = headReasons.Count > 1 ? "（仅享受一项加发）" : "";
            lines.Add($"{++seq}. {app.ApplicantName}（户主）：{headReasonText}{headSuffix}；");
        }

        foreach (var m in members.Where(IsClassifiedMember))
        {
            var flags = EvaluateMemberFlags(m);
            var memberReasons = new List<string>();
            if (flags.SevereIllness) memberReasons.Add("重病");
            if (flags.SevereDisability) memberReasons.Add("重残");
            if (flags.Elderly) memberReasons.Add($"{m.Age}周岁以上老人");
            if (flags.Minor) memberReasons.Add($"{m.Age}周岁以下未成年人");

            var reasonText = string.Join("、", memberReasons);
            var suffix = memberReasons.Count > 1 ? "（仅享受一项加发）" : "";
            lines.Add($"{++seq}. {m.Name}：{reasonText}{suffix}；");
        }

        if (seq == 0) return "";
        var result = new List<string> { "经审核，以下家庭成员符合分类施保加发条件：" };
        result.AddRange(lines);
        return string.Join("\n", result);
    }

    /// <summary>
    /// 构建核实待遇文本
    /// </summary>
    private static string BuildLandStatusText(ApplicationEntity app)
    {
        var area = app.FamilyLandArea;
        if (area <= 0) return "";
        return $"{FormatDecimal(area)}亩";
    }

    /// <summary>
    /// 救助类别勾选框（选项名称采用标准法规长名称）
    /// </summary>
    private static string BuildClassificationCheckbox(string code)
    {
        var isSubsistence = ClassificationConstants.IsCodeSubsistence(code);
        var isLowIncome = ClassificationConstants.IsCodeLowIncome(code);
        var isDestitute = ClassificationConstants.IsCodeDestitute(code);
        var isRigid = ClassificationConstants.IsCodeRigidExpenditure(code);
        return $"{(isSubsistence ? "☑" : "□")}最低生活保障对象 {(isLowIncome ? "☑" : "□")}最低生活保障边缘家庭 {(isDestitute ? "☑" : "□")}特困人员 {(isRigid ? "☑" : "□")}刚性支出困难家庭";
    }
    private static string BuildLandStatusCheckbox(ApplicationEntity app)
    {
        var hasSelf = app.SelfFarmedLandArea > 0;
        var hasSublease = app.SubleasedLandArea > 0;
        var hasContract = app.ContractedLandArea > 0;
        if (!hasSelf && !hasSublease && !hasContract) return "";
        return $"{(hasSelf ? "☑" : "□")}自种 {(hasSublease ? "☑" : "□")}转包 {(hasContract ? "☑" : "□")}承包";
    }

    /// <summary>
    /// 土地亩数明细：自种X亩 转包X亩 承包X亩
    /// </summary>
    private static string BuildLandAreaDetail(ApplicationEntity app)
    {
        var parts = new List<string>();
        if (app.SelfFarmedLandArea > 0) parts.Add($"自种{FormatDecimal(app.SelfFarmedLandArea)}亩");
        if (app.SubleasedLandArea > 0) parts.Add($"转包{FormatDecimal(app.SubleasedLandArea)}亩");
        if (app.ContractedLandArea > 0) parts.Add($"承包{FormatDecimal(app.ContractedLandArea)}亩");
        return parts.Count > 0 ? string.Join(" ", parts) : "";
    }

    /// <summary>
    /// 村级土地说明：享受补贴类型与内容
    /// </summary>
    private static string BuildSubsidyTypeContent(ApplicationEntity app, Services.Domain.SocialAssistance.EconomicDetailData? economicDetail)
    {
        if (economicDetail?.Subsidies == null || economicDetail.Subsidies.Count == 0)
            return "无补贴";

        var sb = new System.Text.StringBuilder();
        foreach (var subsidy in economicDetail.Subsidies)
        {
            if (sb.Length > 0) sb.Append("；");
            sb.Append($"{subsidy.SubsidyType}{FormatDecimal(subsidy.Area)}亩×{FormatDecimal(subsidy.UnitPrice)}元/亩={FormatDecimal(subsidy.Amount)}元");
        }
        return sb.ToString();
    }

    /// <summary>
    /// 村级土地说明：土地详细信息。
    /// 第一句直接使用主表折算面积（表单保存时已按家庭份额折算的现成结果，不再重复计算）；
    /// 第二句按台账全部登记人员口径实时汇总，分类说明死亡继承（点名继承人）/无土地权/外来土地人员，
    /// 分摊公式采用份额制（LandShareCalculator 权威算法，与申请表单 CalculateLandConfirmationArea 同源），
    /// 末值为乘积计算值，与第一句享有面积天然一致；不再读取主表 total_confirmed_land_area / confirmed_person_count 死列。
    /// </summary>
    private static string BuildLandDetailInfo(ApplicationEntity app, Services.Domain.SocialAssistance.EconomicDetailData? economicDetail, HashSet<string> familyNames)
    {
        var selfArea = app.SelfFarmedLandArea > 0 ? FormatDecimal(app.SelfFarmedLandArea) : "0";
        var subleaseArea = app.SubleasedLandArea > 0 ? FormatDecimal(app.SubleasedLandArea) : "0";
        var contractArea = app.ContractedLandArea > 0 ? FormatDecimal(app.ContractedLandArea) : "0";
        var totalArea = app.FamilyLandArea > 0 ? FormatDecimal(app.FamilyLandArea) : "0";

        var sb = new System.Text.StringBuilder();
        sb.Append($"该户享有土地{totalArea}亩，其中自种{selfArea}亩，转包{subleaseArea}亩，承包{contractArea}亩");

        // 土地台账人数 + 特殊状态说明 + 份额制分摊公式（口径与申请表单 CalculateLandConfirmationArea 一致）
        if (economicDetail?.LandConfirmationGroups is { Count: > 0 })
        {
            var groups = economicDetail.LandConfirmationGroups;
            // 登记总人数取全部台账人员（含死亡继承/无土地权等特殊状态，不静默过滤）
            var totalPersonCount = groups.Sum(g => g.Persons.Count);
            var totalConfirmedArea = (decimal)groups.Sum(g => g.TotalArea);

            // 特殊状态人员说明（仅非零项展示）：死亡继承逐人点名继承人
            var specialParts = new List<string>();
            foreach (var group in groups)
            {
                foreach (var p in group.Persons)
                {
                    if (p.LandStatus == LandStatusConstants.DECEASED_INHERITANCE)
                    {
                        specialParts.Add(string.IsNullOrWhiteSpace(p.LandInheritTo)
                            ? $"{p.Name}死亡（继承人未定）"
                            : $"{p.Name}死亡由{p.LandInheritTo}继承");
                    }
                }
            }
            var noRightCount = groups.Sum(g => g.Persons.Count(p => p.LandStatus == LandStatusConstants.NO_LAND_RIGHT));
            var externalCount = groups.Sum(g => g.Persons.Count(p => p.LandStatus == LandStatusConstants.EXTERNAL_LAND));
            if (noRightCount > 0) specialParts.Add($"无土地权{noRightCount}人");
            if (externalCount > 0) specialParts.Add($"外来土地{externalCount}人");

            // 份额制分摊：有效总份额为分母，本户命中份额为分子
            decimal totalShares = 0, familyShares = 0;
            int familyPersonCount = 0;
            foreach (var group in groups)
            {
                var gShares = (decimal)group.TotalShares;
                if (gShares <= 0) continue;

                totalShares += gShares;
                foreach (var kv in LandShareCalculator.ComputeEffectiveShares(group))
                {
                    if (!familyNames.Contains(kv.Key.Trim())) continue;
                    familyShares += kv.Value;
                    familyPersonCount++;
                }
            }

            if (totalPersonCount > 0 && totalShares > 0 && totalConfirmedArea > 0)
            {
                var ratio = familyShares / totalShares;
                var calcArea = Math.Round(totalConfirmedArea * ratio, 2);
                var specialText = specialParts.Count > 0 ? $"（{string.Join("，", specialParts)}）" : string.Empty;
                sb.Append($"。土地台账共登记{totalPersonCount}人{specialText}，参与分摊{totalShares:0.##}份，" +
                          $"本户家庭成员{familyPersonCount}人计{familyShares:0.##}份，占总份额{ratio * 100:0.#}%，" +
                          $"即总台账面积{FormatDecimal(totalConfirmedArea)}亩÷{totalShares:0.##}份×{familyShares:0.##}份≈{FormatDecimal(calcArea)}亩");
            }
        }

        return sb.ToString();
    }

    private static string BuildVerifiedBenefitText(ApplicationEntity app)
    {
        var familyTypeName = ClassificationConstants.ConvertToFamilyTypeName(app.ClassificationResult ?? "");
        return $"经调查核实，该家庭符合{familyTypeName}条件，补助执行时间：";
    }

    /// <summary>
    /// 构建资产核查家庭成员名单文本（部省协查函）
    /// 格式：户主姓名；成员姓名（关系）、成员姓名（关系）…
    /// </summary>
    private string BuildAssetCheckFamilyMemberInfo(AssetVerificationDetail detail)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(detail.ApplicantName))
            parts.Add($"户主{detail.ApplicantName}");

        if (detail.FamilyMembers != null)
        {
            foreach (var m in detail.FamilyMembers)
            {
                if (m.IsHead || m.IdCard == detail.ApplicantIdCard || m.Name == detail.ApplicantName)
                    continue;

                var rel = _dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, m.Relationship ?? "");
                var relDisplay = string.IsNullOrEmpty(rel) ? m.Relationship ?? "" : rel;
                parts.Add($"{m.Name}（{relDisplay}）");
            }
        }

        return parts.Count > 0 ? string.Join("；", parts) : "-";
    }

    /// <summary>
    /// 构建户主及家庭成员状况文本（模板10：最低生活保障申请书）
    /// 格式：户主（姓名、年龄、健康状况、住房情况）；成员（姓名、关系、年龄、健康状况、就业状况）
    /// </summary>
    private string BuildFamilyMemberInfoText(ApplicationEntity app, List<FamilyMember> members)
    {
        var parts = new List<string>();

        // 户主
        var headGender = AddressResolver.ExtractGenderFromIdCard(app.ApplicantIdCard);
        var headAge = AddressResolver.ExtractAgeFromIdCard(app.ApplicantIdCard);
        // 健康状况翻译
        var healthDisplay = string.IsNullOrEmpty(app.HealthStatus) ? "" : $"，{_dictCacheService.GetValue(DictionaryTypeCodes.HealthStatuses, app.HealthStatus)}";
        parts.Add($"户主{app.ApplicantName}，{headGender}，{headAge}岁{healthDisplay}");

        // 其他家庭成员
        if (members != null)
        {
            foreach (var m in members)
            {
                if (m.IsHouseholdHead || m.IdCard == app.ApplicantIdCard)
                    continue;

                var gender = AddressResolver.ExtractGenderFromIdCard(m.IdCard);
                var age = AddressResolver.ExtractAgeFromIdCard(m.IdCard);
                var rel = _dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, m.RelationshipToHead);
                var emp = string.IsNullOrEmpty(m.EmploymentStatus) ? "" : $"，{_dictCacheService.GetValue(DictionaryTypeCodes.EmploymentStatuses, m.EmploymentStatus)}";
                var memberHealth = string.IsNullOrEmpty(m.HealthStatus) ? "" : $"，{_dictCacheService.GetValue(DictionaryTypeCodes.HealthStatuses, m.HealthStatus)}";
                parts.Add($"{m.Name}（{rel}），{gender}，{age}岁{memberHealth}{emp}");
            }
        }

        return parts.Count > 0 ? string.Join("；", parts) : "-";
    }

    /// <summary>
    /// 构建经济收入状况文本（模板10：最低生活保障申请书）
    /// </summary>
    private string BuildIncomeSituationText(ApplicationEntity app, EconomicDetailData? economicDetail)
    {
        var parts = new List<string>();

        // 户籍类型
        var hukouType = app.HukouType?.ToLowerInvariant() switch
        {
            "rural" => "农村",
            "urban" => "城市",
            _ => app.HukouType ?? ""
        };

        void AddMonthly(string label, decimal value)
        {
            if (value != 0)
                parts.Add($"{label}{FormatDecimal(value * 12)}元/年");
        }

        void AddAnnual(string label, decimal value)
        {
            if (value != 0)
                parts.Add($"{label}{FormatDecimal(value)}元/年");
        }

        // 收入构成（年值口径：月值项×12、年值项原样，构成之和 = 家庭年收入 TotalAnnualIncome）
        AddMonthly("务工收入", app.WorkIncomeTotal);
        AddMonthly("经营收入", app.BusinessIncomeTotal);
        AddMonthly("财产性收入", app.PropertyIncomeTotal);
        AddMonthly("转移性收入", app.TransferIncomeTotal);
        AddAnnual("赡养收入", app.AlimonyIncome);          // 赡养存储为年值
        AddMonthly("其他收入", app.OtherIncomeTotal);

        // 土地和补贴本身是年值
        AddAnnual("土地收入", app.LandIncomeTotal);
        AddAnnual("农业补贴", app.SubsidyTotal);

        // 合计（年值为权威口径，月值 = 年值÷12 分解显示）
        var totalAnnual = app.TotalAnnualIncome;
        var totalMonthly = app.TotalFamilyIncome;
        var perCapitaAnnual = app.PerCapitaAnnualIncome;
        parts.Add($"家庭月收入{FormatDecimal(totalMonthly)}元，家庭年收入{FormatDecimal(totalAnnual)}元");
        parts.Add($"人均年收入{FormatDecimal(perCapitaAnnual)}元");
        AddMonthly("刚性支出", app.RigidExpenditure);

        var text = parts.Count > 0 ? string.Join("；", parts) : "-";

        // 前缀：户籍类型说明
        return $"本人为{hukouType}户口，{text}。";
    }

    // ========================
    //  模板8（邻里访问调查表）复合字段构建
    // ========================

    /// <summary>
    /// 构建收入来源详细说明（模板8：邻里访问调查表"主要收入来源"字段）
    /// 逐条列出具体来源与金额，如"养老金7.12元/月、务工收入30元/月、土地收入5748元/年"
    /// </summary>
    private string BuildIncomeSourceSummary(ApplicationEntity app, EconomicDetailData? economicDetail)
    {
        var items = new List<string>();

        if (economicDetail != null)
        {
            // 转移性收入：具体到收入类型（如"养老金"）
            if (economicDetail.TransferIncomes != null)
                foreach (var t in economicDetail.TransferIncomes)
                {
                    var type = string.IsNullOrEmpty(t.IncomeType) ? "转移性收入" : t.IncomeType;
                    items.Add($"{type}{FormatDecimal((t.MonthlyAmount ?? 0) * 12)}元/年");
                }

            // 务工收入
            if (economicDetail.LaborIncomes != null)
                foreach (var l in economicDetail.LaborIncomes)
                    items.Add($"务工收入{FormatDecimal((l.MonthlyIncome ?? 0) * 12)}元/年");

            // 经营收入
            if (economicDetail.BusinessIncomes != null)
                foreach (var b in economicDetail.BusinessIncomes)
                    items.Add($"经营收入{FormatDecimal((b.MonthlyIncome ?? 0) * 12)}元/年");

            // 种植/养殖收入（年收入）
            if (economicDetail.BreedingIncomes != null)
                foreach (var br in economicDetail.BreedingIncomes)
                    items.Add($"{br.BreedingType ?? "种植"}收入{FormatDecimal(br.AnnualIncome)}元/年");

            // 其他收入
            if (economicDetail.OtherIncomes != null)
                foreach (var o in economicDetail.OtherIncomes)
                {
                    var type = string.IsNullOrEmpty(o.IncomeType) ? "其他收入" : o.IncomeType;
                    items.Add($"{type}{FormatDecimal(o.Amount * 12)}元/年");
                }
        }

        // 赡养费（年值）、土地收入（年值）、农业补贴（年值）来自申请汇总字段
        if (app.AlimonyIncome != 0)
            items.Add($"赡养费{FormatDecimal(app.AlimonyIncome)}元/年");
        if (app.LandIncomeTotal != 0)
            items.Add($"土地收入{FormatDecimal(app.LandIncomeTotal)}元/年");
        if (app.SubsidyTotal != 0)
            items.Add($"农业补贴{FormatDecimal(app.SubsidyTotal)}元/年");

        return items.Count > 0 ? string.Join("、", items) : "-";
    }

    private string BuildBreedingIncomeDesc(EconomicDetailData? economicDetail)
    {
        if (economicDetail?.BreedingIncomes == null || economicDetail.BreedingIncomes.Count == 0)
            return "-";

        var items = economicDetail.BreedingIncomes.Select(b =>
            $"{b.BreedingType ?? "种植"}：数量{b.Quantity}，收入{FormatDecimal(b.AnnualIncome)}元");
        return $"有：{string.Join("；", items)}";
    }

    private string BuildBusinessIncomeDesc(EconomicDetailData? economicDetail)
    {
        if (economicDetail?.BusinessIncomes == null || economicDetail.BusinessIncomes.Count == 0)
            return "-";

        var items = economicDetail.BusinessIncomes.Select(b =>
        {
            var vendorType = b.VendorType ?? "";
            var companyName = b.CompanyName ?? "";
            var namePart = (!string.IsNullOrEmpty(companyName), !string.IsNullOrEmpty(vendorType)) switch
            {
                (true, true) => $"{vendorType}（{companyName}）",
                (true, false) => companyName,
                (false, true) => vendorType,
                _ => "经营收入"
            };
            return $"经营{namePart}，月收入{FormatDecimal(b.MonthlyIncome ?? 0)}元";
        });
        return $"有：{string.Join("；", items)}";
    }

    private string BuildLaborIncomeDesc(EconomicDetailData? economicDetail)
    {
        if (economicDetail?.LaborIncomes == null || economicDetail.LaborIncomes.Count == 0)
            return "-";

        var items = economicDetail.LaborIncomes.Select(l =>
        {
            var workUnit = l.WorkUnit ?? "";
            var incomeType = l.IncomeSubType ?? "";
            var desc = (!string.IsNullOrEmpty(workUnit), !string.IsNullOrEmpty(incomeType)) switch
            {
                (true, true) => $"在{workUnit}从事{incomeType}工作",
                (true, false) => $"在{workUnit}工作",
                (false, true) => $"从事{incomeType}工作",
                _ => "务工收入"
            };
            return $"{desc}，月收入{FormatDecimal(l.MonthlyIncome ?? 0)}元，工作{l.MonthsWorked ?? 0}个月/年";
        });
        return $"有：{string.Join("；", items)}";
    }

    private string BuildFarmEquipmentDesc(EconomicDetailData? economicDetail)
    {
        if (economicDetail?.BreedingIncomes == null || economicDetail.BreedingIncomes.Count == 0)
            return "";

        var items = economicDetail.BreedingIncomes.Select(b =>
            $"{b.BreedingType ?? "种植"}，数量{b.Quantity}，年收入{FormatDecimal(b.AnnualIncome)}元");
        return string.Join("；", items);
    }

    private string BuildVehicleDesc(EconomicDetailData? economicDetail)
    {
        if (economicDetail?.Vehicles == null || economicDetail.Vehicles.Count == 0)
            return "";

        var items = economicDetail.Vehicles.Select(v =>
            $"{v.Brand} {v.Model}（{v.LicensePlate}），估值{FormatDecimal(v.EstimatedValue)}元");
        return string.Join("；", items);
    }

    private string BuildHouseDesc(EconomicDetailData? economicDetail)
    {
        if (economicDetail?.FamilyProperties == null || economicDetail.FamilyProperties.Count == 0)
            return "";

        var items = economicDetail.FamilyProperties.Select(p =>
            $"{p.HousingStructure ?? "未知"}结构，面积{FormatDecimal(p.Area)}㎡，{p.HousingNature ?? "未知"}");
        return string.Join("；", items);
    }

    private string BuildGuaranteeTypeDisplay(string classificationResult)
    {
        if (string.IsNullOrEmpty(classificationResult)) return "-";

        if (classificationResult.Contains("Subsistence"))
            return "保障金";
        if (classificationResult.Contains("Destitute"))
            return "供养金";
        if (classificationResult.Contains("LowIncome") || classificationResult.Contains("LowIncomeSingle"))
            return "-";

        return "-";
    }

    /// <summary>
    /// 渐退期审批表「退出渐退期情况」分型拼句（仅本户存在渐退记录才产出；仍在渐退留空）。
    /// 日期统一用复核日 change_date；句式 A–D 见 docs/20260924_业务节点文书直出规范.md 同类约定。
    /// 取数失败时返回空串（不阻断整表预览），业务日志已由服务层记录。
    /// </summary>
    private async Task<string> BuildGraceExitSituationAsync(
        ApplicationEntity app,
        GracePeriodRecord grace)
    {
        // E：仍在渐退（记录有效且档案未停）→ 留空供后续手写
        if (grace.IsActive && app.Status != ApplicationStatusCodes.STOPPED)
            return "";

        try
        {
            var exitRes = await _changeService.GetLatestGraceExitChangeAsync(app.Id, CancellationToken.None);
            var exit = exitRes.IsSuccess ? exitRes.Value : null;

            // A：经济复核 + 触发停保 + 新分类属停保族
            if (exit != null
                && exit.ChangeReasonType == ChangeReasonTypeConstants.EconomicReview
                && exit.TriggeredStop == true
                && !string.IsNullOrEmpty(exit.NewClassification)
                && ClassificationConstants.IsCodeStop(exit.NewClassification))
            {
                var d = exit.ChangeDate?.ToString("yyyy年M月d日") ?? app.StopDate.ToString("yyyy年M月d日");
                return $"于{d}进行经济复核，经审核，家庭经济状况超过保障标准，依据相关规定，经批准，予以停止保障。";
            }

            // B：经济复核但未触发停保（换类/降档等退出渐退）
            if (exit != null && exit.ChangeReasonType == ChangeReasonTypeConstants.EconomicReview)
            {
                var d = exit.ChangeDate?.ToString("yyyy年M月d日") ?? app.StopDate.ToString("yyyy年M月d日");
                return $"于{d}进行经济复核，家庭经济状况发生变化，按规定退出渐退期。";
            }

            // C：户主死亡停保
            if ((exit != null && exit.ChangeReasonType == ChangeReasonTypeConstants.HeadDeceased)
                || app.StopReason == ChangeReasonTypeConstants.HeadDeceased)
            {
                var d = exit?.ChangeDate?.ToString("yyyy年M月d日")
                    ?? (app.StopDate != default ? app.StopDate.ToString("yyyy年M月d日") : "");
                if (string.IsNullOrEmpty(d)) return "";
                return $"于{d}原户主死亡，依据相关规定，经批准，予以停止保障。";
            }

            // D：其他停保（有 stop_reason）
            if (app.Status == ApplicationStatusCodes.STOPPED && !string.IsNullOrEmpty(app.StopReason))
            {
                var d = exit?.ChangeDate?.ToString("yyyy年M月d日")
                    ?? (app.StopDate != default ? app.StopDate.ToString("yyyy年M月d日") : "");
                if (string.IsNullOrEmpty(d)) return "";
                return $"于{d}因{app.StopReason}，经批准，予以停止保障。";
            }

            return "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "生成退出渐退期情况失败");
            return "";
        }
    }

    /// <summary>
    /// 渐退期审批表「变动情况说明」分句拼装（家庭人员/收入/享受类别/保障金/财产状况，分号连接、句号收尾）。
    /// 停旧建新链（OriginalApplicationId>0）：人口与家庭年收入取旧档对比，括号内注明原户主停保原因；
    /// 经济复核同档：人口无变化省略该分句，月人均收入取变更记录新旧值；
    /// 旧档读取/变更查询失败仅跳过对应分句（LogWarn），类别/保障金/财产照常输出——不吞整段。
    /// </summary>
    private async Task<string> BuildGraceChangeDetailAsync(ApplicationEntity app, GracePeriodRecord grace)
    {
        var parts = new List<string>();

        if (app.OriginalApplicationId > 0)
        {
            var oldRes = await _applicationService.GetByIdAsync(app.OriginalApplicationId, CancellationToken.None);
            var old = oldRes.IsSuccess ? oldRes.Value : null;
            if (old == null)
            {
                _logger.LogBusiness("变动情况说明-旧档读取失败降级", ("ApplicationId", app.Id.ToString()), ("OriginalApplicationId", app.OriginalApplicationId.ToString()));
            }
            else
            {
                // 总额/分项/人口对比旧值：Before 快照优先（经济复核链旧档行已被表单覆写为新值，不可信）；
                // 死亡/户主变更链旧档未被编辑，无快照时旧档行即真旧值。
                BeforeSnapshotOldValues? snap = null;
                var snapRes = await _changeService.GetLatestBeforeSnapshotAsync(app.OriginalApplicationId, CancellationToken.None);
                if (snapRes.IsFailure)
                {
                    _logger.LogBusiness("变动情况说明-快照旧值查询失败降级", ("ApplicationId", app.Id.ToString()), ("ErrorCode", snapRes.ErrorCode ?? ""));
                }
                else
                {
                    snap = snapRes.Value;
                }

                var oldFamilySize = snap?.OldFamilySize ?? old.FamilySize;
                if (oldFamilySize != app.FamilySize)
                {
                    var stopDay = old.StopDate != default ? old.StopDate.ToString("yyyy年M月d日") : "";
                    string cause;
                    if (old.StopReason == ChangeReasonTypeConstants.HeadDeceased)
                    {
                        cause = string.IsNullOrEmpty(stopDay) ? "原户主死亡" : $"原户主{old.ApplicantName}于{stopDay}死亡";
                    }
                    else if (old.StopReason.Contains("经济复核"))
                    {
                        // 复核链户主未变，旧档停止仅是停旧建新，不能写"停止保障"
                        cause = "经济复核后重新认定";
                    }
                    else
                    {
                        cause = string.IsNullOrEmpty(stopDay) ? "原户主停止保障" : $"原户主{old.ApplicantName}于{stopDay}停止保障";
                    }
                    parts.Add($"家庭人口由{oldFamilySize}人变为{app.FamilySize}人（{cause}）");
                }

                var incomePart = BuildAnnualIncomeComparisonPart(snap, old, app);
                if (!string.IsNullOrEmpty(incomePart))
                {
                    parts.Add(incomePart);
                }
            }
        }
        else
        {
            // 经济复核同档更新：旧人均收入仅存于变更记录
            var compRes = await _changeService.GetLatestIncomeComparisonAsync(app.Id, CancellationToken.None);
            if (compRes.IsFailure)
            {
                _logger.LogBusiness("变动情况说明-人均收入查询失败降级", ("ApplicationId", app.Id.ToString()), ("ErrorCode", compRes.ErrorCode ?? ""));
            }
            else if (compRes.Value is { } comp
                && comp.OldPerCapitaIncome.HasValue && comp.NewPerCapitaIncome.HasValue
                && comp.OldPerCapitaIncome.Value != comp.NewPerCapitaIncome.Value)
            {
                parts.Add($"月人均收入由{FormatDecimal(comp.OldPerCapitaIncome.Value)}元变为{FormatDecimal(comp.NewPerCapitaIncome.Value)}元");
            }
        }

        var oldClass = ClassificationConstants.ConvertFromCode(grace.OriginalClassification ?? "");
        var newClass = ClassificationConstants.ConvertFromCode(app.ClassificationResult ?? "");
        if (!string.IsNullOrEmpty(oldClass) && !string.IsNullOrEmpty(newClass))
        {
            parts.Add($"享受类别由{oldClass}调整为{newClass}");
        }

        if (grace.OriginalGuaranteeAmount is > 0)
        {
            var newAmt = grace.GraceGrantAmount ?? app.HouseholdMonthlyGuaranteeAmount;
            parts.Add($"月保障金由{FormatDecimal(grace.OriginalGuaranteeAmount.Value)}元调整为{FormatDecimal(newAmt)}元");
        }

        parts.Add("财产状况无变化");
        return string.Join("；", parts) + "。";
    }

    /// <summary>
    /// 拼「家庭年收入由X变为Y，其中…（收入分项对比）」分句；总额与分项均无变化时返回 null。
    /// 旧值来源：Before 快照 Components（复核链真旧值）优先，回退旧档行分项（死亡/户主变更链真旧值）。
    /// 口径统一为年值：务工/经营/财产性/转移性/其他/刚性支出为月值×12，土地收入/农业补贴/赡养费收入为年值原样；
    /// 复核链历史记录（旧档已被覆写、无快照）旧源与新档全等且总额相等 → 不输出，避免误导性"无变化"。
    /// </summary>
    private static string? BuildAnnualIncomeComparisonPart(BeforeSnapshotOldValues? snap, ApplicationEntity old, ApplicationEntity app)
    {
        var oldAnnual = snap?.OldTotalAnnualIncome ?? old.TotalAnnualIncome;
        var totalChanged = oldAnnual != app.TotalAnnualIncome;

        var oldItems = snap?.Components ?? new IncomeComponentValues
        {
            WorkIncomeTotal = old.WorkIncomeTotal,
            BusinessIncomeTotal = old.BusinessIncomeTotal,
            PropertyIncomeTotal = old.PropertyIncomeTotal,
            TransferIncomeTotal = old.TransferIncomeTotal,
            OtherIncomeTotal = old.OtherIncomeTotal,
            RigidExpenditure = old.RigidExpenditure,
            AlimonyIncome = old.AlimonyIncome,
            LandIncomeTotal = old.LandIncomeTotal,
            SubsidyTotal = old.SubsidyTotal
        };
        var newItems = new IncomeComponentValues
        {
            WorkIncomeTotal = app.WorkIncomeTotal,
            BusinessIncomeTotal = app.BusinessIncomeTotal,
            PropertyIncomeTotal = app.PropertyIncomeTotal,
            TransferIncomeTotal = app.TransferIncomeTotal,
            OtherIncomeTotal = app.OtherIncomeTotal,
            RigidExpenditure = app.RigidExpenditure,
            AlimonyIncome = app.AlimonyIncome,
            LandIncomeTotal = app.LandIncomeTotal,
            SubsidyTotal = app.SubsidyTotal
        };

        (string Label, decimal Old, decimal New)[] defs =
        {
            ("务工收入", oldItems.WorkIncomeTotal * 12, newItems.WorkIncomeTotal * 12),
            ("经营收入", oldItems.BusinessIncomeTotal * 12, newItems.BusinessIncomeTotal * 12),
            ("财产性收入", oldItems.PropertyIncomeTotal * 12, newItems.PropertyIncomeTotal * 12),
            ("转移性收入", oldItems.TransferIncomeTotal * 12, newItems.TransferIncomeTotal * 12),
            ("其他收入", oldItems.OtherIncomeTotal * 12, newItems.OtherIncomeTotal * 12),
            ("土地收入", oldItems.LandIncomeTotal, newItems.LandIncomeTotal),
            ("农业补贴", oldItems.SubsidyTotal, newItems.SubsidyTotal),
            ("赡养费收入", oldItems.AlimonyIncome, newItems.AlimonyIncome),
            ("刚性支出", oldItems.RigidExpenditure * 12, newItems.RigidExpenditure * 12),
        };

        var changed = new List<string>();
        var unchanged = new List<string>();
        foreach (var (label, oldValue, newValue) in defs)
        {
            if (oldValue != newValue)
            {
                changed.Add($"{label}由{FormatDecimal(oldValue)}元变为{FormatDecimal(newValue)}元");
            }
            else
            {
                unchanged.Add(label);
            }
        }

        if (!totalChanged && changed.Count == 0)
        {
            return null;
        }

        var detail = new List<string>();
        if (changed.Count > 0)
        {
            detail.Add(string.Join("，", changed));
            if (unchanged.Count > 0)
            {
                detail.Add($"其余分项（{string.Join("、", unchanged)}）无变化");
            }
        }

        if (totalChanged)
        {
            var head = $"家庭年收入由{FormatDecimal(oldAnnual)}元变为{FormatDecimal(app.TotalAnnualIncome)}元";
            if (detail.Count > 0)
            {
                return head + "，其中" + string.Join("，", detail);
            }
            return head + "，各收入分项无变化";
        }

        return "收入分项中" + string.Join("，", detail);
    }

    /// <summary>
    /// 生成收入超标停保的详细经济理由（H4：低保标准单点读 IStandardConfigService，
    /// 缺失/无效时抛 CONFIG_NOT_FOUND，禁止硬编码 632/832 回退值写入文书）。
    /// </summary>
    private async Task<string> BuildIncomeExceededReasonAsync(ApplicationEntity app, string? newClassification)
    {
        var sb = new System.Text.StringBuilder();
        var familySize = app.FamilySize;
        var totalIncome = app.TotalFamilyIncome;
        var perCapitaIncome = app.PerCapitaIncome;
        var rigidExpenditure = app.RigidExpenditure;
        var isRural = ClassificationConstants.HukouType.IsHukouRural(app.HukouType);

        sb.Append($"经核算，您家庭{familySize}口人，家庭月总收入{totalIncome:N2}元，");
        sb.Append($"人均月收入{perCapitaIncome:N2}元。");

        var stdType = isRural ? "RuralSubsistenceStandard" : "UrbanSubsistenceStandard";
        var stdHukou = isRural ? "Rural" : "Urban";
        var stdResult = await _standardConfigService.GetStandardValueAsync(stdType, stdHukou);
        if (!stdResult.IsSuccess || stdResult.Value <= 0)
        {
            _logger.Error($"停保告知书：低保标准配置缺失或无效（{stdType}/{stdHukou}），禁止使用硬编码回退值");
            return $"低保标准配置缺失或无效（{stdType}/{stdHukou}），请在「数据中心-标准配置管理」中维护有效标准后重新生成停保告知书。";
        }
        var standard = stdResult.Value;
        var threshold = standard * ClassificationConstants.LowIncomeMultiplier;

        sb.Append($"{(isRural ? "农村" : "城市")}低保标准为{standard:N0}元/月，");
        sb.Append($"低收入标准上限为{threshold:N0}元/月（{standard:N0}×1.5）。");
        sb.Append($"您家庭人均月收入{perCapitaIncome:N2}元已超过低收入标准上限{threshold:N2}元，");

        // 刚性支出分析
        if (rigidExpenditure > 0)
        {
            var perCapitaRigid = rigidExpenditure / familySize;
            var ratio = perCapitaIncome > 0 ? perCapitaRigid / perCapitaIncome : 0;
            sb.Append($"虽有刚性支出{rigidExpenditure:N2}元/月（占比{ratio:P0}），");

            if (ratio <= 0.5m)
                sb.Append("但占比未超过50%，不满足刚性支出困难家庭条件。");
            else if (ratio >= 0.6m)
                sb.Append("但占比已超过60%，不满足刚性支出困难家庭条件。");
            else if (perCapitaIncome >= standard * 2)
                sb.Append("但人均收入超过低保标准2倍，不满足刚性支出困难家庭条件。");
            else
                sb.Append("扣除刚性支出后人均收入不足低保标准，不满足刚性支出困难家庭条件。");
        }
        else
        {
            sb.Append("且无刚性支出，不满足刚性支出困难家庭条件。");
        }

        sb.Append("根据《牡丹江市低收入人口认定实施细则》（牡民规〔2024〕2号）相关规定，不符合低收入人口认定条件。");

        return sb.ToString();
    }

    public sealed record OrganizationInfoDto(string District, string Town, string DistrictCivilBureau, string DistrictCivilBureauPhone, string OperatorUnitName, string OperatorUnitPhone);
}

/// <summary>
/// 历史补打打印数据（由 ArchiveProductionViewModel.BuildPrintDataAsync 产出，供填充 PrintNavigationData）
/// </summary>
public sealed record ApplicationPrintData(
    string BusinessType,
    long? BusinessId,
    string Classification,
    Dictionary<string, string> FieldData,
    List<Dictionary<string, string>> TableData,
    List<Dictionary<string, string>>? SupporterTableData,
    string ApplicantName);

