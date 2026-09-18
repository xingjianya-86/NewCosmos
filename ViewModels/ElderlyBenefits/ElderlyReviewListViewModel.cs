using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.ElderlyBenefits;

/// <summary>
/// 高龄津贴复核列表 ViewModel（上：待复核队列；下：在享人员检索）。
/// 检索覆盖当前库在享档案；队列由其他进程（低收入归档/信息变更）触发写入。
/// </summary>
public partial class ElderlyReviewListViewModel : PagedSearchViewModelBase
{
    private readonly IElderlyApplicationService _applicationService = null!;
    private readonly ILoggerService _logger = null!;
    private readonly IServiceProvider _serviceProvider = null!;
    private readonly INewPermissionService _permissionService = null!;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    #region 待复核队列

    [ObservableProperty]
    private ObservableCollection<ElderlyReview> _pendingReviews = new();

    [ObservableProperty]
    private int _pendingCount;

    public bool HasPendingReviews => PendingCount > 0;

    partial void OnPendingCountChanged(int value) => OnPropertyChanged(nameof(HasPendingReviews));

    #endregion

    #region 在享检索结果

    [ObservableProperty]
    private ObservableCollection<ElderlyApplication> _searchResults = new();

    /// <summary>是否具备办理权限（复用 ELDERLY_EDIT）</summary>
    [ObservableProperty]
    private bool _canReview;

    #endregion

    public ElderlyReviewListViewModel(
        IElderlyApplicationService applicationService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        INewPermissionService permissionService)
    {
        _applicationService = applicationService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _permissionService = permissionService;
        Title = "高龄津贴复核";
    }

    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        await LoadPermissionAsync();
        await LoadPendingAsync();
        await LoadDataAsync();
    }

    private async Task LoadPermissionAsync()
    {
        try
        {
            var userId = App.CurrentUserId ?? 0;
            if (userId <= 0) return;
            var permissions = await _permissionService.CheckPermissionsAsync(
                userId, new[] { PermissionCodes.ELDERLY_EDIT });
            CanReview = permissions.TryGetValue(PermissionCodes.ELDERLY_EDIT, out var granted) && granted;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "复核权限加载失败");
        }
    }

    private async Task LoadPendingAsync()
    {
        try
        {
            var result = await _applicationService.GetPendingReviewsAsync();
            if (result.IsSuccess && result.Value != null)
            {
                PendingReviews.Clear();
                foreach (var item in result.Value) PendingReviews.Add(item);
            }
            PendingCount = PendingReviews.Count;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "待复核队列加载失败");
        }
    }

    /// <summary>默认列出在享档案；搜索框（姓名/身份证）沿用同一查询。</summary>
    protected override Task LoadDataAsync()
        => LoadPageAsync(
            ct => _applicationService.GetPagedAsync(
                SearchText, ElderlyBenefitConstants.StatusConfirmed, PageIndex, PageSize, ct),
            SearchResults,
            null,
            "加载在享人员...");

    [RelayCommand]
    private async Task ReviewPendingAsync(ElderlyReview? review)
    {
        if (review == null) return;

        Logger.LogBusiness("进入高龄复核（队列）",
            ("ReviewId", review.Id),
            ("IdCard", DataMasker.MaskIdCard(review.IdCard)));

        await NavigateToPageAsync<Pages.ElderlyBenefits.ElderlyReviewPage, ElderlyReviewPageParameter>(
            new ElderlyReviewPageParameter(
                ReviewId: review.Id,
                ApplicationId: review.OldApplicationId,
                IdCard: review.IdCard,
                HistoryId: review.SourceHistoryId));
    }

    [RelayCommand]
    private async Task ReviewApplicationAsync(ElderlyApplication? app)
    {
        if (app == null) return;

        Logger.LogBusiness("进入高龄复核（在享检索）",
            ("ApplicationId", app.Id),
            ("IdCard", DataMasker.MaskIdCard(app.IdCard)));

        await NavigateToPageAsync<Pages.ElderlyBenefits.ElderlyReviewPage, ElderlyReviewPageParameter>(
            new ElderlyReviewPageParameter(ApplicationId: app.Id, IdCard: app.IdCard));
    }

    /// <summary>按身份证直接复核（支持仅在高龄名册、未建档人员）</summary>
    [RelayCommand]
    private async Task ReviewByIdCardAsync()
    {
        var kw = SearchText?.Trim();
        if (string.IsNullOrWhiteSpace(kw) || kw.Length < 15)
        {
            var dialog = _serviceProvider.GetRequiredService<IDialogService>();
            await dialog.DisplayAlertAsync("提示", "请先在搜索框输入完整身份证号（18位）", "确定");
            return;
        }

        Logger.LogBusiness("进入高龄复核（按身份证）", ("IdCard", DataMasker.MaskIdCard(kw)));
        await NavigateToPageAsync<Pages.ElderlyBenefits.ElderlyReviewPage, ElderlyReviewPageParameter>(
            new ElderlyReviewPageParameter(IdCard: kw));
    }

    // 返回上一页：使用基类 GoBackCommand
}
