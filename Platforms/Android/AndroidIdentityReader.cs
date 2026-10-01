using Android.Graphics;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Platform.Ocr;
using ExifInterface = Android.Media.ExifInterface;

namespace NewCosmos.Services.Platform;

/// <summary>
/// Android 端侧身份证 OCR 读取：照片 → PP-OCRv3 推理 → 解析「姓名 + 身份证号」。
/// 全程离线，图片不出设备。
/// </summary>
public sealed class AndroidIdentityReader : IIdentityReader
{
    private readonly ILoggerService _logger;
    private readonly object _initGate = new();
    private PaddleOcrEngine? _engine;

    public AndroidIdentityReader(ILoggerService logger)
    {
        _logger = logger;
    }

    public bool IsSupported => true;

    public Task<Result<IdCardInfo>> ReadAsync(Stream image, CancellationToken ct = default)
        => Task.Run(() => ReadCore(image, ct), ct);

    private Result<IdCardInfo> ReadCore(Stream image, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();

            var engine = EnsureEngine();
            var bytes = ReadAllBytes(image);

            var orientation = ReadOrientation(bytes);
            using var decoded = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
            if (decoded is null)
                return Result.Failure<IdCardInfo>("OCR_DECODE_FAILED", "照片解码失败，请重新拍摄");

            Bitmap? rotated = null;
            var work = decoded;
            if (orientation is 3 or 6 or 8)
            {
                rotated = Rotate(decoded, orientation);
                work = rotated;
            }

            List<string> lines;
            try
            {
                lines = engine.Recognize(work);
            }
            finally
            {
                rotated?.Dispose();
            }

            var info = IdCardOcrParser.Parse(lines);
            if (!info.HasAny)
                return Result.Failure<IdCardInfo>("OCR_NO_FIELD", "未识别出姓名或身份证号，请正对身份证、保证光线充足后重拍");

            return Result.Success(info);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "身份证 OCR 识别失败");
            return Result.FromException<IdCardInfo>(ex);
        }
    }

    private PaddleOcrEngine EnsureEngine()
    {
        if (_engine is not null) return _engine;
        lock (_initGate)
        {
            _engine ??= PaddleOcrEngine.Create();
            return _engine;
        }
    }

    /// <summary>读取 EXIF 方向（1=正常，3=180°，6=90°，8=270°）</summary>
    private static int ReadOrientation(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            using var exif = new ExifInterface(stream);
            return exif.GetAttributeInt("Orientation", 1);
        }
        catch
        {
            return 1;
        }
    }

    private static Bitmap Rotate(Bitmap source, int orientation)
    {
        using var matrix = new Matrix();
        matrix.PostRotate(orientation switch
        {
            3 => 180f,
            6 => 90f,
            8 => 270f,
            _ => 0f
        });
        return Bitmap.CreateBitmap(source, 0, 0, source.Width, source.Height, matrix, true);
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        if (stream is MemoryStream memory) return memory.ToArray();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
