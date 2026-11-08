namespace NewCosmos.Constants;

/// <summary>
/// 证件 OCR 识别常量（模型信息、耗时估算、协议键）。
/// 服务地址配置在 config\network.ini 的 OcrServiceUrl（NetworkOptions），此处不放路径。
/// </summary>
public static class OcrConstants
{
    /// <summary>识别模型展示名（预提示弹窗展示用；服务端 /health 回报的模型与此同源）</summary>
    public const string ModelDisplayName = "Xiaomi-OCR-0";

    /// <summary>单张识别耗时估算（秒）：身份证小图 4–12 秒；户口本等 2048px 大图
    /// prompt 处理实测可达 128 秒（2700+ tokens，i3-4170）。取大图口径保守估算。</summary>
    public const int EstimateSecondsPerImage = 30;

    /// <summary>单张识别耗时区间文案（预提示弹窗展示）：区分小图/大图，避免低估</summary>
    public const string EstimateRangeText = "小图约 5–15 秒，户口本等大图约 1–3 分钟";

    /// <summary>识别请求超时（秒）：服务端 OLLAMA_NUM_PARALLEL=1 单槽串行，
    /// 大图单张推理 ~130s + KIE 空结果 fallback 第二次调用，最坏 2×130s≈260s；
    /// 原 60s 对大图必超时（实测 12:19 请求服务端 2m20s 才完成、手机 60s 已断）。
    /// 须略大于服务端 httpx 300s 上限，让服务端先返回明确 502 而非手机干等。</summary>
    public const int RecognizeTimeoutSeconds = 360;

    /// <summary>批量识别时的进度文案模板，{0}=当前张序号 {1}=总张数</summary>
    public const string ProgressTextFormat = "正在识别 {0}/{1}…";

    /// <summary>批量识别客户端并发路数。
    /// ⚠️ 必须为 1：服务端 OLLAMA_NUM_PARALLEL=1 单槽串行，客户端并发不会减少总推理时间，
    /// 只会让请求在 Ollama 队列里堆积——2026-11-04 实测 3 路并发 + 户口本大图把队列堆到
    /// 300s 触发 ocr_api httpx 超时 → 502「识别服务处理失败」。串行时请求即到即算，不排队、不撞超时。</summary>
    public const int BatchConcurrency = 1;

    /// <summary>识别结果卡片缩略图长边（像素）</summary>
    public const int ThumbnailMaxEdge = 240;

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
