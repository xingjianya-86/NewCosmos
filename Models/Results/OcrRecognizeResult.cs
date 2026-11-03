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

    /// <summary>是否至少识别出一个非空字段或文本行</summary>
    public bool HasAny =>
        Fields.Values.Any(v => !string.IsNullOrWhiteSpace(v)) || Lines.Count > 0;

    /// <summary>取字段值（缺省返回空串）</summary>
    public string GetField(string key)
        => Fields.TryGetValue(key, out var v) ? v : string.Empty;
}
