using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.Reporting;

public class StatisticsService : BaseService, IStatisticsService
{
    protected override string ServiceName => "StatisticsService";
    private readonly IDatabaseService _dbService;

    public StatisticsService(IDatabaseService dbService, ILoggerService logger)
        : base(logger)
    {
        _dbService = dbService;
    }

    public async Task<Result<int>> GetPendingApprovalsCountAsync(CancellationToken ct = default)
    {
        // 待审批 = 业务申请表中已提交的申请。
        var sql = $@"SELECT COUNT(*) FROM nc_biz_applications WHERE status = '{ApplicationStatusCodes.SUBMITTED}' AND deleted_at IS NULL";
        var result = await _dbService.ExecuteScalarAsync(sql, ct);

        if (result.IsSuccess)
        {
            var count = Convert.ToInt32(result.Value);
            LogInfo($"待审批申请数: {count}");
            return Result<int>.Success(count);
        }

        LogError($"操作失败");
        return Result<int>.Failure(ErrorCodes.DB_QUERY_ERROR, result.Message ?? "未知错误");
    }

    public async Task<Result<int>> GetMonthlyArchivesCountAsync(int year, int month, CancellationToken ct = default)
    {
        var startDate = new DateTime(year, month, 1);
        var endDate = startDate.AddMonths(1);

        // 月度归档看真实归档表 nc_biz_archives（ArchiveService 写入），
        // 而非导入镜像表 nc_biz_low_income_archives（其 created_at 是导入时间）
        var sql = @"SELECT COUNT(*) FROM nc_biz_archives
                    WHERE created_at >= $1 AND created_at < $2 AND deleted_at IS NULL";

        var result = await _dbService.ExecuteScalarAsync(sql, ct, startDate, endDate);

        if (result.IsSuccess)
        {
            var count = Convert.ToInt32(result.Value);
            LogInfo($"{year}年{month}月归档数: {count}");
            return Result<int>.Success(count);
        }

        LogError($"操作失败");
        return Result<int>.Failure(ErrorCodes.DB_QUERY_ERROR, result.Message ?? "未知错误");
    }

    public async Task<Result<int>> GetAssetVerificationsCountAsync(CancellationToken ct = default)
    {
        // 资产核查任务的真实表是 nc_biz_asset_checks（nc_biz_asset_verifications 并不存在，
        // 原查询必失败）；待处理状态口径与 AssetVerificationService 一致
        var sql = $@"SELECT COUNT(*) FROM nc_biz_asset_checks
                    WHERE status = '{AssetCheckStatusConstants.SUBMITTED}' AND deleted_at IS NULL";

        var result = await _dbService.ExecuteScalarAsync(sql, ct);

        if (result.IsSuccess)
        {
            var count = Convert.ToInt32(result.Value);
            LogInfo($"待处理资产核查数: {count}");
            return Result<int>.Success(count);
        }

        LogError($"操作失败");
        return Result<int>.Failure(ErrorCodes.DB_QUERY_ERROR, result.Message ?? "未知错误");
    }

    public async Task<Result<int>> GetActiveUsersCountAsync(CancellationToken ct = default)
    {
        var sql = @"SELECT COUNT(*) FROM nc_sys_users WHERE is_active = true";

        var result = await _dbService.ExecuteScalarAsync(sql, ct);

        if (result.IsSuccess)
        {
            var count = Convert.ToInt32(result.Value);
            LogInfo($"活跃用户数: {count}");
            return Result<int>.Success(count);
        }

        LogError($"操作失败");
        return Result<int>.Failure(ErrorCodes.DB_QUERY_ERROR, result.Message ?? "未知错误");
    }

