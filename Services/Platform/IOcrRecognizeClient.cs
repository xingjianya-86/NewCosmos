using NewCosmos.Models.Results;

namespace NewCosmos.Services.Platform;

/// <summary>
/// 证件 OCR 识别客户端（内网 Xiaomi-OCR-0 服务）。
/// 图片 + 照片类别 → 服务端按类别 KIE/文本识别 → 字段与文本行。
/// ⚠️ PII 边界：图片仅发内网识别服务（LAN/ZeroTier），不得发外网端点。
/// </summary>
public interface IOcrRecognizeClient
{
    /// <summary>识别服务地址是否已配置（network.ini OcrServiceUrl）</summary>
    bool IsConfigured { get; }

    /// <summary>识别服务健康状态（/health）；不可达返回失败</summary>
    Task<Result> CheckHealthAsync(CancellationToken ct = default);

    /// <summary>
    /// 对一张照片按类别识别。类别须为 PhotoCategoryConstants 代码；
    /// PERSON_PHOTO 等无需识别的类别返回失败 OCR_NO_FIELD。
    /// </summary>
    Task<Result<OcrRecognizeResult>> RecognizeAsync(Stream image, string categoryCode, CancellationToken ct = default);
}
