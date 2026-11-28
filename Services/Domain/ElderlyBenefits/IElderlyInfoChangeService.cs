using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.ElderlyBenefits;

/// <summary>
/// 高龄年度认证·信息变更单服务：先落变更单，审批通过后再写申请主表。
/// 字段白名单与风险级别见 <see cref="Constants.ElderlyInfoChangeConstants"/>。
/// </summary>
public interface IElderlyInfoChangeService
{
    /// <summary>
    /// 分页查询变更单（默认 Pending/Approved/Rejected，不含被覆盖的 Cancelled），含字段明细。
    /// </summary>
    Task<Result<PagedResult<ElderlyInfoChangeRecord>>> GetPagedAsync(
        string? keyword,
        string? status,
        int pageIndex,
        int pageSize,
        CancellationToken ct = default);

    /// <summary>按认证任务/身份证查询变更单（含字段明细）。</summary>
    Task<Result<List<ElderlyInfoChangeRecord>>> GetByTaskAsync(long taskId, CancellationToken ct = default);

    /// <summary>
    /// 创建变更单：任务必须存在且未停发/去世，关联申请必须 Confirmed；
    /// 字段必须在白名单内；旧值可空（空则取申请主表当前值），新值不能为空。
    /// </summary>
    Task<Result<long>> CreateAsync(
        long taskId,
        string? reason,
        string? sourceType,
        string? sourceFile,
        int? sourceRow,
        string? effectiveMonth,
        IReadOnlyList<ElderlyInfoChangeDetailInput> details,
        string operatorName,
        CancellationToken ct = default);

    /// <summary>
    /// 审批通过：事务内锁申请行、校验当前值仍等于旧值、更新申请主表并回写变更单。
    /// </summary>
    Task<Result> ApproveAsync(
        long changeId,
        string? reviewOpinion,
        string? effectiveMonth,
        string operatorName,
        CancellationToken ct = default);

    /// <summary>驳回变更单（不写申请主表）。</summary>
    Task<Result> RejectAsync(
        long changeId,
        string? reviewOpinion,
        string operatorName,
        CancellationToken ct = default);

    /// <summary>读取任务关联申请主表的当前允许字段值（表单展示“当前值”用）。</summary>
    Task<Result<ElderlyInfoChangeApplicationValues?>> GetApplicationValuesAsync(
        long taskId,
        CancellationToken ct = default);
}
