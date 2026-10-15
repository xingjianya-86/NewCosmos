using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace NewCosmos.Controls;

/// <summary>
/// 手写签名捕获控件：触摸（Android）与鼠标（Windows）通用。
/// 屏幕显示走 ICanvas 矢量绘制（清晰无锯齿），导出走 <see cref="Helpers.SignaturePngEncoder"/> 栅格化为 PNG。
/// 笔迹以画布像素坐标保存，导出时按倍率缩放到目标分辨率。
/// </summary>
public class SignatureCaptureView : GraphicsView
{
    /// <summary>笔迹逻辑线宽（导出按倍率等比放大）。</summary>
    private const float StrokeWidth = 3f;

    private readonly StrokeDrawable _drawable;
    private readonly List<List<PointF>> _strokes = new();
    private List<PointF>? _current;

    public static readonly BindableProperty HasSignatureProperty = BindableProperty.Create(
        nameof(HasSignature), typeof(bool), typeof(SignatureCaptureView), false);

    /// <summary>是否已有签名笔迹（用于启用/禁用「确认」按钮）。</summary>
    public bool HasSignature
    {
        get => (bool)GetValue(HasSignatureProperty);
        private set => SetValue(HasSignatureProperty, value);
    }

    public static readonly BindableProperty TraceTextProperty = BindableProperty.Create(
        nameof(TraceText), typeof(string), typeof(SignatureCaptureView), string.Empty,
        propertyChanged: OnTraceTextChanged);

    /// <summary>
    /// 临摹底图文字（如签名者姓名「张三」）：以淡灰大字显示供用户描红。
    /// 仅屏幕显示，不进导出的签名 PNG（编码器只画笔迹）。
    /// </summary>
    public string TraceText
    {
        get => (string)GetValue(TraceTextProperty);
        set => SetValue(TraceTextProperty, value);
    }

