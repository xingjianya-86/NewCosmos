using Microsoft.Maui.ApplicationModel;
using NewCosmos.Constants;
using NewCosmos.Navigation;
using NewCosmos.Services.Domain.UserManagement;

namespace NewCosmos.Services.Core;

/// <summary>
/// 会话生命周期实现。心跳事件在后台线程触发，所有 UI 操作切主线程。
/// </summary>
public sealed class SessionLifecycleService : ISessionLifecycleService
{
    private readonly ISessionHeartbeatService _heartbeat;
    private readonly IUserSessionService _sessionService;
    private readonly ISessionStore _sessionStore;
    private readonly ISessionGuard _guard;
    private readonly INavigationService _navigationService;
    private readonly IWindowTitleService _windowTitleService;
    private readonly IDialogService _dialogService;
    private readonly IDeviceIdentityProvider _deviceIdentity;
    private readonly ILoggerService _logger;

    private int _ending;

    public SessionLifecycleService(
        ISessionHeartbeatService heartbeat,
        IUserSessionService sessionService,
        ISessionStore sessionStore,
        ISessionGuard guard,
        INavigationService navigationService,
        IWindowTitleService windowTitleService,
        IDialogService dialogService,
        IDeviceIdentityProvider deviceIdentity,
        ILoggerService logger)
    {
        _heartbeat = heartbeat;
        _sessionService = sessionService;
        _sessionStore = sessionStore;
        _guard = guard;
        _navigationService = navigationService;
        _windowTitleService = windowTitleService;
        _dialogService = dialogService;
        _deviceIdentity = deviceIdentity;
        _logger = logger;

        _heartbeat.SessionEnded += HandleSessionEndedAsync;
    }

    public bool IsEnding => Volatile.Read(ref _ending) == 1;

    public async Task StartAsync(Guid sessionId, CancellationToken ct = default)
    {
        _guard.Arm(sessionId);
        await _heartbeat.StartAsync(sessionId, ct);
    }

    public async Task StopAsync()
    {
        await _heartbeat.StopAsync();
        _guard.Disarm();
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        var sessionId = _guard.SessionId;
        if (sessionId.HasValue)
        {
            try
            {
                var revoke = await _sessionService.RevokeAsync(
                    sessionId.Value, SessionConstants.EndReasonUserLogout, App.CurrentUserId, ct);
                if (revoke.IsFailure)
                    _logger.Warn($"[SessionLifecycle] 吊销数据库会话失败（本地仍退出）: {revoke.Message}");
            }
            catch (Exception ex)
            {
                _logger.Warn($"[SessionLifecycle] 吊销数据库会话异常（本地仍退出）: {ex.Message}");
            }
        }

        await EndLocalAsync();
    }

    private async Task HandleSessionEndedAsync(SessionEndedInfo info)
    {
        if (Interlocked.Exchange(ref _ending, 1) == 1) return;

        try
        {
            // 旧会话的迟到事件不能把已重新登录的新会话踢下线
            if (_guard.SessionId != info.SessionId)
            {
                _logger.Info($"[SessionLifecycle] 收到非当前会话的终止事件，忽略: {info.SessionId}");
                return;
            }

            // 桌面给宽限保存；移动端现场使用优先快速回到登录页
            var graceSeconds = 3;
            if (_deviceIdentity.DeviceKind != SessionConstants.DeviceKindAndroid)
            {
                try
                {
                    var policy = await _sessionService.GetPolicyAsync();
                    graceSeconds = Math.Clamp(policy.GraceSeconds, 3, 300);
                }
                catch
                {
                    graceSeconds = SessionConstants.DefaultGraceSeconds;
                }
            }

            var message = SessionConstants.GetEndedMessage(info.EndReason);
            if (_deviceIdentity.DeviceKind != SessionConstants.DeviceKindAndroid)
                message += $"。将在 {graceSeconds} 秒后自动退出，请立即保存当前工作";

            _logger.LogSecurity("会话被终止（客户端退出）", ("SessionId", info.SessionId),
                ("Status", info.Status.ToString()), ("Reason", info.EndReason ?? "unknown"));

            // 提示尽量送达；全局 SnackBar 宿主未就绪时忽略
            try
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                    await _dialogService.ShowSnackBarAsync(message, Components.SnackBarType.Warning, 10000));
            }
            catch { }

            await Task.Delay(TimeSpan.FromSeconds(graceSeconds));

            // 宽限期内用户可能已经用其他端重新登录：绝不能把新会话清掉
            if (_guard.SessionId != info.SessionId)
            {
                _logger.Info("[SessionLifecycle] 宽限期内已切换到新会话，跳过退出收尾");
                return;
            }

            await EndLocalAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SessionLifecycle] 被顶号退出流程异常，强制回登录页");
            try { await EndLocalAsync(); } catch { }
        }
        finally
        {
            Volatile.Write(ref _ending, 0);
        }
    }

    private async Task EndLocalAsync()
    {
        await _heartbeat.StopAsync();
        _guard.Disarm();

        try { await _sessionStore.ClearAsync(); }
        catch (Exception ex) { _logger.Warn($"[SessionLifecycle] 清除本地会话失败: {ex.Message}"); }

        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try
                {
                    // 静态上下文在 UI 线程清空，避免与绑定读取竞争
                    App.ClearSession();
                    await _navigationService.SetRootAsync(NavigationKeys.Login);
                    _windowTitleService.SetPageTitle(null);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[SessionLifecycle] 导航回登录页失败");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SessionLifecycle] 切主线程导航失败");
        }
    }
}
