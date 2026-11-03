using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.AssetVerification;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

using ApplicationEntity = NewCosmos.Models.Entities.Application;

namespace NewCosmos.ViewModels.SocialAssistance;

/// <summary>
/// 业务申请工作流 ViewModel
/// 管理五个 Tab：已提交资产核查、有报告未建档、草稿、已建档未提交、已完结档案
/// </summary>
public partial class ApplicationWorkflowViewModel : PagedSearchViewModelBase
{
    private readonly IAssetVerificationService _verificationService;
    private readonly IApplicationService _applicationService;
    private readonly IGracePeriodService _gracePeriodService;
    private readonly IDialogService _dialogService;
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly INewPermissionService _permissionService;
    private readonly AddressResolver _addressResolver;

    private int _loadVersion;
    private CancellationTokenSource? _tabLoadCts;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    #region Tab 状态

    [ObservableProperty]
    private int _selectedTabIndex;

    public bool IsTab0Selected => SelectedTabIndex == 0;
    public bool IsTab1Selected => SelectedTabIndex == 1;
    public bool IsTab2Selected => SelectedTabIndex == 2;
    public bool IsTab3Selected => SelectedTabIndex == 3;
    public bool IsTab4Selected => SelectedTabIndex == 4;

    partial void OnSelectedTabIndexChanged(int value)
    {
        OnPropertyChanged(nameof(IsTab0Selected));
        OnPropertyChanged(nameof(IsTab1Selected));
        OnPropertyChanged(nameof(IsTab2Selected));
        OnPropertyChanged(nameof(IsTab3Selected));
        OnPropertyChanged(nameof(IsTab4Selected));

        PageIndex = 1;
        // 2026-11：保留搜索词跨 Tab（原实现清空 SearchText——换 Tab 后必须重输关键字，
        // 而"按身份证搜同一个人在不同 Tab 的状态"正是高频操作）。页码重置，关键字沿用。

        var version = ++_loadVersion;
        _tabLoadCts?.Cancel();
        _tabLoadCts = new CancellationTokenSource();

        SafeFireAndForget(async () => await LoadCurrentTabDataAsync(_tabLoadCts.Token, version));
    }

    #endregion

    #region 数据集合

    [ObservableProperty]
    private ObservableCollection<WorkflowCardItem> _submittedItems = new();

    [ObservableProperty]
    private ObservableCollection<WorkflowCardItem> _hasReportItems = new();

    [ObservableProperty]
    private ObservableCollection<WorkflowCardItem> _draftItems = new();

    [ObservableProperty]
    private ObservableCollection<WorkflowCardItem> _archiveBuiltItems = new();

    [ObservableProperty]
    private ObservableCollection<WorkflowCardItem> _archiveCompletedItems = new();

    #endregion

    #region 计数徽章

    [ObservableProperty]
    private int _submittedCount;

    [ObservableProperty]
    private int _hasReportCount;

    [ObservableProperty]
    private int _draftCount;

    [ObservableProperty]
    private int _archiveBuiltCount;

    [ObservableProperty]
    private int _archiveCompletedCount;

    #endregion

    public ApplicationWorkflowViewModel(
        IAssetVerificationService verificationService,
        IApplicationService applicationService,
        IGracePeriodService gracePeriodService,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        INewPermissionService permissionService,
        AddressResolver addressResolver)
    {
        _verificationService = verificationService;
        _applicationService = applicationService;
        _gracePeriodService = gracePeriodService;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _permissionService = permissionService;
        _addressResolver = addressResolver;
        Title = "业务申请工作流";
    }

    #region 页面生命周期

    public override async Task OnAppearingAsync()
    {
        try
        {
            await LoadAllCountsAsync();
            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            // 失败必须留痕（§10）：原实现空 catch 只报通用文案，线上无法排查
            _logger.Error($"工作流页加载失败: {ex.Message}");
            ErrorMessage = "页面加载失败，请重试";
        }
    }

    public override void OnDisappearing()
    {
        _tabLoadCts?.Cancel();
        _tabLoadCts?.Dispose();
        _tabLoadCts = null;
        base.OnDisappearing();
    }

    #endregion

    #region Tab 切换

    [RelayCommand]
    private void SwitchTab(string tabIndex)
    {
        if (int.TryParse(tabIndex, out int idx))
        {
            SelectedTabIndex = idx;
        }
    }

