using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>
/// 快速核验服务：按身份证（+可选姓名）并行聚合查询系统内已有台账，只读展示。
/// 数据源与口径见 QuickVerifyBundle 各分组注释；土地承包台账无身份证列，按姓名匹配并标注同名风险。
/// </summary>
public interface IQuickVerifyService
{
    /// <summary>
    /// 执行快速核验。name 为空时先从人员库反查姓名（供土地台账匹配）。
    /// 全部分组失败 → 整体 Failure（DB 级故障）；部分失败 → Success，失败组带 FailureReason。
    /// </summary>
    Task<Result<QuickVerifyBundle>> VerifyAsync(string idCard, string? name, CancellationToken ct = default);
}
