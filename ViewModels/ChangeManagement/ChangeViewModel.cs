using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Enums;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ChangeManagement;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.ChangeManagement;

using ApplicationEntity = NewCosmos.Models.Entities.Application;

/// <summary>
/// 变更入口ViewModel
/// </summary>
public partial class ChangeViewModel : ViewModelBase
{
    private readonly IApplicationService _applicationService;
    private readonly IChangeService _changeService;
    private readonly IImportedArchiveService _importedArchiveService;
    private readonly IFamilyMemberService _familyMemberService;
    private readonly IDynamicManagementRecordService _dynamicRecordService;
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    /// <summary>页头统计栏对外初始化入口（Page.OnAppearing 触发）</summary>
    public Task InitializeHeaderAsync() => LoadHeaderStatsAsync();

    #endregion

    #region 当前档案信息

    [ObservableProperty]
    private long _applicationId;

    [ObservableProperty]
    private string _applicantName = string.Empty;

    [ObservableProperty]
    private string _applicantIdCard = string.Empty;

    [ObservableProperty]
    private string _currentClassification = string.Empty;

    [ObservableProperty]
    private string _currentClassificationName = string.Empty;

    [ObservableProperty]
    private decimal _currentAmount;

    [ObservableProperty]
    private int _familySize;

    /// <summary>家庭成员名单（户主+共同生活成员，格式：姓名（关系）、…）</summary>
    [ObservableProperty]
    private string _memberNamesDisplay = string.Empty;

    [ObservableProperty]
    private decimal _perCapitaIncome;

    /// <summary>人均年收入（元/年，年值权威口径）</summary>
    [ObservableProperty]
    private decimal _perCapitaAnnualIncome;

    /// <summary>人均年收入（元/年）= 年值权威口径，与归档页展示口径一致</summary>
    public decimal PerCapitaIncomeAnnual => PerCapitaAnnualIncome;

    partial void OnPerCapitaIncomeChanged(decimal value) => OnPropertyChanged(nameof(PerCapitaIncomeAnnual));
    partial void OnPerCapitaAnnualIncomeChanged(decimal value) => OnPropertyChanged(nameof(PerCapitaIncomeAnnual));

    [ObservableProperty]
    private string _hukouType = string.Empty;

    [ObservableProperty]
    private string _archiveStatus = string.Empty;

    [ObservableProperty]
    private string _statusDisplay = string.Empty;

    [ObservableProperty]
    private bool _isApplicationStopped;

    #endregion

    #region 变更历史

    [ObservableProperty]
    private ObservableCollection<ChangeRecord> _changeHistory = new();

    [ObservableProperty]
    private bool _hasChangeHistory;

    #endregion

    #region 档案选择

    /// <summary>档案搜索关键词（姓名/身份证号）</summary>
    [ObservableProperty]
    private string _searchKeyword = string.Empty;

    /// <summary>档案搜索结果（当前库 + 历史导入库统一项）</summary>
    [ObservableProperty]
    private ObservableCollection<ArchiveSearchResultItem> _searchResults = new();

    /// <summary>是否有搜索结果（控制结果列表可见性）</summary>
    [ObservableProperty]
    private bool _hasSearchResults;

    /// <summary>是否已选定档案（控制档案信息卡片可见性）</summary>
    [ObservableProperty]
    private bool _hasSelectedApplication;

    /// <summary>是否未选档案（控制引导提示可见性）</summary>
    [ObservableProperty]
    private bool _isNoArchiveHintVisible = true;

    /// <summary>选中的搜索结果项（导入库档案用于后续建档溯源）</summary>
    private ArchiveSearchResultItem? _selectedSearchItem;