    private static void OnTraceTextChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is SignatureCaptureView view)
        {
            view._drawable.TraceText = newValue as string ?? string.Empty;
            view.Invalidate();
        }
    }

    /// <summary>笔迹变化事件（首次落笔 / 清空时触发）。</summary>
    public event Action? SignatureChanged;

    public SignatureCaptureView()
    {
        _drawable = new StrokeDrawable(_strokes, StrokeWidth);
        Drawable = _drawable;
        BackgroundColor = Colors.White;

        // GraphicsView 不直接暴露指针虚方法，用 PointerGestureRecognizer 捕获触摸/鼠标
        var recognizer = new PointerGestureRecognizer();
        recognizer.PointerPressed += OnPointerPressedHandler;
        recognizer.PointerMoved += OnPointerMovedHandler;
        recognizer.PointerReleased += OnPointerReleasedHandler;
        GestureRecognizers.Add(recognizer);
    }

    private void OnPointerPressedHandler(object? sender, PointerEventArgs e)
    {
        var pos = e.GetPosition(this);
        if (pos == null) return;
        _current = new List<PointF> { new((float)pos.Value.X, (float)pos.Value.Y) };
        _strokes.Add(_current);
        RaiseHasSignatureIfChanged(true);
        Invalidate();
    }

    private void OnPointerMovedHandler(object? sender, PointerEventArgs e)
    {
        if (_current == null) return;
        var pos = e.GetPosition(this);
        if (pos == null) return;
        _current.Add(new PointF((float)pos.Value.X, (float)pos.Value.Y));
        Invalidate();
    }

    private void OnPointerReleasedHandler(object? sender, PointerEventArgs e)
    {
        _current = null;
        Invalidate();
    }

    /// <summary>清空全部笔迹。</summary>
    public void Clear()
    {
        _strokes.Clear();
        _current = null;
        RaiseHasSignatureIfChanged(false);
        Invalidate();
    }

    /// <summary>
    /// 导出签名 PNG。空白签名返回 null。
    /// 输出宽度固定 <paramref name="exportWidth"/>，高度按控件宽高比推算（与文书里的签名框比例一致）。
    /// </summary>
    public byte[]? GetSignaturePng(int exportWidth = 800)
    {
        if (_strokes.Count == 0 || Width <= 0 || Height <= 0)
            return null;

        var ratio = Height / Width;
        var exportHeight = Math.Max(1, (int)Math.Round(exportWidth * ratio));
        var sx = exportWidth / (float)Width;
        var sy = exportHeight / (float)Height;

        var scaled = new List<IReadOnlyList<(float X, float Y)>>(_strokes.Count);
        foreach (var stroke in _strokes)
        {
            var pts = new List<(float X, float Y)>(stroke.Count);
            foreach (var p in stroke)
                pts.Add((p.X * sx, p.Y * sy));
            scaled.Add(pts);
        }

        return Helpers.SignaturePngEncoder.Encode(scaled, exportWidth, exportHeight, StrokeWidth * sx);
    }

    private void RaiseHasSignatureIfChanged(bool value)
    {
        if (HasSignature == value) return;
        HasSignature = value;
        SignatureChanged?.Invoke();
    }

    /// <summary>把笔迹画到画布（圆头线段，黑墨白底）；笔迹前先画临摹底图与签名基线（仅屏幕显示）。</summary>
    private sealed class StrokeDrawable : IDrawable
    {
        private readonly IReadOnlyList<List<PointF>> _strokes;
        private readonly float _width;

        /// <summary>临摹底图文字（淡灰大字，供描红；不进导出）。</summary>
        public string TraceText { get; set; } = string.Empty;

        public StrokeDrawable(IReadOnlyList<List<PointF>> strokes, float width)
        {
            _strokes = strokes;
            _width = width;
        }

        public void Draw(ICanvas canvas, RectF rect)
        {
            canvas.Antialias = true;

            // 1) 临摹底图：淡灰大字居中（描红引导，导出不含）
            if (!string.IsNullOrWhiteSpace(TraceText) && rect.Width > 0 && rect.Height > 0)
            {
                var fontSize = (int)Math.Clamp(rect.Height * 0.55f, 24f, 72f);
                canvas.Font = new Microsoft.Maui.Graphics.Font("sans-serif", fontSize);
                canvas.FontSize = fontSize;
                canvas.FontColor = new Color(0f, 0f, 0f, 0.12f); // 12% 黑，仅隐约可见
                canvas.DrawString(TraceText, rect.X, rect.Y, rect.Width, rect.Height,
                    HorizontalAlignment.Center, VerticalAlignment.Center);
            }

            // 2) 签名基线：浅灰虚线（像纸上签名的那条横线，仅屏幕显示）
            if (rect.Width > 0 && rect.Height > 0)
            {
                var baseY = rect.Y + rect.Height * 0.78f;
                canvas.StrokeColor = new Color(0.78f, 0.80f, 0.84f); // 浅灰
                canvas.StrokeSize = 1f;
                canvas.StrokeDashPattern = new[] { 6f, 4f };
                canvas.DrawLine(rect.X + rect.Width * 0.08f, baseY, rect.X + rect.Width * 0.92f, baseY);
                canvas.StrokeDashPattern = Array.Empty<float>(); // 复位，避免影响笔迹
            }

            // 3) 签名笔迹（黑墨圆头线段）
            canvas.StrokeColor = Colors.Black;
            canvas.StrokeSize = _width;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;

            foreach (var stroke in _strokes)
            {
                if (stroke == null || stroke.Count == 0) continue;

                if (stroke.Count == 1)
                {
                    // 单点：画一个实心小圆（轻点一下也算笔迹）
                    canvas.FillColor = Colors.Black;
                    canvas.FillEllipse(stroke[0].X - _width / 2f, stroke[0].Y - _width / 2f, _width, _width);
                    continue;
                }

                using var path = new PathF();
                path.MoveTo(stroke[0].X, stroke[0].Y);
                for (var i = 1; i < stroke.Count; i++)
                    path.LineTo(stroke[i].X, stroke[i].Y);

                canvas.DrawPath(path);
            }
        }
    }
}