    #endregion

    #region 数据加载

    protected override async Task LoadDataAsync()
    {
        await ExecuteAsync(async ct =>
        {
            await LoadCurrentTabDataAsync(ct, _loadVersion);
        }, "加载数据...");
    }

    private async Task LoadCurrentTabDataAsync(CancellationToken ct, int version)
    {
        Models.Results.PagedResult<AssetVerificationTask>? assetPage = null;
        Models.Results.PagedResult<ApplicationEntity>? appPage = null;
        IReadOnlyDictionary<long, DateTime>? graceMap = null;
        string? loadError = null;

        try
        {
            switch (SelectedTabIndex)
            {
                case 0:
                    // 已提交资产核查 - 状态 0（只显示户主）
                    var result0 = await _verificationService.SearchByDateRangePagedAsync(
                        UIConstants.FullRangeStart, UIConstants.FullRangeEnd,
                        SearchText, "0", PageIndex, PageSize, ct, onlyHead: true);
                    if (result0.IsSuccess && result0.Value != null)
                        assetPage = result0.Value;
                    else
                    {
                        _logger.Error("工作流Tab0(已提交经济核对)查询失败: " + result0.ErrorCode + " " + result0.Message);
                        loadError = "已提交经济核对列表加载失败: " + (result0.Message ?? "未知错误");
                    }
                    break;
                case 1:
                    // 有报告未建档 - 状态 1（只显示户主）
                    var result1 = await _verificationService.SearchByDateRangePagedAsync(
                        UIConstants.FullRangeStart, UIConstants.FullRangeEnd,
                        SearchText, "1", PageIndex, PageSize, ct, onlyHead: true);
                    if (result1.IsSuccess && result1.Value != null)
                        assetPage = result1.Value;
                    else
                    {
                        _logger.Error("工作流Tab1(有报告未建档)查询失败: " + result1.ErrorCode + " " + result1.Message);
                        loadError = "有报告未建档列表加载失败: " + (result1.Message ?? "未知错误");
                    }
                    break;
                case 2:
                    // 草稿
                    var result2 = await _applicationService.GetDraftPagedAsync(SearchText, PageIndex, PageSize, ct);
                    if (result2.IsSuccess && result2.Value != null)
                        appPage = result2.Value;
                    else
                    {
                        _logger.Error("工作流Tab2(草稿)查询失败: " + result2.ErrorCode + " " + result2.Message);
                        loadError = "草稿列表加载失败: " + (result2.Message ?? "未知错误");
                    }
                    break;
                case 3:
                    // 已建档未提交
                    var result3 = await _applicationService.GetArchiveBuiltNotSubmittedPagedAsync(SearchText, PageIndex, PageSize, ct);
                    if (result3.IsSuccess && result3.Value != null)
                        appPage = result3.Value;
                    else
                    {
                        _logger.Error("工作流Tab3(已建档未提交)查询失败: " + result3.ErrorCode + " " + result3.Message);
                        loadError = "已建档未提交列表加载失败: " + (result3.Message ?? "未知错误");
                    }
                    break;
                case 4:
                    // 已完结档案
                    var result4 = await _applicationService.GetArchivedPagedAsync(SearchText, PageIndex, PageSize, ct);
                    if (result4.IsSuccess && result4.Value != null)
                        appPage = result4.Value;
                    else
                    {
                        _logger.Error("工作流Tab4(已完结档案)查询失败: " + result4.ErrorCode + " " + result4.Message);
                        loadError = "已完结档案列表加载失败: " + (result4.Message ?? "未知错误");
                    }
                    break;
            }

            // 资产核查两 Tab：预解析"县/镇/村"行政区名称，卡片家庭地址按完整地址展示
            if (assetPage != null)
                await _addressResolver.WarmupFullAddressAsync(assetPage.Items.Select(x => x.Community), ct);

            // 草稿/已建档两 Tab：批量取当前有效渐退期到期日（卡片注释）。
            // 一次 = ANY 查询防 N+1；失败仅记 Warn、注释留空，不打断列表加载。
            if (appPage != null && SelectedTabIndex is 2 or 3 && appPage.Items.Count > 0)
            {
                var graceResult = await _gracePeriodService.GetActiveEndDateMapByApplicationIdsAsync(
                    appPage.Items.Select(x => x.Id).ToList(), ct);
                if (graceResult.IsSuccess)
                    graceMap = graceResult.Value;
                else
                    _logger.Warn("工作流渐退期到期日批量查询失败: " + graceResult.ErrorCode + " " + graceResult.Message);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.Error("工作流列表加载异常: " + ex.Message);
            loadError = "列表加载失败: " + ex.Message;
        }

        if (version != _loadVersion) return;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (version != _loadVersion) return;

            // 加载失败：显式提示并保留旧数据，禁止静默清空列表
            if (!string.IsNullOrEmpty(loadError))
            {
                ErrorMessage = loadError;
                return;
            }

            ErrorMessage = string.Empty;

            // 填充段复用基类模板半段；版本守卫在本方法内保留（防切 Tab 后旧响应覆盖）
            switch (SelectedTabIndex)
            {
                case 0:
                    if (assetPage != null) FillPagedPage(SubmittedItems, MapAssetPage(assetPage, MapSubmittedItem));
                    break;
                case 1:
                    if (assetPage != null) FillPagedPage(HasReportItems, MapAssetPage(assetPage, MapHasReportItem));
                    break;
                case 2:
                    if (appPage != null) FillPagedPage(DraftItems, MapApplicationPage(appPage, "草稿", "Danger", WorkflowCardAction.EditDelete, graceMap));
                    break;
                case 3:
                    // 已建档未提交（此 Tab 支持批量勾选归档）
                    if (appPage != null) FillPagedPage(ArchiveBuiltItems, MapApplicationPage(appPage, "已建档", "Warning", WorkflowCardAction.EditDelete, graceMap, batchSelectable: true));
                    break;
                case 4:
                    if (appPage != null) FillPagedPage(ArchiveCompletedItems, MapApplicationPage(appPage, "已完结", "Success", WorkflowCardAction.ViewArchive));
                    break;
            }
        });
    }

    private async Task LoadAllCountsAsync()
    {
        try
        {
            // 5 个计数并行（原实现 5 条串行 await；互不依赖，WhenAll 后逐个取值容错保留旧值）
            var submittedTask = _verificationService.CountByDateRangeAsync(
                UIConstants.FullRangeStart, UIConstants.FullRangeEnd, "0", onlyHead: true);
            var hasReportTask = _verificationService.CountByDateRangeAsync(
                UIConstants.FullRangeStart, UIConstants.FullRangeEnd, "1", onlyHead: true);
            var draftTask = _applicationService.GetDraftCountAsync();
            var archiveBuiltTask = _applicationService.GetArchiveBuiltNotSubmittedCountAsync();
            var archiveCompletedTask = _applicationService.GetArchivedCountAsync();

            await Task.WhenAll(submittedTask, hasReportTask, draftTask, archiveBuiltTask, archiveCompletedTask);

            // 已提交资产核查 - 状态 0（只统计户主）
            if (submittedTask.Result.IsSuccess)
                SubmittedCount = (int)submittedTask.Result.Value;

            // 有报告未建档 - 状态 1（只统计户主）
            if (hasReportTask.Result.IsSuccess)
                HasReportCount = (int)hasReportTask.Result.Value;

            // 草稿
            if (draftTask.Result.IsSuccess)
                DraftCount = draftTask.Result.Value;

            // 已建档未提交
            if (archiveBuiltTask.Result.IsSuccess)
                ArchiveBuiltCount = archiveBuiltTask.Result.Value;

            // 已完结档案
            if (archiveCompletedTask.Result.IsSuccess)
                ArchiveCompletedCount = archiveCompletedTask.Result.Value;
        }
        catch (Exception ex)
        {
            // 计数失败保留旧值并留痕（原实现空 catch 吞异常，违反 §7；徽章显示旧值优于静默无痕）
            _logger.Warn($"工作流计数加载失败: {ex.Message}");
        }
    }

    #endregion

    #region 导航到档案制作页面

    [RelayCommand]
    private async Task NavigateToArchiveProductionAsync(WorkflowCardItem? item)
    {
        if (item == null) return;

        try
        {
            switch (item.Source)
            {
                case AssetVerificationTask task:
                    await NavigateToPageAsync<Pages.SocialAssistance.ApplicationFormPage, AssetCheckFormParameter>(new AssetCheckFormParameter(task.Id));
                    break;
                case ApplicationEntity app:
                    await NavigateToPageAsync<Pages.SocialAssistance.ApplicationFormPage, FormPageParameter>(new FormPageParameter(FormOperationMode.Edit, app.Id));
                    break;
            }
        }
        catch (Exception ex)
        {
            await _dialogService.DisplayAlertAsync("错误", "打开申请表单失败: " + ex.Message, "确定");
        }
    }

    /// <summary>
    /// 已完结 Tab：查看档案（FormOperationMode.View 只读打开，与档案查询页查看入口同模式）
    /// </summary>
    [RelayCommand]
    private async Task ViewArchiveAsync(WorkflowCardItem? item)
    {
        if (item?.Source is not ApplicationEntity app) return;

        try
        {
            await NavigateToPageAsync<Pages.SocialAssistance.ApplicationFormPage, FormPageParameter>(new FormPageParameter(FormOperationMode.View, app.Id));
        }
        catch (Exception ex)
        {
            await _dialogService.DisplayAlertAsync("错误", "查看档案失败: " + ex.Message, "确定");
        }
    }

    /// <summary>
    /// 已建档 Tab：直达"出文书/归档"（档案制作页 → 档案输出 → 完成归档）。
    /// 原路径必须绕道五步表单点提交才能到档案制作页（6 次点击/3 层），此按钮 1 次直达（§14 归档管线入口同参数）。
    /// </summary>
    [RelayCommand]
    private async Task OpenArchiveOutputAsync(WorkflowCardItem? item)
    {
        if (item?.Source is not ApplicationEntity app) return;

        try
        {
            await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveProductionPage, ApplicationArchiveParameter>(
                new ApplicationArchiveParameter(app.Id));
        }
        catch (Exception ex)
        {
            await _dialogService.DisplayAlertAsync("错误", "打开档案制作失败: " + ex.Message, "确定");
        }
    }

    [RelayCommand]
    private async Task EditDraftAsync(WorkflowCardItem? item)
    {
        if (item?.Source is not ApplicationEntity draft) return;

        try
        {
            await NavigateToPageAsync<Pages.SocialAssistance.ApplicationFormPage, FormPageParameter>(new FormPageParameter(FormOperationMode.Edit, draft.Id));
        }
        catch (Exception ex)
        {
            await _dialogService.DisplayAlertAsync("错误", "编辑草稿失败: " + ex.Message, "确定");
        }
    }

    [RelayCommand]
    private async Task DeleteDraftAsync(WorkflowCardItem? item)
    {
        if (item?.Source is not ApplicationEntity draft) return;

        var confirm = await _dialogService.DisplayAlertAsync("确认删除",
            $"确定要删除草稿 [{draft.ApplicationNo}] {draft.ApplicantName} 吗？\n此操作不可恢复。",
            "删除", "取消");

        if (!confirm) return;

        try
        {
            var result = await _applicationService.DeleteAsync(draft.Id, CancellationToken);
            if (result.IsSuccess)
            {
                _logger.LogBusiness("草稿已删除",
                    ("ApplicationId", draft.Id),
                    ("ApplicationNo", draft.ApplicationNo));

                DraftItems.Remove(item);
                // 徽章=服务端总数-1（原实现 DraftItems.Count 是【当前页】条目数，多页场景删除后徽章骤减）
                if (DraftCount > 0) DraftCount--;
                TotalCount = Math.Max(0, TotalCount - 1);

                // 轻提示替代第二个阻断弹窗（删除已有一次确认框，成功后再弹框=每条两次多余点击）
                await ShowSnackBarAsync("草稿已删除");
            }
            else
            {
                await _dialogService.DisplayAlertAsync("删除失败", result.Message ?? "未知错误", "确定");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"删除草稿失败: {ex.Message}");
            await _dialogService.DisplayAlertAsync("错误", "删除草稿失败: " + ex.Message, "确定");
        }
    }

    #endregion

    #region 统一卡片映射

    private static PagedResult<WorkflowCardItem> MapAssetPage(
        PagedResult<AssetVerificationTask> page,
        Func<AssetVerificationTask, WorkflowCardItem> map)
        => PagedResult<WorkflowCardItem>.FromList(
            page.Items.Select(map).ToList(), page.PageIndex, page.PageSize, page.TotalCount);

    private static PagedResult<WorkflowCardItem> MapApplicationPage(
        PagedResult<ApplicationEntity> page,
        string statusText,
        string statusKind,
        WorkflowCardAction action,
        IReadOnlyDictionary<long, DateTime>? graceMap = null,
        bool batchSelectable = false)
        => PagedResult<WorkflowCardItem>.FromList(
            page.Items.Select(a => MapApplicationItem(a, statusText, statusKind, action, graceMap, batchSelectable)).ToList(),
            page.PageIndex, page.PageSize, page.TotalCount);

    /// <summary>渐退期注释文案：进行中"渐退期至 yyyy-MM-dd"；已过到期日标"已到期"。</summary>
    private static string BuildGraceNote(IReadOnlyDictionary<long, DateTime>? graceMap, long applicationId)
    {
        if (graceMap == null || !graceMap.TryGetValue(applicationId, out var endDate)) return string.Empty;
        return endDate >= DateTime.Today
            ? $"渐退期至 {endDate:yyyy-MM-dd}"
            : $"渐退期已到期（至 {endDate:yyyy-MM-dd}）";
    }

    private WorkflowCardItem MapSubmittedItem(AssetVerificationTask t) => new()
    {
        Source = t,
        Name = t.ArchiveName,
        IdCard = t.ArchiveIdCard,
        StatusText = "已提交",
        StatusKind = "Info",
        MetaLabel = "与户主关系",
        MetaValue = DictDisplayHelper.GetFamilyRelationshipDisplay(t.Relationship),
        Address = _addressResolver.BuildFullAddress(t.Community, t.FamilyAddress),
        Time = t.CreatedAt,
        Action = WorkflowCardAction.ArchiveProduction
    };

    private WorkflowCardItem MapHasReportItem(AssetVerificationTask t) => new()
    {
        Source = t,
        Name = t.ArchiveName,
        IdCard = t.ArchiveIdCard,
        StatusText = "有报告未建档",
        StatusKind = "Warning",
        MetaLabel = "与户主关系",
        MetaValue = DictDisplayHelper.GetFamilyRelationshipDisplay(t.Relationship),
        Address = _addressResolver.BuildFullAddress(t.Community, t.FamilyAddress),
        Time = t.CreatedAt,
        Action = WorkflowCardAction.ArchiveProduction
    };

    private static WorkflowCardItem MapApplicationItem(
        ApplicationEntity a, string statusText, string statusKind, WorkflowCardAction action,
        IReadOnlyDictionary<long, DateTime>? graceMap = null,
        bool batchSelectable = false) => new()
    {
        Source = a,
        Name = a.ApplicantName,
        IdCard = a.ApplicantIdCard,
        StatusText = statusText,
        StatusKind = statusKind,
        ShowSingleRescue = a.IsSingleRescue,
        GraceNote = BuildGraceNote(graceMap, a.Id),
        MetaLabel = "编号",
        MetaValue = a.ApplicationNo,
        Address = null,
        Time = a.CreatedAt,
        Action = action,
        IsBatchSelectable = batchSelectable
    };

    #endregion

    #region 返回

    // 返回上一页：使用基类 GoBackCommand（含栈守卫与窗口标题恢复）

    #endregion

    #region 近亲属备案

    /// <summary>
    /// 卡片级"近亲属备案"入口（权限 NEAR_RELATIVE_MANAGE 校验）：
    /// 带户主上下文（申请ID/姓名/身份证）打开录入页，自动填关联对象行。
    /// </summary>
    [RelayCommand]
    private async Task OpenNearRelativeForAsync(WorkflowCardItem? item)
    {
        if (item?.Source is not ApplicationEntity app) return;

        var userId = App.CurrentUserId;
        if (!userId.HasValue)
        {
            await _dialogService.DisplayAlertAsync("提示", "登录状态已失效，请重新登录", "确定");
            return;
        }

        var hasPermission = await _permissionService.HasPermissionAsync(userId.Value, PermissionCodes.NEAR_RELATIVE_MANAGE, CancellationToken);
        if (!hasPermission)
        {
            await _dialogService.DisplayAlertAsync("提示", "您没有近亲属备案的操作权限", "确定");
            return;
        }

        await NavigateToPageAsync<Pages.SocialAssistance.NearRelativeEntryPage, NearRelativeEntryParameter>(
            new NearRelativeEntryParameter(
                ApplicationId: app.Id,
                ApplicantName: app.ApplicantName,
                ApplicantIdCard: app.ApplicantIdCard));
    }

    #endregion
}
