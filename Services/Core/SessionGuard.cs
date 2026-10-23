namespace NewCosmos.Services.Core;

/// <summary>
/// 会话闸门实现：状态用 volatile 读，Bypass 用 AsyncLocal（只影响当前异步流）。
/// Singleton 生命周期，但只保存"当前会话"这一不可变快照，不持有每次操作的可变业务状态。
/// </summary>
public sealed class SessionGuard : ISessionGuard
{
    private readonly object _gate = new();
    private readonly AsyncLocal<int> _bypassDepth = new();

    private Guid? _sessionId;
    private bool _armed;
    private bool _revoked;
    private DateTime _blockedFromUtc = DateTime.MinValue;

    public Guid? SessionId
    {
        get { lock (_gate) return _sessionId; }
    }

    public bool IsArmed
    {
        get { lock (_gate) return _armed; }
    }

    public bool IsBlocked
    {
        get
        {
            lock (_gate)
            {
                // 宽限期内（_blockedFromUtc 未到）不拦截，保证被顶号后还能保存表单
                return _armed && _revoked
                    && _bypassDepth.Value == 0
                    && DateTime.UtcNow >= _blockedFromUtc;
            }
        }
    }

    public void Arm(Guid sessionId)
    {
        lock (_gate)
        {
            _sessionId = sessionId;
            _armed = true;
            _revoked = false;
            _blockedFromUtc = DateTime.MinValue;
        }
    }

    public void Disarm()
    {
        lock (_gate)
        {
            _sessionId = null;
            _armed = false;
            _revoked = false;
            _blockedFromUtc = DateTime.MinValue;
        }
    }

    public void Revoke(TimeSpan? grace = null)
    {
        lock (_gate)
        {
            _revoked = true;
            _blockedFromUtc = DateTime.UtcNow + (grace ?? TimeSpan.Zero);
        }
    }

    public IDisposable Bypass()
    {
        _bypassDepth.Value++;
        return new BypassScope(this);
    }

    private sealed class BypassScope : IDisposable
    {
        private readonly SessionGuard _owner;

        public BypassScope(SessionGuard owner) => _owner = owner;

        public void Dispose()
        {
            if (_owner._bypassDepth.Value > 0)
                _owner._bypassDepth.Value--;
        }
    }
}
