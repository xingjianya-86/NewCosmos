using Android.Content;
using Android.Graphics;
using Android.Hardware.Camera2;
using Android.Hardware.Camera2.Params;
using Android.Hardware.Display;
using Android.Media;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Microsoft.Maui.Handlers;
using NewCosmos.Controls;

namespace NewCosmos.Platforms.Android.Camera;

/// <summary>
/// Android 原生 Camera2 相机预览 Handler：TextureView 预览 + ImageReader(JPEG) 拍照。
/// 用于身份证扫描页的实时取景框（叠加层，MAUI XAML 绘制）。
/// </summary>
public class CameraPreviewViewHandler : ViewHandler<CameraPreviewView, FrameLayout>, ICameraPreviewController
{
    public static readonly IPropertyMapper<CameraPreviewView, CameraPreviewViewHandler> PropertyMapper =
        new PropertyMapper<CameraPreviewView, CameraPreviewViewHandler>(ViewMapper);

    private TextureView? _textureView;
    private CameraDevice? _cameraDevice;
    private CameraCaptureSession? _captureSession;
    private ImageReader? _imageReader;
    private HandlerThread? _backgroundThread;
    private Handler? _backgroundHandler;
    private global::Android.Util.Size? _previewSize;
    private global::Android.Util.Size? _captureSize;
    private string? _cameraId;
    private int _sensorOrientation;
    private int _viewWidth;
    private int _viewHeight;
    private bool _isOpening;
    private TaskCompletionSource<byte[]>? _captureTcs;
    private readonly object _sync = new();
    private DisplayManager? _displayManager;
    private readonly DisplayListener _displayListener;

