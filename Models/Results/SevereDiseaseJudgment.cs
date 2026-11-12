namespace NewCosmos.Models.Results;

/// <summary>
/// 重病判定结果（服务端 /ai/severity 契约与客户端确定性层共用）。
/// Verdict 取值与服务端 JSON 一致：severe / not_severe / unknown（勿另造语义）。
/// </summary>
public sealed record SevereDiseaseJudgment
{
    public const string VerdictSevere = "severe";
    public const string VerdictNotSevere = "not_severe";
    public const string VerdictUnknown = "unknown";
    public const string SourceDeterministic = "deterministic";
    public const string SourceLlm = "llm";

    public string Verdict { get; init; } = VerdictUnknown;

    /// <summary>命中的重病清单项；必须∈清单（服务端+客户端双侧校验，胡扯项置空）</summary>
    public string MatchedItem { get; init; } = string.Empty;

    public double Confidence { get; init; }

    /// <summary>判定依据（展示给经办人的建议条文案）</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>deterministic=本地清单毫秒级 / llm=服务端模型兜底</summary>
    public string Source { get; init; } = SourceLlm;

    /// <summary>清单文件 sha256 前16位；客户端缓存按此失效（政策改版自动重判）</summary>
    public string PolicyVersion { get; init; } = string.Empty;

    /// <summary>服务端模型名（llm 源才有值）</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>
    /// 允许自动预勾选：severe 且 matched_item 非空（即∈清单）。
    /// 这是自动勾选的唯一闸门；配合"只勾不取消、人工优先"使用（2026-11-11 补测防线）。
    /// </summary>
    public bool AutoEligible => Verdict == VerdictSevere && MatchedItem.Length > 0;
}
