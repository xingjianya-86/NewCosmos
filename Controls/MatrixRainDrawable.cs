using Microsoft.Maui.Graphics;

namespace NewCosmos.Controls;

/// <summary>
/// 黑客帝国 0/1 数字雨（Matrix Rain）绘制器。
/// 状态由外部定时器推进（Advance），Draw 只读状态，随机只在 Advance 中产生（与 EternalRegressionDrawable 规范一致）。
/// </summary>
public sealed class MatrixRainDrawable : IDrawable
{
    private const float CellW = 7f;    // 列宽（高密度：全屏约 274 列）
    private const float CellH = 16f;   // 行高
    private const float FontSize = 11f;

    /// <summary>头部亮绿（Matrix 经典色）</summary>
    private static readonly Color HeadColor = Color.FromArgb("#00FF41");

    /// <summary>每帧复用：字体与 0/1 字符（避免逐字符 new string / new Font 的高频分配）</summary>
    private static readonly Microsoft.Maui.Graphics.Font RainFont = new("Consolas", (int)FontSize);
    private static readonly string CharZero = "0";
    private static readonly string CharOne = "1";

    private readonly Random _random = new();
    private List<RainColumn> _columns = new();
    private int _colCount;
    private float _height;

    public void Advance()
    {
        foreach (var col in _columns)
        {
            col.HeadY += col.Speed;
            // 整列尾迹完全落出底部 → 重置到顶部上方（随机延迟形成错落雨滴）
            if (col.HeadY - col.TrailLength * CellH > _height)
            {
                col.HeadY = -col.TrailLength * CellH - _random.Next(0, 200);
                col.Speed = 1.6f + (float)_random.NextDouble() * 4.4f;
                col.TrailLength = 6 + _random.Next(10);
                RandomizeChars(col);
            }
        }
    }

    public void Draw(ICanvas canvas, RectF rect)
    {
        var w = rect.Width;
        var h = rect.Height;
        if (w <= 0 || h <= 0) return;
        _height = h;

        var count = Math.Max(1, (int)(w / CellW));
        if (count != _colCount)
        {
            _colCount = count;
            _columns = new List<RainColumn>(count);
            for (var i = 0; i < count; i++)
                _columns.Add(CreateColumn(i));
        }

        canvas.Font = RainFont;
        canvas.FontSize = FontSize;

        foreach (var col in _columns)
        {
            for (var i = 0; i < col.TrailLength; i++)
            {
                var y = col.HeadY + i * CellH;
                if (y < -CellH || y > h) continue;

                if (i == 0)
                {
                    // 头部：亮绿
                    canvas.FontColor = HeadColor;
                }
                else
                {
                    // 尾迹：绿色渐暗（越往下越透明）
                    var alpha = Math.Clamp(1f - (i / (float)col.TrailLength), 0.05f, 0.9f);
                    canvas.FontColor = new Color(0f, 1f, 0.25f, alpha * 0.55f);
                }

                var ch = col.Chars[i % col.Chars.Length];
                canvas.DrawString(ch == '0' ? CharZero : CharOne, col.X, y, CellW, CellH,
                    HorizontalAlignment.Center, VerticalAlignment.Center);
            }
        }
    }

    private RainColumn CreateColumn(int index)
    {
        var col = new RainColumn
        {
            X = index * CellW,
            HeadY = -_random.Next(0, 1600),
            Speed = 1.6f + (float)_random.NextDouble() * 4.4f,
            TrailLength = 6 + _random.Next(10)
        };
        RandomizeChars(col);
        return col;
    }

    private void RandomizeChars(RainColumn col)
    {
        col.Chars = new char[col.TrailLength];
        for (var i = 0; i < col.Chars.Length; i++)
            col.Chars[i] = _random.Next(2) == 0 ? '0' : '1';
    }

    private sealed class RainColumn
    {
        public float X;
        public float HeadY;
        public float Speed;
        public int TrailLength;
        public char[] Chars = Array.Empty<char>();
    }
}
