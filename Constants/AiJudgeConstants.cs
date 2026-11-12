namespace NewCosmos.Constants;

using NewCosmos.Models.Results;

/// <summary>
/// AI 重病判定常量（超时、路径、协议键）。
/// 服务与 OCR 同端点同地址：config\network.ini 的 OcrServiceUrl（deploy\ocr 一个服务两条路由），
/// 此处不放路径。
/// </summary>
public static class AiJudgeConstants
{
    /// <summary>判定请求超时（秒）：须大于服务端 AI_REQUEST_TIMEOUT_SECONDS=90，
    /// 让服务端先返回明确 502 而非客户端干等（同 OcrConstants.RecognizeTimeoutSeconds 的口径）。
    /// 实测热态 LLM 判定 4–6s；OCR 用过后抢模型最坏换入 ~38s（重载 7.9s + prompt 全量 25.8s）。</summary>
    public const int JudgeTimeoutSeconds = 100;

    /// <summary>服务端判定路由（POST，JSON {name, code}）</summary>
    public const string SeverityPath = "/ai/severity";

    /// <summary>服务端响应 JSON 键（deploy\ocr\ocr_api.py /ai/severity 契约，2026-11-11 P0 定稿）</summary>
    public static class ResponseKeys
    {
        public const string Ok = "ok";
        public const string Verdict = "verdict";
        public const string MatchedItem = "matched_item";
        public const string Confidence = "confidence";
        public const string Reason = "reason";
        public const string Source = "source";
        public const string PolicyVersion = "policy_version";
        public const string Model = "model";
        public const string ElapsedMs = "elapsed_ms";
    }

    /// <summary>
    /// 建议条文案唯一拼装点（verdict=severe 才展示，见 ApplicationFormViewModel.ApplyApplicantJudgment）。
    /// autoChecked=true 表示已通过 ShouldAutoCheck 闸门自动勾选（告知可人工取消）；
    /// channel=清单直配（确定性，毫秒/离线）或模型判定（LLM 兜底）。
    /// </summary>
    public static string FormatSuggestion(SevereDiseaseJudgment judgment, bool autoChecked)
    {
        var matched = string.IsNullOrWhiteSpace(judgment.MatchedItem) ? "未归入清单项" : judgment.MatchedItem;
        var channel = judgment.Source == SevereDiseaseJudgment.SourceDeterministic ? "清单直配" : "模型判定";
        return autoChecked
            ? $"AI重病判定（{channel}）：命中【{matched}】，已自动勾选——{judgment.Reason}，可人工取消"
            : $"AI重病判定（{channel}）：建议重病·命中【{matched}】——{judgment.Reason}，请人工确认";
    }
}
