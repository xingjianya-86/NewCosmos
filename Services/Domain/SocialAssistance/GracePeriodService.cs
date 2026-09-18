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
    /// 统计渐退期将在指定天数内到期（含已到期未处理）的户数
    /// </summary>
    public async Task<Result<int>> GetExpiringCountAsync(int withinDays, CancellationToken ct = default)
    {
        try
        {
            // end_date <= today + withinDays（含已过期但仍标记在渐退期内的记录，这类更需要处理）
            var cutoff = DateTime.Today.AddDays(withinDays + 1);

            var sql = @"SELECT COUNT(*) FROM nc_biz_grace_periods
                        WHERE is_active = TRUE
                          AND deleted_at IS NULL
                          AND end_date IS NOT NULL
                          AND end_date < $1";

            var result = await _db.ExecuteScalarAsync(sql, ct, cutoff);
            if (result.IsFailure)
                return Result.Failure<int>(result.ErrorCode!, result.Message!);

            var count = Convert.ToInt32(result.Value);
            LogInfo($"渐退期{withinDays}天内到期户数: {count}");
            return Result.Success(count);
        }
        catch (Exception ex)
        {
            LogException(ex, "统计渐退期到期户数失败");
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

            var countSql = @"SELECT COUNT(*)
                FROM nc_biz_grace_periods gp
                JOIN nc_biz_applications a ON gp.application_id = a.id AND a.deleted_at IS NULL
                WHERE gp.is_active = TRUE
                  AND gp.deleted_at IS NULL
                  AND gp.end_date IS NOT NULL
                  AND gp.end_date < $1
                  AND a.status != 'Stopped'
                  AND ($2 = '' OR (a.applicant_name ILIKE '%' || $2 || '%' OR a.applicant_id_card ILIKE '%' || $2 || '%'))";

            var countResult = await _db.ExecuteScalarAsync(countSql, ct, today, keywordTrimmed);
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
            var dataSql = @"SELECT a.id AS application_id, a.application_no, a.applicant_name, a.applicant_id_card,
                                   a.classification_result,
                                   gp.grace_period_months, gp.start_date, gp.end_date,
                                   gp.original_classification, gp.original_guarantee_amount,
                                   ($1::date - gp.end_date) AS expired_days
                FROM nc_biz_grace_periods gp
                JOIN nc_biz_applications a ON gp.application_id = a.id AND a.deleted_at IS NULL
                WHERE gp.is_active = TRUE
                  AND gp.deleted_at IS NULL
                  AND gp.end_date IS NOT NULL
                  AND gp.end_date < $1
                  AND a.status != 'Stopped'
                  AND ($2 = '' OR (a.applicant_name ILIKE '%' || $2 || '%' OR a.applicant_id_card ILIKE '%' || $2 || '%'))
                ORDER BY gp.end_date ASC
                LIMIT $3 OFFSET $4";

            var dataResult = await _db.QueryAsync<GracePeriodExpiringItem>(
                dataSql, ct, today, keywordTrimmed, pageSize, offset);
            if (dataResult.IsFailure)
                return Result.Failure<PagedResult<GracePeriodExpiringItem>>(dataResult.ErrorCode!, dataResult.Message!);

            LogInfo($"渐退期到期未处理列表: 总{totalCount}条, 第{pageIndex}页");

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
        CancellationToken ct = default)
    {
        try
        {
            var existSql = @"SELECT id FROM nc_biz_grace_periods
                WHERE application_id = $1 AND is_active = TRUE AND deleted_at IS NULL LIMIT 1";
            var existResult = await _db.QuerySingleAsync<object>(existSql, ct, applicationId);
            if (existResult.IsFailure)
                return Result.Failure<bool>(existResult.ErrorCode!, existResult.Message!);

            var now = DateTime.UtcNow;
            if (existResult.Value != null && Convert.ToInt64(existResult.Value) > 0)
            {
                var updateSql = @"UPDATE nc_biz_grace_periods SET
                    is_active = TRUE,
                    grace_period_months = $2, start_date = $3, end_date = $4,
                    original_classification = $5, original_guarantee_amount = $6,
                    updated_at = NOW()
                    WHERE application_id = $1 AND deleted_at IS NULL";
                var updateResult = await _db.ExecuteNonQueryAsync(updateSql, ct,
                    applicationId, months, startDate, endDate,
                    originalClassification, (object?)originalGuaranteeAmount);
                if (updateResult.IsFailure)
                    return Result.Failure<bool>(updateResult.ErrorCode!, updateResult.Message!);
            }
            else
            {
                var insertSql = @"INSERT INTO nc_biz_grace_periods (
                    application_id, is_active, grace_period_months, start_date, end_date,
                    original_classification, original_guarantee_amount, started_at, created_at, updated_at
                ) VALUES ($1, TRUE, $2, $3, $4, $5, $6, NOW(), NOW(), NOW())";
                var insertResult = await _db.ExecuteNonQueryAsync(insertSql, ct,
                    applicationId, months, startDate, endDate,
                    originalClassification, (object?)originalGuaranteeAmount);
                if (insertResult.IsFailure)
                    return Result.Failure<bool>(insertResult.ErrorCode!, insertResult.Message!);
            }

            LogInfo($"激活渐退期: ApplicationId={applicationId}, 月数={months}");
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
                        original_classification, original_guarantee_amount
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
    /// 获取所有当前有效的渐退期记录
    /// </summary>
    public async Task<Result<List<GracePeriodRecord>>> GetActiveListAsync(CancellationToken ct = default)
    {
        try
        {
            var sql = @"SELECT id, application_id, is_active, grace_period_months, start_date, end_date,
                        original_classification, original_guarantee_amount
                FROM nc_biz_grace_periods
                WHERE is_active = TRUE AND deleted_at IS NULL
                ORDER BY end_date";
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

            var sql = @"SELECT id, application_id, is_active, grace_period_months, start_date, end_date,
                        original_classification, original_guarantee_amount
                FROM nc_biz_grace_periods
                WHERE is_active = TRUE AND deleted_at IS NULL
                  AND start_date <= $2 AND end_date >= $1
                ORDER BY end_date";
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
