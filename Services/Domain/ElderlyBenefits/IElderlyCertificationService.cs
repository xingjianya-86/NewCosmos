using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.ElderlyBenefits;

/// <summary>
/// 普惠高龄年度认证服务：批次、名单快照、认证结果、凭证、停发决定。
/// 腾讯收集表导入在 <see cref="Services.Import.IElderlyCertificationImportService"/>，本服务只负责认证域数据。
/// </summary>
public interface IElderlyCertificationService
{
    /// <summary>批次列表（按年度倒序、轮次倒序）。</summary>
    Task<Result<List<ElderlyCertCampaign>>> GetCampaignsAsync(CancellationToken ct = default);

    /// <summary>按ID取批次。</summary>
    Task<Result<ElderlyCertCampaign?>> GetCampaignAsync(long id, CancellationToken ct = default);

    /// <summary>新建批次（同年度同轮次唯一）。</summary>
    Task<Result<long>> CreateCampaignAsync(
        int year,
        int round,
        string name,
        DateTime periodStart,
        DateTime periodEnd,
        bool requireEvidence,
        string operatorName,
        CancellationToken ct = default);

    /// <summary>
    /// 生成/刷新应认证名单快照：在享 Confirmed 档案 + 名册 Active 未建档人员，按身份证去重。
    /// 幂等：已存在的任务不覆盖快照，只补新增人员。返回本次新增任务数。
    /// </summary>
    Task<Result<int>> GenerateRosterAsync(long campaignId, string operatorName, CancellationToken ct = default);

    /// <summary>名单分页（关键字、认证状态、凭证状态）。</summary>
    Task<Result<PagedResult<ElderlyCertTask>>> GetTasksPagedAsync(
        long campaignId,
        string? keyword,
        string? certStatus,
        string? evidenceStatus,
        int pageIndex,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>按任务ID取单条。</summary>
    Task<Result<ElderlyCertTask?>> GetTaskAsync(long id, CancellationToken ct = default);

    /// <summary>手工更正认证结果（导入异常时使用），写审计日志。</summary>
    Task<Result> UpdateTaskResultAsync(
        long taskId,
        string certStatus,
        string? verifyMethod,
        DateTime? verifiedAt,
        string? verifiedBy,
        string? note,
        DateTime? deathDate,
        string operatorName,
        CancellationToken ct = default);

    /// <summary>批次统计。</summary>
    Task<Result<ElderlyCertStats>> GetStatsAsync(long campaignId, CancellationToken ct = default);

    /// <summary>某任务的凭证列表（includeData=false 不取图片二进制）。</summary>
    Task<Result<List<ElderlyCertEvidence>>> GetEvidenceAsync(long taskId, bool includeData = false, CancellationToken ct = default);

    /// <summary>批次内未匹配图片（task_id 为空）。</summary>
    Task<Result<List<ElderlyCertEvidence>>> GetUnmatchedEvidenceAsync(long campaignId, int limit = 200, CancellationToken ct = default);

    /// <summary>把未匹配图片手工挂接到任务。</summary>
    Task<Result> AssignEvidenceAsync(long evidenceId, long taskId, int seq, string operatorName, CancellationToken ct = default);

    /// <summary>
    /// 停发预览分页：仅取停发候选认证状态（Pending/Refused/Failed/Deceased/MovedOut/NotLocated），
    /// 按村/姓名排序。keyword 匹配姓名/身份证；status 为空表示全部候选状态。
    /// </summary>
    Task<Result<PagedResult<ElderlyCertTask>>> GetStopPreviewPagedAsync(
        long campaignId,
        string? keyword,
        string? status,
        int pageIndex,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>
    /// 设置停发决定（暂缓 Deferred / 排除 Excluded / 重新进入待停发 Pending），写审计日志。
    /// 已停发（Approved）的任务不允许回退，避免绕开停发回写。
    /// </summary>
    Task<Result> SetStopDecisionAsync(
        long taskId,
        string decision,
        string? note,
        DateTime? followUpDate,
        string operatorName,
        CancellationToken ct = default);

    /// <summary>停发办理成功后回写认证任务（逐人确认流程调用）。</summary>
    Task<Result> MarkStoppedAsync(
        long taskId,
        long applicationId,
        string stopReason,
        DateTime? dueStopDate,
        DateTime? actualStopDate,
        string operatorName,
        CancellationToken ct = default);
}
