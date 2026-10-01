namespace NewCosmos.Controls;

/// <summary>
/// 黑客帝国 0/1 数据流框体（标题栏 + 绿色边框 + 数字雨）。
/// 定时器驱动数字雨逐帧推进，页面加载时启动、卸载时停止（与 EternalRegressionView 同模式）。
/// </summary>
public partial class MatrixRainView : ContentView
{
    private readonly MatrixRainDrawable _drawable = new();
    private IDispatcherTimer? _timer;

    public MatrixRainView()
    {
        InitializeComponent();
        RainCanvas.Drawable = _drawable;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
#if WINDOWS
        _timer?.Stop();
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(100); // ~10fps（与永劫回归同频，双全屏动画总负载最低）
        _timer.Tick += OnTick;
        _timer.Start();
#endif
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        _timer?.Stop();
        _timer = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _drawable.Advance();
        RainCanvas.Invalidate();
    }
}
