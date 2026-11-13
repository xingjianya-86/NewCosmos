using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.DeathStatement;
using NewCosmos.Models.Entities;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.ArchiveManagement;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.ElderlyBenefits;

/// <summary>
/// 普惠高龄下月待办列表 ViewModel（三 Tab：待新增 / 需停旧增新 / 死亡待停发）
/// 列出具体待办老人名单（姓名/身份证/年龄/来源/状态标注），支持快捷办理。
/// Transfer 工单 = 待复核队列（nc_biz_elderly_reviews Pending）中的「已完结档案」子集，
/// 复核办结（Pending→Completed）后该人自动退出待办；待新增建档确认后同样退出。
/// 死亡待停发 Tab = 死亡联动待办（方案2，2026-10-31）：死亡申报自动生成，仅 ELDERLY_EDIT 承接人可见。
/// </summary>
public partial class ElderlyPendingListViewModel : ViewModelBase
{
    private readonly IElderlyApplicationService _applicationService = null!;
    private readonly IDeathStatementService _deathStatementService = null!;
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

    /// <summary>死亡待停发列表（死亡联动待办 ElderlyStop 域；live 已办的行由服务自愈退出）</summary>
    [ObservableProperty]
    private ObservableCollection<DeathLinkageTaskItem> _deathStopItems = new();

    [ObservableProperty]
    private int _selectedTabIndex = 0;

