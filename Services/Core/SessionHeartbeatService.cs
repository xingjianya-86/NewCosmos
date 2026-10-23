using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Domain.UserManagement;

namespace NewCosmos.Services.Core;
/// <summary>
/// 心跳实现：单循环、可重启；不因单次网络失败判定失效，避免 ZeroTier/4G 抖动误踢。
/// </summary>
public sealed class SessionHeartbeatService : ISessionHeartbeatService
{
    private readonly IUserSessionService _sessionService;
    private readonly ISessionGuard _guard;
    private readonly IDeviceIdentityProvider _deviceIdentity;
    private readonly ILoggerService _logger;
    private readonly object _gate = new();

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private Guid _sessionId;
    private int _graceSeconds = SessionConstants.DefaultGraceSeconds;

    public SessionHeartbeatService(
        IUserSessionService sessionService,
        ISessionGuard guard,
        IDeviceIdentityProvider deviceIdentity,
        ILoggerService logger)
    {
        _sessionService = sessionService;
        _guard = guard;
        _deviceIdentity = deviceIdentity;
        _logger = logger;
    }

    public event Func<SessionEndedInfo, Task>? SessionEnded;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
                return _loop is { IsCompleted: false } && _cts is { IsCancellationRequested: false };
        }
    }

    public async Task StartAsync(Guid sessionId, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_sessionId == sessionId && _loop is { IsCompleted: false } && _cts is { IsCancellationRequested: false })
                return;
        }

        var isAndroid = string.Equals(_deviceIdentity.DeviceKind, SessionConstants.DeviceKindAndroid, StringComparison.OrdinalIgnoreCase);
        int interval;
        int grace;
        try
        {
            var policy = await _sessionService.GetPolicyAsync(ct);
            interval = isAndroid ? policy.HeartbeatMobileSeconds : policy.HeartbeatDesktopSeconds;
            grace = isAndroid ? 3 : policy.GraceSeconds;
        }
        catch
        {
            interval = isAndroid
                ? SessionConstants.DefaultHeartbeatMobileSeconds
                : SessionConstants.DefaultHeartbeatDesktopSeconds;
            grace = isAndroid ? 3 : SessionConstants.DefaultGraceSeconds;
        }

        interval = Math.Clamp(interval, 15, 600);
        grace = Math.Clamp(grace, 0, 300);

        lock (_gate)
        {
            if (_sessionId == sessionId && _loop is { IsCompleted: false } && _cts is { IsCancellationRequested: false })
                return;

            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();
            _sessionId = sessionId;
            _graceSeconds = grace;

            var token = _cts.Token;
            _loop = Task.Run(() => RunLoopAsync(sessionId, interval, token));
        }

        _logger.Info($"[SessionHeartbeat] 已启动: SessionId={sessionId}, Interval={interval}s, Grace={grace}s");
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? cts;
        Task? loop;
        lock (_gate)
        {
            cts = _cts;
            loop = _loop;
            _cts = null;
            _loop = null;
            _sessionId = Guid.Empty;
        }

        if (cts == null) return;

        try { cts.Cancel(); } catch { /* 已释放 */ }

        if (loop != null)
        {
            // 心跳可能正卡在数据库命令上：最多等 3 秒，避免登出/被踢时界面长时间无响应
            var completed = await Task.WhenAny(loop, Task.Delay(3000)) == loop;
            if (completed)
            {
                try { await loop; }
                catch (OperationCanceledException) { }
                catch (Exception ex) { _logger.Warn($"[SessionHeartbeat] 停止时异常: {ex.Message}"); }
            }
            else
            {
                _logger.Warn("[SessionHeartbeat] 停止等待超时（后台循环将在命令返回后自行结束）");
            }
        }

        if (loop is null || loop.IsCompleted)
            cts.Dispose();

        _logger.Info("[SessionHeartbeat] 已停止");
    }

    private async Task RunLoopAsync(Guid sessionId, int intervalSeconds, CancellationToken token)
    {
        var consecutiveFailures = 0;

        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            Result<SessionTouchResult> touch;
            try
            {
                touch = await _sessionService.TouchAsync(sessionId, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                LogNetworkFailure(consecutiveFailures, ex.Message);
                continue;
            }

            if (touch.IsFailure)
            {
                // 网络/数据库不可用：不判定会话失效，等下一次心跳
                consecutiveFailures++;
                LogNetworkFailure(consecutiveFailures, touch.Message);
                continue;
            }

            consecutiveFailures = 0;
            if (touch.Value.IsActive)
                continue;

            _logger.Warn($"[SessionHeartbeat] 会话已失效: Status={touch.Value.Status}, Reason={touch.Value.EndReason}");
            _logger.LogSecurity("登录会话被终止", ("SessionId", sessionId),
                ("Status", touch.Value.Status.ToString()), ("Reason", touch.Value.EndReason ?? "unknown"));

            // 宽限期内的写入放行（用户需要保存未完成表单），宽限期后闸门开始拦截；
            // 本地收尾（导航登录页）会 Disarm。事件用后台任务触发，避免心跳循环与收尾互相等待。
            if (_guard.SessionId != sessionId)
            {
                // 已切换到新会话（例如用户已重新登录），旧循环的结论不能作用于新会话
                _logger.Info("[SessionHeartbeat] 会话已切换，忽略旧会话的失效结论");
                return;
            }

            var graceSeconds = Volatile.Read(ref _graceSeconds);
            _guard.Revoke(TimeSpan.FromSeconds(graceSeconds));

            RaiseEnded(new SessionEndedInfo
            {
                SessionId = sessionId,
                Status = touch.Value.Status,
                EndReason = touch.Value.EndReason
            });
            return;
        }
    }

    private void LogNetworkFailure(int failures, string? message)
    {
        // 弱网可能持续很久：首次与每 10 次记一条，避免日志刷屏
        if (failures == 1 || failures % 10 == 0)
            _logger.Warn($"[SessionHeartbeat] 心跳失败 {failures} 次（不判定会话失效）: {message}");
    }

    private void RaiseEnded(SessionEndedInfo info)
    {
        var handler = SessionEnded;
        if (handler == null) return;

        _ = Task.Run(async () =>
        {
            foreach (var target in handler.GetInvocationList().Cast<Func<SessionEndedInfo, Task>>())
            {
                try
                {
                    await target(info);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[SessionHeartbeat] 会话终止处理失败");
                }
            }
        });
    }
}
