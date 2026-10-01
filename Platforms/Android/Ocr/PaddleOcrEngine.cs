using System.Text;
using Android.Graphics;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace NewCosmos.Services.Platform.Ocr;

/// <summary>
/// 端侧 PP-OCRv3 推理引擎（det + rec，ONNX Runtime）。
/// 仅 Android 编译；模型作为 AndroidAsset 随包分发，路径 Ocr/xxx.onnx。
/// </summary>
internal sealed class PaddleOcrEngine : IDisposable
{
    private const int DetMaxSide = 960;
    private const int RecHeight = 48;
    /// <summary>识别最大宽度：身份证号等长行需足够宽，压缩会丢末位（v3 320 曾致末位误识）</summary>
    private const int RecMaxWidth = 960;

    private readonly InferenceSession _det;
    private readonly InferenceSession _rec;
    private readonly string _detInputName;
    private readonly string _recInputName;
    private readonly string[] _charTable;
    private readonly object _gate = new();

    private PaddleOcrEngine(InferenceSession det, InferenceSession rec, string[] charTable)
    {
        _det = det;
        _rec = rec;
        _detInputName = det.InputMetadata.Keys.First();
        _recInputName = rec.InputMetadata.Keys.First();
        _charTable = charTable;
    }

    /// <summary>从 APK 资源加载模型（首次调用较慢）</summary>
    public static PaddleOcrEngine Create()
    {
        var detBytes = ReadAsset("Ocr/ch_PP-OCRv4_det_infer.onnx");
        var recBytes = ReadAsset("Ocr/ch_PP-OCRv4_rec_infer.onnx");

        var det = new InferenceSession(detBytes);
        var rec = new InferenceSession(recBytes);

        var dictText = rec.ModelMetadata.CustomMetadataMap.TryGetValue("character", out var d) ? d : string.Empty;
        return new PaddleOcrEngine(det, rec, BuildCharTable(dictText));
    }

    /// <summary>对整张图片做检测+识别，返回按行自上而下的文本</summary>
    public List<string> Recognize(Bitmap source)
    {
        lock (_gate)
        {
            var width = source.Width;
            var height = source.Height;
            if (width < 8 || height < 8) return new List<string>();

            var ratio = Math.Min(1.0, (double)DetMaxSide / Math.Max(width, height));
            var resizedW = Math.Max(32, (int)Math.Round(width * ratio / 32) * 32);
            var resizedH = Math.Max(32, (int)Math.Round(height * ratio / 32) * 32);

            using var resized = Bitmap.CreateScaledBitmap(source, resizedW, resizedH, true);

            var detTensor = BuildDetTensor(resized, resizedW, resizedH);
            float[] pred;
            using (var results = _det.Run(new[] { NamedOnnxValue.CreateFromTensor(_detInputName, detTensor) }))
            {
                pred = results.First().AsTensor<float>().ToArray();
            }

            var boxes = TextLineDetector.Detect(pred, resizedW, resizedH);
            var texts = new List<string>();
            foreach (var box in boxes)
            {
                using var crop = CropByBox(source, box, ratio);
                if (crop is null) continue;
                var text = RecognizeLine(crop);
                if (!string.IsNullOrWhiteSpace(text))
                    texts.Add(text);
            }
            return texts;
        }
    }

    /// <summary>识别单行文本</summary>
    private string RecognizeLine(Bitmap line)
    {
        var srcW = line.Width;
        var srcH = line.Height;
        if (srcW < 2 || srcH < 2) return string.Empty;

        var targetW = (int)Math.Ceiling(RecHeight * (double)srcW / srcH);
        targetW = Math.Clamp(targetW, 1, RecMaxWidth);

        using var scaled = Bitmap.CreateScaledBitmap(line, targetW, RecHeight, true);
        var tensor = BuildRecTensor(scaled, targetW);

        using var results = _rec.Run(new[] { NamedOnnxValue.CreateFromTensor(_recInputName, tensor) });
        return CtcDecode(results.First().AsTensor<float>());
    }

