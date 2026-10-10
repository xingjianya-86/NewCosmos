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
            var detailResult = await econService.LoadAllAsync(app.Id, CancellationToken);
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
                var surveyResult = await _householdSurveyService.GetByApplicationIdAsync(applicationId, CancellationToken);
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
                applicationId, ClassificationConstants.StopCategoryCodes, CancellationToken);
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
                var spForm = await specialApprovalFormService.GetByApplicationIdAsync(applicationId, CancellationToken);
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
            var pairsResult = await nearRelativeService.GetPairsByApplicationIdAsync(applicationId, CancellationToken);
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
            var deathResult = await _familyMemberService.GetDeadIdCardsByApplicationIdAsync(applicationId, CancellationToken);
            if (deathResult.IsSuccess && deathResult.Value != null)
                deathIdCards = deathResult.Value;

            // 共同生活成员（索引字段，不含户主本人、不含已死亡成员）
            var sharedMembers = membersResult.Value
                .Where(m => m.MemberCategory == MemberCategoryConstants.SHARED_LIVING && !m.IsHouseholdHead && m.IdCard != app.ApplicantIdCard && !deathIdCards.Contains(m.IdCard ?? ""))
                .ToList();
            _logger.Info($"共同生活成员数: {sharedMembers.Count}");

            // 户主（申请人本人）索引：只写模板1/15/16/74 的专用字段
            //（SHARED_MEMBER_* 三表——定期复核/入户调查/特困入户调查——不含户主，成员从第 1 行起）
            var headGender = AddressResolver.ExtractGenderFromIdCard(app.ApplicantIdCard);
            var headAge = AddressResolver.ExtractAgeFromIdCard(app.ApplicantIdCard);
            var headBirthDate = IdCardValidator.ExtractBirthDate(app.ApplicantIdCard)?.ToString("yyyy-MM-dd") ?? "";
            var headHealthDisplay = _dictCacheService.GetValue(DictionaryTypeCodes.HealthStatuses, app.HealthStatus ?? "");
            var headEmploymentDisplay = _dictCacheService.GetValue(DictionaryTypeCodes.EmploymentStatuses, app.EmploymentStatus ?? "");
            var headHukouDisplay = _dictCacheService.GetValue(DictionaryTypeCodes.HukouTypes, app.HukouType ?? "");
            var headIncomeAnnual = app.TotalAnnualIncome > 0 ? FormatDecimal(app.TotalAnnualIncome) : "";

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

            // 共同生活成员：SHARED_MEMBER_*（定期复核/入户调查/特困入户调查三表）不含户主，从第 1 位起最多 6 人；
            // FAMILY_MEMBER_*/SHARED_*（模板1/15/16/74）第 1 位是户主，成员仍从第 2 位起最多 5 人（i<5 守卫，容量与既往一致）
            for (int i = 0; i < sharedMembers.Count && i < 6; i++)
            {
                var m = sharedMembers[i];
                var sharedIdx = i + 1;
                _fieldData[$"{FieldKeys.SHARED_MEMBER_NAME}_{sharedIdx}"] = m.Name;
                _fieldData[$"{FieldKeys.SHARED_MEMBER_GENDER}_{sharedIdx}"] = m.Gender ?? "";
                _fieldData[$"{FieldKeys.SHARED_MEMBER_AGE}_{sharedIdx}"] = m.Age?.ToString() ?? "";
                _fieldData[$"{FieldKeys.SHARED_MEMBER_RELATION}_{sharedIdx}"] = _dictCacheService.GetValue(DictionaryTypeCodes.FamilyRelationships, m.RelationshipToHead);
                _fieldData[$"{FieldKeys.SHARED_MEMBER_EMPLOYMENT}_{sharedIdx}"] = _dictCacheService.GetValue(DictionaryTypeCodes.EmploymentStatuses, m.EmploymentStatus ?? "");

                if (i < 5)
                {
                    var suffix = i + 2;

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
            // [CT 豁免] StartNew 的调度令牌：查询本体已用 economicCts（30 秒超时）控制，不参与业务取消
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
        _logger.LogBusiness("经济明细查询完成", ("IsSuccess", economicResult.IsSuccess.ToString()));

        // 农业补贴折算条件注记（比例≠100% 时非空），经济明细加载成功后计算，供下方 INCOME_SUBSIDY 等输出附加
        var subsidyRatioNote = "";

        if (economicResult.IsSuccess && economicResult.Value != null)
        {
            var economic = economicResult.Value;
            subsidyRatioNote = SubsidyRatioHelper.BuildNote(economic.Subsidies);
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
        _fieldData[FieldKeys.INCOME_SUBSIDY] = FormatDecimal(app.SubsidyTotal) + subsidyRatioNote;  // 年收入直接显示；折算后附条件注记
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
        await BuildSidebarFromApplicationAsync(app, civilAssistantName, orgInfo, app.TotalFamilyIncome, app.WorkIncomeTotal, app.BusinessIncomeTotal, app.PropertyIncomeTotal, app.TransferIncomeTotal, app.AlimonyIncome, app.OtherIncomeTotal, app.RigidExpenditure, subsidyRatioNote);
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
        _cachedFamilyMembers = (await _familyMemberService.GetByApplicationIdAsync(applicationId, CancellationToken)).Value ?? new();
    }

    private async Task LoadSupportersDataAsync(long applicationId)
    {
        _cachedSupporters = (await _supporterService.GetByApplicationIdAsync(applicationId, CancellationToken)).Value ?? new();
    }

    private async Task LoadCaregiversDataAsync(long applicationId)
    {
        _cachedCaregivers = (await _caregiverService.GetByApplicationIdAsync(applicationId, CancellationToken)).Value ?? new();
    }

    private async Task LoadHouseholdSurveyDataAsync(long applicationId)
    {
        _cachedHouseholdSurvey = (await _householdSurveyService.GetByApplicationIdAsync(applicationId, CancellationToken)).Value;
    }

    private async Task LoadCapabilityAssessmentDataAsync(long applicationId)
    {
        var result = await _capabilityAssessmentService.GetByApplicationIdAsync(applicationId, CancellationToken);
        if (result.IsSuccess) _cachedCapabilityAssessment = result.Value;
    }

    private async Task LoadEconomicDetailsDataAsync(long applicationId)
    {
        _cachedEconomicDetails = (await _economicDetailService.LoadAllAsync(applicationId, CancellationToken)).Value;
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
            var detailResult = await econService.LoadAllAsync(app.Id, CancellationToken);
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
            var deathResult = await _familyMemberService.GetDeadIdCardsByApplicationIdAsync(app.Id, CancellationToken);
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
            app.TransferIncomeTotal, app.AlimonyIncome, app.OtherIncomeTotal, app.RigidExpenditure,
            SubsidyRatioHelper.BuildNote(economicDetail?.Subsidies));
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

