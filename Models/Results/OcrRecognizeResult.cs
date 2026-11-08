namespace NewCosmos.Models.Results;

/// <summary>
/// 证件 OCR 识别结果（服务端 deploy\ocr\ocr_api.py /ocr/recognize 响应契约）。
/// fields 键见 Constants\OcrFieldKeys.cs；lines 为原始识别文本行（供展示与兜底解析）。
/// </summary>
public sealed class OcrRecognizeResult
{
    /// <summary>照片类别（PhotoCategoryConstants 代码）</summary>
    public string Category { get; init; } = string.Empty;

    /// <summary>识别模式：kie / kie_fallback / text（服务端口径）</summary>
    public string Mode { get; init; } = string.Empty;

    /// <summary>KIE 字段键值（键见 OcrFieldKeys；文本类为空字典）</summary>
    public Dictionary<string, string> Fields { get; init; } = new();

    /// <summary>原始识别文本行（自上而下；展示与人工核对）</summary>
    public List<string> Lines { get; init; } = new();

    /// <summary>模型原始输出（KIE 为 JSON 文本；展示用）</summary>
    public string Raw { get; init; } = string.Empty;

    /// <summary>服务端推理耗时（毫秒）</summary>
    public long ElapsedMs { get; init; }

    // ── 以下为客户端附加字段（不在服务端契约内，客户端填充，批量识别溯源展示用）──

    /// <summary>来源照片 Id（0 = 扫描页单拍、无存档照片）</summary>
    public long PhotoId { get; set; }

    /// <summary>来源照片展示标签（如「张三_1234_身份证_01」），分配页卡片溯源用</summary>
    public string PhotoLabel { get; set; } = string.Empty;

    /// <summary>来源照片缩略图（WebP，长边 ≤240px；null = 无图或解码失败）</summary>
    public byte[]? Thumbnail { get; set; }

    /// <summary>识别失败/无内容原因（成功识别出字段时为空串）；分配页识别清单用</summary>
    public string FailReason { get; set; } = string.Empty;

    /// <summary>是否至少识别出一个非空字段或文本行</summary>
    public bool HasAny =>
        Fields.Values.Any(v => !string.IsNullOrWhiteSpace(v)) || Lines.Count > 0;

    /// <summary>取字段值（缺省返回空串）</summary>
    public string GetField(string key)
        => Fields.TryGetValue(key, out var v) ? v : string.Empty;
}