    private static Bitmap? CropByBox(Bitmap source, Box box, double ratio)
    {
        var x0 = (int)Math.Floor(box.X0 / ratio);
        var y0 = (int)Math.Floor(box.Y0 / ratio);
        var x1 = (int)Math.Ceiling(box.X1 / ratio);
        var y1 = (int)Math.Ceiling(box.Y1 / ratio);

        x0 = Math.Clamp(x0, 0, source.Width - 1);
        y0 = Math.Clamp(y0, 0, source.Height - 1);
        x1 = Math.Clamp(x1, x0 + 1, source.Width);
        y1 = Math.Clamp(y1, y0 + 1, source.Height);
        if (x1 - x0 < 2 || y1 - y0 < 2) return null;

        return Bitmap.CreateBitmap(source, x0, y0, x1 - x0, y1 - y0);
    }

    private static DenseTensor<float> BuildDetTensor(Bitmap bmp, int w, int h)
    {
        var pixels = new int[w * h];
        bmp.GetPixels(pixels, 0, w, 0, 0, w, h);

        var tensor = new DenseTensor<float>(new[] { 1, 3, h, w });
        for (var i = 0; i < pixels.Length; i++)
        {
            // PP-OCR v4/v6 det 归一化：mean=std=0.5 → (x/255-0.5)/0.5 = x/127.5-1
            var color = pixels[i];
            var y = i / w;
            var x = i % w;
            tensor[0, 0, y, x] = ((color >> 16) & 0xFF) / 127.5f - 1f;
            tensor[0, 1, y, x] = ((color >> 8) & 0xFF) / 127.5f - 1f;
            tensor[0, 2, y, x] = (color & 0xFF) / 127.5f - 1f;
        }
        return tensor;
    }

    private static DenseTensor<float> BuildRecTensor(Bitmap bmp, int w)
    {
        var pixels = new int[w * RecHeight];
        bmp.GetPixels(pixels, 0, w, 0, 0, w, RecHeight);

        var tensor = new DenseTensor<float>(new[] { 1, 3, RecHeight, w });
        for (var i = 0; i < pixels.Length; i++)
        {
            var color = pixels[i];
            var y = i / w;
            var x = i % w;
            tensor[0, 0, y, x] = ((color >> 16) & 0xFF) / 127.5f - 1f;
            tensor[0, 1, y, x] = ((color >> 8) & 0xFF) / 127.5f - 1f;
            tensor[0, 2, y, x] = (color & 0xFF) / 127.5f - 1f;
        }
        return tensor;
    }

    /// <summary>CTC 贪心解码：跳过 blank(0)，折叠连续重复</summary>
    private string CtcDecode(Tensor<float> output)
    {
        var dims = output.Dimensions;
        if (dims.Length != 3) return string.Empty;

        var timeSteps = dims[1];
        var classes = dims[2];
        var data = output.ToArray();

        var sb = new StringBuilder();
        var prev = -1;
        for (var t = 0; t < timeSteps; t++)
        {
            var offset = t * classes;
            var best = 0;
            var bestValue = float.NegativeInfinity;
            for (var c = 0; c < classes; c++)
            {
                var v = data[offset + c];
                if (v <= bestValue) continue;
                bestValue = v;
                best = c;
            }

            if (best != 0 && best != prev && best < _charTable.Length)
                sb.Append(_charTable[best]);
            prev = best;
        }
        return sb.ToString();
    }

    /// <summary>构建字符表：索引 0=blank，其后为字典字符，末尾补空格（与 RapidOCR 一致）</summary>
    private static string[] BuildCharTable(string dictText)
    {
        var table = new List<string> { "blank" };
        if (!string.IsNullOrEmpty(dictText))
        {
            var lines = dictText.Split('\n');
            for (var i = 0; i < lines.Length; i++)
                table.Add(lines[i].TrimEnd('\r'));

            if (table.Count > 1 && table[^1].Length == 0)
                table.RemoveAt(table.Count - 1);
        }
        table.Add(" ");
        return table.ToArray();
    }

    private static byte[] ReadAsset(string path)
    {
        using var stream = global::Android.App.Application.Context.Assets!.Open(path);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    public void Dispose()
    {
        _det.Dispose();
        _rec.Dispose();
    }
}
