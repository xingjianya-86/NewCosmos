using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.NearRelative;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.SocialAssistance;

/// <summary>
/// 近亲属备案列表页 ViewModel（独立页面：全部备案名册的展示与维护入口）。
/// 数据源 INearRelativeService.GetBriefsAsync（全量名册，无年月维度）；搜索为客户端过滤。
/// 新增/编辑/删除均校验 NEAR_RELATIVE_MANAGE 权限；编辑跳录入页并带 StaffId。
/// </summary>
public partial class NearRelativeListViewModel : ViewModelBase
{
    private readonly INearRelativeService _nearRelativeService;
    private readonly IDialogService _dialogService;
    private readonly ILoggerService _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly INewPermissionService _permissionService;

    /// <summary>全量名册快照（过滤前）</summary>
    private List<NearRelativeBrief> _allItems = new();

    #region 抽象属性实现
    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;
    #endregion

    #region 属性

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<NearRelativeBrief> _items = new();

    [ObservableProperty]
    private NearRelativeBrief? _selectedItem;

    [ObservableProperty]
    private string _statusText = string.Empty;

    #endregion

    public NearRelativeListViewModel(
        INearRelativeService nearRelativeService,
        IDialogService dialogService,
        ILoggerService logger,
        IServiceProvider serviceProvider,
        INewPermissionService permissionService)
    {
        _nearRelativeService = nearRelativeService;
        _dialogService = dialogService;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _permissionService = permissionService;
        Title = "近亲属备案";
    }

    public override async Task OnAppearingAsync()
    {
        await base.OnAppearingAsync();
        await LoadAsync();
    }

    #region 加载与过滤

    private async Task LoadAsync()
    {
        var result = await ExecuteAsync(
            () => _nearRelativeService.GetBriefsAsync(ct: CancellationToken),
            "正在加载备案名册...");

        // 加载失败显式暴露，保留旧数据，绝不静默清空
        if (result.IsFailure)
        {
            ErrorMessage = result.Message ?? "备案名册加载失败";
            return;
        }

        ErrorMessage = string.Empty;
        _allItems = result.Value ?? new List<NearRelativeBrief>();
        ApplyFilter();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    [RelayCommand]
    private void Search() => ApplyFilter();

    private void ApplyFilter()
    {
        var keyword = SearchText?.Trim() ?? string.Empty;
        var filtered = string.IsNullOrEmpty(keyword)
            ? _allItems
            : _allItems.Where(b =>
                (!string.IsNullOrEmpty(b.StaffName) && b.StaffName.Contains(keyword, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(b.WorkUnit) && b.WorkUnit.Contains(keyword, StringComparison.OrdinalIgnoreCase))).ToList();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            Items.Clear();
            foreach (var brief in filtered)
                Items.Add(brief);
            StatusText = filtered.Count == _allItems.Count
                ? $"共 {filtered.Count} 条备案"
                : $"匹配 {filtered.Count} / 共 {_allItems.Count} 条备案";
        });
    }

    #endregion

    #region 命令

    /// <summary>新增备案（跳录入页）</summary>
    [RelayCommand]
    private async Task NewEntryAsync()
    {
        if (!await EnsurePermissionAsync()) return;
        await NavigateToPageAsync<Pages.SocialAssistance.NearRelativeEntryPage>();
    }

    /// <summary>编辑指定备案（带 StaffId 打开录入页）</summary>
    [RelayCommand]
    private async Task EditAsync(NearRelativeBrief? item)
    {
        if (item == null) return;
        if (!await EnsurePermissionAsync()) return;
        await NavigateToPageAsync<Pages.SocialAssistance.NearRelativeEntryPage, NearRelativeEntryParameter>(
            new NearRelativeEntryParameter(StaffId: item.Id));
    }

    /// <summary>删除指定备案（二次确认）</summary>
    [RelayCommand]
    private async Task DeleteAsync(NearRelativeBrief? item)
    {
        if (item == null) return;
        if (!await EnsurePermissionAsync()) return;

        var confirm = await _dialogService.DisplayAlertAsync(
            "确认删除",
            $"确定要删除 [{item.StaffName}] 的备案及其 {item.LinkCount} 名对象吗？此操作不可恢复。",
            "删除", "取消");
        if (!confirm) return;

        var result = await ExecuteAsync(
            () => _nearRelativeService.DeleteAsync(item.Id, CancellationToken),
            "正在删除...");
        if (result.IsFailure)
        {
            await _dialogService.DisplayAlertAsync("提示", result.Message ?? "删除失败", "确定");
            return;
        }

        _logger.LogBusiness("删除近亲属备案", ("StaffId", item.Id.ToString()));
        await LoadAsync();
        await _dialogService.DisplayAlertAsync("成功", "备案已删除", "确定");
    }

    // 返回上一页：使用基类 GoBackCommand（含栈守卫与窗口标题恢复）

    #endregion

    private async Task<bool> EnsurePermissionAsync()
    {
        var userId = App.CurrentUserId;
        if (!userId.HasValue)
        {
            await _dialogService.DisplayAlertAsync("提示", "登录状态已失效，请重新登录", "确定");
            return false;
        }

        var hasPermission = await _permissionService.HasPermissionAsync(
            userId.Value, PermissionCodes.NEAR_RELATIVE_MANAGE, CancellationToken);
        if (!hasPermission)
        {
            await _dialogService.DisplayAlertAsync("提示", "您没有近亲属备案的操作权限", "确定");
            return false;
        }

        return true;
    }
}
