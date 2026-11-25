#if WINDOWS
using System.Runtime.InteropServices;
using NewCosmos.Services.Platform;

namespace NewCosmos.Platforms.Windows;

/// <summary>
/// Windows 屏幕常亮：<c>SetThreadExecutionState</c> 阻止显示器休眠。
/// 桌面端 OCR 识别一般很快，保留实现是为了让 <see cref="IScreenAwakeService"/> 两端都可注入、调用处无平台分支。
/// </summary>
public sealed class WindowsScreenAwakeService : ScreenAwakeServiceBase
{
    /// <summary>ES_CONTINUOUS：后续状态保持到再次调用为止。</summary>
    private const uint EsContinuous = 0x80000000;

    /// <summary>ES_DISPLAY_REQUIRED：请求显示器保持点亮。</summary>
    private const uint EsDisplayRequired = 0x00000002;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetThreadExecutionState(uint esFlags);

    protected override void SetAwakeCore(bool awake)
        => SetExecutionState(awake ? EsContinuous | EsDisplayRequired : EsContinuous);

    private static void SetExecutionState(uint flags)
    {
        try
        {
            SetThreadExecutionState(flags);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ScreenAwake] SetThreadExecutionState 失败: {ex.Message}");
        }
    }
}
#endif
