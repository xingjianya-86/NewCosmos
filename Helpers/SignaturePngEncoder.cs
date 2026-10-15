using System.IO.Compression;

namespace NewCosmos.Helpers;

/// <summary>
/// 手写签名栅格化 + PNG 编码器（纯托管实现，零原生依赖，Windows/Android 通用）。
///
/// 为什么不用第三方图形库：签名导出只是"把粗线段画进像素缓冲再编码 PNG"，
/// 引入 SkiaSharp 等原生包会把 Android so / Windows dll 一并带进安装包，收益不匹配风险。
/// 本类用 <see cref="ZLibStream"/>（.NET 6+ 原生 zlib）+ 手写 CRC32 生成标准 PNG，
/// 产出为灰度图（0=黑墨, 255=白底），Office COM 的 InlineShapes.AddPicture 可直接插入 docx。
/// </summary>
public static class SignaturePngEncoder
{
    /// <summary>超采样倍数：先按 N 倍绘制再降采样，得到近似抗锯齿的平滑笔迹。</summary>
    private const int Supersample = 2;

    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    /// <summary>
    /// 把签名笔迹编码为 PNG 字节。
    /// </summary>
    /// <param name="strokes">笔迹（每条 = 一串连续点，坐标为画布像素坐标）</param>
    /// <param name="canvasWidth">画布逻辑宽度（像素）</param>
    /// <param name="canvasHeight">画布逻辑高度（像素）</param>
    /// <param name="lineWidth">逻辑线宽（像素）</param>
    /// <returns>PNG 字节；无有效笔迹时返回 null</returns>
    public static byte[]? Encode(IReadOnlyList<IReadOnlyList<(float X, float Y)>> strokes,
        int canvasWidth, int canvasHeight, float lineWidth = 3f)
    {
        if (strokes == null || strokes.Count == 0 || canvasWidth <= 0 || canvasHeight <= 0)
            return null;

        var w = canvasWidth * Supersample;
        var h = canvasHeight * Supersample;
        var scale = Supersample;
        var thick = Math.Max(1f, lineWidth * scale);

        // 灰度像素缓冲：255 = 白底
        var pixels = new byte[w * h];
        Array.Fill(pixels, (byte)255);

        foreach (var stroke in strokes)
        {
            if (stroke == null || stroke.Count == 0) continue;

            if (stroke.Count == 1)
            {
                DrawDisc(pixels, w, h, stroke[0].X * scale, stroke[0].Y * scale, thick / 2f);
                continue;
            }

            for (var i = 1; i < stroke.Count; i++)
            {
                DrawSegment(pixels, w, h,
                    stroke[i - 1].X * scale, stroke[i - 1].Y * scale,
                    stroke[i].X * scale, stroke[i].Y * scale, thick);
            }
        }

        if (Supersample > 1)
            pixels = Downsample(pixels, w, h, Supersample);

        return EncodeGrayscalePng(pixels, canvasWidth, canvasHeight);
    }

    /// <summary>画粗线段：沿 Bresenham 主轴逐点画实心圆盘，天然形成圆头笔画。</summary>
    private static void DrawSegment(byte[] px, int w, int h,
        float x0, float y0, float x1, float y1, float thickness)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        var steps = (int)Math.Max(Math.Abs(dx), Math.Abs(dy));
        if (steps <= 0)
        {
            DrawDisc(px, w, h, x0, y0, thickness / 2f);
            return;
        }