    /// <summary>回调内统一兜底：Java 相机回调线程上的异常若越过 JNI 边界会直接终结进程（已实测闪退）。</summary>
    private static void SafeInvoke(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CameraPreview] {what} 异常: {ex.Message}");
        }
    }

    public CameraPreviewViewHandler() : base(PropertyMapper)
    {
        _displayListener = new DisplayListener(this);
    }

    protected override FrameLayout CreatePlatformView()
    {
        var context = Context;
        var container = new FrameLayout(context);
        _textureView = new TextureView(context);
        _textureView.LayoutParameters = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);
        _textureView.SurfaceTextureListener = new SurfaceListener(this);
        container.AddView(_textureView);
        return container;
    }

    protected override void DisconnectHandler(FrameLayout platformView)
    {
        Stop();
        if (_textureView != null)
            _textureView.SurfaceTextureListener = null;
        base.DisconnectHandler(platformView);
    }

    public Task StartAsync()
    {
        StartBackgroundThread();
        RegisterDisplayListener();
        if (_textureView?.IsAvailable == true)
            OpenCamera();
        return Task.CompletedTask;
    }

    public void Stop()
    {
        try
        {
            UnregisterDisplayListener();
            _captureSession?.Close();
            _captureSession = null;
            _cameraDevice?.Close();
            _cameraDevice = null;
            _imageReader?.Close();
            _imageReader = null;
            StopBackgroundThread();
            _captureTcs?.TrySetResult(Array.Empty<byte>());
            _captureTcs = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CameraPreview stop 异常: {ex.Message}");
        }
    }

    public Task<byte[]> CaptureAsync()
    {
        lock (_sync)
        {
            if (_cameraDevice == null || _captureSession == null || _imageReader == null)
                return Task.FromResult(Array.Empty<byte>());

            var readerSurface = _imageReader.Surface;
            if (readerSurface == null)
                return Task.FromResult(Array.Empty<byte>());

            _captureTcs = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                var builder = _cameraDevice.CreateCaptureRequest(CameraTemplate.StillCapture);
                builder.AddTarget(readerSurface);
                builder.Set(CaptureRequest.JpegOrientation!, GetJpegOrientation());
                _captureSession.Capture(builder.Build(), null, _backgroundHandler);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CameraPreview 拍照异常: {ex.Message}");
                _captureTcs.TrySetResult(Array.Empty<byte>());
            }
            return _captureTcs.Task;
        }
    }

    // ---------------- 相机打开 ----------------

    private void OpenCamera()
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity?.GetSystemService(Context.CameraService) is not CameraManager manager)
            return;

        // 纹理回调可能早于 OnAppearing→StartAsync 触发：这里必须先建后台线程，
        // 否则 CreateCaptureSession 的 HandlerExecutor 会拿到 null Handler → 回调线程空引用闪退（实测）。
        StartBackgroundThread();

        if (_cameraDevice != null || _isOpening)
            return; // 预览可用与 StartAsync 会各触发一次，避免重复打开

        _isOpening = true;
        try
        {
            _cameraId = PickBackCameraId(manager);
            if (_cameraId == null)
            {
                _isOpening = false;
                return;
            }

            var characteristics = manager.GetCameraCharacteristics(_cameraId);
            _sensorOrientation = characteristics.Get(CameraCharacteristics.SensorOrientation) is Java.Lang.Integer so
                ? so.IntValue() : 0;

            var map = characteristics.Get(CameraCharacteristics.ScalerStreamConfigurationMap) as StreamConfigurationMap;
            if (map != null)
            {
                _previewSize = ChoosePreviewSize(map);
                _captureSize = ChooseCaptureSize(map);
            }
            _previewSize ??= new global::Android.Util.Size(1280, 720);
            _captureSize ??= new global::Android.Util.Size(1920, 1080);

            manager.OpenCamera(_cameraId, new DeviceStateListener(this), _backgroundHandler);
        }
        catch (Exception ex)
        {
            _isOpening = false;
            Serilog.Log.Error(ex, "[CameraPreview] 打开相机失败");
            // 拍照等待中必须结束，否则扫描页会一直停在"正在拍照…"。
            _captureTcs?.TrySetResult(Array.Empty<byte>());
        }
    }

    private static string? PickBackCameraId(CameraManager manager)
    {
        var ids = manager.GetCameraIdList();
        string? fallback = null;
        foreach (var id in ids)
        {
            fallback ??= id;
            var ch = manager.GetCameraCharacteristics(id);
            if (ch.Get(CameraCharacteristics.LensFacing) is Java.Lang.Integer facing
                && facing.IntValue() == (int)LensFacing.Back)
                return id;
        }
        return fallback;
    }

    private static global::Android.Util.Size ChoosePreviewSize(StreamConfigurationMap map)
    {
        var sizes = map.GetOutputSizes((int)global::Android.Graphics.ImageFormatType.Yuv420888)
                    ?? map.GetOutputSizes(Java.Lang.Class.FromType(typeof(SurfaceTexture)));
        return PickClosest(sizes, 1440, 1920);
    }

    private static global::Android.Util.Size ChooseCaptureSize(StreamConfigurationMap map)
    {
        var sizes = map.GetOutputSizes((int)global::Android.Graphics.ImageFormatType.Jpeg);
        return PickClosest(sizes, 2600, 4096);
    }

    private static global::Android.Util.Size PickClosest(global::Android.Util.Size[]? sizes, int targetWidth, int maxWidth)
    {
        if (sizes == null || sizes.Length == 0)
            return new global::Android.Util.Size(targetWidth, targetWidth * 3 / 4);

        global::Android.Util.Size best = sizes[0];
        long bestScore = long.MaxValue;
        foreach (var s in sizes)
        {
            if (s.Width > maxWidth)
                continue;
            var score = Math.Abs((long)s.Width - targetWidth);
            if (score < bestScore)
            {
                bestScore = score;
                best = s;
            }
        }
        return best;
    }

    private void CreateCaptureSession()
    {
        var texture = _textureView?.SurfaceTexture;
        if (texture == null || _cameraDevice == null)
            return;

        // 后台 Handler 必须非空：回调线程上构建 HandlerExecutor 时为空会直接崩进程
        var handler = _backgroundHandler ??= new Handler(global::Android.OS.Looper.MainLooper!);

        var surface = new Surface(texture);
        _imageReader = ImageReader.NewInstance(_captureSize!.Width, _captureSize.Height,
            global::Android.Graphics.ImageFormatType.Jpeg, 2);
        _imageReader.SetOnImageAvailableListener(new ImageListener(this), handler);

        var readerSurface = _imageReader.Surface;
        if (readerSurface == null)
            return;

        var outputs = new List<Surface> { surface, readerSurface };
        var sessionListener = new SessionStateListener(this);
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            // API 30+：SessionConfiguration 为官方替代路径（旧 List 重载在 Android 11 起过时，CA1422）；
            // minSdk 24 设备仍走 else 分支的旧重载。0 = SessionConfiguration.TYPE_REGULAR
            var outputConfigs = outputs.Select(s => new OutputConfiguration(s)).ToList();
            var sessionConfig = new SessionConfiguration(
                0, outputConfigs, new HandlerExecutor(handler), sessionListener);
            _cameraDevice.CreateCaptureSession(sessionConfig);
        }
        else
        {
            _cameraDevice.CreateCaptureSession(outputs, sessionListener, handler);
        }
    }

    /// <summary>Handler 版 IExecutor 适配（SessionConfiguration 需要 Executor；Handler.getExecutor 未绑定）</summary>
    private sealed class HandlerExecutor : Java.Lang.Object, Java.Util.Concurrent.IExecutor
    {
        private readonly Handler? _handler;
        public HandlerExecutor(Handler handler) => _handler = handler;
        public void Execute(Java.Lang.IRunnable? command)
        {
            if (command == null) return;

            // 兜底：Handler 意外为空时就地执行，绝不抛空引用（Java 回调线程抛异常 = 进程闪退）
            if (_handler == null)
            {
                SafeInvoke("Executor.Run", command.Run);
                return;
            }
            _handler.Post(command);
        }
    }

    private void StartPreview()
    {
        if (_cameraDevice == null || _captureSession == null || _textureView?.SurfaceTexture == null)
            return;

        var surfaceTexture = _textureView.SurfaceTexture;
        if (surfaceTexture == null)
            return;

        try
        {
            var builder = _cameraDevice.CreateCaptureRequest(CameraTemplate.Preview);
            builder.AddTarget(new Surface(surfaceTexture));
            _captureSession.SetRepeatingRequest(builder.Build(), null, _backgroundHandler);

            // 预览尺寸此时才确定，重新校正一次方向
            ConfigureTransform(_viewWidth, _viewHeight);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"启动预览失败: {ex.Message}");
        }
    }

    // ---------------- 方向处理 ----------------

    /// <summary>
    /// 注册显示旋转监听：Application 已声明 ConfigChanges.Orientation，转屏不重建 Activity；
    /// 180° 翻转（或布局未变尺寸时）<c>OnSurfaceTextureSizeChanged</c> 不会触发，
    /// 只靠尺寸回调会让取景画面停在上一方向（用户实测"预览歪/倒，转手机不跟"）。
    /// </summary>
    private void RegisterDisplayListener()
    {
        if (_displayManager != null)
            return;

        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity?.GetSystemService(Context.DisplayService) is not DisplayManager manager)
            return;

        var handler = _backgroundHandler ??= new Handler(Looper.MainLooper!);
        try
        {
            manager.RegisterDisplayListener(_displayListener, handler);
            _displayManager = manager;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CameraPreview] 注册显示监听失败: {ex.Message}");
        }
    }

    private void UnregisterDisplayListener()
    {
        try
        {
            _displayManager?.UnregisterDisplayListener(_displayListener);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CameraPreview] 注销显示监听失败: {ex.Message}");
        }
        _displayManager = null;
    }

    /// <summary>屏幕旋转变化：回主线程按控件当前尺寸重算预览矩阵（尺寸没变也必须重算）。</summary>
    private void OnDisplayRotated()
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity == null)
        {
            ApplyTransformToCurrentView();
            return;
        }
        activity.RunOnUiThread(() => SafeInvoke("DisplayRotated", ApplyTransformToCurrentView));
    }

    private void ApplyTransformToCurrentView()
    {
        var texture = _textureView;
        if (texture == null)
            return;

        var width = texture.Width > 0 ? texture.Width : _viewWidth;
        var height = texture.Height > 0 ? texture.Height : _viewHeight;
        ConfigureTransform(width, height);
    }

    /// <summary>JPEG 输出方向：传感器方向 + 设备物理旋转（后置相机）。</summary>
    private int GetJpegOrientation()
        => (_sensorOrientation + GetDeviceOrientationDegrees() + 360) % 360;

    /// <summary>
    /// 设备相对自然方向的物理旋转角（JPEG 方向用）：与屏幕旋转相反——
    /// ROTATION_90（屏幕内容顺时针转 90°）对应设备逆时针 270°。
    /// </summary>
    private static int GetDeviceOrientationDegrees() => GetDisplayRotation() switch
    {
        SurfaceOrientation.Rotation90 => 270,
        SurfaceOrientation.Rotation180 => 180,
        SurfaceOrientation.Rotation270 => 90,
        _ => 0
    };

    /// <summary>当前屏幕旋转——预览与拍照方向计算的唯一取数点（勿另抄一份）。</summary>
    private static SurfaceOrientation GetDisplayRotation()
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity == null)
            return SurfaceOrientation.Rotation0;

        if (OperatingSystem.IsAndroidVersionAtLeast(30) && activity.Display is { } display)
            return display.Rotation;

