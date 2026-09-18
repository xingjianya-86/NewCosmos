using Microsoft.Maui.Graphics;

namespace NewCosmos.Controls;

/// <summary>
/// 「翁法罗斯 · 永劫回归」底层氛围：CRT 扫描线 + 噪点（无字符矩阵雨）。
/// 状态由外部计时器推进（Advance），绘制只读状态，避免在 Draw 中做随机/推进。
/// </summary>
public sealed class EternalRegressionDrawable : IDrawable
{
    private readonly Random _random = new();
    private float _scanY;
    private bool _scanDown;
    private readonly float[] _noiseOffsets = new float[96];

    /// <summary>故障强度（0-1），随轮回推进增强：噪点/扫描线越到后期越剧烈</summary>
    public float NoiseLevel { get; set; }

    public EternalRegressionDrawable()
    {
        for (var i = 0; i < _noiseOffsets.Length; i++)
            _noiseOffsets[i] = (float)_random.NextDouble() * 100f;
    }

    /// <summary>推进一帧动画状态（由 DispatcherTimer 在 UI 线程调用）</summary>
    public void Advance()
    {
        // 扫描线上下往返（慢速）
        const float step = 1.2f;
        if (_scanDown)
        {
            _scanY += step;
            if (_scanY > _scanMax) { _scanY = _scanMax; _scanDown = false; }
        }
        else
        {
            _scanY -= step;
            if (_scanY < 0) { _scanY = 0; _scanDown = true; }
        }
    }

    private float _scanMax = 720f;

    public void Draw(ICanvas canvas, RectF rect)
    {
        var w = rect.Width;
        var h = rect.Height;
        if (w <= 0 || h <= 0) return;
        _scanMax = h;

        // CRT 扫描线：移动高亮带 + 静态细线（强度随故障等级增强）
        var scanAlpha = 0.05f + 0.08f * NoiseLevel;
        for (var y = 0f; y < h; y += 3f)
        {
            canvas.FillColor = new Color(1f, 1f, 1f, scanAlpha);
            canvas.FillRectangle(0, y, w, 1f);
        }
        canvas.FillColor = new Color(0.6f, 1f, 1f, 0.16f + 0.28f * NoiseLevel);
        canvas.FillRectangle(0, _scanY, w, 2f);

        // 噪点（近似 fractalNoise 覆盖；强度随故障等级增强）
        var noiseAlpha = 0.09f + 0.20f * NoiseLevel;
        for (var i = 0; i < _noiseOffsets.Length; i++)
        {
            var x = ((_noiseOffsets[i] * 1.7f) % w + w) % w;
            var y = ((_noiseOffsets[i] * 7.3f + _scanY * 0.5f) % h + h) % h;
            canvas.FillColor = new Color(1f, 1f, 1f, noiseAlpha);
            canvas.FillRectangle(x, y, 1.5f, 1.5f);
        }
    }
}
