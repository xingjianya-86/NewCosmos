using NewCosmos.Models.Results;

namespace NewCosmos.Services.Core;

/// <summary>
/// AI 判定缓存（nc_sys_ai_judgments）：按规范化输入键存取 LLM 判定结果，
/// policy_version 有效性锚点由实现内部取随包清单版本（调用方无需关心）。
/// 缓存是优化通道：读/写失败均不阻断判定（调用方降级走服务端），但 DB 故障须显式 Result.Failure 上抛（§7）。
/// </summary>
public interface IAiJudgmentCacheService
{
    /// <summary>
    /// 读缓存。命中=Success(判定)；未命中或清单版本已变=Success(null)（缓存未命中不是错误）；
    /// DB 故障=Failure（调用方降级走服务端并记 Warn）。
    /// </summary>
    Task<Result<SevereDiseaseJudgment?>> GetAsync(string inputKey, CancellationToken ct = default);

    /// <summary>写缓存（upsert，同键覆盖）；DB 故障=Failure（调用方仅记 Warn，不失败判定）</summary>
    Task<Result> SetAsync(string inputKey, SevereDiseaseJudgment judgment, CancellationToken ct = default);
}
