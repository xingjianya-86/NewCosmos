namespace NewCosmos.Constants;

/// <summary>
/// 证件 OCR 识别常量（模型信息、耗时估算、协议键）。
/// 服务地址配置在 config\network.ini 的 OcrServiceUrl（NetworkOptions），此处不放路径。
/// </summary>
public static class OcrConstants
{
    /// <summary>识别模型展示名（预提示弹窗展示用；服务端 /health 回报的模型与此同源）</summary>
    public const string ModelDisplayName = "Xiaomi-OCR-0";

    /// <summary>单张识别耗时估算（秒）：i3-4170 CPU 推理实测区间 5–15 秒，取中值用于预计耗时</summary>
    public const int EstimateSecondsPerImage = 10;

    /// <summary>单张识别耗时区间文案（预提示弹窗展示）</summary>
    public const string EstimateRangeText = "每张约 5–15 秒";

    /// <summary>识别请求超时（秒）：CPU 推理 5–15 秒 + 队列等待余量</summary>
    public const int RecognizeTimeoutSeconds = 60;

    /// <summary>批量识别时的进度文案模板，{0}=当前张序号 {1}=总张数</summary>
    public const string ProgressTextFormat = "正在识别 {0}/{1}…";

    /// <summary>预提示弹窗标题</summary>
    public const string PromptTitle = "读取识别（" + ModelDisplayName + "）";

    /// <summary>批量识别阈值：超过该张数预计耗时改用「约 X 分 Y 秒」文案</summary>
    public const int LongEstimateThresholdSeconds = 60;

    /// <summary>预计耗时文案：&lt;60s →「约 N 秒」；≥60s →「约 X 分 Y 秒」</summary>
    public static string FormatEstimate(int imageCount)
    {
        var seconds = Math.Max(1, imageCount) * EstimateSecondsPerImage;
        return seconds < LongEstimateThresholdSeconds
            ? $"约 {seconds} 秒"
            : $"约 {seconds / 60} 分 {seconds % 60} 秒";
    }

    /// <summary>服务端响应 JSON 键（deploy\ocr\ocr_api.py 响应契约）</summary>
    public static class ResponseKeys
    {
        public const string Ok = "ok";
        public const string Category = "category";
        public const string Mode = "mode";
        public const string Fields = "fields";
        public const string Lines = "lines";
        public const string Raw = "raw";
        public const string ElapsedMs = "elapsed_ms";
    }
}