        var radius = thickness / 2f;
        for (var i = 0; i <= steps; i++)
        {
            var t = i / (float)steps;
            DrawDisc(px, w, h, x0 + dx * t, y0 + dy * t, radius);
        }
    }

    /// <summary>在 (cx,cy) 画半径 radius 的实心圆（直接写 0 = 黑墨）。</summary>
    private static void DrawDisc(byte[] px, int w, int h, float cx, float cy, float radius)
    {
        var r2 = radius * radius;
        var minX = Math.Max(0, (int)(cx - radius));
        var maxX = Math.Min(w - 1, (int)(cx + radius));
        var minY = Math.Max(0, (int)(cy - radius));
        var maxY = Math.Min(h - 1, (int)(cy + radius));

        for (var y = minY; y <= maxY; y++)
        {
            var dy = y - cy;
            var row = y * w;
            for (var x = minX; x <= maxX; x++)
            {
                var dx = x - cx;
                if (dx * dx + dy * dy <= r2)
                    px[row + x] = 0;
            }
        }
    }

    /// <summary>N 倍超采样降回原尺寸：对 N×N 块取均值，得到平滑边缘。</summary>
    private static byte[] Downsample(byte[] src, int w, int h, int factor)
    {
        var ow = w / factor;
        var oh = h / factor;
        var dst = new byte[ow * oh];

        for (var oy = 0; oy < oh; oy++)
        {
            for (var ox = 0; ox < ow; ox++)
            {
                var sum = 0;
                for (var fy = 0; fy < factor; fy++)
                {
                    var srcRow = (oy * factor + fy) * w + ox * factor;
                    for (var fx = 0; fx < factor; fx++)
                        sum += src[srcRow + fx];
                }
                dst[oy * ow + ox] = (byte)(sum / (factor * factor));
            }
        }

        return dst;
    }

    /// <summary>编码 8-bit 灰度 PNG：IHDR + IDAT（zlib）+ IEND，每块带 CRC32。</summary>
    private static byte[] EncodeGrayscalePng(byte[] pixels, int width, int height)
    {
        using var ms = new MemoryStream();

        // PNG 签名
        ms.Write(PngSignature, 0, PngSignature.Length);

        // IHDR: width, height, bitdepth=8, colortype=0(灰度), compression=0, filter=0, interlace=0
        var ihdr = new byte[13];
        WriteBigEndian(ihdr, 0, (uint)width);
        WriteBigEndian(ihdr, 4, (uint)height);
        ihdr[8] = 8;
        ihdr[9] = 0;
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;
        WriteChunk(ms, "IHDR", ihdr);

        // IDAT: 每行 前置过滤字节 0 (None) + width 字节像素
        using (var idat = new MemoryStream())
        {
            using (var zlib = new ZLibStream(idat, CompressionLevel.Optimal, leaveOpen: true))
            {
                var line = new byte[width + 1];
                for (var y = 0; y < height; y++)
                {
                    line[0] = 0; // filter: None
                    Buffer.BlockCopy(pixels, y * width, line, 1, width);
                    zlib.Write(line, 0, line.Length);
                }
            }
            WriteChunk(ms, "IDAT", idat.ToArray());
        }

        WriteChunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    /// <summary>写入一个 PNG 数据块：长度 + 类型 + 数据 + CRC32(类型+数据)。</summary>
    private static void WriteChunk(Stream s, string type, byte[] data)
    {
        var typeBytes = new byte[4];
        for (var i = 0; i < 4; i++) typeBytes[i] = (byte)type[i];

        var len = new byte[4];
        WriteBigEndian(len, 0, (uint)data.Length);
        s.Write(len, 0, 4);
        s.Write(typeBytes, 0, 4);
        if (data.Length > 0) s.Write(data, 0, data.Length);

        var crc = Crc32(typeBytes, 0, typeBytes.Length);
        crc = Crc32(data, 0, data.Length, crc);
        var crcBytes = new byte[4];
        WriteBigEndian(crcBytes, 0, crc);
        s.Write(crcBytes, 0, 4);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[n] = c;
        }
        return table;
    }

    /// <summary>标准 CRC-32（PNG 规范多项式 0xEDB88320），支持流式续算。</summary>
    private static uint Crc32(byte[] data, int offset, int length, uint seed = 0xFFFFFFFFu)
    {
        var crc = seed;
        for (var i = offset; i < offset + length; i++)
            crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }
}
