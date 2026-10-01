using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.SocialAssistance;

/// <summary>
/// 渐退期管理列表ViewModel
/// 列出进行中与已到期的渐退期档案，支持搜索、分页、跳转经济复核（允许提前复核）；
/// 选中行联动下方家庭成员明细表（户主+共同生活成员，排除赡养与死亡成员）。
/// </summary>
public partial class GracePeriodExpiringListViewModel : PagedSearchViewModelBase
{
    private readonly IGracePeriodService _gracePeriodService;
    private readonly IFamilyMemberService _familyMemberService;
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IDialogService _dialogService;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    /// <summary>到期未处理列表</summary>
    [ObservableProperty]
    private ObservableCollection<GracePeriodExpiringItem> _expiringItems = new();

    /// <summary>选中的渐退行（下方成员明细表联动）</summary>
    [ObservableProperty]
    private GracePeriodExpiringItem? _selectedGraceItem;

    /// <summary>选中户的家庭成员名单</summary>
    [ObservableProperty]
    private ObservableCollection<FamilyMember> _memberItems = new();

    /// <summary>是否已选中户（成员明细区显隐）</summary>
    [ObservableProperty]
    private bool _hasSelectedItem;

    /// <summary>成员名单加载失败提示（显式失败，不吞成空表）</summary>
    [ObservableProperty]
    private string _memberLoadError = "";

    public GracePeriodExpiringListViewModel(
        IGracePeriodService gracePeriodService,
        IFamilyMemberService familyMemberService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        IDialogService dialogService,
        Services.Domain.Reporting.IStatisticsService statisticsService)
    {
        _gracePeriodService = gracePeriodService;
        _familyMemberService = familyMemberService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _dialogService = dialogService;
        _statisticsService = statisticsService;
    }

    private readonly Services.Domain.Reporting.IStatisticsService _statisticsService = null!;

    /// <summary>统计加载失败占位</summary>
    private const string StatNA = "—";

    /// <summary>在享保障户数（背景上下文）</summary>
    [ObservableProperty]
    private string _activeCountText = StatNA;

    /// <summary>进行中/已到期户数（与列表 TotalCount 同口径，胶囊化展示）</summary>
    [ObservableProperty]
    private string _expiringCountText = StatNA;

    private async Task LoadHeaderStatsAsync()
    {
        try
        {
            var activeResult = await _statisticsService.GetSocialAssistanceStatsAsync();
            ActiveCountText = activeResult.IsSuccess && activeResult.Value != null
                ? activeResult.Value.ActiveCount.ToString("N0") : StatNA;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "渐退期页头统计加载失败");
        }
    }

    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        // 进页重置页码：防止翻页后记录减少导致 OFFSET 越界（计数有、名单空）
        PageIndex = 1;
        await LoadDataAsync();
        _ = LoadHeaderStatsAsync();
    }

    protected override Task LoadDataAsync()
    {
        // 翻页/搜索后清空选中与成员名单，防止展示陈旧户的名单
        SelectedGraceItem = null;
        return LoadPageAsync(
            ct => _gracePeriodService.GetExpiringPagedAsync(SearchText, PageIndex, PageSize, ct),
            ExpiringItems,
            _ => ExpiringCountText = TotalCount.ToString("N0"),
            "加载渐退期列表...");
    }

    /// <summary>选中行切换 → 联动加载成员明细（末次写入生效，await 后校验选中未变再落数据）</summary>
    partial void OnSelectedGraceItemChanged(GracePeriodExpiringItem? value)
    {
        HasSelectedItem = value != null;
        MemberLoadError = "";
        if (value == null)
        {
            MemberItems.Clear();
            return;
        }
        _ = LoadMembersAsync(value.ApplicationId);
    }

    /// <summary>
    /// 加载选中户成员名单：口径同档案输出文本（户主+共同生活成员，排除赡养与死亡成员，户主在前）。
    /// 失败显式写 MemberLoadError，不吞成空表。
    /// </summary>
    private async Task LoadMembersAsync(long applicationId)
    {
        try
        {
            var result = await _familyMemberService.GetByApplicationIdAsync(applicationId);
            if (result.IsFailure || result.Value == null)
            {
                if (SelectedGraceItem?.ApplicationId == applicationId)
                    MemberLoadError = $"成员名单加载失败: {result.Message}";
                return;
            }

            // 已登记死亡成员（历史数据未软删，统一过滤）
            var deathResult = await _familyMemberService.GetDeadIdCardsByApplicationIdAsync(applicationId);
            var deathIdCards = (deathResult.IsSuccess && deathResult.Value != null)
                ? deathResult.Value.ToHashSet()
                : new HashSet<string>();

            var eligible = result.Value
                .Where(m => m.MemberCategory != MemberCategoryConstants.SUPPORT
                    && !deathIdCards.Contains(m.IdCard ?? ""))
                .OrderByDescending(m => m.IsHouseholdHead)
                .ThenBy(m => m.Id)
                .ToList();

            // 竞态守卫：await 期间用户可能已切换/取消选中
            if (SelectedGraceItem?.ApplicationId != applicationId) return;

            MemberItems.Clear();
            foreach (var m in eligible) MemberItems.Add(m);
            MemberLoadError = "";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "渐退期成员名单加载失败");
            if (SelectedGraceItem?.ApplicationId == applicationId)
                MemberLoadError = "成员名单加载失败";
        }
    }

    /// <summary>
    /// 重新核算：跳转到经济复核页面（复用 ApplicationFormPage 的 Review 模式）
    /// </summary>
    [RelayCommand]
    private async Task ReReviewAsync(GracePeriodExpiringItem? item)
    {
        if (item == null) return;

        _logger.LogBusiness("渐退期-重新核算",
            ("ApplicationId", item.ApplicationId),
            ("Name", DataMasker.MaskName(item.ApplicantName ?? "")));

        try
        {
            await NavigateToPageAsync<Pages.SocialAssistance.ApplicationFormPage, FormPageParameter>(new FormPageParameter(FormOperationMode.Review, item.ApplicationId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "导航到经济复核页面失败");
            await _dialogService.DisplayAlertAsync("错误",
                $"打开经济复核页面失败: {ex.Message}", "确定");
        }
    }

    // 返回上一页：使用基类 GoBackCommand（含栈守卫与窗口标题恢复）
}