    public async Task<Result<DashboardStatistics>> GetDashboardStatisticsAsync(CancellationToken ct = default)
    {
        LogInfo("获取仪表盘统计数");

        try
        {
            var statistics = new DashboardStatistics();

            // 单条 SQL 合并 4 项统计：同表计数用 COUNT(*) FILTER 聚合，不同表用子查询交叉连接一次取回。
            // 口径（2026-08 与用户确认，依托 B 线业务时间轴）：
            // - 本月总新增 = nc_biz_applications 中 status='Approved' 且 first_approved_at∈B线周期（月报"新增救助明细"口径；
            //   用不可变的首次审批时间，避免任何一次编辑/变更把老档案重算成"本月新增"）
            // - 本月总退出 = status='Stopped' 且 stop_date∈B线周期（月报"停保汇总"口径）
            // - 新申请资产核查 = nc_biz_asset_checks 中 status='0'（已申请未出授权报告）且 application_date∈B线周期
            // - 本月新增高龄老人 = nc_biz_elderly_applications 中 status∈(Confirmed,Stopped) 且 apply_date∈当月自然月
            // B 线周期日期运算统一引用 BusinessCycleHelper（唯一事实来源，与 GetCycleRangeAsync 同源）。
            var now = DateTime.Now;
            var bStart = Helpers.BusinessCycleHelper.GetStart(now.Year, now.Month);  // 周期起点（默认上月16日；结算日可配置）
            var bEnd = Helpers.BusinessCycleHelper.GetEnd(now.Year, now.Month);      // 周期终点（默认本月16日；结算日可配置）
            var mStart = new DateTime(now.Year, now.Month, 1);                        // 当月1日
            var mEnd = mStart.AddMonths(1);                                           // 次月1日

            var sql = $@"
                SELECT
                    f.monthly_new_additions,
                    f.monthly_exits,
                    v.new_asset_checks,
                    e.monthly_new_elderly
                FROM
                    (SELECT COUNT(*) FILTER (WHERE status = '{ApplicationStatusCodes.APPROVED}' AND first_approved_at >= $1 AND first_approved_at < $2) AS monthly_new_additions,
                            COUNT(*) FILTER (WHERE status = '{ApplicationStatusCodes.STOPPED}' AND stop_date >= $1::date AND stop_date < $2::date
                                {StoppedArchiveFilter.NotRebuildContinuationSql("nc_biz_applications")}) AS monthly_exits
                     FROM nc_biz_applications
                     WHERE deleted_at IS NULL) f
                CROSS JOIN
                    (SELECT COUNT(*) AS new_asset_checks
                     FROM nc_biz_asset_checks
                     WHERE status = '{AssetCheckStatusConstants.SUBMITTED}' AND application_date >= $1::date AND application_date < $2::date AND deleted_at IS NULL) v
                CROSS JOIN
                    (SELECT COUNT(*) AS monthly_new_elderly
                      FROM nc_biz_elderly_applications
                      WHERE status IN ('{ElderlyBenefitConstants.StatusConfirmed}', '{ElderlyBenefitConstants.StatusStopped}') AND apply_date >= $3::date AND apply_date < $4::date AND deleted_at IS NULL
                        AND source_type IS NULL) e";

            var result = await _dbService.QuerySingleAsync<DashboardCounts>(sql, ct, bStart, bEnd, mStart, mEnd);

            // 查询失败必须失败返回，不再伪造"全 0"的假数据——
            // 假数据会让仪表盘在数据库异常时看起来"一切正常但没业务"。
            // 调用方 MainViewModel 对失败已有 try/catch 与降级处理。
            if (result.IsFailure || result.Value is null)
            {
                LogError($"仪表盘统计查询失败: {result.Message}");
                return Result<DashboardStatistics>.Failure(
                    string.IsNullOrEmpty(result.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : result.ErrorCode,
                    result.Message ?? "仪表盘统计查询失败");
            }

            var counts = result.Value;
            statistics.MonthlyNewAdditions = counts.MonthlyNewAdditions;
            statistics.MonthlyExits = counts.MonthlyExits;
            statistics.NewAssetChecks = counts.NewAssetChecks;
            statistics.MonthlyNewElderly = counts.MonthlyNewElderly;

            LogInfo($"仪表盘统计完成: 新增={statistics.MonthlyNewAdditions}, 退出={statistics.MonthlyExits}, 资产核查={statistics.NewAssetChecks}, 高龄={statistics.MonthlyNewElderly}");
            return Result<DashboardStatistics>.Success(statistics);
        }
        catch (Exception ex)
        {
            LogError($"获取仪表盘统计失败");
            return Result<DashboardStatistics>.Failure(ErrorCodes.DB_QUERY_ERROR, ex.Message);
        }
    }

    private class DashboardCounts
    {
        public int MonthlyNewAdditions { get; set; }
        public int MonthlyExits { get; set; }
        public int NewAssetChecks { get; set; }
        public int MonthlyNewElderly { get; set; }
    }

    public async Task<Result<SocialAssistanceModuleStats>> GetSocialAssistanceStatsAsync(CancellationToken ct = default)
    {
        LogInfo("获取低收入人口救助帮扶模块统计");

        try
        {
            // 口径与 GetDashboardStatisticsAsync 完全一致（B 线周期，日期运算引用 BusinessCycleHelper）：
            // - 在享保障对象 = status=APPROVED
            // - 本月总新增   = status=APPROVED 且 first_approved_at∈B线周期
            // - 本月总退出   = status=STOPPED 且 stop_date∈B线周期
            var now = DateTime.Now;
            var bStart = Helpers.BusinessCycleHelper.GetStart(now.Year, now.Month); // 周期起点（默认上月16日；结算日可配置）
            var bEnd = Helpers.BusinessCycleHelper.GetEnd(now.Year, now.Month);     // 周期终点（默认本月16日；结算日可配置）

            var sql = $@"
                SELECT
                    COUNT(*) FILTER (WHERE status = '{ApplicationStatusCodes.APPROVED}') AS active_count,
                    COUNT(*) FILTER (WHERE status = '{ApplicationStatusCodes.APPROVED}' AND first_approved_at >= $1 AND first_approved_at < $2) AS monthly_new_additions,
                    COUNT(*) FILTER (WHERE status = '{ApplicationStatusCodes.STOPPED}' AND stop_date >= $1::date AND stop_date < $2::date
                        {StoppedArchiveFilter.NotRebuildContinuationSql("nc_biz_applications")}) AS monthly_exits
                FROM nc_biz_applications
                WHERE deleted_at IS NULL";

            var result = await _dbService.QuerySingleAsync<SocialAssistanceModuleStats>(sql, ct, bStart, bEnd);

            if (result.IsFailure || result.Value is null)
            {
                LogError($"救助帮扶模块统计查询失败: {result.Message}");
                return Result<SocialAssistanceModuleStats>.Failure(
                    string.IsNullOrEmpty(result.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : result.ErrorCode,
                    result.Message ?? "模块统计查询失败");
            }

            LogInfo($"救助帮扶模块统计完成: 在享={result.Value.ActiveCount}, 新增={result.Value.MonthlyNewAdditions}, 退出={result.Value.MonthlyExits}");
            return Result<SocialAssistanceModuleStats>.Success(result.Value);
        }
        catch (Exception ex)
        {
            LogError($"获取救助帮扶模块统计失败");
            return Result<SocialAssistanceModuleStats>.Failure(ErrorCodes.DB_QUERY_ERROR, ex.Message);
        }
    }

    public async Task<Result<ElderlyModuleStats>> GetElderlyStatsAsync(CancellationToken ct = default)
    {
        LogInfo("获取高龄津贴发放模块统计");

        try
        {
            // 口径与 GetDashboardStatisticsAsync 的 MonthlyNewElderly 一致：
            // - 在享领取人数 = 当前库 status='Confirmed' 且未死亡（death_date IS NULL）
            //   + 导入库 nc_biz_elderly_subsidy_history 中尚未在当前库建档（身份证不在当前库未删记录）
            //   且未登记死亡（不在 nc_biz_death_records.member_id_card）的记录数。
            // - 本月新增登记 = status∈(Confirmed,Stopped) 且 apply_date∈当月自然月
            //   （排除导入库补录：source_type 非空者的 apply_date 为补录当天，非真实新增）
            var now = DateTime.Now;
            var mStart = new DateTime(now.Year, now.Month, 1); // 当月1日
            var mEnd = mStart.AddMonths(1);                    // 次月1日

            var sql = $@"
                SELECT
                    ((SELECT COUNT(*) FROM nc_biz_elderly_applications
                       WHERE status = '{ElderlyBenefitConstants.StatusConfirmed}'
                         AND death_date IS NULL AND deleted_at IS NULL)
                    + (SELECT COUNT(*) FROM nc_biz_elderly_subsidy_history h
                       WHERE NOT EXISTS (SELECT 1 FROM nc_biz_elderly_applications e
                                         WHERE e.id_card = h.id_card AND e.deleted_at IS NULL)
                         AND NOT EXISTS (SELECT 1 FROM nc_biz_death_records d
                                         WHERE d.member_id_card = h.id_card))) AS active_count,
                    (SELECT COUNT(*) FROM nc_biz_elderly_applications
                     WHERE status IN ('{ElderlyBenefitConstants.StatusConfirmed}', '{ElderlyBenefitConstants.StatusStopped}')
                       AND apply_date >= $1::date AND apply_date < $2::date
                       AND source_type IS NULL AND deleted_at IS NULL) AS monthly_new";

            var result = await _dbService.QuerySingleAsync<ElderlyModuleStats>(sql, ct, mStart, mEnd);

            if (result.IsFailure || result.Value is null)
            {
                LogError($"高龄津贴模块统计查询失败: {result.Message}");
                return Result<ElderlyModuleStats>.Failure(
                    string.IsNullOrEmpty(result.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : result.ErrorCode,
                    result.Message ?? "模块统计查询失败");
            }

            LogInfo($"高龄津贴模块统计完成: 在享={result.Value.ActiveCount}, 本月新增={result.Value.MonthlyNew}");
            return Result<ElderlyModuleStats>.Success(result.Value);
        }
        catch (Exception ex)
        {
            LogError($"获取高龄津贴模块统计失败");
            return Result<ElderlyModuleStats>.Failure(ErrorCodes.DB_QUERY_ERROR, ex.Message);
        }
    }

    public async Task<Result<AssetVerificationModuleStats>> GetAssetVerificationStatsAsync(CancellationToken ct = default)
    {
        LogInfo("获取家庭经济状况核对模块统计");

        try
        {
            // 口径：
            // - 待上传报告 = status=SUBMITTED（已提交待核查，不限时间，与 GetAssetVerificationsCountAsync 一致）
            // - 本月已完成 = status=VERIFIED 且 updated_at∈当月自然月
            // - 本月新增申请 = application_date∈当月自然月
            // - 全年累计申请 = application_date∈当年（end 复用次月1日：12 月时即次年1月1日，恰好覆盖全年）
            var now = DateTime.Now;
            var mStart = new DateTime(now.Year, now.Month, 1); // 当月1日
            var mEnd = mStart.AddMonths(1);                    // 次月1日
            var yStart = new DateTime(now.Year, 1, 1);         // 当年1月1日

            var sql = $@"
                SELECT
                    COUNT(*) FILTER (WHERE status = '{AssetCheckStatusConstants.SUBMITTED}') AS pending_report_count,
                    COUNT(*) FILTER (WHERE status = '{AssetCheckStatusConstants.VERIFIED}' AND updated_at >= $1 AND updated_at < $2) AS monthly_completed,
                    COUNT(*) FILTER (WHERE application_date >= $1::date AND application_date < $2::date) AS monthly_new,
                    COUNT(*) FILTER (WHERE application_date >= $3::date AND application_date < $2::date) AS year_total
                FROM nc_biz_asset_checks
                WHERE deleted_at IS NULL";

            var result = await _dbService.QuerySingleAsync<AssetVerificationModuleStats>(sql, ct, mStart, mEnd, yStart);

            if (result.IsFailure || result.Value is null)
            {
                LogError($"经济状况核对模块统计查询失败: {result.Message}");
                return Result<AssetVerificationModuleStats>.Failure(
                    string.IsNullOrEmpty(result.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : result.ErrorCode,
                    result.Message ?? "模块统计查询失败");
            }

            LogInfo($"经济状况核对模块统计完成: 待传={result.Value.PendingReportCount}, 完成={result.Value.MonthlyCompleted}, 新增={result.Value.MonthlyNew}, 年累计={result.Value.YearTotal}");
            return Result<AssetVerificationModuleStats>.Success(result.Value);
        }
        catch (Exception ex)
        {
            LogError($"获取经济状况核对模块统计失败");
            return Result<AssetVerificationModuleStats>.Failure(ErrorCodes.DB_QUERY_ERROR, ex.Message);
        }
    }

    public async Task<Result<ElderlyStopStats>> GetElderlyStopStatsAsync(CancellationToken ct = default)
    {
        LogInfo("获取高龄津贴停发统计");

        try
        {
            // 口径：本月/年累计停发人次，时间锚 = stopped_at（与停发办理页列表口径一致）
            var now = DateTime.Now;
            var mStart = new DateTime(now.Year, now.Month, 1);
            var mEnd = mStart.AddMonths(1);
            var yStart = new DateTime(now.Year, 1, 1);

            const string sql = $@"
                SELECT
                    COUNT(*) FILTER (WHERE stopped_at >= $1 AND stopped_at < $2) AS monthly_stopped,
                    COUNT(*) FILTER (WHERE stopped_at >= $3 AND stopped_at < $2) AS year_stopped
                FROM nc_biz_elderly_applications
                WHERE status = '{ElderlyBenefitConstants.StatusStopped}' AND deleted_at IS NULL
                  AND (stop_reason IS NULL OR stop_reason <> '{ElderlyBenefitConstants.StopReasonReview}')";

            var result = await _dbService.QuerySingleAsync<ElderlyStopStats>(sql, ct, mStart, mEnd, yStart);

            if (result.IsFailure || result.Value is null)
            {
                LogError($"高龄停发统计查询失败: {result.Message}");
                return Result<ElderlyStopStats>.Failure(
                    string.IsNullOrEmpty(result.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : result.ErrorCode,
                    result.Message ?? "停发统计查询失败");
            }

            return Result<ElderlyStopStats>.Success(result.Value);
        }
        catch (Exception ex)
        {
            LogError($"获取高龄停发统计失败");
            return Result<ElderlyStopStats>.Failure(ErrorCodes.DB_QUERY_ERROR, ex.Message);
        }
    }

    public async Task<Result<TempReliefStats>> GetTempReliefStatsAsync(CancellationToken ct = default)
    {
        LogInfo("获取临时救助统计");

        try
        {
            const string sql = $@"
                SELECT
                    COUNT(*) FILTER (WHERE status = '{TempReliefConstants.StatusDraft}') AS draft_count,
                    COUNT(*) FILTER (WHERE status = '{TempReliefConstants.StatusConfirmed}') AS confirmed_total,
                    COUNT(*) FILTER (WHERE status = '{TempReliefConstants.StatusConfirmed}' AND confirmed_at >= $1 AND confirmed_at < $2) AS year_confirmed,
                    COALESCE(SUM(confirm_amount) FILTER (WHERE status = '{TempReliefConstants.StatusConfirmed}' AND confirmed_at >= $1 AND confirmed_at < $2), 0) AS year_confirmed_amount
                FROM nc_biz_temp_relief_applications
                WHERE deleted_at IS NULL";

            var yStart = new DateTime(DateTime.Now.Year, 1, 1); // 当年1月1日
            var yEnd = new DateTime(DateTime.Now.Year + 1, 1, 1); // 次年1月1日
            var result = await _dbService.QuerySingleAsync<TempReliefStats>(sql, ct, yStart, yEnd);

            if (result.IsFailure || result.Value is null)
            {
                LogError($"临时救助统计查询失败: {result.Message}");
                return Result<TempReliefStats>.Failure(
                    string.IsNullOrEmpty(result.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : result.ErrorCode,
                    result.Message ?? "临时救助统计查询失败");
            }

            return Result<TempReliefStats>.Success(result.Value);
        }
        catch (Exception ex)
        {
            LogError($"获取临时救助统计失败");
            return Result<TempReliefStats>.Failure(ErrorCodes.DB_QUERY_ERROR, ex.Message);
        }
    }

    public async Task<Result<ChangeStats>> GetChangeStatsAsync(CancellationToken ct = default)
    {
        LogInfo("获取保障对象变更统计");

        try
        {
            var now = DateTime.Now;
            var mStart = new DateTime(now.Year, now.Month, 1);
            var mEnd = mStart.AddMonths(1);
            var yStart = new DateTime(now.Year, 1, 1);

            const string sql = $@"
                SELECT
                    COUNT(*) FILTER (WHERE change_date >= $1::date AND change_date < $2::date) AS monthly_changes,
                    COUNT(*) FILTER (WHERE change_date >= $3::date AND change_date < $2::date) AS year_changes
                FROM nc_biz_change_records
                WHERE deleted_at IS NULL";

            var result = await _dbService.QuerySingleAsync<ChangeStats>(sql, ct, mStart, mEnd, yStart);

            if (result.IsFailure || result.Value is null)
            {
                LogError($"变更统计查询失败: {result.Message}");
                return Result<ChangeStats>.Failure(
                    string.IsNullOrEmpty(result.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : result.ErrorCode,
                    result.Message ?? "变更统计查询失败");
            }

            return Result<ChangeStats>.Success(result.Value);
        }
        catch (Exception ex)
        {
            LogError($"获取变更统计失败");
            return Result<ChangeStats>.Failure(ErrorCodes.DB_QUERY_ERROR, ex.Message);
        }
    }

    public async Task<Result<ArchiveStats>> GetArchiveStatsAsync(CancellationToken ct = default)
    {
        LogInfo("获取救助档案统计");

        try
        {
            const string sql = @"SELECT COUNT(*) AS total_archives FROM nc_biz_archives WHERE deleted_at IS NULL";

            var result = await _dbService.QuerySingleAsync<ArchiveStats>(sql, ct);

            if (result.IsFailure || result.Value is null)
            {
                LogError($"档案统计查询失败: {result.Message}");
                return Result<ArchiveStats>.Failure(
                    string.IsNullOrEmpty(result.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : result.ErrorCode,
                    result.Message ?? "档案统计查询失败");
            }

            return Result<ArchiveStats>.Success(result.Value);
        }
        catch (Exception ex)
        {
            LogError($"获取档案统计失败");
            return Result<ArchiveStats>.Failure(ErrorCodes.DB_QUERY_ERROR, ex.Message);
        }
    }
}
