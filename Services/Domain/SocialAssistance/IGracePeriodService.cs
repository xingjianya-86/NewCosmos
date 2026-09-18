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
    /// 统计渐退期将在指定天数内到期（含已到期未处理）的户数
    /// </summary>
    Task<Result<int>> GetExpiringCountAsync(int withinDays, CancellationToken ct = default);

    /// <summary>
    /// 激活/更新渐退期记录（UPSERT 到 nc_biz_grace_periods，已存在则覆盖）
    /// </summary>
    Task<Result<bool>> ActivateAsync(long applicationId, int months,
        DateTime startDate, DateTime endDate,
        string? originalClassification, decimal? originalGuaranteeAmount,
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
    /// 获取所有当前有效的渐退期记录（供列表/报表）
    /// </summary>
    Task<Result<List<GracePeriodRecord>>> GetActiveListAsync(CancellationToken ct = default);

    /// <summary>
    /// 查询指定年月处于渐退期的申请列表（每月报表用）
    /// </summary>
    Task<Result<List<GracePeriodRecord>>> GetMonthlyAsync(int year, int month, CancellationToken ct = default);

    /// <summary>
    /// 渐退期到期未处理列表（分页查询）
    /// 条件：渐退期 is_active=TRUE 且 end_date &lt; 今天（已到期）
    /// 且关联档案 status != 'Stopped'（已停保的不再显示）
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
