using NewCosmos.Models.Results;

namespace NewCosmos.Services.Platform;

/// <summary>
/// 身份证读取（拍照 + OCR）抽象。
/// Android 为端侧离线 OCR（PP-OCRv3 ONNX）；Windows 为桩实现（不做端侧识别）。
/// </summary>
public interface IIdentityReader
{
    /// <summary>当前平台是否支持端侧 OCR</summary>
    bool IsSupported { get; }

    /// <summary>对身份证照片执行 OCR，返回姓名/身份证号（以及原始文本行）</summary>
    Task<Result<IdCardInfo>> ReadAsync(Stream image, CancellationToken ct = default);
}