    public bool IsTab0Selected => SelectedTabIndex == 0;
    public bool IsTab1Selected => SelectedTabIndex == 1;
    public bool IsTab2Selected => SelectedTabIndex == 2;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsTab0Selected));
        OnPropertyChanged(nameof(IsTab1Selected));
        OnPropertyChanged(nameof(IsTab2Selected));
    }

    /// <summary>待新增计数文本</summary>
    [ObservableProperty]
    private string _pendingNewCountText = "0";

    /// <summary>需停旧增新计数文本</summary>
    [ObservableProperty]
    private string _pendingTransferCountText = "0";

    /// <summary>死亡待停发计数文本</summary>
    [ObservableProperty]
    private string _deathStopCountText = "0";

    /// <summary>
    /// 是否具备死亡停发承接权限（ELDERLY_EDIT）。
    /// false（典型如村级：只申报不办理）→ 第三 Tab 整体隐藏（申报与办理分离，方案2）。
    /// </summary>
    [ObservableProperty]
    private bool _canHandleDeathStop;

    public ElderlyPendingListViewModel(
        IElderlyApplicationService applicationService,
        IDeathStatementService deathStatementService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        IDialogService dialogService,
        INewPermissionService permissionService)
    {
        _applicationService = applicationService;
        _deathStatementService = deathStatementService;
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
            // 死亡待停发 Tab 对无承接权限者隐藏：忽略越权切页手势（VM 层兜底，UI 已 IsVisible=否）
            if (idx == 2 && !CanHandleDeathStop) return;
            SelectedTabIndex = idx;
        }
    }

    /// <summary>
    /// 加载下月待办明细并分流到两个 Tab + 死亡联动待办（第三 Tab，仅 ELDERLY_VIEW…承接人可见）
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

            // 死亡待停发（方案2）：承接权限过滤后才加载展示（村级无 ELDERLY_EDIT → Tab 隐藏 + 清空）
            await LoadDeathStopAsync();
            return Result.Success();
        }, "加载待办...");
    }

    /// <summary>
    /// 加载死亡联动待办（ElderlyStop 域）。服务侧先按范围幂等补齐再实时判定办结，
    /// 仅返回未办任务；范围解析失败仅记日志（Tab 显隐由权限决定，数据空不误导权限态）。
    /// </summary>
    private async Task LoadDeathStopAsync()
    {
        DeathStopItems.Clear();
        CanHandleDeathStop = await HasPermissionAsync(PermissionCodes.ELDERLY_EDIT);
        if (!CanHandleDeathStop)
        {
            DeathStopCountText = "0";
            return;
        }

        try
        {
            var scopeResult = await _deathStatementService.ResolveScopeAsync(CancellationToken);
            if (scopeResult.IsFailure)
            {
                _logger.Warn($"死亡待停发加载跳过（范围解析失败）: {scopeResult.Message}");
                DeathStopCountText = "0";
                return;
            }

            var tasks = await _deathStatementService.GetLinkageTasksAsync(
                scopeResult.Value, new[] { DeathLinkageConstants.DomainElderlyStop }, CancellationToken);
            if (tasks.IsFailure)
            {
                _logger.Warn($"死亡待停发加载失败: {tasks.Message}");
                DeathStopCountText = "0";
                return;
            }

            foreach (var task in tasks.Value ?? new List<DeathLinkageTaskItem>())
                DeathStopItems.Add(task);
            DeathStopCountText = DeathStopItems.Count.ToString("N0");

            _logger.LogBusiness("加载死亡待停发", ("Count", DeathStopItems.Count));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "死亡待停发加载异常");
            DeathStopCountText = "0";
        }
    }

    /// <summary>单项权限校验（承接动作门控；查询异常按无权限处理，保守拒绝）</summary>
    private async Task<bool> HasPermissionAsync(string permissionCode)
    {
        try
        {
            var userId = App.CurrentUserId ?? 0;
            if (userId <= 0) return false;
            var permissions = await _permissionService.CheckPermissionsAsync(userId, new[] { permissionCode });
            return permissions != null && permissions.TryGetValue(permissionCode, out var granted) && granted;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"权限校验失败: {permissionCode}");
            return false;
        }
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
    /// 死亡待停发：跳高龄停发页（同联动卡预填契约：死亡日期/时刻/原因带入，
    /// 目标页仍走自身校验与确认，不跨域写）。
    /// </summary>
    [RelayCommand]
    private async Task HandleDeathStopAsync(DeathLinkageTaskItem? task)
    {
        if (task == null) return;

        // 办理权限门控（复用 HasPermissionAsync；无权限不跳转——与联动卡禁用口径一致）
        if (!await HasPermissionAsync(PermissionCodes.ELDERLY_EDIT))
        {
            await _dialogService.DisplayAlertAsync("提示", "无权限办理停发，请联系管理员分配「编辑普惠高龄」权限。", "确定");
            return;
        }

        if (task.RefElderlyId is not { } elderlyId)
        {
            await _dialogService.DisplayAlertAsync("提示", "任务缺少关联高龄档案ID，请到死亡情况说明页处理。", "确定");
            return;
        }

        var appResult = await _applicationService.GetByIdAsync(elderlyId, CancellationToken);
        if (appResult.IsFailure || appResult.Value == null)
        {
            await _dialogService.DisplayAlertAsync("提示", $"高龄档案加载失败：{appResult.Message}", "确定");
            return;
        }

        _logger.LogBusiness("高龄待办-死亡停发办理",
            ("Name", DataMasker.MaskName(task.DeceasedName)),
            ("IdCard", DataMasker.MaskIdCard(task.DeceasedIdCard)),
            ("TaskId", task.Id));

        DateTime? deathDate = DateTime.TryParse(task.DeathDateText, out var parsed) ? parsed : null;
        await NavigateToPageAsync<Pages.ElderlyBenefits.ElderlyStopPage, ElderlyStopPrefill>(
            new ElderlyStopPrefill(appResult.Value, deathDate, task.DeathTimeText, task.DeathReason));
    }

    /// <summary>
    /// 刷新待办列表（处理后返回页面时自动重新加载）
    /// </summary>
    [RelayCommand]
    private Task RefreshAsync() => LoadPendingAsync();

    /// <summary>行内「照片」：打开该人员的档案照片（查询/预览/勾选批量推送打印/插入），预填身份证自动检索。</summary>
    [RelayCommand]
    private async Task OpenPhotosAsync(string? idCard)
    {
        if (string.IsNullOrWhiteSpace(idCard))
        {
            await _dialogService.DisplayAlertAsync("提示", "该记录缺少身份证号，无法查看照片", "确定");
            return;
        }

        _logger.LogBusiness("高龄待办-档案照片", ("IdCard", DataMasker.MaskIdCard(idCard)));
        await NavigateToPageAsync<Pages.ArchiveManagement.ArchivePhotoQueryPage, ArchivePhotoQueryParameter>(
            new ArchivePhotoQueryParameter(idCard.Trim()));
    }

    // 返回上一页：使用基类 GoBackCommand（含栈守卫与窗口标题恢复）
}
