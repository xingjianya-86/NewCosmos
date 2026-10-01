using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.SocialAssistance;

using ApplicationEntity = NewCosmos.Models.Entities.Application;
using GracePeriodExpiringItem = NewCosmos.Models.Entities.GracePeriodExpiringItem;

/// <summary>
/// 渐退期管理服务实现/// </summary>
public class GracePeriodService : BaseService, IGracePeriodService
{
    protected override string ServiceName => "GracePeriodService";
    private readonly IDatabaseService _db;

    public GracePeriodService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
    }

    /// <summary>
    /// 检查渐退期资格    /// </summary>
    public GracePeriodCheckResult CheckEligibility(
        string oldClassification, 
        string newClassification,
        decimal perCapitaIncome,
        decimal standard)
    {
        var result = new GracePeriodCheckResult();

        // 检查是否为低保→低收入
        var wasSubsistence = ClassificationConstants.IsCodeSubsistence(oldClassification);
        var nowLowIncome = ClassificationConstants.IsCodeLowIncome(newClassification) 
                          && !ClassificationConstants.IsCodeSubsistence(newClassification);

        if (!wasSubsistence || !nowLowIncome)
        {
            LogInfo($"渐退期检查: 不符合条件 ({oldClassification})");
            return result;
        }

        // 检查收入区间
        var lowIncomeThreshold = standard * ClassificationConstants.LowIncomeMultiplier;
        var incomeInRange = perCapitaIncome >= standard && perCapitaIncome < lowIncomeThreshold;

        if (incomeInRange)
        {
            result.IsEligible = true;
            result.OriginalClassification = oldClassification;
            result.Months = GracePeriodConstants.DEFAULT_MONTHS;

            // 计算渐退期日期
            var today = DateTime.Today;
            var nextMonth = new DateTime(today.Year, today.Month, 1).AddMonths(1);
            result.StartDate = nextMonth;
            result.EndDate = nextMonth.AddMonths(result.Months).AddDays(-1);

            LogInfo($"渐退期检查: 符合条件");
        }
        else
        {
            LogInfo($"渐退期检查: 收入不在区间 (人均={perCapitaIncome:F2})");
        }

        return result;
    }

    /// <summary>
    /// 设置渐退期    /// </summary>
    public GracePeriodInfo SetGracePeriod(int months, decimal? originalGuaranteeAmount = null)
    {
        // 验证月数范围
        months = Math.Clamp(months, GracePeriodConstants.MIN_MONTHS, GracePeriodConstants.MAX_MONTHS);

        var today = DateTime.Today;
        var nextMonth = new DateTime(today.Year, today.Month, 1).AddMonths(1);

        var info = new GracePeriodInfo
        {
            IsInGracePeriod = true,
            GracePeriodMonths = months,
            GracePeriodStartDate = nextMonth,
            GracePeriodEndDate = nextMonth.AddMonths(months).AddDays(-1),
            OriginalGuaranteeAmount = originalGuaranteeAmount ?? 0
        };

        LogInfo($"设置渐退期: 月数={months}");

        return info;
    }

    /// <summary>
    /// 清除渐退期    /// </summary>
    public void ClearGracePeriod(ApplicationEntity application)
    {
        application.IsInGracePeriod = false;
        application.GracePeriodMonths = null;
        application.GracePeriodStartDate = null;
        application.GracePeriodEndDate = null;
        application.OriginalClassificationResult = null;
        application.OriginalGuaranteeAmount = null;

        LogInfo($"执行操作");
    }

    /// <summary>
    /// 应用渐退期到申请
    /// </summary>
    public void ApplyGracePeriod(ApplicationEntity application, GracePeriodInfo info)
    {
        application.IsInGracePeriod = info.IsInGracePeriod;
        application.GracePeriodMonths = info.GracePeriodMonths;
        application.GracePeriodStartDate = info.GracePeriodStartDate;
        application.GracePeriodEndDate = info.GracePeriodEndDate;
        application.OriginalClassificationResult = info.OriginalClassificationResult;
        application.OriginalGuaranteeAmount = info.OriginalGuaranteeAmount;

        LogInfo($"应用渐退期: ApplicationId={application.Id}");
    }

    /// <summary>
    /// 检查是否仍在渐退期内
    /// </summary>
    public bool IsInGracePeriod(ApplicationEntity application)
    {
        if (!application.IsInGracePeriod)
            return false;

        if (application.GracePeriodEndDate == null)
            return false;

        var isInPeriod = DateTime.Today <= application.GracePeriodEndDate.Value;

        if (!isInPeriod)
        {
            LogInfo($"渐退期已过期: ApplicationId={application.Id}");
        }

        return isInPeriod;
    }

    /// <summary>
    /// 统计进行中 + 已到期的渐退期户数（渐退期管理页/横幅口径）
    /// </summary>
    public async Task<Result<int>> GetExpiringCountAsync(int withinDays, CancellationToken ct = default)
    {
        try
        {
            // 进行中 + 已到期均计入（渐退期管理页口径）；Draft 新档（户主死亡待认定）一并纳入
            _ = withinDays;
            var sql = $@"SELECT COUNT(*) FROM nc_biz_grace_periods g
                        JOIN nc_biz_applications a ON g.application_id = a.id AND a.deleted_at IS NULL
                        WHERE g.is_active = TRUE
                          AND g.deleted_at IS NULL
                          AND g.end_date IS NOT NULL
                          AND a.status IN ('{ApplicationStatusCodes.APPROVED}', '{ApplicationStatusCodes.COMPLETED}', '{ApplicationStatusCodes.DRAFT}')";

            var result = await _db.ExecuteScalarAsync(sql, ct);
            if (result.IsFailure)
                return Result.Failure<int>(result.ErrorCode!, result.Message!);

            var count = Convert.ToInt32(result.Value);
            LogInfo($"渐退期进行中+已到期户数: {count}");
            return Result.Success(count);
        }
        catch (Exception ex)
        {
            LogException(ex, "统计渐退期到期户数失败");
            return Result.FromException<int>(ex);
        }
    }

    /// <summary>
    /// 首页预警口径：N 天内到期 + 已到期的渐退期户数（end_date &lt;= 今天 + withinDays，
    /// 已到期因 end_date &lt; 今天天然被同一条件覆盖）。
    /// 与 GetExpiringCountAsync（进行中+已到期全量，渐退期管理页/胶囊口径）相互独立。
    /// </summary>
    public async Task<Result<int>> GetWarningCountAsync(int withinDays, CancellationToken ct = default)
    {
        try
        {
            var sql = $@"SELECT COUNT(*) FROM nc_biz_grace_periods g
                        JOIN nc_biz_applications a ON g.application_id = a.id AND a.deleted_at IS NULL
                        WHERE g.is_active = TRUE
                          AND g.deleted_at IS NULL
                          AND g.end_date IS NOT NULL
                          AND a.status IN ('{ApplicationStatusCodes.APPROVED}', '{ApplicationStatusCodes.COMPLETED}', '{ApplicationStatusCodes.DRAFT}')
                          AND g.end_date <= CURRENT_DATE + $1";

            var result = await _db.ExecuteScalarAsync(sql, ct, withinDays);
            if (result.IsFailure)
                return Result.Failure<int>(result.ErrorCode!, result.Message!);

            var count = Convert.ToInt32(result.Value);
            LogInfo($"渐退期到期预警户数({withinDays}天内+已到期): {count}");
            return Result.Success(count);
        }
        catch (Exception ex)
        {
            LogException(ex, "统计渐退期到期预警户数失败");
            return Result.FromException<int>(ex);
        }
    }

    /// <summary>
    /// 渐退期到期未处理列表（分页查询）
    /// </summary>
    public async Task<Result<PagedResult<GracePeriodExpiringItem>>> GetExpiringPagedAsync(
        string? keyword, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        try
        {
            var today = DateTime.Today;
            var keywordTrimmed = keyword?.Trim() ?? string.Empty;

            var countSql = $@"SELECT COUNT(*)
                FROM nc_biz_grace_periods gp
                JOIN nc_biz_applications a ON gp.application_id = a.id AND a.deleted_at IS NULL
                WHERE gp.is_active = TRUE
                  AND gp.deleted_at IS NULL
                  AND gp.end_date IS NOT NULL
                  AND a.status IN ('{ApplicationStatusCodes.APPROVED}', '{ApplicationStatusCodes.COMPLETED}', '{ApplicationStatusCodes.DRAFT}')
                  AND ($1 = '' OR (a.applicant_name ILIKE '%' || $1 || '%' OR a.applicant_id_card ILIKE '%' || $1 || '%'))";

            var countResult = await _db.ExecuteScalarAsync(countSql, ct, keywordTrimmed);
            if (countResult.IsFailure)
                return Result.Failure<PagedResult<GracePeriodExpiringItem>>(countResult.ErrorCode!, countResult.Message!);

            var totalCount = Convert.ToInt32(countResult.Value);
            if (totalCount == 0)
                return Result.Success(new PagedResult<GracePeriodExpiringItem>
                {
                    Items = new List<GracePeriodExpiringItem>(),
                    TotalCount = 0
                });

            var offset = (pageIndex - 1) * pageSize;
            var dataSql = $@"SELECT a.id AS application_id, a.application_no, a.applicant_name, a.applicant_id_card,
                                   a.classification_result,
                                   gp.grace_period_months,
                                   gp.start_date AS grace_period_start_date,
                                   gp.end_date AS grace_period_end_date,
                                   gp.original_classification, gp.original_guarantee_amount,
                                   gp.grace_grant_amount,
                                   (gp.end_date >= $1::date) AS is_active_period,
                                   ABS($1::date - gp.end_date) AS duration_days,
                                   ($1::date - gp.end_date) AS expired_days
                FROM nc_biz_grace_periods gp
                JOIN nc_biz_applications a ON gp.application_id = a.id AND a.deleted_at IS NULL
                WHERE gp.is_active = TRUE
                  AND gp.deleted_at IS NULL
                  AND gp.end_date IS NOT NULL
                  AND a.status IN ('{ApplicationStatusCodes.APPROVED}', '{ApplicationStatusCodes.COMPLETED}', '{ApplicationStatusCodes.DRAFT}')
                  AND ($2 = '' OR (a.applicant_name ILIKE '%' || $2 || '%' OR a.applicant_id_card ILIKE '%' || $2 || '%'))
                ORDER BY gp.end_date ASC
                LIMIT $3 OFFSET $4";

            var dataResult = await _db.QueryAsync<GracePeriodExpiringItem>(
                dataSql, ct, today, keywordTrimmed, pageSize, offset);
            if (dataResult.IsFailure)
                return Result.Failure<PagedResult<GracePeriodExpiringItem>>(dataResult.ErrorCode!, dataResult.Message!);

            LogInfo($"渐退期列表: 总{totalCount}条, 第{pageIndex}页");

            return Result.Success(new PagedResult<GracePeriodExpiringItem>
            {
                Items = dataResult.Value ?? new List<GracePeriodExpiringItem>(),
                TotalCount = totalCount
            });
        }
        catch (Exception ex)
        {
            LogException(ex, "查询渐退期到期未处理列表失败");
            return Result.FromException<PagedResult<GracePeriodExpiringItem>>(ex);
        }
    }

    /// <summary>
    /// 激活/更新渐退期记录（UPSERT 到 nc_biz_grace_periods）
    /// </summary>
    public async Task<Result<bool>> ActivateAsync(
        long applicationId, int months,
        DateTime startDate, DateTime endDate,
        string? originalClassification, decimal? originalGuaranteeAmount,
        decimal? graceGrantAmount = null,
        CancellationToken ct = default)
    {
        try
        {
            var existSql = @"SELECT id FROM nc_biz_grace_periods
                WHERE application_id = $1 AND is_active = TRUE AND deleted_at IS NULL LIMIT 1";
            var existResult = await _db.ExecuteScalarAsync(existSql, ct, applicationId);
            if (existResult.IsFailure)
                return Result.Failure<bool>(existResult.ErrorCode!, existResult.Message!);

            if (existResult.Value > 0)
            {
                var updateSql = @"UPDATE nc_biz_grace_periods SET
                    is_active = TRUE,
                    grace_period_months = $2, start_date = $3, end_date = $4,
                    original_classification = $5, original_guarantee_amount = $6,
                    grace_grant_amount = $7,
                    updated_at = NOW()
                    WHERE application_id = $1 AND deleted_at IS NULL";
                var updateResult = await _db.ExecuteNonQueryAsync(updateSql, ct,
                    applicationId, months, startDate, endDate,
                    originalClassification, (object?)originalGuaranteeAmount,
                    (object?)graceGrantAmount);
                if (updateResult.IsFailure)
                    return Result.Failure<bool>(updateResult.ErrorCode!, updateResult.Message!);
            }
            else
            {
                var insertSql = @"INSERT INTO nc_biz_grace_periods (
                    application_id, is_active, grace_period_months, start_date, end_date,
                    original_classification, original_guarantee_amount, grace_grant_amount,
                    started_at, created_at, updated_at
                ) VALUES ($1, TRUE, $2, $3, $4, $5, $6, $7, NOW(), NOW(), NOW())";
                var insertResult = await _db.ExecuteNonQueryAsync(insertSql, ct,
                    applicationId, months, startDate, endDate,
                    originalClassification, (object?)originalGuaranteeAmount,
                    (object?)graceGrantAmount);
                if (insertResult.IsFailure)
                    return Result.Failure<bool>(insertResult.ErrorCode!, insertResult.Message!);
            }

            LogInfo($"激活渐退期: ApplicationId={applicationId}, 月数={months}, 应发额={graceGrantAmount}");
            return Result.Success(true);
        }
        catch (Exception ex)
        {
            LogException(ex, "激活渐退期失败");
            return Result.FromException<bool>(ex);
        }
    }

    /// <summary>
    /// 清除渐退期记录（置 is_active=false）
    /// </summary>
    public async Task<Result<bool>> ClearAsync(long applicationId, CancellationToken ct = default)
    {
        try
        {
            var sql = @"UPDATE nc_biz_grace_periods SET
                is_active = FALSE,
                ended_at = NOW(),
                updated_at = NOW()
                WHERE application_id = $1 AND is_active = TRUE AND deleted_at IS NULL";
            var result = await _db.ExecuteNonQueryAsync(sql, ct, applicationId);
            if (result.IsFailure)
                return Result.Failure<bool>(result.ErrorCode!, result.Message!);

            LogInfo($"清除渐退期: ApplicationId={applicationId}");
            return Result.Success(true);
        }
        catch (Exception ex)
        {
            LogException(ex, "清除渐退期失败");
            return Result.FromException<bool>(ex);
        }
    }

    /// <summary>
    /// 获取指定申请当前有效的渐退期记录
    /// </summary>
    public async Task<Result<GracePeriodRecord?>> GetActiveAsync(long applicationId, CancellationToken ct = default)
    {
        try
        {
            var sql = @"SELECT id, application_id, is_active, grace_period_months, start_date, end_date,
                        original_classification, original_guarantee_amount, grace_grant_amount
                FROM nc_biz_grace_periods
                WHERE application_id = $1 AND is_active = TRUE AND deleted_at IS NULL
                ORDER BY id DESC LIMIT 1";
            var result = await _db.QuerySingleAsync<GracePeriodRecord>(sql, ct, applicationId);
            if (result.IsFailure)
                return Result.Failure<GracePeriodRecord?>(result.ErrorCode!, result.Message!);

            return Result.Success<GracePeriodRecord?>(result.Value);
        }
        catch (Exception ex)
        {
            LogException(ex, "获取渐退期记录失败");
            return Result.FromException<GracePeriodRecord?>(ex);
        }
    }

    /// <summary>
    /// 批量取多份申请的当前有效渐退期到期日（工作流列表注释用，= ANY 一次查询防 N+1）。
    /// 同一申请多条时取 id 最新一条；无有效记录的申请不出现在字典中。
    /// </summary>
    public async Task<Result<IReadOnlyDictionary<long, DateTime>>> GetActiveEndDateMapByApplicationIdsAsync(
        IReadOnlyCollection<long> applicationIds, CancellationToken ct = default)
    {
        try
        {
            if (applicationIds.Count == 0)
                return Result.Success<IReadOnlyDictionary<long, DateTime>>(new Dictionary<long, DateTime>());

            var sql = @"SELECT application_id, end_date
                FROM nc_biz_grace_periods
                WHERE application_id = ANY($1) AND is_active = TRUE AND deleted_at IS NULL AND end_date IS NOT NULL
                ORDER BY id DESC";
            var result = await _db.QueryAsync<GracePeriodRecord>(sql, ct, applicationIds.ToArray());
            if (result.IsFailure)
                return Result.Failure<IReadOnlyDictionary<long, DateTime>>(result.ErrorCode!, result.Message!);

            var map = new Dictionary<long, DateTime>();
            foreach (var row in result.Value ?? new List<GracePeriodRecord>())
            {
                if (row.EndDate.HasValue && !map.ContainsKey(row.ApplicationId))
                    map[row.ApplicationId] = row.EndDate.Value.Date;
            }

            return Result.Success<IReadOnlyDictionary<long, DateTime>>(map);
        }
        catch (Exception ex)
        {
            LogException(ex, "批量获取渐退期到期日失败");
            return Result.FromException<IReadOnlyDictionary<long, DateTime>>(ex);
        }
    }

    /// <summary>
    /// 获取指定申请最近一条渐退期记录（不过滤 is_active，退出后补打审批表仍可取数）
    /// </summary>
    public async Task<Result<GracePeriodRecord?>> GetLatestAsync(long applicationId, CancellationToken ct = default)
    {
        try
        {
            var sql = @"SELECT id, application_id, is_active, grace_period_months, start_date, end_date,
                        original_classification, original_guarantee_amount, grace_grant_amount
                FROM nc_biz_grace_periods
                WHERE application_id = $1 AND deleted_at IS NULL
                ORDER BY id DESC LIMIT 1";
            var result = await _db.QuerySingleAsync<GracePeriodRecord>(sql, ct, applicationId);
            if (result.IsFailure)
                return Result.Failure<GracePeriodRecord?>(result.ErrorCode!, result.Message!);

            return Result.Success<GracePeriodRecord?>(result.Value);
        }
        catch (Exception ex)
        {
            LogException(ex, "获取最近渐退期记录失败");
            return Result.FromException<GracePeriodRecord?>(ex);
        }
    }

    /// <summary>
    /// 获取所有当前有效的渐退期记录
    /// </summary>
    public async Task<Result<List<GracePeriodRecord>>> GetActiveListAsync(CancellationToken ct = default)
    {
        try
        {
            // 上限保护：在效渐退期随业务增长，防无界列表（当前无消费方，留作接口 API 兜底）
            var sql = @"SELECT id, application_id, is_active, grace_period_months, start_date, end_date,
                        original_classification, original_guarantee_amount
                FROM nc_biz_grace_periods
                WHERE is_active = TRUE AND deleted_at IS NULL
                ORDER BY end_date
                LIMIT 1000";
            var result = await _db.QueryAsync<GracePeriodRecord>(sql, ct);
            if (result.IsFailure)
                return Result.Failure<List<GracePeriodRecord>>(result.ErrorCode!, result.Message!);

            return Result.Success(result.Value ?? new List<GracePeriodRecord>());
        }
        catch (Exception ex)
        {
            LogException(ex, "获取渐退期记录列表失败");
            return Result.FromException<List<GracePeriodRecord>>(ex);
        }
    }

    /// <summary>
    /// 查询指定年月处于渐退期的申请列表（每月报表用）
    /// </summary>
    public async Task<Result<List<GracePeriodRecord>>> GetMonthlyAsync(int year, int month, CancellationToken ct = default)
    {
        try
        {
            var monthStart = new DateTime(year, month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);

            // 上限保护：月度渐退期按月有界，仍加兜底防异常积压（当前无消费方，留作接口 API 兜底）
            var sql = @"SELECT id, application_id, is_active, grace_period_months, start_date, end_date,
                        original_classification, original_guarantee_amount
                FROM nc_biz_grace_periods
                WHERE is_active = TRUE AND deleted_at IS NULL
                  AND start_date <= $2 AND end_date >= $1
                ORDER BY end_date
                LIMIT 1000";
            var result = await _db.QueryAsync<GracePeriodRecord>(sql, ct, monthStart, monthEnd);
            if (result.IsFailure)
                return Result.Failure<List<GracePeriodRecord>>(result.ErrorCode!, result.Message!);

            return Result.Success(result.Value ?? new List<GracePeriodRecord>());
        }
        catch (Exception ex)
        {
            LogException(ex, "查询月度渐退期记录失败");
            return Result.FromException<List<GracePeriodRecord>>(ex);
        }
    }
}
