using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.ElderlyBenefits;

/// <summary>
/// 普惠高龄下月待办列表 ViewModel（双 Tab：待新增 / 需停旧增新）
/// 列出具体待办老人名单（姓名/身份证/年龄/来源/状态标注），支持快捷办理。
/// Transfer 工单 = 待复核队列（nc_biz_elderly_reviews Pending）中的「已完结档案」子集，
/// 复核办结（Pending→Completed）后该人自动退出待办；待新增建档确认后同样退出。
/// </summary>
public partial class ElderlyPendingListViewModel : ViewModelBase
{
    private readonly IElderlyApplicationService _applicationService = null!;
    private readonly ILoggerService _logger = null!;
    private readonly IServiceProvider _serviceProvider = null!;
    private readonly IDialogService _dialogService = null!;
    private readonly INewPermissionService _permissionService = null!;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    /// <summary>待新增列表（正式表无 Confirmed 且历史名册无记录）</summary>
    [ObservableProperty]
    private ObservableCollection<ElderlyPendingItem> _pendingNewItems = new();

    /// <summary>需停旧增新列表（正式表 Confirmed 在享或历史名册有记录）</summary>
    [ObservableProperty]
    private ObservableCollection<ElderlyPendingItem> _pendingTransferItems = new();

    [ObservableProperty]
    private int _selectedTabIndex = 0;

    public bool IsTab0Selected => SelectedTabIndex == 0;
    public bool IsTab1Selected => SelectedTabIndex == 1;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsTab0Selected));
        OnPropertyChanged(nameof(IsTab1Selected));
    }

    /// <summary>待新增计数文本</summary>
    [ObservableProperty]
    private string _pendingNewCountText = "0";

    /// <summary>需停旧增新计数文本</summary>
    [ObservableProperty]
    private string _pendingTransferCountText = "0";

    public ElderlyPendingListViewModel(
        IElderlyApplicationService applicationService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        IDialogService dialogService,
        INewPermissionService permissionService)
    {
        _applicationService = applicationService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _dialogService = dialogService;
        _permissionService = permissionService;
        Title = "高龄津贴下月待办";
    }

    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        await LoadPendingAsync();
    }

    [RelayCommand]
    private void SwitchTab(string? tabIndex)
    {
        if (int.TryParse(tabIndex, out var idx))
        {
            SelectedTabIndex = idx;
        }
    }

    /// <summary>
    /// 加载下月待办明细并分流到两个 Tab
    /// </summary>
    private async Task LoadPendingAsync()
    {
        await ExecuteAsync(async () =>
        {
            var result = await _applicationService.GetPendingElderlyListAsync(CancellationToken);
            if (result.IsFailure)
                return result;

            PendingNewItems.Clear();
            PendingTransferItems.Clear();

            foreach (var item in result.Value ?? new List<ElderlyPendingItem>())
            {
                if (item.IsTransfer)
                    PendingTransferItems.Add(item);
                else
                    PendingNewItems.Add(item);
            }

            PendingNewCountText = PendingNewItems.Count.ToString("N0");
            PendingTransferCountText = PendingTransferItems.Count.ToString("N0");

            _logger.LogBusiness("加载高龄下月待办",
                ("PendingNew", PendingNewItems.Count),
                ("PendingTransfer", PendingTransferItems.Count));
            return Result.Success();
        }, "加载待办...");
    }

    /// <summary>
    /// 待新增：打开高龄申请表单（Create 模式，预填身份证/姓名，自动判类）
    /// </summary>
    [RelayCommand]
    private async Task HandleNewAsync(ElderlyPendingItem? item)
    {
        if (item == null) return;

        _logger.LogBusiness("高龄待办-待新增办理",
            ("Name", DataMasker.MaskName(item.Name)),
            ("IdCard", DataMasker.MaskIdCard(item.IdCard)));

        await NavigateToPageAsync<Pages.ElderlyBenefits.ElderlyApplicationFormPage, ElderlyFormPageParameter>(
            new ElderlyFormPageParameter(FormOperationMode.Create,
                PrefillIdCard: item.IdCard,
                PrefillName: item.Name));
    }

    /// <summary>
    /// 需停旧增新：改跳高龄津贴复核页统一办理（同事务停旧 stop_reason=REVIEW + 建新 + 复核留痕）。
    /// 复核页按 ApplicationId → HistoryId → IdCard 顺序定位人员并评估类别/身份变化。
    /// </summary>
    [RelayCommand]
    private async Task HandleTransferAsync(ElderlyPendingItem? item)
    {
        if (item == null) return;

        // 办理权限门控（对齐复核列表页 CanReview，复用 ELDERLY_EDIT）
        try
        {
            var userId = App.CurrentUserId ?? 0;
            var permissions = userId > 0
                ? await _permissionService.CheckPermissionsAsync(userId, new[] { PermissionCodes.ELDERLY_EDIT })
                : null;
            if (permissions == null || !permissions.TryGetValue(PermissionCodes.ELDERLY_EDIT, out var granted) || !granted)
            {
                await _dialogService.DisplayAlertAsync("提示", "无权限办理复核，请联系管理员分配「编辑普惠高龄」权限。", "确定");
                return;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "待办复核权限校验失败");
            await _dialogService.DisplayAlertAsync("提示", "权限校验失败，请稍后重试。", "确定");
            return;
        }

        _logger.LogBusiness("高龄待办-需停旧增新转复核",
            ("Name", DataMasker.MaskName(item.Name)),
            ("IdCard", DataMasker.MaskIdCard(item.IdCard)),
            ("ElderlyApplicationId", item.ElderlyApplicationId),
            ("HistoryId", item.HistoryId),
            ("ReviewId", item.ReviewId));

        await NavigateToPageAsync<Pages.ElderlyBenefits.ElderlyReviewPage, ElderlyReviewPageParameter>(
            new ElderlyReviewPageParameter(
                ReviewId: item.ReviewId > 0 ? item.ReviewId : null,
                ApplicationId: item.ElderlyApplicationId > 0 ? item.ElderlyApplicationId : null,
                IdCard: item.IdCard,
                HistoryId: item.HistoryId > 0 ? item.HistoryId : null));
    }

    /// <summary>
    /// 刷新待办列表（处理后返回页面时自动重新加载）
    /// </summary>
    [RelayCommand]
    private Task RefreshAsync() => LoadPendingAsync();

    // 返回上一页：使用基类 GoBackCommand（含栈守卫与窗口标题恢复）
}
