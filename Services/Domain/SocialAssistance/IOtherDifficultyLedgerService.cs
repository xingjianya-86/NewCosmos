using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>
/// 其他困难人员台账联动服务：非死亡性停保 → 转入其他困难人员台账；恢复保障 → 撤回停保转入行。
/// 台账表：nc_biz_other_difficulty_families / nc_biz_other_difficulty_persons（source='stop' 标记本地转入）。
/// </summary>
public interface IOtherDifficultyLedgerService
{
    /// <summary>
    /// 停保后尝试转入其他困难人员台账。
    /// 仅当满足全部条件才转入：非死亡性停保、曾获得保障（first_approved_at 非空）、
    /// 该户（户主或任一共同生活成员）没有其他仍在保/在途的非停保类档案。
    /// 已存在同户台账行时不重复写入。失败只告警，不影响停保本身。
    /// </summary>
    Task<Result> TransferOnStopAsync(long applicationId, string? stopReason, DateTime? stopDate, CancellationToken ct = default);

    /// <summary>恢复保障后撤回由该档案停保转入的台账行（source='stop'），导入行不动。</summary>
    Task<Result> RemoveOnResumeAsync(long applicationId, CancellationToken ct = default);

    /// <summary>
    /// 档案审批通过/恢复在保 → 若其保障成员已在其他困难人员台账中，写“转出/停止”记录
    /// （nc_biz_other_difficulty_stops，来源 source='approval'）。台账原行保留不删。
    /// 幂等：同人同停止日不重复写。失败只告警，不影响审批本身。
    /// </summary>
    Task<Result> RecordTransferOutAsync(long applicationId, CancellationToken ct = default);

    /// <summary>档案停保 → 撤回由该档案审批自动写入的其他困难人员转出记录（source='approval'）。</summary>
    Task<Result> RemoveTransferOutAsync(long applicationId, CancellationToken ct = default);
}
