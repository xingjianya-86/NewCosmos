using NewCosmos.Models.Results;

namespace NewCosmos.Services.Core;

/// <summary>
/// 申请档案状态流转的**单一权威入口**（AGENTS.md §9 规范 2：状态机单一权威）。
///
/// 职责：
/// ① 状态机校验（<c>ApplicationStateMachine.ValidateTransition</c>）；
/// ② 状态列与配套列的单点写入（status / first_approved_at / submit_* / stop_reason / stop_date / updated_*）；
/// ③ 审计留痕（写 nc_biz_application_logs，AGENTS.md §8）。
///
/// **所有** nc_biz_applications.status 的写入必须走本服务，禁止在各 Service 内
/// 直写 UPDATE ... SET status（历史上 7 处散落直写、3 处绕过状态机，产生过
/// Draft→Stopped 脏状态与手工脚本补丁）。
/// </summary>
public interface IApplicationStatusService
{
    /// <summary>Draft → Submitted（写 submit_at/submit_by）</summary>
    Task<Result> SubmitAsync(long applicationId, string submittedBy, CancellationToken ct = default);

    /// <summary>Submitted → Approved（写 first_approved_at，只写一次）</summary>
    Task<Result> ApproveAsync(long applicationId, string approvedBy, CancellationToken ct = default);

    /// <summary>Draft/Submitted → Refused</summary>
    Task<Result> RefuseAsync(long applicationId, string refusedBy, CancellationToken ct = default);

    /// <summary>
    /// 归档完结：→ Approved + current_step=6（已归档）+ first_approved_at（只写一次）。
    /// 已 Approved/Completed 的档案仅推进步骤，不改状态（避免降级）。
    /// </summary>
    Task<Result> CompleteArchiveAsync(long applicationId, string operatorName, CancellationToken ct = default);

    /// <summary>
    /// 停保：→ Stopped（写 stop_reason/stop_date）。
    /// 状态机不允许 Draft/Refused → Stopped（从未进入保障，不存在"停保"一说）；
    /// Approved/Completed → Stopped 允许；Submitted → Stopped 严格状态机不允许，
    /// 但业务上"全员死亡的在途申请"需能关闭，故 <paramref name="allowSubmittedOverride"/> 放行并记警告。
    /// </summary>
    Task<Result> StopAsync(
        long applicationId,
        string stopReason,
        DateTime stopDate,
        string operatorName,
        bool allowSubmittedOverride = false,
        CancellationToken ct = default);
}
