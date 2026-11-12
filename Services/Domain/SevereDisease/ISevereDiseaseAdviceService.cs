using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.SevereDisease;

/// <summary>
/// 重病判定建议服务：编排"本地确定性 → 缓存 → 服务端 LLM"三级管线，
/// 只产出建议（含自动预勾选闸门判断），最终勾选权始终在经办人（§14 重病判定权威口径的执行入口）。
/// </summary>
public interface ISevereDiseaseAdviceService
{
    /// <summary>本地确定性判定（毫秒、离线、可审计）；未命中返回 null（判定链第一步，UI 可先用它秒出建议）</summary>
    SevereDiseaseJudgment? TryDeterministic(string? name, string? code);

    /// <summary>
    /// 完整判定：①本地确定性 ②缓存（best-effort，失败降级服务端）③服务端 LLM（结果写缓存）。
    /// 失败=无建议（Result.Failure 显式上抛），调用方不得因此阻塞表单保存。
    /// </summary>
    Task<Result<SevereDiseaseJudgment>> JudgeAsync(string? name, string? code, CancellationToken ct = default);

    /// <summary>
    /// 自动预勾选闸门：judgment.AutoEligible（severe 且 matched_item∈清单）且用户未否决过。
    /// 只勾不取消——AI 永不把勾选改为取消；用户取消过一次（true→false 只能来自用户）即为否决，
    /// 本次会话内不再自动勾选（人工优先，2026-11-11 补测定稿的安全策略）。
    /// </summary>
    static bool ShouldAutoCheck(SevereDiseaseJudgment? judgment, bool userRejected)
        => judgment is { AutoEligible: true } && !userRejected;

    /// <summary>
    /// 费用口径阈值（上年度居民人均可支配收入，城乡两档）。
    /// 返回 null = 配置缺失/无效（调用方降级为不判定，禁止硬编码回退值）。
    /// </summary>
    Task<Result<decimal?>> GetCostThresholdAsync(bool isRural, CancellationToken ct = default);
}
