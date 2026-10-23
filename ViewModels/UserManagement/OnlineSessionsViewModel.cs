using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Components;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;
using NewCosmos.Services.Domain.UserManagement;
using NewCosmos.Services.UserManagement;
using NewCosmos.ViewModels.Base;
using System.Collections.ObjectModel;

namespace NewCosmos.ViewModels.UserManagement;

/// <summary>
/// 在线会话管理：查看当前活动会话（桌面/移动），支持单条强制下线与全部下线（保留操作者自身）。
/// 权限：USER_VIEW 可看，USER_MANAGE_STATUS 可操作。
/// </summary>
public partial class OnlineSessionsViewModel : ViewModelBase
{
    private readonly IUserSessionService _sessionService;
    private readonly INewPermissionService _permissionService;
    private readonly IDataScopeService _dataScopeService;
    private readonly IDialogService _dialogService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerService _logger;

    private int _currentUserId;
    private IReadOnlyCollection<int>? _loadedOrganizationIds;

    protected override IServiceProvider ServiceProvider => _serviceProvider;
    protected override ILoggerService Logger => _logger;

    public OnlineSessionsViewModel(
        IUserSessionService sessionService,
        INewPermissionService permissionService,
        IDataScopeService dataScopeService,
        IDialogService dialogService,
        IServiceProvider serviceProvider,
        ILoggerService logger)
    {
        _sessionService = sessionService;
        _permissionService = permissionService;
        _dataScopeService = dataScopeService;
        _dialogService = dialogService;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public ObservableCollection<OnlineSessionItem> Sessions { get; } = new();

    [ObservableProperty]
    private bool _canView;

    [ObservableProperty]
    private bool _canKick;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public bool HasSessions => Sessions.Count > 0;
    public bool HasNoSessions => Sessions.Count == 0;

    public async Task InitializeAsync()
    {
        _currentUserId = App.CurrentUserId ?? 0;

        try
        {
            var permissions = await _permissionService.CheckPermissionsAsync(_currentUserId,
                new[] { PermissionCodes.USER_VIEW, PermissionCodes.USER_MANAGE_STATUS });

            CanView = permissions.GetValueOrDefault(PermissionCodes.USER_VIEW);
            CanKick = permissions.GetValueOrDefault(PermissionCodes.USER_MANAGE_STATUS);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载在线会话权限失败");
        }

        if (!CanView)
        {
            StatusMessage = "您没有查看在线会话的权限";
            return;
        }

        await LoadAsync();
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (!CanView || IsBusy) return;

        IsBusy = true;
        try
        {
            IReadOnlyCollection<int>? organizationIds = null;
            try
            {
                var isSuperAdmin = await _dataScopeService.IsSuperAdminAsync(_currentUserId, CancellationToken);
                if (!isSuperAdmin)
                {
                    // 非超管必须带组织边界；空集合表示"无可管理组织"，应显示空列表而不是全部
                    organizationIds = await _dataScopeService.GetManageableOrganizationIdsAsync(_currentUserId, CancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"解析数据范围失败，按全部范围查询在线会话: {ex.Message}");
            }
            _loadedOrganizationIds = organizationIds;

            var result = await _sessionService.ListActiveAsync(organizationIds, CancellationToken);
            if (result.IsFailure)
            {
                await ShowFailureAsync(result, "加载在线会话");
                StatusMessage = "加载失败，请重试";
                return;
            }

            Sessions.Clear();
            foreach (var record in result.Value)
                Sessions.Add(Map(record));

            var online = Sessions.Count(s => s.IsOnline);
            StatusMessage = $"共 {Sessions.Count} 个活动会话（其中 {online} 个在线）";
            OnPropertyChanged(nameof(HasSessions));
            OnPropertyChanged(nameof(HasNoSessions));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task KickAsync(OnlineSessionItem? item)
    {
        if (item == null) return;
        if (!CanKick)
        {
            await _dialogService.DisplayAlertAsync("权限不足", "您没有强制下线的权限", "确定");
            return;
        }

        var confirm = await _dialogService.DisplayAlertAsync(
            "强制下线",
            $"确定要结束 {item.UserText} 在「{item.DeviceName}」上的会话吗？\n该设备会在下一次心跳时退出登录。",
            "确定", "取消");
        if (!confirm) return;

        var result = await _sessionService.RevokeAsync(
            item.SessionId, SessionConstants.EndReasonAdminKick, _currentUserId, CancellationToken);

        if (result.IsFailure)
        {
            await ShowFailureAsync(result, "强制下线");
            return;
        }

        _logger.LogSecurity("管理员强制下线会话", ("TargetUserId", item.UserId),
            ("SessionId", item.SessionId), ("DeviceKind", item.DeviceKindText), ("ByUserId", _currentUserId));
        await _dialogService.ShowSnackBarAsync("已强制下线", SnackBarType.Success);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task KickAllAsync()
    {
        if (!CanKick || Sessions.Count == 0) return;

        var confirm = await _dialogService.DisplayAlertAsync(
            "全部下线",
            $"确定要结束当前管理范围内的全部活动会话吗？（列表显示 {Sessions.Count} 个）\n您自己的当前会话会保留。",
            "确定", "取消");
        if (!confirm) return;

        var result = await _sessionService.RevokeAllActiveAsync(
            SessionConstants.EndReasonAdminKick, _currentUserId, App.CurrentSessionId,
            _loadedOrganizationIds, CancellationToken);

        if (result.IsFailure)
        {
            await ShowFailureAsync(result, "全部下线");
            return;
        }

        _logger.LogSecurity("管理员全部强制下线", ("Count", result.Value), ("ByUserId", _currentUserId));
        await _dialogService.ShowSnackBarAsync($"已结束 {result.Value} 个会话", SnackBarType.Success);
        await LoadAsync();
    }

    private static OnlineSessionItem Map(UserSessionRecord record) => new()
    {
        SessionId = record.SessionId,
        UserId = record.UserId,
        Username = record.Username,
        FullName = record.FullName,
        OrganizationName = record.OrganizationName ?? string.Empty,
        DeviceKindText = string.Equals(record.DeviceKind, SessionConstants.DeviceKindAndroid, StringComparison.OrdinalIgnoreCase)
            ? "移动端"
            : "桌面端",
        DeviceName = string.IsNullOrWhiteSpace(record.DeviceName) ? "未知设备" : record.DeviceName,
        AppVersion = record.AppVersion,
        LoginAtText = record.LoginAt.ToLocalTime().ToString("MM-dd HH:mm"),
        LastSeenText = record.LastSeenAt.ToLocalTime().ToString("MM-dd HH:mm:ss"),
        IsOnline = record.IsOnline
    };
}

/// <summary>在线会话列表行（只读展示模型）</summary>
public sealed class OnlineSessionItem
{
    public Guid SessionId { get; init; }
    public int UserId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string FullName { get; init; } = string.Empty;
    public string OrganizationName { get; init; } = string.Empty;
    public string DeviceKindText { get; init; } = string.Empty;
    public string DeviceName { get; init; } = string.Empty;
    public string AppVersion { get; init; } = string.Empty;
    public string LoginAtText { get; init; } = string.Empty;
    public string LastSeenText { get; init; } = string.Empty;
    public bool IsOnline { get; init; }

    public string UserText => string.IsNullOrWhiteSpace(FullName) ? Username : $"{FullName}（{Username}）";
    public string OnlineText => IsOnline ? "在线" : "离线";
}
