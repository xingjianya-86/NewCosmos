using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;

using GracePeriodExpiringItem = NewCosmos.Models.Entities.GracePeriodExpiringItem;

namespace NewCosmos.Services.Domain.SocialAssistance;

using ApplicationEntity = NewCosmos.Models.Entities.Application;

/// <summary>
/// 渐退期管理服务接    /// </summary>
public interface IGracePeriodService
{
    /// <summary>
    /// 检查渐退期资    /// </summary>
    GracePeriodCheckResult CheckEligibility(
        string oldClassification, 
        string newClassification,
        decimal perCapitaIncome,
        decimal standard);

    /// <summary>
    /// 设置渐退    /// </summary>
    GracePeriodInfo SetGracePeriod(int months, decimal? originalGuaranteeAmount = null);

    /// <summary>
    /// 按给定月数计算渐退期起止日（起算 = 业务所属月份的次月1日），不写库；
    /// 供表单保存时渐退期状态已在内存但起止日缺失的兜底取数，禁止调用点自算日期。
    /// </summary>
    GracePeriodInfo CalculatePeriod(int months);

    /// <summary>
    /// 清除渐退    /// </summary>
    void ClearGracePeriod(ApplicationEntity application);

    /// <summary>
    /// 应用渐退期到申请
    /// </summary>
    void ApplyGracePeriod(ApplicationEntity application, GracePeriodInfo info);

    /// <summary>
    /// 检查是否仍在渐退期内
    /// </summary>
    bool IsInGracePeriod(ApplicationEntity application);

    /// <summary>
    /// 统计进行中 + 已到期的渐退期户数（渐退期管理页/横幅口径）
    /// </summary>
    Task<Result<int>> GetExpiringCountAsync(int withinDays, CancellationToken ct = default);

    /// <summary>
    /// 首页预警口径：N 天内到期 + 已到期的渐退期户数（end_date &lt;= 今天 + withinDays）
    /// </summary>
    Task<Result<int>> GetWarningCountAsync(int withinDays, CancellationToken ct = default);

    /// <summary>
    /// 激活/更新渐退期记录（UPSERT 到 nc_biz_grace_periods，已存在则覆盖）
    /// graceGrantAmount：渐退期内实际应发月保障金（原额超户口类型上限时已封顶）
    /// </summary>
    Task<Result<bool>> ActivateAsync(long applicationId, int months,
        DateTime startDate, DateTime endDate,
        string? originalClassification, decimal? originalGuaranteeAmount,
        decimal? graceGrantAmount = null,
        CancellationToken ct = default);

    /// <summary>
    /// 清除渐退期记录（置 is_active=false）
    /// </summary>
    Task<Result<bool>> ClearAsync(long applicationId, CancellationToken ct = default);

    /// <summary>
    /// 在效渐退期随档案转移（停旧建新：旧档停保、新档承载同一家庭）：
    /// 同一行记录改挂目标档案，起止日/月数/原分类/原额/应发额一律不动——复核不得修改渐退期。
    /// 源档无在效记录 → no-op；目标档已有在效记录 → 跳过并告警（防重复）。
    /// </summary>
    Task<Result<bool>> TransferAsync(long sourceApplicationId, long targetApplicationId, CancellationToken ct = default);

    /// <summary>
    /// 获取指定申请当前有效的渐退期记录
    /// </summary>
    Task<Result<GracePeriodRecord?>> GetActiveAsync(long applicationId, CancellationToken ct = default);

    /// <summary>
    /// 批量取多份申请的当前有效渐退期到期日（application_id = ANY 一次查询防 N+1）
    /// </summary>
    Task<Result<IReadOnlyDictionary<long, DateTime>>> GetActiveEndDateMapByApplicationIdsAsync(
        IReadOnlyCollection<long> applicationIds, CancellationToken ct = default);

    /// <summary>
    /// 获取指定申请最近一条渐退期记录（不过滤 is_active，退出后补打审批表仍可取数）
    /// </summary>
    Task<Result<GracePeriodRecord?>> GetLatestAsync(long applicationId, CancellationToken ct = default);

    /// <summary>
    /// 获取所有当前有效的渐退期记录（供列表/报表）
    /// </summary>
    Task<Result<List<GracePeriodRecord>>> GetActiveListAsync(CancellationToken ct = default);

    /// <summary>
    /// 查询指定年月处于渐退期的申请列表（每月报表用）
    /// </summary>
    Task<Result<List<GracePeriodRecord>>> GetMonthlyAsync(int year, int month, CancellationToken ct = default);

    /// <summary>
    /// 渐退期列表（进行中 + 已到期，分页查询）
    /// 条件：渐退期 is_active=TRUE 且 end_date 非空
    /// 关联档案 status ∈ Approved/Completed/Draft（含户主死亡新建草稿）
    /// 支持按姓名/身份证关键词模糊搜索
    /// </summary>
    Task<Result<PagedResult<GracePeriodExpiringItem>>> GetExpiringPagedAsync(
        string? keyword,
        int pageIndex,
        int pageSize,
        CancellationToken ct = default);
}

/// <summary>
/// 复核/变更流程对渐退期的处置结果（调用方据此决定 triggered_grace_period 与内存状态写入，
/// 只有 Activated 才允许写库/写内存）
/// </summary>
public enum GraceReconcileOutcome
{
    /// <summary>已有在效记录原样保留，或本次判定不满足渐退条件：不新建、不修改（复核不得修改渐退期）</summary>
    Preserved,

    /// <summary>无在效记录且本次判定命中渐退 → 首次进入，按业务月口径创建在效记录</summary>
    Activated,

    /// <summary>分类离开低收入（或强制结清）→ 结清既有渐退期</summary>
    Cleared
}

/// <summary>
/// 渐退期信    /// </summary>
public class GracePeriodInfo
{
    /// <summary>
    /// 是否处于渐退    /// </summary>
    public bool IsInGracePeriod { get; set; }

    /// <summary>
    /// 渐退期月    /// </summary>
    public int GracePeriodMonths { get; set; }

    /// <summary>
    /// 渐退期开始日    /// </summary>
    public DateTime GracePeriodStartDate { get; set; }

    /// <summary>
    /// 渐退期结束日    /// </summary>
    public DateTime GracePeriodEndDate { get; set; }

    /// <summary>
    /// 渐退前原分类结果
    /// </summary>
    public string OriginalClassificationResult { get; set; } = string.Empty;

    /// <summary>
    /// 渐退前原保障金额
    /// </summary>
    public decimal OriginalGuaranteeAmount { get; set; }
}
