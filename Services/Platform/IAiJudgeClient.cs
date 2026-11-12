using NewCosmos.Models.Results;

namespace NewCosmos.Services.Platform;

/// <summary>
/// 内网 AI 重病判定客户端（服务端 deploy\ocr\ocr_api.py /ai/severity）。
/// </summary>
public interface IAiJudgeClient
{
    /// <summary>判定服务地址是否已配置（network.ini OcrServiceUrl，AI 与 OCR 同服务）</summary>
    bool IsConfigured { get; }

    /// <summary>服务健康检查（诊断用；/health 与 OCR 共用）</summary>
    Task<Result> CheckHealthAsync(CancellationToken ct = default);

    /// <summary>
    /// 调用服务端判定。name 必填（调用方先用 Icd10Catalog 把编码转成名称），code 可选作判别上下文。
    /// 失败一律 Result.Failure 显式返回（§7）：调用方降级为"无建议"，不影响表单保存。
    /// </summary>
    Task<Result<SevereDiseaseJudgment>> JudgeAsync(string? name, string? code, CancellationToken ct = default);
}
