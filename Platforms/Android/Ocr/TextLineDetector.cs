namespace NewCosmos.Services.Platform.Ocr;

/// <summary>检测框（像素坐标，左上-右下）</summary>
internal readonly record struct Box(int X0, int Y0, int X1, int Y1);

/// <summary>
/// 文本行检测后处理（DB 概率图的简化实现）：
/// 阈值二值化 → 膨胀 → 连通域 → 行合并 → 外扩。
/// 用于从 PP-OCR det 输出中提取候选文本行框，供识别模型裁剪。
/// </summary>
internal static class TextLineDetector
{
    private const float Thresh = 0.3f;
    private const int MinComponentPixels = 6;
    private const int MinComponentHeight = 4;

    /// <summary>从 det 概率图提取文本行框（坐标系与输入同）</summary>
    public static List<Box> Detect(float[] pred, int w, int h)
    {
        var bin = new bool[w * h];
        for (var i = 0; i < bin.Length; i++)
            bin[i] = pred[i] > Thresh;

        Dilate(bin, w, h, Math.Max(1, h / 300));

        var components = ConnectedComponents(bin, w, h);
        return MergeIntoLines(components);
    }

    /// <summary>3x3 膨胀，迭代 iterations 次（分离水平/垂直两遍）</summary>
    private static void Dilate(bool[] bin, int w, int h, int iterations)
    {
        var tmp = new bool[bin.Length];
        for (var it = 0; it < iterations; it++)
        {
            // 水平
            Array.Copy(bin, tmp, bin.Length);
            for (var y = 0; y < h; y++)
            {
                var row = y * w;
                for (var x = 0; x < w; x++)
                {
                    if (bin[row + x]) continue;
                    if ((x > 0 && bin[row + x - 1]) || (x < w - 1 && bin[row + x + 1]))
                        tmp[row + x] = true;
                }
            }
            // 垂直
            Array.Copy(tmp, bin, bin.Length);
            for (var y = 0; y < h; y++)
            {
                var row = y * w;
                for (var x = 0; x < w; x++)
                {
                    if (tmp[row + x]) continue;
                    if ((y > 0 && tmp[row - w + x]) || (y < h - 1 && tmp[row + w + x]))
                        bin[row + x] = true;
                }
            }
        }
    }

    private static List<Box> ConnectedComponents(bool[] bin, int w, int h)
    {
        var boxes = new List<Box>();
        var queue = new Queue<int>();

        for (var i = 0; i < bin.Length; i++)
        {
            if (!bin[i]) continue;
            bin[i] = false;
            queue.Enqueue(i);

            int minX = w, minY = h, maxX = -1, maxY = -1, count = 0;
            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                var y = p / w;
                var x = p % w;
                count++;
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;

                for (var dy = -1; dy <= 1; dy++)
                {
                    var ny = y + dy;
                    if (ny < 0 || ny >= h) continue;
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        var nx = x + dx;
                        if (nx < 0 || nx >= w) continue;
                        var np = ny * w + nx;
                        if (!bin[np]) continue;
                        bin[np] = false;
                        queue.Enqueue(np);
                    }
                }
            }

            if (count >= MinComponentPixels && maxY - minY >= MinComponentHeight)
                boxes.Add(new Box(minX, minY, maxX, maxY));
        }
        return boxes;
    }

    /// <summary>将字符级连通域按垂直重叠 + 水平邻近合并为整行</summary>
    private static List<Box> MergeIntoLines(List<Box> boxes)
    {
        var ordered = boxes.OrderBy(b => b.Y0).ThenBy(b => b.X0).ToList();
        var used = new bool[ordered.Count];
        var lines = new List<Box>();

        for (var i = 0; i < ordered.Count; i++)
        {
            if (used[i]) continue;
            var current = ordered[i];
            used[i] = true;

            var changed = true;
            while (changed)
            {
                changed = false;
                for (var j = 0; j < ordered.Count; j++)
                {
                    if (used[j]) continue;
                    if (!ShouldMerge(current, ordered[j])) continue;
                    current = Union(current, ordered[j]);
                    used[j] = true;
                    changed = true;
                }
            }
            lines.Add(Expand(current));
        }

        return lines.OrderBy(b => b.Y0).ThenBy(b => b.X0).ToList();
    }

    private static bool ShouldMerge(Box a, Box b)
    {
        var overlap = Math.Min(a.Y1, b.Y1) - Math.Max(a.Y0, b.Y0);
        if (overlap <= 0) return false;

        var minHeight = Math.Min(a.Y1 - a.Y0, b.Y1 - b.Y0);
        if (minHeight <= 0 || overlap / (double)minHeight < 0.4) return false;

        var gap = b.X0 > a.X1 ? b.X0 - a.X1 : (a.X0 > b.X1 ? a.X0 - b.X1 : 0);
        var height = Math.Max(a.Y1 - a.Y0, b.Y1 - b.Y0);
        return gap <= height * 2;
    }

    private static Box Union(Box a, Box b)
        => new(Math.Min(a.X0, b.X0), Math.Min(a.Y0, b.Y0), Math.Max(a.X1, b.X1), Math.Max(a.Y1, b.Y1));

    /// <summary>近似 unclip：水平外扩 0.4 倍行高、垂直 0.2 倍行高</summary>
    private static Box Expand(Box b)
    {
        var height = b.Y1 - b.Y0;
        var dx = Math.Max(2, (int)(height * 0.4));
        var dy = Math.Max(1, (int)(height * 0.2));
        return new Box(b.X0 - dx, b.Y0 - dy, b.X1 + dx, b.Y1 + dy);
    }
}
