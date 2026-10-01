namespace NewCosmos.Controls;

/// <summary>
/// 相机预览控制契约：由各平台 Handler 实现（Android 为原生 Camera2）。
/// </summary>
public interface ICameraPreviewController
{
    /// <summary>启动预览（需已获得相机权限）</summary>
    Task StartAsync();

    /// <summary>停止预览并释放相机</summary>
    void Stop();

    /// <summary>拍照，返回 JPEG 字节（失败返回空数组）</summary>
    Task<byte[]> CaptureAsync();
}

/// <summary>
/// 应用内相机预览控件（用于身份证扫描时叠加实时取景框）。
/// Android 由 Camera2 Handler 实现；其他平台无 Handler 时为空操作（功能仅在 Android 启用）。
/// </summary>
public class CameraPreviewView : View
{
    public Task StartAsync()
        => Handler is ICameraPreviewController controller ? controller.StartAsync() : Task.CompletedTask;

    public void Stop()
    {
        if (Handler is ICameraPreviewController controller)
            controller.Stop();
    }

    public Task<byte[]> CaptureAsync()
        => Handler is ICameraPreviewController controller
            ? controller.CaptureAsync()
            : Task.FromResult(Array.Empty<byte>());
}