    /// <summary>
    /// 搜索档案：当前库（nc_biz_applications）+ 5 个历史导入库 双源合并
    /// </summary>
    [RelayCommand]
    private async Task SearchApplicationsAsync()
    {
        var keyword = SearchKeyword?.Trim();
        if (string.IsNullOrEmpty(keyword))
        {
            await ShowTipAsync("请输入姓名或身份证号后再搜索");
            return;
        }

        try
        {
            IsBusy = true;
            _logger.LogBusiness("变更页搜索档案", ("Keyword", keyword));

            SearchResults.Clear();

            // ① 当前库
            var current = await _applicationService.GetPagedAsync(1, 20, null, keyword, CancellationToken);
            if (current.IsSuccess && current.Value != null)
            {
                foreach (var app in current.Value.Items)
                {
                    SearchResults.Add(MapCurrentToItem(app));
                }
            }
            else if (current.IsFailure)
            {
                _logger.Error($"当前库档案搜索失败: {current.Message}");
            }

            // ② 5 个历史导入库（农村/城市最低生活保障、最低生活保障边缘、特困人员、刚性支出困难家庭）
            var imported = await _importedArchiveService.SearchAsync(keyword, CancellationToken);
            if (imported.IsSuccess && imported.Value != null)
            {
                foreach (var item in imported.Value)
                {
                    SearchResults.Add(item);
                }
            }
            else if (imported.IsFailure)
            {
                _logger.Error($"导入库档案搜索失败: {imported.Message}");
                await ShowTipAsync($"历史导入库搜索失败：{imported.Message}\n当前仅显示当前库结果。");
            }

            HasSearchResults = SearchResults.Count > 0;

            if (!HasSearchResults)
            {
                await ShowTipAsync("未找到匹配的档案，请更换关键词重试");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"档案搜索失败: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>当前库档案映射为统一搜索结果项</summary>
    private static ArchiveSearchResultItem MapCurrentToItem(ApplicationEntity app) => new()
    {
        Source = ArchiveSearchSource.Current,
        ApplicationId = app.Id,
        ApplicantName = app.ApplicantName,
        ApplicantIdCard = app.ApplicantIdCard,
        Address = app.Address ?? string.Empty,
        FamilySize = app.FamilySize,
        TotalAmount = app.TotalGuaranteeAmount
    };

    /// <summary>
    /// 选中档案：当前库直接加载；导入库记录加载信息，待点击变更类型时自动建档
    /// </summary>
    [RelayCommand]
    private async Task SelectApplicationAsync(ArchiveSearchResultItem item)
    {
        if (item == null) return;

        SearchResults.Clear();
        HasSearchResults = false;

        if (item.IsFromCurrentLibrary)
        {
            _selectedSearchItem = null;
            await LoadApplicationInfoAsync(item.ApplicationId);
            return;
        }

        // 导入库档案：记录选中项，后续点击变更类型时自动建档
        _selectedSearchItem = item;

        // 用搜索项的基本信息显示摘要（无需加载完整档案）
        ApplicationId = 0;
        ApplicantName = item.ApplicantName;
        ApplicantIdCard = item.ApplicantIdCard;
        CurrentClassification = item.DerivedClassificationCode;
        CurrentClassificationName = item.DerivedClassificationName;
        CurrentAmount = item.TotalAmount;
        // 人均收入与户口类型从导入库带出（与当前库 LoadApplicationInfoAsync 的中文显示一致）
        // 导入库 per_capita_income 为年人均（城市库 monthly*12），月人均 = 年人均÷12；
        // 年人均原样存入 PerCapitaAnnualIncome（年值权威口径）
        PerCapitaIncome = item.PerCapitaIncome / 12;
        PerCapitaAnnualIncome = item.PerCapitaIncome;
        HukouType = PageDefaultValues.GetDisplay(
            PageDefaultValues.HukouTypeOptions,
            PageDefaultValues.HukouTypeCodes,
            item.HukouType);
        ArchiveStatus = "Imported";
        StatusDisplay = $"待建档（{item.SourceDisplay}）";
        IsApplicationStopped = false;
        HasSelectedApplication = true;
        IsNoArchiveHintVisible = false;

        // 家庭人数实读导入库成员计数（不再取搜索项的 FamilySize 值）
        await LoadImportedFamilyMemberSummaryAsync(item);
    }

    /// <summary>
    /// 加载导入库档案的家庭成员并计数、生成名单
    /// 导入库成员无赡养人概念，直接取人员表条数；户主关系为"本人/户主"等
    /// </summary>
    private async Task LoadImportedFamilyMemberSummaryAsync(ArchiveSearchResultItem item)
    {
        try
        {
            var detailResult = await _importedArchiveService.GetImportedFamilyDetailAsync(item, CancellationToken);
            if (detailResult.IsSuccess && detailResult.Value != null)
            {
                var detail = detailResult.Value;
                var names = new List<string>();
                foreach (var m in detail.Members)
                {
                    var rel = string.IsNullOrWhiteSpace(m.Relationship) ? "家庭成员" : DictDisplayHelper.GetFamilyRelationshipDisplay(m.Relationship);
                    names.Add($"{m.Name}（{rel}）");
                }
                FamilySize = detail.Members.Count;
                MemberNamesDisplay = string.Join("、", names);
            }
            else
            {
                _logger.Error($"导入库成员加载失败: {detailResult.Message}");
                FamilySize = item.FamilySize;
                MemberNamesDisplay = $"{item.ApplicantName}（户主）";
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"导入库成员加载异常: {ex.Message}");
            FamilySize = item.FamilySize;
            MemberNamesDisplay = $"{item.ApplicantName}（户主）";
        }
    }

    /// <summary>
    /// 更换档案：清空当前选择，回到搜索状态
    /// </summary>
    [RelayCommand]
    private void DeselectApplication()
    {
        ApplicationId = 0;
        ApplicantName = string.Empty;
        ApplicantIdCard = string.Empty;
        CurrentClassification = string.Empty;
        CurrentClassificationName = string.Empty;
        CurrentAmount = 0;
        FamilySize = 0;
        MemberNamesDisplay = string.Empty;
        PerCapitaIncome = 0;
        HukouType = string.Empty;
        ArchiveStatus = string.Empty;
        StatusDisplay = string.Empty;
        IsApplicationStopped = false;
        HasSelectedApplication = false;
        IsNoArchiveHintVisible = true;
        _selectedSearchItem = null;
        ChangeHistory.Clear();
        HasChangeHistory = false;
    }

    private async Task ShowTipAsync(string message)
    {
        var dialog = ServiceProvider.GetRequiredService<IDialogService>();
        await dialog.DisplayAlertAsync("提示", message, "确定");
    }

    /// <summary>
    /// 变更操作前置校验：必须先选定档案
    /// </summary>
    private async Task<bool> EnsureApplicationSelectedAsync()
    {
        if (HasSelectedApplication) return true;
        await ShowTipAsync("请先在页面上方搜索并选择档案");
        return false;
    }

    /// <summary>
    /// 变更入口兜底校验：必须存在有效的当前库档案ID（未建档/自动建档失败时拦截，防止带 0 进入导致页面空白）
    /// </summary>
    private async Task<bool> EnsureValidApplicationIdAsync()
    {
        if (ApplicationId > 0) return true;
        await ShowTipAsync("未找到有效档案，请返回重新搜索并选择当前库档案");
        return false;
    }

    /// <summary>
    /// 变更操作统一前置：读取数据来源 → 判断建档状态 → 导入库档案自动建档后进入对应变更流程
    /// </summary>
    private async Task<bool> PrepareForChangeAsync()
    {
        // 当前库档案（原生或已建档的导入库）：直接进入
        if (_selectedSearchItem == null) return true;

        try
        {
            IsBusy = true;

            // 导入库档案：自动建档迁移进入当前库，再进入对应变更流程（幂等：已建档直接返回既有档案ID）
            var item = _selectedSearchItem;
            _logger.LogBusiness("导入库档案自动建档后进入变更",
                ("SourceTable", item.SourceTable), ("SourceId", item.SourceId));

            var migrateResult = await _importedArchiveService.MigrateToCurrentAsync(
                item, item.DerivedClassificationCode, CancellationToken);
            if (migrateResult.IsFailure || migrateResult.Value == null)
            {
                await ShowTipAsync($"自动建档失败：{migrateResult.Message}");
                return false;
            }

            ApplicationId = migrateResult.Value.ApplicationId;
            _selectedSearchItem = null;
            _logger.LogBusiness("导入库档案建档完成",
                ("NewApplicationId", ApplicationId));

            // 若自动创建了高龄补贴草稿，提示用户
            var elderlyCount = migrateResult.Value.AutoCreatedElderlyIds?.Count ?? 0;
            if (elderlyCount > 0)
            {
                await ShowTipAsync($"已自动创建高龄补贴草稿 {elderlyCount} 条");
            }

            // 建档后刷新档案信息（户主/金额/分类等），供变更页面与原户主信息展示使用
            await LoadApplicationInfoAsync(ApplicationId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"导入库档案自动建档失败: {ex.Message}");
            await ShowTipAsync("自动建档失败，请查看日志");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    #endregion

    public ChangeViewModel(
        IApplicationService applicationService,
        IChangeService changeService,
        IImportedArchiveService importedArchiveService,
        IFamilyMemberService familyMemberService,
        IDynamicManagementRecordService dynamicRecordService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        Services.Domain.Reporting.IStatisticsService statisticsService,
        IGracePeriodService gracePeriodService,
        Services.Utilities.IBusinessTimelineService businessTimelineService)
    {
        _applicationService = applicationService;
        _changeService = changeService;
        _importedArchiveService = importedArchiveService;
        _familyMemberService = familyMemberService;
        _dynamicRecordService = dynamicRecordService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _statisticsService = statisticsService;
        _gracePeriodService = gracePeriodService;
        _businessTimelineService = businessTimelineService;
        Title = "保障对象动态管理";
    }

    private readonly Services.Domain.Reporting.IStatisticsService _statisticsService = null!;
    private readonly IGracePeriodService _gracePeriodService = null!;
    private readonly Services.Utilities.IBusinessTimelineService _businessTimelineService = null!;

    #region 页头统计栏

    /// <summary>统计加载失败占位</summary>
    private const string StatNA = "—";

    /// <summary>在享保障户数</summary>
    [ObservableProperty]
    private string _activeCountText = StatNA;

    /// <summary>本月变更笔数</summary>
    [ObservableProperty]
    private string _monthlyChangesText = StatNA;

    /// <summary>渐退期临期户数</summary>
    [ObservableProperty]
    private string _graceExpiringText = StatNA;

    private async Task LoadHeaderStatsAsync()
    {
        try
        {
            var socialTask = SafeStat<Services.Domain.Reporting.SocialAssistanceModuleStats>(
                () => _statisticsService.GetSocialAssistanceStatsAsync(),
                s => ActiveCountText = s.ActiveCount.ToString("N0"));
            var changeTask = SafeStat<Services.Domain.Reporting.ChangeStats>(
                () => _statisticsService.GetChangeStatsAsync(),
                s => MonthlyChangesText = s.MonthlyChanges.ToString("N0"));
            var graceTask = SafeStat<int>(
                () => _gracePeriodService.GetExpiringCountAsync(GracePeriodConstants.EXPIRING_WARNING_DAYS),
                v => GraceExpiringText = v.ToString("N0"));
            await Task.WhenAll(socialTask, changeTask, graceTask);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "动态管理页头统计加载失败");
        }
    }

    private async Task SafeStat<T>(Func<Task<Result<T>>> call, Action<T> apply)
    {
        try
        {
            var result = await call();
            if (result.IsSuccess && result.Value != null)
                apply(result.Value);
            else
                _logger.Warn($"页头统计加载失败: {result.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "页头统计加载失败");
        }
    }

    #endregion

    /// <summary>
    /// 加载当前库档案的家庭成员并计数、生成名单
    /// 家庭人数口径：共同生活成员（非赡养抚养扶养） + 户主 1 人，与分类判定一致
    /// </summary>
    private async Task<Result> LoadFamilyMemberSummaryAsync(long applicationId)
    {
        var result = await _familyMemberService.GetByApplicationIdAsync(applicationId, CancellationToken);
        if (result.IsFailure)
        {
            _logger.Error($"家庭成员加载失败: {result.Message}");
            return result;
        }

        var members = result.Value ?? new List<FamilyMember>();

        // 剔除赡养抚养扶养义务人（Support）——不计入家庭人口，仅共同生活成员参与
        var householdMembers = members
            .Where(m => !string.Equals(m.MemberCategory, MemberCategoryConstants.SUPPORT, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(m => m.IsApplicant)
            .ThenBy(m => m.CreatedAt)
            .ToList();

        // 户主是否已有成员记录（relationship_to_head = 'Head' 或 is_applicant）；否则由申请人补为户主
        var hasHeadRecord = householdMembers.Any(m => m.IsApplicant
            || string.Equals(m.RelationshipToHead, "Head", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(m.MemberCategory));

        var names = new List<string>();
        if (!hasHeadRecord)
        {
            names.Add($"{ApplicantName}（户主）");
        }

        foreach (var member in householdMembers)
        {
            var rel = string.IsNullOrWhiteSpace(member.RelationshipToHead)
                ? (member.IsApplicant ? "户主" : "家庭成员")
                : DictDisplayHelper.GetFamilyRelationshipDisplay(member.RelationshipToHead);
            names.Add($"{member.Name}（{rel}）");
        }

        FamilySize = householdMembers.Count + (hasHeadRecord ? 0 : 1);
        MemberNamesDisplay = string.Join("、", names);
        return Result.Success();
    }

    /// <summary>
    /// 加载档案信息
    /// </summary>
    public async Task LoadApplicationInfoAsync(long applicationId)
    {
        ApplicationId = applicationId;
        IsBusy = true;

        try
        {
            _logger.LogBusiness("加载档案信息", ("ApplicationId", applicationId));

            var result = await _applicationService.GetByIdAsync(applicationId, CancellationToken);

            if (result.IsSuccess && result.Value != null)
            {
                var app = result.Value;
                ApplicantName = app.ApplicantName;
                ApplicantIdCard = app.ApplicantIdCard;
                CurrentClassification = app.ClassificationResult ?? string.Empty;
                CurrentClassificationName = ClassificationConstants.ConvertFromCode(CurrentClassification);
                CurrentAmount = app.TotalGuaranteeAmount;
                PerCapitaIncome = app.PerCapitaIncome;
                PerCapitaAnnualIncome = app.PerCapitaAnnualIncome;
                // 户口类型中文显示：复用 PageDefaultValues 标准映射（Rural→农村户口、Urban→城市户口），避免散落魔法值
                HukouType = PageDefaultValues.GetDisplay(
                    PageDefaultValues.HukouTypeOptions,
                    PageDefaultValues.HukouTypeCodes,
                    app.HukouType ?? string.Empty);

                ArchiveStatus = app.Status ?? ApplicationStatusCodes.DRAFT;
                var statusObj = ApplicationStatusExtensions.FromCode(ArchiveStatus);
                StatusDisplay = statusObj.GetDescription();
                IsApplicationStopped = statusObj == ApplicationStatus.Stopped;

                HasSelectedApplication = true;
                IsNoArchiveHintVisible = false;

                // 家庭人数实读家庭成员计数（不再取数据库 family_size 值）
                await LoadFamilyMemberSummaryAsync(applicationId);

                _logger.LogBusiness("档案信息加载完成");
            }
            else
            {
                _logger.Error($"操作失败");
            }

            // 加载变更历史
            await LoadChangeHistoryAsync();
        }
        catch (Exception ex)
        {
            _logger.Error($"操作失败: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 加载变更历史
    /// </summary>
    private async Task LoadChangeHistoryAsync()
    {
        try
        {
            var history = await _changeService.GetChangeHistoryAsync(ApplicationId, CancellationToken);

            ChangeHistory.Clear();
            foreach (var record in history)
            {
                ChangeHistory.Add(record);
            }

            HasChangeHistory = ChangeHistory.Count > 0;
        }
        catch (Exception ex)
        {
            _logger.Error($"操作失败: {ex.Message}");
        }
    }

    #region 导航命令

    /// <summary>
    /// 导航到经济状况复核（复用低收入人口认定申请表单，锁定户主/成员，放开经济/调查/认定）
    /// 导入库建档档案未补全时进入补全模式；已补全/原生档案进入复核模式
    /// </summary>
    [RelayCommand]
    private async Task NavigateToEconomicReviewAsync()
    {
        if (!await EnsureApplicationSelectedAsync()) return;
        if (!await PrepareForChangeAsync()) return;
        if (!await EnsureValidApplicationIdAsync()) return;

        var mode = await ResolveChangeEntryModeAsync(FormOperationMode.Review);

        _logger.LogBusiness("导航到经济状况复核", ("ApplicationId", ApplicationId), ("Mode", mode.ToString()));
        await NavigateToPageAsync<Pages.SocialAssistance.ApplicationFormPage, FormPageParameter>(new FormPageParameter(mode, ApplicationId));
    }

    /// <summary>
    /// 变更入口模式解析：导入库建档档案（source_type=ImportedArchive）且未补全（data_completed_at 为空）
    /// → 先进补全模式（保存后标记已补全，此后进入正常变更模式）；原生档案/已补全档案直接进入正常变更模式。
    /// 经济状况复核与家庭成员变更共用，保证"导入库数据先补全"的流程一致。
    /// </summary>
    private async Task<FormOperationMode> ResolveChangeEntryModeAsync(FormOperationMode normalMode)
    {
        var appResult = await _applicationService.GetByIdAsync(ApplicationId, CancellationToken);
        if (appResult.IsSuccess && appResult.Value != null
            && string.Equals(appResult.Value.SourceType, "ImportedArchive", StringComparison.OrdinalIgnoreCase)
            && appResult.Value.DataCompletedAt == null)
        {
            return FormOperationMode.Completion;
        }
        return normalMode;
    }

    /// <summary>
    /// 导航到家庭信息修正（复用低收入人口认定申请表单，可编辑全部步骤但户主姓名/身份证锁定）
    /// 仅限本月本周期的当前库档案
    /// </summary>
    [RelayCommand]
    private async Task NavigateToFamilyCorrectionAsync()
    {
        if (!await EnsureApplicationSelectedAsync()) return;
        if (!await PrepareForChangeAsync()) return;
        if (!await EnsureValidApplicationIdAsync()) return;

        // 前端周期校验：检查档案是否属于本月经济复核周期
        var periodValidation = await ValidateCurrentPeriodAsync();
        if (periodValidation != null)
        {
            await ShowTipAsync(periodValidation);
            return;
        }

        _logger.LogBusiness("导航到家庭信息修正", ("ApplicationId", ApplicationId));
        await NavigateToPageAsync<Pages.SocialAssistance.ApplicationFormPage, FormPageParameter>(
            new FormPageParameter(FormOperationMode.ReviewWithFamilyCorrection, ApplicationId));
    }

    /// <summary>
    /// 导航到编辑家庭信息（复用低收入人口认定申请表单，可编辑全部步骤但户主姓名/身份证锁定，无周期限制）
    /// </summary>
    [RelayCommand]
    private async Task NavigateToEditFamilyInfoAsync()
    {
        if (!await EnsureApplicationSelectedAsync()) return;
        if (!await PrepareForChangeAsync()) return;
        if (!await EnsureValidApplicationIdAsync()) return;

        _logger.LogBusiness("导航到编辑家庭信息", ("ApplicationId", ApplicationId));
        await NavigateToPageAsync<Pages.SocialAssistance.ApplicationFormPage, FormPageParameter>(
            new FormPageParameter(FormOperationMode.EditFamilyInfo, ApplicationId));
    }

    /// <summary>
    /// 校验档案是否属于本月经济复核周期
    /// </summary>
    private async Task<string?> ValidateCurrentPeriodAsync()
    {
        try
        {
            // 获取当前经济复核周期（A线）
            var currentTimeline = await _businessTimelineService.GetCurrentTimelineAsync(Models.Enums.TimelineType.EconomicReview);
            var cycleStart = currentTimeline.CycleStartDate;
            var cycleEnd = currentTimeline.CycleEndDate;

            // 获取档案信息
            var appResult = await _applicationService.GetByIdAsync(ApplicationId, CancellationToken);
            if (appResult.IsFailure || appResult.Value == null)
            {
                return "档案不存在或已被删除";
            }

            var application = appResult.Value;

            // 检查档案状态：必须是已批准（Approved）状态才能进行家庭信息修正
            if (application.Status != Constants.ApplicationStatusCodes.APPROVED)
            {
                return "只能修正已批准状态的档案";
            }

            // 检查档案的最近变更记录是否在当前周期内
            // 如果档案没有任何变更记录，说明是新档案，可以修正
            // 如果有变更记录，检查最近一次变更是否在当前周期内
            var changeHistory = await _changeService.GetChangeHistoryAsync(ApplicationId, CancellationToken);
            if (changeHistory != null && changeHistory.Count > 0)
            {
                var latestChange = changeHistory.OrderByDescending(c => c.ChangedAt).First();
                if (latestChange.ChangedAt < cycleStart || latestChange.ChangedAt > cycleEnd)
                {
                    return $"该档案最近一次变更不在当前复核周期内（{cycleStart:MM月dd日}-{cycleEnd:MM月dd日}）";
                }
            }

            return null; // 校验通过
        }
        catch (Exception ex)
        {
            _logger.Error($"周期校验失败: {ex.Message}");
            return "周期校验失败，请稍后重试";
        }
    }

    /// <summary>
    /// 导航到家庭成员变更（复用低收入人口认定申请表单：锁户主、Step2 增删成员、经济可调、重新认定，停旧建新）
    /// 导入库建档档案未补全时先进入数据补全模式（与经济状况复核一致）
    /// </summary>
    [RelayCommand]
    private async Task NavigateToMemberChangeAsync()
    {
        if (!await EnsureApplicationSelectedAsync()) return;
        if (!await PrepareForChangeAsync()) return;
        if (!await EnsureValidApplicationIdAsync()) return;

        var mode = await ResolveChangeEntryModeAsync(FormOperationMode.MemberChange);

        _logger.LogBusiness("导航到家庭成员变更", ("ApplicationId", ApplicationId), ("Mode", mode.ToString()));
        await NavigateToPageAsync<Pages.SocialAssistance.ApplicationFormPage, FormPageParameter>(new FormPageParameter(mode, ApplicationId));
    }

    /// <summary>
    /// 导航到户主死亡变更页面
    /// </summary>
    [RelayCommand]
    private async Task NavigateToHouseholdDeathAsync()
    {
        if (!await EnsureApplicationSelectedAsync()) return;
        if (!await PrepareForChangeAsync()) return;
        if (!await EnsureValidApplicationIdAsync()) return;

        _logger.LogBusiness("导航到户主死亡变更", ("ApplicationId", ApplicationId));
        await NavigateToPageAsync<Pages.ChangeManagement.HouseholdDeathChangePage, long>(ApplicationId);
    }

    /// <summary>
    /// 导航到户主变更页面
    /// </summary>
    [RelayCommand]
    private async Task NavigateToHeadChangeAsync()
    {
        if (!await EnsureApplicationSelectedAsync()) return;
        if (!await PrepareForChangeAsync()) return;
        if (!await EnsureValidApplicationIdAsync()) return;

        _logger.LogBusiness("导航到户主变更", ("ApplicationId", ApplicationId));
        await NavigateToPageAsync<Pages.ChangeManagement.HeadChangePage, long>(ApplicationId);
    }

    #endregion

}
