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
