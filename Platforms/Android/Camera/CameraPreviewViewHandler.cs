using Android.Content;
using Android.Graphics;
using Android.Hardware.Camera2;
using Android.Hardware.Camera2.Params;
using Android.Media;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Microsoft.Maui.Handlers;
using NewCosmos.Controls;

namespace NewCosmos.Platforms.Android.Camera;

/// <summary>
/// Android 原生 Camera2 相机预览 Handler：TextureView 预览 + ImageReader(JPEG) 拍照�?/// 用于身份证扫描页的实时取景框（叠加层�?MAUI XAML 绘制）�?/// </summary>
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
    private TaskCompletionSource<byte[]>? _captureTcs;
    private readonly object _sync = new();

    public CameraPreviewViewHandler() : base(PropertyMapper) { }

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
        if (_textureView?.IsAvailable == true)
            OpenCamera();
        return Task.CompletedTask;
    }

    public void Stop()
    {
        try
        {
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

        try
        {
            _cameraId = PickBackCameraId(manager);
            if (_cameraId == null)
                return;

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
            System.Diagnostics.Debug.WriteLine($"打开相机失败: {ex.Message}");
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

        var surface = new Surface(texture);
        _imageReader = ImageReader.NewInstance(_captureSize!.Width, _captureSize.Height,
            global::Android.Graphics.ImageFormatType.Jpeg, 2);
        _imageReader.SetOnImageAvailableListener(new ImageListener(this), _backgroundHandler);

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
                0, outputConfigs, new HandlerExecutor(_backgroundHandler!), sessionListener);
            _cameraDevice.CreateCaptureSession(sessionConfig);
        }
        else
        {
            _cameraDevice.CreateCaptureSession(outputs, sessionListener, _backgroundHandler);
        }
    }

    /// <summary>Handler 版 IExecutor 适配（SessionConfiguration 需要 Executor；Handler.getExecutor 未绑定）</summary>
    private sealed class HandlerExecutor : Java.Lang.Object, Java.Util.Concurrent.IExecutor
    {
        private readonly Handler _handler;
        public HandlerExecutor(Handler handler) => _handler = handler;
        public void Execute(Java.Lang.IRunnable? command)
        {
            if (command != null)
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

    private int GetJpegOrientation()
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        var display = activity?.WindowManager?.DefaultDisplay;
        var rotation = display?.Rotation ?? SurfaceOrientation.Rotation0;

        var deviceOrientation = rotation switch
        {
            SurfaceOrientation.Rotation90 => 270,
            SurfaceOrientation.Rotation180 => 180,
            SurfaceOrientation.Rotation270 => 90,
            _ => 0
        };
        return (_sensorOrientation + deviceOrientation + 360) % 360;
    }

    /// <summary>
    /// 预览方向校正：将传感器方向的缓冲区旋转到与屏幕一致的朝向，并等比裁切铺满控件。
    /// relative = 传感器方向 − 屏幕旋转（后置相机）；90/270 时宽高互换后再计算缩放。
    /// </summary>
    private void ConfigureTransform(int viewWidth, int viewHeight)
    {
        _viewWidth = viewWidth;
        _viewHeight = viewHeight;

        if (_textureView == null || _previewSize == null || viewWidth == 0 || viewHeight == 0)
            return;

        var relative = (_sensorOrientation - GetDisplayRotationDegrees() + 360) % 360;
        var bufferWidth = _previewSize.Width;
        var bufferHeight = _previewSize.Height;

        var rotatedWidth = relative is 90 or 270 ? bufferHeight : bufferWidth;
        var rotatedHeight = relative is 90 or 270 ? bufferWidth : bufferHeight;

        var centerX = viewWidth / 2f;
        var centerY = viewHeight / 2f;
        var scale = Math.Max(viewWidth / (float)rotatedWidth, viewHeight / (float)rotatedHeight);

        using var matrix = new Matrix();
        matrix.PostTranslate(centerX - bufferWidth / 2f, centerY - bufferHeight / 2f);
        matrix.PostRotate(relative, centerX, centerY);
        matrix.PostScale(scale, scale, centerX, centerY);
        _textureView.SetTransform(matrix);
    }

    private static int GetDisplayRotationDegrees()
    {
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        var rotation = activity?.WindowManager?.DefaultDisplay?.Rotation ?? SurfaceOrientation.Rotation0;
        return rotation switch
        {
            SurfaceOrientation.Rotation90 => 90,
            SurfaceOrientation.Rotation180 => 180,
            SurfaceOrientation.Rotation270 => 270,
            _ => 0
        };
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

    private sealed class SurfaceListener : Java.Lang.Object, TextureView.ISurfaceTextureListener
    {
        private readonly CameraPreviewViewHandler _owner;
        public SurfaceListener(CameraPreviewViewHandler owner) => _owner = owner;

        public void OnSurfaceTextureAvailable(SurfaceTexture surface, int width, int height)
        {
            _owner.OpenCamera();
            _owner.ConfigureTransform(width, height);
        }

        public bool OnSurfaceTextureDestroyed(SurfaceTexture surface) => true;

        public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int width, int height)
            => _owner.ConfigureTransform(width, height);

        public void OnSurfaceTextureUpdated(SurfaceTexture surface) { }
    }

    private sealed class DeviceStateListener : CameraDevice.StateCallback
    {
        private readonly CameraPreviewViewHandler _owner;
        public DeviceStateListener(CameraPreviewViewHandler owner) => _owner = owner;

        public override void OnOpened(CameraDevice camera)
        {
            _owner._cameraDevice = camera;
            _owner.CreateCaptureSession();
        }

        public override void OnDisconnected(CameraDevice camera)
        {
            camera.Close();
            _owner._cameraDevice = null;
        }

        public override void OnError(CameraDevice camera, CameraError error)
        {
            camera.Close();
            _owner._cameraDevice = null;
            _owner._captureTcs?.TrySetResult(Array.Empty<byte>());
        }
    }

    private sealed class SessionStateListener : CameraCaptureSession.StateCallback
    {
        private readonly CameraPreviewViewHandler _owner;
        public SessionStateListener(CameraPreviewViewHandler owner) => _owner = owner;

        public override void OnConfigured(CameraCaptureSession session)
        {
            _owner._captureSession = session;
            _owner.StartPreview();
        }

        public override void OnConfigureFailed(CameraCaptureSession session)
            => System.Diagnostics.Debug.WriteLine("相机会话配置失败");
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
