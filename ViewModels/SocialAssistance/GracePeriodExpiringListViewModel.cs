using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.SocialAssistance;

/// <summary>
/// 渐退期到期处理列表ViewModel
/// 列出所有渐退期已到期但尚未停保处理的档案，支持搜索、分页、跳转经济复核
/// </summary>
public partial class GracePeriodExpiringListViewModel : PagedSearchViewModelBase
{
    private readonly IGracePeriodService _gracePeriodService;
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

    public GracePeriodExpiringListViewModel(
        IGracePeriodService gracePeriodService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        IDialogService dialogService,
        Services.Domain.Reporting.IStatisticsService statisticsService)
    {
        _gracePeriodService = gracePeriodService;
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

    /// <summary>渐退期临期户数（与列表 TotalCount 同口径，胶囊化展示）</summary>
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
        await LoadDataAsync();
        _ = LoadHeaderStatsAsync();
    }

    protected override Task LoadDataAsync()
        => LoadPageAsync(
            ct => _gracePeriodService.GetExpiringPagedAsync(SearchText, PageIndex, PageSize, ct),
            ExpiringItems,
            _ => ExpiringCountText = TotalCount.ToString("N0"),
            "加载渐退期到期列表...");

    /// <summary>
    /// 重新核算：跳转到经济复核页面（复用 ApplicationFormPage 的 Review 模式）
    /// </summary>
    [RelayCommand]
    private async Task ReReviewAsync(GracePeriodExpiringItem? item)
    {
        if (item == null) return;

        _logger.LogBusiness("渐退期到期-重新核算",
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
