namespace NewCosmos.Services.Platform;

/// <summary>
/// 屏幕常亮控制：OCR 识别（模型冷启动 + 多张并发可能数十秒）期间防止系统熄屏打断等待。
/// 两端实现：
///  - Android：给当前窗口置 <c>WindowManagerFlags.KeepScreenOn</c>；
///  - Windows：SetThreadExecutionState(ES_DISPLAY_REQUIRED)（桌面端很少用到，保持接口两端可注入）。
/// 用引用计数租约：同一页面重复进入、或单张/批量识别嵌套申请时，最外层释放才真正解除常亮。
/// </summary>
public interface IScreenAwakeService
{
    /// <summary>申请屏幕常亮，返回释放令牌；Dispose 归还，重复 Dispose 无副作用。</summary>
    IDisposable KeepAwake();
}

/// <summary>
/// <see cref="IScreenAwakeService"/> 的引用计数实现：只在 0→1 时真正置常亮、1→0 时解除。
/// 平台差异由 <see cref="SetAwakeCore"/> 承担。
/// </summary>
public abstract class ScreenAwakeServiceBase : IScreenAwakeService
{
    private readonly object _sync = new();
    private int _references;

    public IDisposable KeepAwake()
    {
        lock (_sync)
        {
            _references++;
            if (_references == 1)
                SetAwakeCore(true);
        }
        return new Lease(this);
    }

    /// <summary>置/清常亮（仅在计数跨越 0 时调用一次，实现方无需再做幂等判断）。</summary>
    protected abstract void SetAwakeCore(bool awake);

    private void ReleaseLease()
    {
        lock (_sync)
        {
            if (_references == 0)
                return;
            _references--;
            if (_references == 0)
                SetAwakeCore(false);
        }
    }

    private sealed class Lease : IDisposable
    {
        private ScreenAwakeServiceBase? _owner;

        public Lease(ScreenAwakeServiceBase owner) => _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.ReleaseLease();
    }
}