#pragma warning disable CA1422 // DefaultDisplay 在 API 30+ 标记过时，仅为 API 24–29 兜底
        return activity.WindowManager?.DefaultDisplay?.Rotation ?? SurfaceOrientation.Rotation0;
#pragma warning restore CA1422
    }

    private static int GetDisplayRotationDegrees() => GetDisplayRotation() switch
    {
        SurfaceOrientation.Rotation90 => 90,
        SurfaceOrientation.Rotation180 => 180,
        SurfaceOrientation.Rotation270 => 270,
        _ => 0
    };

    /// <summary>
    /// 预览方向校正：将传感器方向的缓冲区旋转到与屏幕一致的朝向，并等比放大铺满控件（多余部分居中裁切）。
    /// relative = 传感器方向 − 屏幕旋转（后置相机，均以顺时针为正）；90/270 时宽高互换后再计算缩放。
    /// </summary>
    private void ConfigureTransform(int viewWidth, int viewHeight)
    {
        if (viewWidth <= 0 || viewHeight <= 0)
            return;

        _viewWidth = viewWidth;
        _viewHeight = viewHeight;

        var textureView = _textureView;
        if (textureView == null || _previewSize == null)
            return;

        try
        {
            var relative = (_sensorOrientation - GetDisplayRotationDegrees() + 360) % 360;
            var bufferWidth = _previewSize.Width;
            var bufferHeight = _previewSize.Height;

            var rotatedWidth = relative is 90 or 270 ? bufferHeight : bufferWidth;
            var rotatedHeight = relative is 90 or 270 ? bufferWidth : bufferHeight;

            var centerX = viewWidth / 2f;
            var centerY = viewHeight / 2f;
            var scale = Math.Max(viewWidth / (float)rotatedWidth, viewHeight / (float)rotatedHeight);

            using var matrix = new Matrix();
            // 依次：缓冲区中点对齐控件中心 → 按屏幕方向旋转 → 等比放大到铺满（居中裁切）
            matrix.PostTranslate(centerX - bufferWidth / 2f, centerY - bufferHeight / 2f);
            matrix.PostRotate(relative, centerX, centerY);
            matrix.PostScale(scale, scale, centerX, centerY);
            textureView.SetTransform(matrix);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CameraPreview] ConfigureTransform 异常: {ex.Message}");
        }
    }

    // ---------------- 线程 ----------------

    private void StartBackgroundThread()
    {
        if (_backgroundThread != null)
            return;
        _backgroundThread = new HandlerThread("NewCosmosCamera");
        _backgroundThread.Start();
        _backgroundHandler = new Handler(_backgroundThread.Looper!);
    }

    private void StopBackgroundThread()
    {
        try
        {
            _backgroundThread?.QuitSafely();
            _backgroundThread?.Join();
            _backgroundThread = null;
            _backgroundHandler = null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"停止相机线程异常: {ex.Message}");
        }
    }

    // ---------------- 回调 ----------------

    /// <summary>默认显示器 id（android.view.Display.DEFAULT_DISPLAY = 0）。</summary>
    private const int DefaultDisplayId = 0;

    /// <summary>显示旋转监听：转屏 / 180° 翻转时主动重算预览矩阵。</summary>
    private sealed class DisplayListener : Java.Lang.Object, DisplayManager.IDisplayListener
    {
        private readonly CameraPreviewViewHandler _owner;
        public DisplayListener(CameraPreviewViewHandler owner) => _owner = owner;

        public void OnDisplayAdded(int displayId) { }

        public void OnDisplayRemoved(int displayId) { }

        public void OnDisplayChanged(int displayId)
        {
            if (displayId != DefaultDisplayId)
                return;
            SafeInvoke("OnDisplayChanged", _owner.OnDisplayRotated);
        }
    }

    private sealed class SurfaceListener : Java.Lang.Object, TextureView.ISurfaceTextureListener
    {
        private readonly CameraPreviewViewHandler _owner;
        public SurfaceListener(CameraPreviewViewHandler owner) => _owner = owner;

        public void OnSurfaceTextureAvailable(SurfaceTexture surface, int width, int height)
        {
            SafeInvoke("OnSurfaceTextureAvailable", () =>
            {
                _owner.OpenCamera();
                _owner.ConfigureTransform(width, height);
            });
        }

        public bool OnSurfaceTextureDestroyed(SurfaceTexture surface) => true;

        public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height)
            => SafeInvoke("OnSurfaceTextureSizeChanged", () => _owner.ConfigureTransform(width, height));

        public void OnSurfaceTextureUpdated(SurfaceTexture surface) { }
    }

    private sealed class DeviceStateListener : CameraDevice.StateCallback
    {
        private readonly CameraPreviewViewHandler _owner;
        public DeviceStateListener(CameraPreviewViewHandler owner) => _owner = owner;

        public override void OnOpened(CameraDevice camera)
            => SafeInvoke("OnOpened", () =>
            {
                _owner._isOpening = false;
                _owner._cameraDevice = camera;
                _owner.CreateCaptureSession();
            });

        public override void OnDisconnected(CameraDevice camera)
            => SafeInvoke("OnDisconnected", () =>
            {
                _owner._isOpening = false;
                camera.Close();
                _owner._cameraDevice = null;
            });

        public override void OnError(CameraDevice camera, CameraError error)
            => SafeInvoke("OnError", () =>
            {
                _owner._isOpening = false;
                camera.Close();
                _owner._cameraDevice = null;
                _owner._captureTcs?.TrySetResult(Array.Empty<byte>());
            });
    }

    private sealed class SessionStateListener : CameraCaptureSession.StateCallback
    {
        private readonly CameraPreviewViewHandler _owner;
        public SessionStateListener(CameraPreviewViewHandler owner) => _owner = owner;

        public override void OnConfigured(CameraCaptureSession session)
            => SafeInvoke("OnConfigured", () =>
            {
                _owner._captureSession = session;
                _owner.StartPreview();
            });

        public override void OnConfigureFailed(CameraCaptureSession session)
        {
            Serilog.Log.Error("[CameraPreview] 相机会话配置失败");
            // 配置失败若只写 Debug，CaptureAsync 会永久挂起（页面无超时）。
            _owner._captureTcs?.TrySetResult(Array.Empty<byte>());
        }
    }

    private sealed class ImageListener : Java.Lang.Object, ImageReader.IOnImageAvailableListener
    {
        private readonly CameraPreviewViewHandler _owner;
        public ImageListener(CameraPreviewViewHandler owner) => _owner = owner;

        public void OnImageAvailable(ImageReader? reader)
        {
            if (reader == null)
                return;

            try
            {
                using var image = reader.AcquireNextImage();
                if (image == null)
                    return;

                var buffer = image.GetPlanes()![0].Buffer!;
                var bytes = new byte[buffer.Remaining()];
                buffer.Get(bytes);
                image.Close();

                _owner._captureTcs?.TrySetResult(bytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"读取拍照数据异常: {ex.Message}");
                _owner._captureTcs?.TrySetResult(Array.Empty<byte>());
            }
        }
    }
}
