using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.NavigationData;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

using ApplicationEntity = NewCosmos.Models.Entities.Application;

namespace NewCosmos.ViewModels.SocialAssistance;

/// <summary>
/// 异地协助列表 ViewModel：分页搜索 + 新增/编辑/出文书/办结/删除。
/// 只查 application_type=RemoteAssistance，不进入正式工作流/月报/统计。
/// </summary>
public partial class RemoteAssistanceListViewModel : PagedSearchViewModelBase
{
    private readonly IApplicationService _applicationService;
    private readonly IDialogService _dialogService;
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly INewPermissionService _permissionService;

    private int _loadVersion;

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    #region 状态

    [ObservableProperty]
    private ObservableCollection<ApplicationEntity> _items = new();

    [ObservableProperty]
    private string _selectedStatus = string.Empty;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>状态筛选选项</summary>
    public string[] StatusOptions { get; } = ["", "RemoteAssistanceDraft", "RemoteAssistanceCompleted"];

    /// <summary>下拉框显示文本转换</summary>
    public string GetStatusLabel(string value) => value switch
    {
        "" => "全部状态",
        ApplicationStatusCodes.REMOTE_ASSISTANCE_DRAFT => "办理中",
        ApplicationStatusCodes.REMOTE_ASSISTANCE_COMPLETED => "已办结",
        _ => value
    };

    #endregion

    public RemoteAssistanceListViewModel(
        IApplicationService applicationService,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        INewPermissionService permissionService)
    {
        _applicationService = applicationService;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _permissionService = permissionService;
        Title = "异地协助";
    }

    protected override async Task LoadDataAsync()
    {
        await ExecuteAsync(async ct =>
        {
            var version = ++_loadVersion;
            var result = await _applicationService.GetRemoteAssistancePagedAsync(
                SearchText ?? string.Empty, PageIndex, PageSize,
                string.IsNullOrWhiteSpace(SelectedStatus) ? null : SelectedStatus, ct);

            if (version != _loadVersion) return;

            if (result.IsFailure)
            {
                _logger.Error($"异地协助列表加载失败: {result.ErrorCode} {result.Message}");
                ErrorMessage = "列表加载失败，请重试";
                return;
            }

            Items.Clear();
            foreach (var item in result.Value?.Items ?? [])
            {
                Items.Add(item);
            }
            TotalCount = result.Value?.TotalCount ?? 0;
        }, "加载异地协助记录...");
    }

    partial void OnSelectedStatusChanged(string value)
    {
        PageIndex = 1;
        SafeFireAndForget(async () => await LoadDataAsync());
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        var userId = App.CurrentUserId ?? 0;
        if (userId <= 0)
        {
            await _dialogService.DisplayAlertAsync("提示", "登录状态已失效，请重新登录", "确定");
            return;
        }

        var hasPermission = await _permissionService.HasPermissionAsync(userId, PermissionCodes.INCOME_CREATE, CancellationToken);
        if (!hasPermission)
        {
            await _dialogService.DisplayAlertAsync("提示", "您没有新增异地协助的权限", "确定");
            return;
        }

        await NavigateToPageAsync<Pages.SocialAssistance.ApplicationFormPage, FormPageParameter>(
            new FormPageParameter(FormOperationMode.RemoteAssistance));
    }

    [RelayCommand]
    private async Task EditAsync(ApplicationEntity? item)
    {
        if (item == null) return;

        var userId = App.CurrentUserId ?? 0;
        if (userId <= 0)
        {
            await _dialogService.DisplayAlertAsync("提示", "登录状态已失效，请重新登录", "确定");
            return;
        }

        var hasPermission = await _permissionService.HasPermissionAsync(userId, PermissionCodes.INCOME_EDIT, CancellationToken);
        if (!hasPermission)
        {
            await _dialogService.DisplayAlertAsync("提示", "您没有编辑异地协助的权限", "确定");
            return;
        }

        await NavigateToPageAsync<Pages.SocialAssistance.ApplicationFormPage, FormPageParameter>(
            new FormPageParameter(FormOperationMode.RemoteAssistance, item.Id));
    }

    [RelayCommand]
    private async Task OutputDocumentsAsync(ApplicationEntity? item)
    {
        if (item == null) return;

        // 设置模板文档类型过滤：只出入户调查表和邻里访问调查表
        PrintNavigationData.ClearDocumentMode();
        PrintNavigationData.TemplateDocTypeFilter = new[]
        {
            TemplateDocTypes.Survey,
            TemplateDocTypes.NeighborVisit
        };
        PrintNavigationData.IsRemoteAssistance = true;

        await NavigateToPageAsync<Pages.ArchiveManagement.ArchiveProductionPage, ApplicationArchiveParameter>(
            new ApplicationArchiveParameter(item.Id));
    }

    [RelayCommand]
    private async Task CompleteAsync(ApplicationEntity? item)
    {
        if (item == null) return;

        if (item.Status != ApplicationStatusCodes.REMOTE_ASSISTANCE_DRAFT)
        {
            await _dialogService.DisplayAlertAsync("提示", "仅办理中状态可以办结", "确定");
            return;
        }

        var proceed = await _dialogService.DisplayAlertAsync(
            "确认办结",
            $"确定办结「{item.ApplicantName}」的异地协助吗？办结后不可再编辑。",
            "确认办结", "取消");
        if (!proceed) return;

        var userId = App.CurrentUserId ?? 0;
        if (userId <= 0)
        {
            await _dialogService.DisplayAlertAsync("提示", "登录状态已失效，请重新登录", "确定");
            return;
        }

        var operatorName = string.IsNullOrEmpty(App.CurrentUserName) ? "System" : App.CurrentUserName;
        var result = await _applicationService.CompleteRemoteAssistanceAsync(item.Id, operatorName, CancellationToken);
        if (result.IsFailure)
        {
            _logger.Error($"异地协助办结失败: {result.ErrorCode} {result.Message}");
            await _dialogService.DisplayAlertAsync("办结失败", result.Message ?? "未知错误", "确定");
            return;
        }

        _logger.LogBusiness("异地协助办结", ("ApplicationId", item.Id), ("Operator", operatorName));
        await _dialogService.DisplayAlertAsync("提示", "异地协助已办结", "确定");
        await LoadDataAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync(ApplicationEntity? item)
    {
        if (item == null) return;

        if (item.Status != ApplicationStatusCodes.REMOTE_ASSISTANCE_DRAFT)
        {
            await _dialogService.DisplayAlertAsync("提示", "仅办理中状态可以删除", "确定");
            return;
        }

        var proceed = await _dialogService.DisplayAlertAsync(
            "确认删除",
            $"确定删除「{item.ApplicantName}」的异地协助记录吗？此操作不可恢复。",
            "删除", "取消");
        if (!proceed) return;

        var result = await _applicationService.DeleteAsync(item.Id, CancellationToken);
        if (result.IsFailure)
        {
            _logger.Error($"异地协助删除失败: {result.ErrorCode} {result.Message}");
            await _dialogService.DisplayAlertAsync("删除失败", result.Message ?? "未知错误", "确定");
            return;
        }

        _logger.LogBusiness("异地协助删除", ("ApplicationId", item.Id));
        await LoadDataAsync();
    }

    public static string GetStatusText(string? status) => status switch
    {
        ApplicationStatusCodes.REMOTE_ASSISTANCE_DRAFT => "办理中",
        ApplicationStatusCodes.REMOTE_ASSISTANCE_COMPLETED => "已办结",
        _ => status ?? "—"
    };
}
