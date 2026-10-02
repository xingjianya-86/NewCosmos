using System.Text;
using System.Text.Json;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.Domain.TempRelief;
using NewCosmos.Services.System;

namespace NewCosmos.Services.Domain.Reporting;

/// <summary>
/// 月报表服务实现（统计基准：nc_biz_applications 状态流转；周期 B 线＝[上月(结算日+1)日, 本月(结算日+1)日)，默认结算日 15）
/// </summary>
public class MonthlyReportService : BaseService, IMonthlyReportService
{
    protected override string ServiceName => "MonthlyReportService";
    private readonly IDatabaseService _db;
    private readonly IStandardConfigService _standardConfigService;

    public MonthlyReportService(IDatabaseService db, ILoggerService logger, IStandardConfigService standardConfigService) : base(logger)
    {
        _db = db;
        _standardConfigService = standardConfigService;
    }

    // ─────────────────────────── 周期与展示辅助 ───────────────────────────

    /// <summary>
    /// B 线统计周期：上月(结算日+1) 00:00（含）~ 本月(结算日+1) 00:00（不含）。
    /// 默认结算日 15（例：2026-08 → [2026-07-16, 2026-08-16)）；上级业务截止 20 号时结算日=20（[上月21, 本月21)）。
    /// </summary>
    public async Task<Result<MonthlyCycleRange>> GetCycleRangeAsync(int year, int month, CancellationToken ct = default)
    {
        // 入参校验：非法年月直接显式失败，避免 new DateTime(year, 0, 16) 抛异常冒泡到 fire-and-forget
        if (year is < 1 or > 9999 || month is < 1 or > 12)
            return Result.Failure<MonthlyCycleRange>(ErrorCodes.VALIDATION_FAILED, $"无效的统计周期: {year}年{month}月");

        // 日期运算统一收敛到 BusinessCycleHelper（唯一事实来源）
        var start = Helpers.BusinessCycleHelper.GetStart(year, month);
        var end = Helpers.BusinessCycleHelper.GetEnd(year, month);
        LogInfo($"统计周期: {start:yyyy-MM-dd} ~ {end.AddDays(-1):yyyy-MM-dd}");
        return Result.Success(new MonthlyCycleRange { Start = start, End = end });
    }

    public async Task<Result<List<string>>> GetTownOptionsAsync(CancellationToken ct = default)
    {
        var sql = @"SELECT b.town FROM (
                        SELECT DISTINCT town FROM nc_biz_applications WHERE deleted_at IS NULL AND town IS NOT NULL AND town <> ''
                        UNION
                        SELECT DISTINCT town FROM nc_biz_monthly_reports WHERE town IS NOT NULL AND town <> ''
                    ) b ORDER BY b.town";
        var result = await _db.QueryAsync<string>(sql, ct);
        if (result.IsFailure)
            return Result.Failure<List<string>>(result.ErrorCode!, result.Message!);
        return Result.Success(result.Value ?? new List<string>());
    }

    // ─────────────────── 报表生成/读取 ───────────────────

    public async Task<Result<MonthlyReport>> GenerateAsync(int year, int month, string town, CancellationToken ct = default)
    {
        LogInfo($"生成月报表: {year}年{month}月 乡镇={town ?? "全部"}");

        // 支持重新生成覆盖：先删同 (year, month) 记录（uk_monthly_reports_year_month 唯一）
        var delResult = await _db.ExecuteNonQueryAsync(
            "DELETE FROM nc_biz_monthly_reports WHERE year = $1 AND month = $2", ct, year, month);
        if (delResult.IsFailure)
            return Result.Failure<MonthlyReport>(delResult.ErrorCode!, delResult.Message!);

        var statsResult = await GetStatisticsAsync(year, month, ct);
        if (statsResult.IsFailure)
            return Result.Failure<MonthlyReport>(statsResult.ErrorCode!, statsResult.Message!);
        var stats = statsResult.Value;

        var sql = @"INSERT INTO nc_biz_monthly_reports 
            (year, month, town, report_type, status, total_archives, new_archives, stopped_archives,
             total_amount, new_amount, rural_subsistence_count, urban_subsistence_count,
             rural_low_income_count, urban_low_income_count, rural_destitute_count, urban_destitute_count,
             rigid_expenditure_count, generated_at, created_at)
            VALUES ($1, $2, $3, 'Monthly', 'Generated', $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, NOW(), NOW())
            RETURNING id";

        var insertResult = await _db.ExecuteScalarAsync(sql, ct,
            year, month, string.IsNullOrEmpty(town) ? "全部" : town,
            stats.TotalArchives, stats.NewArchives, stats.StoppedArchives, stats.TotalAmount, 0,
            stats.ClassificationCounts.GetValueOrDefault(ClassificationConstants.RuralSubsistence, 0),
            stats.ClassificationCounts.GetValueOrDefault(ClassificationConstants.UrbanSubsistence, 0),
            stats.ClassificationCounts.GetValueOrDefault(ClassificationConstants.RuralLowIncome, 0),
            stats.ClassificationCounts.GetValueOrDefault(ClassificationConstants.UrbanLowIncome, 0),
            stats.ClassificationCounts.GetValueOrDefault(ClassificationConstants.RuralDestituteScattered, 0) + stats.ClassificationCounts.GetValueOrDefault(ClassificationConstants.RuralDestituteCentralized, 0),
            stats.ClassificationCounts.GetValueOrDefault(ClassificationConstants.UrbanDestituteScattered, 0) + stats.ClassificationCounts.GetValueOrDefault(ClassificationConstants.UrbanDestituteCentralized, 0),
            stats.ClassificationCounts.GetValueOrDefault(ClassificationConstants.RuralRigidExpenditure, 0) + stats.ClassificationCounts.GetValueOrDefault(ClassificationConstants.UrbanRigidExpenditure, 0));

        if (insertResult.IsFailure)
            return Result.Failure<MonthlyReport>(insertResult.ErrorCode!, insertResult.Message!);

        var report = new MonthlyReport
        {
            Id = insertResult.Value,
            Year = year,
            Month = month,
            Town = string.IsNullOrEmpty(town) ? "全部" : town,
            TotalArchives = stats.TotalArchives,
            NewArchives = stats.NewArchives,
            StoppedArchives = stats.StoppedArchives,
            TotalAmount = stats.TotalAmount
        };

        Logger.LogBusiness("生成月报表", ("ReportId", report.Id), ("Year", year), ("Month", month));
        return Result.Success(report);
    }

    public async Task<Result<MonthlyReport>> GetAsync(int year, int month, CancellationToken ct = default)
    {
        var sql = "SELECT * FROM nc_biz_monthly_reports WHERE year = $1 AND month = $2 ORDER BY id DESC LIMIT 1";
        return await _db.QuerySingleAsync<MonthlyReport>(sql, ct, year, month);
    }

    public async Task<Result<List<MonthlyReport>>> GetListAsync(int? year = null, CancellationToken ct = default)
    {
        string sql;
        object[] parameters;
        if (year.HasValue)
        {
            sql = "SELECT * FROM nc_biz_monthly_reports WHERE year = $1 ORDER BY month DESC";
            parameters = new object[] { year.Value };
        }
        else
        {
            sql = "SELECT * FROM nc_biz_monthly_reports ORDER BY year DESC, month DESC";
            parameters = Array.Empty<object>();
        }
        return await _db.QueryAsync<MonthlyReport>(sql, ct, parameters);
    }

    public async Task<Result<bool>> DeleteAsync(int year, int month, CancellationToken ct = default)
    {
        var result = await _db.ExecuteNonQueryAsync(
            "DELETE FROM nc_biz_monthly_reports WHERE year = $1 AND month = $2", ct, year, month);
        if (result.IsFailure)
            return Result.Failure<bool>(result.ErrorCode!, result.Message!);
        Logger.LogBusiness("删除月报历史", ("Year", year), ("Month", month));
        return Result.Success(result.Value > 0);
    }

    public async Task<Result<List<MonthlyReportSummary>>> GetReportHistoryAsync(int? year = null, CancellationToken ct = default)
    {
        string sql;
        object[] parameters;
        if (year.HasValue)
        {
            sql = "SELECT * FROM nc_biz_monthly_reports WHERE year = $1 ORDER BY month DESC LIMIT 1000";
            parameters = new object[] { year.Value };
        }
        else
        {
            sql = "SELECT * FROM nc_biz_monthly_reports ORDER BY year DESC, month DESC LIMIT 1000";
            parameters = Array.Empty<object>();
        }
        return await _db.QueryAsync<MonthlyReportSummary>(sql, ct, parameters);
    }

    public async Task<Result<List<MonthlyReportSummary>>> GetReportHistoryAsync(int? year, int? month, string town, string status, CancellationToken ct = default)
    {
        var conditions = new SqlConditionBuilder()
            .AddIf(year.HasValue, "year = {0}", (object?)year)
            .AddIf(month.HasValue, "month = {0}", (object?)month)
            .AddIf(!string.IsNullOrEmpty(town), "town = {0}", town)
            .AddIf(!string.IsNullOrEmpty(status), "status = {0}", status);

        var sql = $"SELECT * FROM nc_biz_monthly_reports{conditions.ToWhereClause()} ORDER BY year DESC, month DESC LIMIT 1000";
        return await _db.QueryAsync<MonthlyReportSummary>(sql, ct, conditions.GetParameters());
    }

    public async Task<Result<PagedResult<MonthlyReportSummary>>> GetReportHistoryAsync(int? year, int? month, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        var conditions = new SqlConditionBuilder()
            .AddIf(year.HasValue, "year = {0}", (object?)year)
            .AddIf(month.HasValue, "month = {0}", (object?)month);

        var where = conditions.ToWhereClause();
        var countSql = $"SELECT COUNT(*) FROM nc_biz_monthly_reports{where}";
        var querySql = $"SELECT * FROM nc_biz_monthly_reports{where}";

        var countResult = await _db.ExecuteScalarAsync<long>(countSql, ct, conditions.GetParameters());
        if (countResult.IsFailure)
            return Result.Failure<PagedResult<MonthlyReportSummary>>(countResult.ErrorCode!, countResult.Message!);

        var offset = (pageIndex - 1) * pageSize;
        querySql += $" ORDER BY year DESC, month DESC LIMIT ${conditions.ParamCount + 1} OFFSET ${conditions.ParamCount + 2}";
        var pageParams = new List<object?>(conditions.GetParameters()) { pageSize, offset };

        var listResult = await _db.QueryAsync<MonthlyReportSummary>(querySql, ct, pageParams.ToArray());
        if (listResult.IsFailure)
            return Result.Failure<PagedResult<MonthlyReportSummary>>(listResult.ErrorCode!, listResult.Message!);

        return Result.Success(PagedResult<MonthlyReportSummary>.FromList(listResult.Value, pageIndex, pageSize, Convert.ToInt32(countResult.Value)));
    }

    public async Task<Result<PagedResult<MonthlyReportSummary>>> GetSummaryListAsync(int pageIndex, int pageSize, int? year = null, CancellationToken ct = default)
        => await GetReportHistoryAsync(year, null, pageIndex, pageSize, ct);

    // ─────────────────── 统计（基于 nc_biz_applications） ───────────────────

    public async Task<Result<MonthlyStatistics>> GetStatisticsAsync(int year, int month, CancellationToken ct = default)
    {
        LogInfo($"获取统计数据: {year}年{month}月");

        var cycleResult = await GetCycleRangeAsync(year, month, ct);
        if (cycleResult.IsFailure)
            return Result.Failure<MonthlyStatistics>(cycleResult.ErrorCode!, cycleResult.Message!);
        var start = cycleResult.Value.Start;
        var end = cycleResult.Value.End;

        // 口径（2026-08 与用户确认，基准=nc_biz_applications 状态流转）：
        // - 新增 = status=ApplicationStatusCodes.APPROVED 且 original_application_id IS NULL（排除停旧建新接续档案）且 first_approved_at∈周期
        //   （用不可变的首次审批时间，避免编辑/变更把老档案重算成"新增"）
        // - 停保 = status=ApplicationStatusCodes.STOPPED 且 stop_date∈周期
        // - 在保 = status<>ApplicationStatusCodes.STOPPED 且 (stop_date IS NULL OR stop_date>=月末)
        var aggregateSql = $@"SELECT
                COUNT(*) FILTER (WHERE status = '{ApplicationStatusCodes.APPROVED}' AND (stop_date IS NULL OR stop_date >= $3::date)) AS total_archives,
                COUNT(*) FILTER (WHERE status = '{ApplicationStatusCodes.APPROVED}' AND original_application_id IS NULL AND first_approved_at >= $1::timestamp AND first_approved_at < $2::timestamp) AS new_archives,
                COUNT(*) FILTER (WHERE status = '{ApplicationStatusCodes.STOPPED}' AND stop_date >= $1::date AND stop_date < $2::date
                    {StoppedArchiveFilter.NotRebuildContinuationSql("nc_biz_applications")}) AS stopped_archives,
                COALESCE(SUM(total_guarantee_amount) FILTER (WHERE status = '{ApplicationStatusCodes.APPROVED}' AND (stop_date IS NULL OR stop_date >= $3::date)), 0) AS total_amount
            FROM nc_biz_applications
            WHERE deleted_at IS NULL;
";

        // 查询失败必须作为 Failure 向上传播，绝不允许"吞异常返回空集合"：
        // 全 0 的"成功"会被 GenerateAsync 写成一份全 0 的正式月报，且环境问题会被静默掩盖。
        var aggregateResult = await _db.QuerySingleAsync<MonthlyAggregate>(aggregateSql, ct, start, end, end);
        if (aggregateResult.IsFailure || aggregateResult.Value == null)
        {
            LogError($"月报聚合统计查询失败: {aggregateResult.Message}");
            return Result.Failure<MonthlyStatistics>(
                string.IsNullOrEmpty(aggregateResult.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : aggregateResult.ErrorCode,
                aggregateResult.Message ?? "月报聚合统计查询失败");
        }

        var stats = new MonthlyStatistics
        {
            TotalArchives = aggregateResult.Value.TotalArchives,
            NewArchives = aggregateResult.Value.NewArchives,
            StoppedArchives = aggregateResult.Value.StoppedArchives,
            TotalAmount = aggregateResult.Value.TotalAmount
        };
        stats.AverageAmount = stats.TotalArchives > 0 ? stats.TotalAmount / stats.TotalArchives : 0;

        var classificationSql = $@"SELECT classification_result, COUNT(*) AS classification_count, COALESCE(SUM(total_guarantee_amount), 0) AS total_amount
                                  FROM nc_biz_applications
                                  WHERE status = '{ApplicationStatusCodes.APPROVED}' AND (stop_date IS NULL OR stop_date >= $1::date) AND deleted_at IS NULL
                                  GROUP BY classification_result;
";
        var classificationResult = await _db.QueryAsync<ClassificationCount>(classificationSql, ct, end);
        if (classificationResult.IsFailure)
        {
            LogError($"月报分类统计查询失败: {classificationResult.Message}");
            return Result.Failure<MonthlyStatistics>(
                string.IsNullOrEmpty(classificationResult.ErrorCode) ? ErrorCodes.DB_QUERY_ERROR : classificationResult.ErrorCode,
                classificationResult.Message ?? "月报分类统计查询失败");
        }

        foreach (var item in classificationResult.Value ?? new List<ClassificationCount>())
        {
            if (string.IsNullOrEmpty(item.ClassificationResult)) continue;
            stats.ClassificationCounts[item.ClassificationResult] = item.Count;
            stats.ClassificationAmounts[item.ClassificationResult] = item.TotalAmount;
        }

        return Result.Success(stats);
    }

    // ─────────────────── 六张表单数据 ───────────────────

    /// <summary>
    /// 分类过滤白名单：最低生活保障=低保+低保单人；最低生活保障边缘家庭=低保边缘；特困人员=分散/集中供养；刚性支出困难家庭=刚性支出。
    /// 空/null = 不过滤。返回 null 表示不过滤。
    /// </summary>
    private static string[]? CategoryCodes(string? category)
    {
        return category switch
        {
            "最低生活保障" => new[]
            {
                ClassificationConstants.RuralSubsistence, ClassificationConstants.UrbanSubsistence,
                ClassificationConstants.RuralLowIncomeSingle, ClassificationConstants.UrbanLowIncomeSingle
            },
            "最低生活保障边缘家庭" => new[] { ClassificationConstants.RuralLowIncome, ClassificationConstants.UrbanLowIncome },
            "特困人员" => new[]
            {
                ClassificationConstants.RuralDestituteScattered, ClassificationConstants.RuralDestituteCentralized,
                ClassificationConstants.UrbanDestituteScattered, ClassificationConstants.UrbanDestituteCentralized
            },
            "刚性支出困难家庭" => new[]
            {
                ClassificationConstants.RuralRigidExpenditure, ClassificationConstants.UrbanRigidExpenditure
            },
            _ => null
        };
    }

    /// <summary>
    /// 家庭住址（与 AddressResolver.BuildAddress 同口径）：
    /// 详细地址已含镇/村/街道时视为完整地址直接返回（避免重复前缀）；
    /// 否则拼 村/社区 + 详细地址，缺失段跳过、重复段去重。
    /// </summary>
    private static string JoinFullAddress(string? district, string? town, string? community, string? detail)
    {
        var detailTrim = detail?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(detailTrim)
            && (detailTrim.Contains("镇") || detailTrim.Contains("村") || detailTrim.Contains("街道")))
            return detailTrim;

        var parts = new[] { community?.Trim(), detailTrim }
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => p!)
            .ToList();
        var result = string.Empty;
        foreach (var p in parts)
        {
            if (result.Contains(p, StringComparison.Ordinal)) continue;
            result += p;
        }
        return result;
    }

    /// <summary>
    /// 婚姻状况码 → 中文（与 nc_dict_items MaritalStatuses 字典一致）
    /// </summary>
    private static string MapMaritalStatus(string code)
        => code switch
        {
            "Widowed" => "丧偶",
            "Single" => "未婚",
            "Married" => "已婚",
            "Divorced" => "离婚",
            "Other" => "其他",
            _ => code
        };

    /// <summary>
    /// 健康状况码 → 中文（与 FamilyMember.HealthStatusDisplay 一致）
    /// </summary>
    private static string MapHealthStatus(string code)
        => code switch
        {
            "Healthy" => "健康或良好",
            "Weak" => "一般或较弱",
            "SevereIllness" => "重病",
            "SevereDisability" => "重残",
            "SevereIllnessAndDisability" => "重病且重残",
            _ => code
        };

    /// <summary>
    /// 分类施保类型（重病/重残/高龄/未成年，去重顿号连接；与 ClassificationService.CalculateClassifiedSubsidyAsync 的 Types 口径一致）。
    /// 月报「分类施保增加」查询限定为下月满 60 且未享受分类施保的成员，referenceDate 传下月 1 日，避免当前年龄未满 60 漏判高龄。
    /// </summary>
    private static string BuildClassifiedType(List<ClassifiedAddPersonRow> members, DateTime referenceDate)
    {
        var types = new List<string>();
        if (members.Any(m => DictionaryConstants.HealthStatus.HasSevereDisease(m.HealthStatus ?? "")))
            types.Add("重病");
        if (members.Any(m => DictionaryConstants.HealthStatus.HasSevereDisability(m.HealthStatus ?? "")
            || ClassificationConstants.DisabilityLevel.IsSevereForAssistance(
                m.DisabilityLevel ?? "", m.DisabilityType)))
            types.Add("重残");
        if (members.Any(m => AgeAt(m.BirthDate, referenceDate) >= AgeConstants.ELDERLY_THRESHOLD))
            types.Add("高龄");
        if (members.Any(m => AgeAt(m.BirthDate, referenceDate) < AgeConstants.MINOR_THRESHOLD))
            types.Add("未成年");
        return types.Count > 0 ? string.Join("、", types) : "";
    }

    /// <summary>
    /// 指定时点的周岁年龄（不足 1 岁记 0）
    /// </summary>
    private static int AgeAt(DateTime? birth, DateTime at)
    {
        if (!birth.HasValue) return 0;
        var age = at.Year - birth.Value.Year;
        if (at < birth.Value.AddYears(age)) age--;
        return age;
    }

    /// <summary>
    /// 身份证计算周岁（不足 1 岁记 0）
    /// </summary>
    private static string CalcAge(string idCard)
    {
        var birth = IdCardValidator.ExtractBirthDate(idCard);
        if (birth == null) return "";
        var now = DateTime.Today;
        var age = now.Year - birth.Value.Year;
        if (now.Month < birth.Value.Month || (now.Month == birth.Value.Month && now.Day < birth.Value.Day))
            age--;
        return age.ToString();
    }

    /// <summary>
    /// 享受人口实算 SQL 片段：按 nc_biz_family_members 统计（剔除赡养抚养扶养义务人 Support），
    /// 无户主成员记录时补计申请人本人；绝不为 0。与变更模块 LoadFamilyMemberSummaryAsync 同口径。
    /// </summary>
    /// <summary>
    /// 家庭人口计算：优先取主表 family_size（新档案核定享受人口，不含赡养人/非享受补录成员）；
    /// 主表为 0 时回退成员表剔赡养计算（兜底，避免未核定户显示 0）。
    /// </summary>
    private static string MemberCountSql(string alias)
        => $@"CASE WHEN {alias}.family_size > 0 THEN {alias}.family_size
              ELSE COALESCE((
                    SELECT COUNT(*) +
                        CASE WHEN EXISTS (
                            SELECT 1 FROM nc_biz_family_members fh
                            WHERE fh.application_id = {alias}.id AND fh.deleted_at IS NULL
                              AND (fh.is_applicant = true
                                   OR fh.relationship_to_head = 'Head'
                                   OR COALESCE(fh.member_category, '') = ''
                                   OR fh.member_category = 'HouseholdHead')
                        ) THEN 0 ELSE 1 END
                    FROM nc_biz_family_members fm
                    WHERE fm.application_id = {alias}.id AND fm.deleted_at IS NULL
                      AND COALESCE(fm.member_category, '') <> 'Support'
              ), 1) END AS family_size";

    /// <summary>
    /// 按户主身份证批量查询"初次享受日期"：
    /// 导入库建档户（链根 source_type='ImportedArchive'）取链根建档时间 created_at
    /// （= 纳入月份首日；无纳入月份的档案为迁移建档日），其余档案取首次审批时间 first_approved_at。
    /// 一次性查询避免 N+1。
    /// </summary>
    private async Task<Result<Dictionary<string, string>>> LoadApprovalDateMapAsync(IEnumerable<string?> idCards, CancellationToken ct = default)
    {
        var cards = (idCards ?? Enumerable.Empty<string?>())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (cards.Length == 0)
            return Result.Success(new Dictionary<string, string>());

        // 初次享受日期：导入库建档户取链根建档时间（纳入时间），否则取首次审批时间；
        // MAX(first_approved_at) 兜底需在 COALESCE 内（GROUP BY 后逐户聚合）
        var sql = @"SELECT applicant_id_card,
                           TO_CHAR(COALESCE(MIN(created_at) FILTER (WHERE source_type = 'ImportedArchive'),
                                            MAX(first_approved_at)), 'YYYY-MM-DD') AS imported_date
                    FROM nc_biz_applications
                    WHERE applicant_id_card IS NOT NULL AND applicant_id_card <> ''
                      AND applicant_id_card = ANY($1::text[])
                    GROUP BY applicant_id_card";
        // 注意：string[] 必须包一层 object[]，否则会被 params object[] 展开成多个参数，
        // $1 将变成单个字符串而非数组，导致 ANY($1) 解析失败（42809/22P02）
        var result = await _db.QueryAsync<ImportedDateRow>(sql, ct, new object[] { cards });
        if (result.IsFailure)
            return Result.Failure<Dictionary<string, string>>(result.ErrorCode!, result.Message!);

        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in result.Value ?? new List<ImportedDateRow>())
        {
            if (!string.IsNullOrWhiteSpace(item.ApplicantIdCard) && !string.IsNullOrWhiteSpace(item.ImportedDate))
                map[item.ApplicantIdCard] = item.ImportedDate;
        }
        return Result.Success(map);
    }

    public async Task<Result<List<MonthlyAddedRow>>> GetAddedRowsAsync(int year, int month, string town, string? category = null, CancellationToken ct = default)
    {
        var cycle = await GetCycleRangeAsync(year, month, ct);
        if (cycle.IsFailure) return Result.Failure<List<MonthlyAddedRow>>(cycle.ErrorCode!, cycle.Message!);
        var c = cycle.Value;

        var codes = CategoryCodes(category);
        var parameters = new List<object> { c.Start, c.End, town ?? "" };
        var categorySql = string.Empty;
        if (codes != null)
        {
            parameters.Add(codes);
            categorySql = " AND a.classification_result = ANY($4::text[])";
        }

        var sql = $@"SELECT a.applicant_name, a.applicant_id_card, a.gender,
                            {MemberCountSql("a")},
                            a.classification_result, a.first_approved_at AS approval_at,
                            a.district, a.town, a.community, a.address,
                            a.hukou_type, a.application_reason, a.application_reason_detail,
                            a.marital_status, a.health_status, a.bank_account, a.total_family_income,
                            a.work_income_total, a.business_income_total, a.property_income_total,
                            a.transfer_income_total, a.other_income_total, a.per_capita_income,
                            a.total_annual_income, a.per_capita_annual_income,
                            a.rigid_expenditure, a.alimony_income, a.family_land_area,
                            a.land_income_total, a.subsidy_total,
                            a.id AS application_id
                     FROM nc_biz_applications a
                     WHERE a.status = '{ApplicationStatusCodes.APPROVED}' AND a.first_approved_at >= $1 AND a.first_approved_at < $2 AND a.deleted_at IS NULL
                       AND a.original_application_id IS NULL
                       AND ($3 = '' OR a.town = $3)
                       {categorySql}
                       -- 排除停旧建新接续档案（original_application_id 非空），此类属于变更接续非真正新增
                       -- 排除周期内经跨类复核（CategoryAdd）进入的户：此类户由下方跨类新增行体现，避免重复
                       AND NOT EXISTS (
                           SELECT 1 FROM nc_biz_change_records cr
                           WHERE cr.application_id = a.id
                             AND cr.change_type = 'CategoryAdd'
                             AND cr.change_date >= $1::date AND cr.change_date < $2::date
                             AND cr.deleted_at IS NULL
                       )
                     ORDER BY a.first_approved_at ASC";
        var result = await _db.QueryAsync<AddedRowData>(sql, ct, parameters.ToArray());
        if (result.IsFailure)
            return Result.Failure<List<MonthlyAddedRow>>(result.ErrorCode!, result.Message!);

        // 跨类新增行（周期内经跨类复核 CategoryAdd 进入本分类组的户）：
        // 以 cr.change_date 作为审批时间（approval_at 覆盖），new_classification 作为享受分类。
        // 同一申请人（身份证）存在多条 CategoryAdd 时（如停旧建新旧/新档案），只取最新一条，
        // 避免会议记录/新增明细重复（如杨树贵 43/47）。
        var crossParams = new List<object> { c.Start, c.End, town ?? "" };
        var crossCategorySql = string.Empty;
        if (codes != null)
        {
            crossParams.Add(codes);
            crossCategorySql = " AND cr.new_classification = ANY($4::text[])";
        }
        var crossSql = $@"SELECT a.applicant_name, a.applicant_id_card, a.gender,
                            {MemberCountSql("a")},
                            cr.new_classification AS classification_result,
                            cr.change_date::timestamp AS approval_at,
                            a.district, a.town, a.community, a.address,
                            a.hukou_type, a.application_reason, a.application_reason_detail,
                            a.marital_status, a.health_status, a.bank_account, a.total_family_income,
                            a.work_income_total, a.business_income_total, a.property_income_total,
                            a.transfer_income_total, a.other_income_total, a.per_capita_income,
                            a.total_annual_income, a.per_capita_annual_income,
                            a.rigid_expenditure, a.alimony_income, a.family_land_area,
                            a.land_income_total, a.subsidy_total,
                            a.id AS application_id
                     FROM (
                         SELECT cr.id AS change_id,
                                ROW_NUMBER() OVER (PARTITION BY a.applicant_id_card ORDER BY cr.change_date DESC, cr.id DESC) AS rn
                         FROM nc_biz_change_records cr
                         JOIN nc_biz_applications a ON cr.application_id = a.id AND a.deleted_at IS NULL
                         WHERE cr.change_type = 'CategoryAdd'
                           AND cr.change_date >= $1::date AND cr.change_date < $2::date
                           AND cr.deleted_at IS NULL
                           AND ($3 = '' OR a.town = $3)
                           {crossCategorySql}
                     ) t
                     JOIN nc_biz_change_records cr ON cr.id = t.change_id
                     JOIN nc_biz_applications a ON cr.application_id = a.id AND a.deleted_at IS NULL
                     WHERE t.rn = 1";
        var crossResult = await _db.QueryAsync<AddedRowData>(crossSql, ct, crossParams.ToArray());
        if (crossResult.IsFailure)
            return Result.Failure<List<MonthlyAddedRow>>(crossResult.ErrorCode!, crossResult.Message!);

        var rows = result.Value ?? new List<AddedRowData>();
        rows.AddRange(crossResult.Value ?? new List<AddedRowData>());
        var dateMapResult = await LoadApprovalDateMapAsync(rows.Select(r => r.ApplicantIdCard), ct);
        if (dateMapResult.IsFailure)
            return Result.Failure<List<MonthlyAddedRow>>(dateMapResult.ErrorCode!, dateMapResult.Message!);

        var appIds = rows.Select(r => r.ApplicationId ?? 0).Where(i => i > 0).Distinct().ToArray();

        var outRows = new List<MonthlyAddedRow>();
        // 批量加载户主家庭关系（低收入模板新增家庭关系列）
        var relationMapResult = await LoadHeadRelationMapAsync(appIds, ct);
        var relationMap = relationMapResult.IsSuccess ? relationMapResult.Value : new Dictionary<long, string>();
        // 批量加载"家庭情况说明"重建所需的补充数据（病残家庭成员/赡养人/资产计数）
        var sickResult = await LoadSickMembersAsync(appIds, ct);
        if (sickResult.IsFailure)
            return Result.Failure<List<MonthlyAddedRow>>(sickResult.ErrorCode!, sickResult.Message!);
        var supporterResult = await LoadSupporterGroupsAsync(appIds, ct);
        if (supporterResult.IsFailure)
            return Result.Failure<List<MonthlyAddedRow>>(supporterResult.ErrorCode!, supporterResult.Message!);
        var assetResult = await LoadAssetCountsAsync(appIds, ct);
        if (assetResult.IsFailure)
            return Result.Failure<List<MonthlyAddedRow>>(assetResult.ErrorCode!, assetResult.Message!);
        var sickMap = sickResult.Value;
        var supporterMap = supporterResult.Value;
        var assetMap = assetResult.Value;
        foreach (var item in rows)
        {
            var categoryCode = item.ClassificationResult ?? "";
            var appId = item.ApplicationId ?? 0;
            outRows.Add(new MonthlyAddedRow
            {
                ApplicationId = appId,
                AuditDate = item.ApprovalAt?.ToString("yyyy-MM-dd") ?? "",
                Name = item.ApplicantName ?? "",
                IdCard = item.ApplicantIdCard ?? "",
                Gender = item.Gender ?? "",
                Address = JoinFullAddress(item.District, item.Town, item.Community, item.Address),
                Town = item.Town ?? "",
                Community = item.Community ?? "",
                FamilySize = (item.FamilySize ?? 0).ToString(),
                Classification = ClassificationConstants.ConvertToMajorCategoryName(categoryCode),
                CategoryCode = categoryCode,
                IsRural = ClassificationConstants.IsCodeRural(categoryCode),
                ReasonDetail = item.ApplicationReasonDetail ?? "",
                MaritalStatus = MapMaritalStatus(item.MaritalStatus ?? ""),
                HealthStatus = MapHealthStatus(item.HealthStatus ?? ""),
                BankAccount = item.BankAccount ?? "",
                // 年值权威口径优先；无年值列兜底用月值×12（历史数据兼容）
                AnnualIncome = item.TotalAnnualIncome ?? (item.TotalFamilyIncome ?? 0) * 12,
                Age = CalcAge(item.ApplicantIdCard ?? ""),
                Relation = relationMap.TryGetValue(appId, out var rel) && !string.IsNullOrWhiteSpace(rel)
                    ? rel
                    : "户主",
                // 现场重建"家庭情况说明"的数据快照（会议记录每次生成都用最新明细，不用库中旧文本）
                FamilyContext = new FamilySituationContext
                {
                    ApplicantName = item.ApplicantName ?? "",
                    HukouType = MapHukouType(item.HukouType ?? ""),
                    FamilySize = item.FamilySize ?? 0,
                    SickMembers = sickMap.TryGetValue(appId, out var sick) ? sick : new(),
                    Reason = MapApplicationReason(item.ApplicationReason ?? ""),
                    WorkIncomeTotal = item.WorkIncomeTotal ?? 0,
                    BusinessIncomeTotal = item.BusinessIncomeTotal ?? 0,
                    PropertyIncomeTotal = item.PropertyIncomeTotal ?? 0,
                    TransferIncomeTotal = item.TransferIncomeTotal ?? 0,
                    OtherIncomeTotal = item.OtherIncomeTotal ?? 0,
                    AlimonyIncome = item.AlimonyIncome ?? 0,
                    TotalFamilyIncome = item.TotalFamilyIncome ?? 0,
                    PerCapitaIncome = item.PerCapitaIncome ?? 0,
                    TotalAnnualIncome = item.TotalAnnualIncome ?? 0,
                    PerCapitaAnnualIncome = item.PerCapitaAnnualIncome ?? 0,
                    RigidExpenditure = item.RigidExpenditure ?? 0,
                    FamilyLandArea = item.FamilyLandArea ?? 0,
                    LandIncomeTotal = item.LandIncomeTotal ?? 0,
                    SubsidyTotal = item.SubsidyTotal ?? 0,
                    PropertyCount = assetMap.TryGetValue(appId, out var a) ? a.Item1 : 0,
                    VehicleCount = assetMap.TryGetValue(appId, out var b) ? b.Item2 : 0,
                    MachineryCount = assetMap.TryGetValue(appId, out var m) ? m.Item3 : 0,
                    SupporterGroups = supporterMap.TryGetValue(appId, out var g) ? g : new()
                }
            });
        }
        return Result.Success(outRows);
    }

    /// <summary>
    /// 批量查询户主（is_applicant）的家庭关系，一次性查询避免 N+1
    /// </summary>
    private async Task<Result<Dictionary<long, string>>> LoadHeadRelationMapAsync(IEnumerable<long> applicationIds, CancellationToken ct = default)
    {
        var ids = (applicationIds ?? Array.Empty<long>())
            .Where(i => i > 0)
            .Distinct()
            .ToArray();
        var map = new Dictionary<long, string>();
        if (ids.Length == 0)
            return Result.Success(map);
        var result = await _db.QueryAsync<HeadRelationRow>(
            @"SELECT application_id, relationship_to_head
              FROM nc_biz_family_members
              WHERE application_id = ANY($1) AND deleted_at IS NULL AND is_applicant = true",
            ct, ids);
        if (result.IsSuccess && result.Value != null)
        {
            foreach (var r in result.Value)
                if (r.ApplicationId.HasValue && !string.IsNullOrWhiteSpace(r.RelationshipToHead))
                    map[r.ApplicationId.Value] = r.RelationshipToHead;
        }
        return Result.Success(map);
    }

    /// <summary>
    /// 批量加载重病/重残家庭成员（家庭情况说明"家庭成员中…"一句）
    /// </summary>
    private async Task<Result<Dictionary<long, List<(string Name, string Health)>>>> LoadSickMembersAsync(
        long[] applicationIds, CancellationToken ct = default)
    {
        var map = new Dictionary<long, List<(string, string)>>();
        if (applicationIds.Length == 0) return Result.Success(map);
        var result = await _db.QueryAsync<SickMemberRow>(
            @"SELECT application_id, name, health_status
              FROM nc_biz_family_members
              WHERE application_id = ANY($1) AND deleted_at IS NULL
                AND health_status IN ('SevereIllness', 'SevereDisability', 'SevereIllnessAndDisability')",
            ct, applicationIds);
        if (result.IsFailure)
            return Result.Failure<Dictionary<long, List<(string, string)>>>(result.ErrorCode!, result.Message!);
        foreach (var m in result.Value ?? new List<SickMemberRow>())
        {
            if (!m.ApplicationId.HasValue) continue;
            var display = m.HealthStatus switch
            {
                "SevereIllness" => "重病",
                "SevereDisability" => "重残",
                "SevereIllnessAndDisability" => "重病且重残",
                _ => m.HealthStatus ?? ""
            };
            if (!map.TryGetValue(m.ApplicationId.Value, out var list))
                map[m.ApplicationId.Value] = list = new List<(string, string)>();
            list.Add((m.Name ?? "", display));
        }
        return Result.Success(map);
    }

    /// <summary>
    /// 批量加载赡养/抚养/扶养人汇总（按 application_id + person_type 分组，年给付合计）
    /// </summary>
    private async Task<Result<Dictionary<long, List<(string Type, int Count, decimal Fee)>>>> LoadSupporterGroupsAsync(
        long[] applicationIds, CancellationToken ct = default)
    {
        var map = new Dictionary<long, List<(string, int, decimal)>>();
        if (applicationIds.Length == 0) return Result.Success(map);
        var result = await _db.QueryAsync<SupporterGroupRow>(
            @"SELECT application_id, person_type, COUNT(*) AS cnt, SUM(annual_support_fee) AS total_fee
              FROM nc_biz_supporters
              WHERE application_id = ANY($1) AND deleted_at IS NULL
              GROUP BY application_id, person_type",
            ct, applicationIds);
        if (result.IsFailure)
            return Result.Failure<Dictionary<long, List<(string, int, decimal)>>>(result.ErrorCode!, result.Message!);
        foreach (var r in result.Value ?? new List<SupporterGroupRow>())
        {
            if (!r.ApplicationId.HasValue) continue;
            if (!map.TryGetValue(r.ApplicationId.Value, out var list))
                map[r.ApplicationId.Value] = list = new List<(string, int, decimal)>();
            list.Add((r.PersonType ?? "", (int)(r.Cnt ?? 0), r.TotalFee ?? 0));
        }

        // 兜底：nc_biz_supporters 无记录的存量户，取家庭成员年资助费（annual_support_fee>0），按"赡养人"归组
        var missingIds = applicationIds.Where(id => !map.ContainsKey(id)).ToArray();
        if (missingIds.Length > 0)
        {
            var fallback = await _db.QueryAsync<SupporterGroupRow>(
                @"SELECT x.application_id, '赡养' AS person_type, COUNT(*) AS cnt, SUM(x.fee) AS total_fee
                  FROM (
                      SELECT application_id, annual_support_fee AS fee
                      FROM nc_biz_family_members
                      WHERE application_id = ANY($1) AND deleted_at IS NULL AND annual_support_fee > 0
                  ) x
                  GROUP BY x.application_id",
                ct, missingIds);
            if (fallback.IsFailure)
                return Result.Failure<Dictionary<long, List<(string, int, decimal)>>>(fallback.ErrorCode!, fallback.Message!);
            foreach (var r in fallback.Value ?? new List<SupporterGroupRow>())
            {
                if (!r.ApplicationId.HasValue || map.ContainsKey(r.ApplicationId.Value)) continue;
                map[r.ApplicationId.Value] = new List<(string, int, decimal)> { (r.PersonType ?? "", (int)(r.Cnt ?? 0), r.TotalFee ?? 0) };
            }
        }
        return Result.Success(map);
    }

    /// <summary>
    /// 批量统计房产/车辆/农机具数量（家庭情况说明资产描述一句）
    /// </summary>
    private async Task<Result<Dictionary<long, (int Props, int Vehs, int Macs)>>> LoadAssetCountsAsync(
        long[] applicationIds, CancellationToken ct = default)
    {
        var map = new Dictionary<long, (int, int, int)>();
        if (applicationIds.Length == 0) return Result.Success(map);
        async Task<Result<Dictionary<long, int>>> CountAsync(string table)
        {
            // 表名来自下方白名单常量，禁止传参
            var r = await _db.QueryAsync<AssetCountRow>(
                $"SELECT application_id, COUNT(*) AS cnt FROM {table} WHERE application_id = ANY($1) AND deleted_at IS NULL GROUP BY application_id",
                ct, applicationIds);
            if (r.IsFailure)
                return Result.Failure<Dictionary<long, int>>(r.ErrorCode!, r.Message!);
            var m = new Dictionary<long, int>();
            foreach (var row in r.Value ?? new List<AssetCountRow>())
                if (row.ApplicationId.HasValue)
                    m[row.ApplicationId.Value] = (int)(row.Cnt ?? 0);
            return Result.Success(m);
        }

        var props = await CountAsync("nc_biz_properties");
        if (props.IsFailure) return Result.Failure<Dictionary<long, (int, int, int)>>(props.ErrorCode!, props.Message!);
        var vehs = await CountAsync("nc_biz_vehicles");
        if (vehs.IsFailure) return Result.Failure<Dictionary<long, (int, int, int)>>(vehs.ErrorCode!, vehs.Message!);
        var macs = await CountAsync("nc_biz_machineries");
        if (macs.IsFailure) return Result.Failure<Dictionary<long, (int, int, int)>>(macs.ErrorCode!, macs.Message!);

        foreach (var id in applicationIds)
        {
            map[id] = (props.Value.TryGetValue(id, out var p) ? p : 0,
                       vehs.Value.TryGetValue(id, out var v) ? v : 0,
                       macs.Value.TryGetValue(id, out var m) ? m : 0);
        }
        return Result.Success(map);
    }

    /// <summary>户口类型码→显示名（家庭情况说明用）</summary>
    private static string MapHukouType(string code) => code switch
    {
        "Rural" => "农村户口",
        "Urban" => "城市户口",
        _ => code
    };

    /// <summary>申请原因码→字典显示名（家庭情况说明"因…导致"一句用）</summary>
    private static string MapApplicationReason(string code) => code switch
    {
        "Illness" => "因病",
        "Disaster" => "因灾",
        "Disability" => "因残",
        "Education" => "因学",
        "LowIncome" => "收入低",
        "Unemployment" => "失业",
        "LandLoss" => "失地",
        "Accident" => "因意外事故",
        "Other" => "其他",
        _ => code
    };

    public async Task<Result<List<MonthlyStoppedRow>>> GetStoppedRowsAsync(int year, int month, string town, string? category = null, CancellationToken ct = default)
    {
        var cycle = await GetCycleRangeAsync(year, month, ct);
        if (cycle.IsFailure) return Result.Failure<List<MonthlyStoppedRow>>(cycle.ErrorCode!, cycle.Message!);
        var c = cycle.Value;

        var codes = CategoryCodes(category);
        var parameters = new List<object> { c.Start, c.End, town ?? "" };
        var categorySql = string.Empty;
        if (codes != null)
        {
            parameters.Add(codes);
            categorySql = " AND a.classification_result = ANY($4::text[])";
        }

        // 停保日期在 nc_biz_applications.stop_date；初次享受日期：导入库建档户取纳入时间，其余取首次审批时间（LoadApprovalDateMapAsync）
        // 户月保障金=household_monthly_guarantee_amount（不含分类施保），分类施保独立列
        var sql = $@"SELECT a.applicant_name AS head_name, a.applicant_id_card AS head_id_card,
                            {MemberCountSql("a")},
                            a.address, a.classification_result,
                            a.household_monthly_guarantee_amount AS total_guarantee_amount,
                            a.classified_subsidy_amount,
                            COALESCE(a.stop_reason, '') AS stop_reason, a.stop_date, a.first_approved_at,
                            a.district, a.town, a.community
                     FROM nc_biz_applications a
                     WHERE a.status = '{ApplicationStatusCodes.STOPPED}' AND a.stop_date >= $1 AND a.stop_date < $2 AND a.deleted_at IS NULL
                       AND ($3 = '' OR a.town = $3)
                       {categorySql}
                       -- 停旧建新接续（同大类继续享受，如成员变更/同类别复核/户主变更）不算停保
                       {StoppedArchiveFilter.NotRebuildContinuationSql("a")}
                     ORDER BY a.stop_date ASC, a.first_approved_at DESC";
        var result = await _db.QueryAsync<StoppedRowData>(sql, ct, parameters.ToArray());
        if (result.IsFailure)
            return Result.Failure<List<MonthlyStoppedRow>>(result.ErrorCode!, result.Message!);

        // 跨类停止行（周期内经跨类复核 CategoryStop 离开本分类组的户）：
        // 旧分类作为享受分类（classification_result 覆盖），change_date 作为停保日期。
        var crossParams = new List<object> { c.Start, c.End, town ?? "" };
        var crossCategorySql = string.Empty;
        if (codes != null)
        {
            crossParams.Add(codes);
            crossCategorySql = " AND cr.old_classification = ANY($4::text[])";
        }
        var crossSql = $@"SELECT a.applicant_name AS head_name, a.applicant_id_card AS head_id_card,
                            {MemberCountSql("a")},
                            a.address, cr.old_classification AS classification_result,
                            cr.old_guarantee_amount AS total_guarantee_amount,
                            0 AS classified_subsidy_amount,
                            cr.change_reason AS stop_reason, cr.change_date AS stop_date, a.first_approved_at,
                            a.district, a.town, a.community,
                            a.source_table, a.source_id
                     FROM nc_biz_change_records cr
                     JOIN nc_biz_applications a ON cr.application_id = a.id AND a.deleted_at IS NULL
                     WHERE cr.change_type = 'CategoryStop'
                       AND cr.change_date >= $1::date AND cr.change_date < $2::date
                       AND cr.deleted_at IS NULL
                       -- 停保状态（Stopped）的档案由普通停保行显示（含户月保障金/分类施保），
                       -- 排除跨类 CategoryStop 行，避免停旧建新户重复（解维平 44）
                       AND a.status <> '{ApplicationStatusCodes.STOPPED}'
                       AND ($3 = '' OR a.town = $3)
                       {crossCategorySql}";
        var crossResult = await _db.QueryAsync<StoppedRowData>(crossSql, ct, crossParams.ToArray());
        if (crossResult.IsFailure)
            return Result.Failure<List<MonthlyStoppedRow>>(crossResult.ErrorCode!, crossResult.Message!);

        var rows = result.Value ?? new List<StoppedRowData>();
        var crossRows = crossResult.Value ?? new List<StoppedRowData>();
        // 跨类停止行：批量追溯导入库（农村/城市低保家庭表）旧户月保障金 + 分类施保
        var importedMapResult = await LoadImportedStopAmountMapAsync(
            crossRows.Where(r => r.SourceId.HasValue && r.SourceId.Value > 0)
                     .Select(r => (r.SourceTable ?? "", r.SourceId!.Value)),
            ct);
        if (importedMapResult.IsFailure)
            return Result.Failure<List<MonthlyStoppedRow>>(importedMapResult.ErrorCode!, importedMapResult.Message!);
        var importedMap = importedMapResult.Value;
        foreach (var r in crossRows)
        {
            if (r.SourceId.HasValue
                && importedMap.TryGetValue((r.SourceTable ?? "", r.SourceId.Value), out var oldAmount))
            {
                // 户月保障金 = 导入库 monthly_guarantee_amount；分类施保 = 家庭分类施保 + 人员分类施保
                r.TotalGuaranteeAmount = oldAmount.Monthly;
                r.ClassifiedSubsidyAmount = oldAmount.Classified;
            }
        }
        rows.AddRange(crossRows);
        // 跨类停止行按停保日期排序（与现有停保行排序一致）
        rows = rows.OrderBy(r => r.StopDate).ThenByDescending(r => r.FirstApprovedAt).ToList();

        // 最低生活保障边缘家庭"停止、减员汇总表"：追加减员行
        // （成员变更减员 / 成员死亡；change_date∈周期，旧分类属低保边缘家庭）
        var reductionRows = new List<MemberReductionRowData>();
        if (string.Equals(category, "最低生活保障边缘家庭", StringComparison.Ordinal))
        {
            var reductionResult = await QueryMemberReductionRowsAsync(c, town, ct);
            if (reductionResult.IsFailure)
                return Result.Failure<List<MonthlyStoppedRow>>(reductionResult.ErrorCode!, reductionResult.Message!);
            reductionRows = reductionResult.Value;
        }

        var dateMapResult = await LoadApprovalDateMapAsync(
            rows.Select(r => r.HeadIdCard).Concat(reductionRows.Select(r => r.HeadIdCard)), ct);
        if (dateMapResult.IsFailure)
            return Result.Failure<List<MonthlyStoppedRow>>(dateMapResult.ErrorCode!, dateMapResult.Message!);
        var dateMap = dateMapResult.Value;
        var cycleEnd = c.End.AddDays(-1); // 周期末日（15 日）

        var outRows = new List<MonthlyStoppedRow>();
        foreach (var item in rows)
        {
            var birth = string.IsNullOrEmpty(item.HeadIdCard)
                ? ""
                : IdCardValidator.ExtractBirthDate(item.HeadIdCard)?.ToString("yyyy-MM-dd") ?? "";
            var categoryCode = item.ClassificationResult ?? "";
            var display = ClassificationConstants.ConvertToMajorCategoryName(categoryCode);
            var enjoy = dateMap.TryGetValue(item.HeadIdCard ?? "", out var dt) ? dt : cycleEnd.ToString("yyyy-MM-dd");
            outRows.Add(new MonthlyStoppedRow
            {
                HeadName = item.HeadName ?? "",
                HeadIdCard = item.HeadIdCard ?? "",
                HeadBirth = birth,
                FamilySize = (item.FamilySize ?? 0).ToString(),
                Address = JoinFullAddress(item.District, item.Town, item.Community, item.Address),
                Category = display,
                // 分类施保列=户分类施保金额（贺传波等停保户=208.00）
                Classification = (item.ClassifiedSubsidyAmount ?? 0).ToString("F2"),
                MonthAmount = (item.TotalGuaranteeAmount ?? 0).ToString("F2"),
                StopReason = item.StopReason ?? "",
                StopDate = item.StopDate?.ToString("yyyy-MM-dd") ?? "",
                EnjoyDate = enjoy,
                CategoryCode = categoryCode,
                BizType = "停止",
                IsRural = ClassificationConstants.IsCodeRural(categoryCode)
            });
        }

        // 减员行：户主/身份证/住址取变更所在档案；家庭人口=变更后（After 快照）；
        // 停止时间=变更日期；原因=减员成员「姓名（原因）」，死亡附死亡时间
        foreach (var item in reductionRows)
        {
            var categoryCode = item.OldClassification ?? "";
            var enjoy = !string.IsNullOrEmpty(item.HeadIdCard) && dateMap.TryGetValue(item.HeadIdCard!, out var dt)
                ? dt
                : cycleEnd.ToString("yyyy-MM-dd");
            outRows.Add(new MonthlyStoppedRow
            {
                HeadName = item.HeadName ?? "",
                HeadIdCard = item.HeadIdCard ?? "",
                HeadBirth = string.IsNullOrEmpty(item.HeadIdCard)
                    ? ""
                    : IdCardValidator.ExtractBirthDate(item.HeadIdCard)?.ToString("yyyy-MM-dd") ?? "",
                FamilySize = item.FamilySize?.ToString() ?? "",
                Address = JoinFullAddress(item.District, item.Town, item.Community, item.Address),
                Category = ClassificationConstants.ConvertToMajorCategoryName(categoryCode),
                Classification = "",
                MonthAmount = "",
                StopReason = BuildReductionReasonText(item),
                StopDate = item.ChangeDate?.ToString("yyyy-MM-dd") ?? "",
                EnjoyDate = enjoy,
                CategoryCode = categoryCode,
                BizType = "减员",
                IsRural = ClassificationConstants.IsCodeRural(categoryCode)
            });
        }

        return Result.Success(outRows);
    }

    /// <summary>
    /// 成员减员行数据（边缘家庭停止、减员汇总表）：成员变更减员 / 成员死亡变更记录，
    /// change_date∈周期，旧分类属于最低生活保障边缘家庭；家庭人口取变更后（After 快照）。
    /// </summary>
    private async Task<Result<List<MemberReductionRowData>>> QueryMemberReductionRowsAsync(
        MonthlyCycleRange c, string? town, CancellationToken ct)
    {
        var codes = CategoryCodes("最低生活保障边缘家庭");
        if (codes == null)
            return Result.Success(new List<MemberReductionRowData>());

        var sql = @"SELECT cr.id AS change_id, cr.application_id, cr.change_date, cr.old_classification,
                           a.applicant_name AS head_name, a.applicant_id_card AS head_id_card,
                           COALESCE((SELECT s.snapshot_data->>'FamilySize'
                                     FROM nc_biz_change_snapshots s
                                     WHERE s.change_id = cr.id AND s.snapshot_type = 'After'
                                     LIMIT 1), a.family_size::text)::int AS family_size,
                           a.district, a.town, a.community, a.address,
                           COALESCE((SELECT string_agg(d.field_label || '|' || COALESCE(d.new_value, ''), E'\n' ORDER BY d.id)
                                     FROM nc_biz_change_details d
                                     WHERE d.change_id = cr.id AND d.field_name IN ('MemberRemove','MemberDeath')), '') AS member_detail,
                           COALESCE((SELECT string_agg(dr.member_name || '|' || TO_CHAR(dr.death_date, 'YYYY-MM-DD'), E'\n')
                                     FROM nc_biz_death_records dr
                                     WHERE dr.application_id = cr.application_id), '') AS death_info,
                           COALESCE(cr.change_reason, '') AS fallback_reason
                    FROM nc_biz_change_records cr
                    JOIN nc_biz_applications a ON a.id = cr.application_id AND a.deleted_at IS NULL
                    WHERE cr.deleted_at IS NULL
                      AND cr.change_type IN ('MemberRemove','MemberDeath')
                      AND cr.change_date >= $1::date AND cr.change_date < $2::date
                      AND ($3 = '' OR a.town = $3)
                      AND cr.old_classification = ANY($4::text[])
                    ORDER BY cr.change_date, cr.id";
        // string[] 必须包一层 object[]，否则会被 params object[] 展开（同 LoadApprovalDateMapAsync 注释）
        var result = await _db.QueryAsync<MemberReductionRowData>(sql, ct,
            new object[] { c.Start, c.End, town ?? string.Empty, codes });
        if (result.IsFailure)
            return Result.Failure<List<MemberReductionRowData>>(result.ErrorCode!, result.Message!);
        return Result.Success(result.Value ?? new List<MemberReductionRowData>());
    }

    /// <summary>
    /// 减员原因文本：每人「姓名（原因）」，多人用"、"；
    /// 死亡写「姓名（人员死亡，死亡时间：yyyy-MM-dd）」（死亡时间优先取死亡记录，缺则明细事由日期）。
    /// 明细缺失的历史记录用死亡记录兜底，再兜底变更原因原文。
    /// </summary>
    private static string BuildReductionReasonText(MemberReductionRowData row)
    {
        var deathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in (row.DeathInfo ?? string.Empty)
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var seg = line.Split('|');
            if (seg.Length >= 2 && !string.IsNullOrWhiteSpace(seg[0]))
                deathMap[seg[0].Trim()] = seg[1].Trim();
        }

        var parts = new List<string>();
        foreach (var line in (row.MemberDetail ?? string.Empty)
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var seg = line.Split('|');
            var name = seg.Length > 0 ? seg[0].Trim() : string.Empty;
            var reasonName = seg.Length > 1 ? seg[1].Trim() : string.Empty;
            var date = seg.Length > 2 ? seg[2].Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(name)) continue;

            var isDeath = string.Equals(reasonName,
                              MemberChangeReasonConstants.GetRemoveName(MemberChangeReasonConstants.RemoveDeath),
                              StringComparison.Ordinal)
                          || reasonName.Contains("死亡", StringComparison.Ordinal);
            if (isDeath)
            {
                if (deathMap.TryGetValue(name, out var deathDate) && !string.IsNullOrWhiteSpace(deathDate))
                    date = deathDate;
                parts.Add(string.IsNullOrWhiteSpace(date)
                    ? $"{name}（人员死亡）"
                    : $"{name}（人员死亡，死亡时间：{date}）");
            }
            else
            {
                parts.Add(string.IsNullOrWhiteSpace(reasonName) ? name : $"{name}（{reasonName}）");
            }
        }

        // 历史记录无明细：用死亡记录兜底
        if (parts.Count == 0)
        {
            parts.AddRange(deathMap.Select(kv => $"{kv.Key}（人员死亡，死亡时间：{kv.Value}）"));
        }

        return parts.Count > 0 ? string.Join("、", parts) : row.FallbackReason ?? string.Empty;
    }

    public async Task<Result<List<MonthlyAmountChangeRow>>> GetIncreaseRowsAsync(int year, int month, string town, string? category = null, CancellationToken ct = default)
        => await GetAmountChangeRowsAsync(year, month, town, increase: true, category, ct);

    public async Task<Result<List<MonthlyAmountChangeRow>>> GetDecreaseRowsAsync(int year, int month, string town, string? category = null, CancellationToken ct = default)
        => await GetAmountChangeRowsAsync(year, month, town, increase: false, category, ct);

    private async Task<Result<List<MonthlyAmountChangeRow>>> GetAmountChangeRowsAsync(int year, int month, string town, bool increase, string? category = null, CancellationToken ct = default)
    {
        var cycle = await GetCycleRangeAsync(year, month, ct);
        if (cycle.IsFailure) return Result.Failure<List<MonthlyAmountChangeRow>>(cycle.ErrorCode!, cycle.Message!);
        var c = cycle.Value;

        // 增发/减发业务上仅面向最低生活保障；CategoryCodes 兜底解析
        var codes = CategoryCodes(string.IsNullOrEmpty(category) ? "最低生活保障" : category);
        var parameters = new List<object> { c.Start, c.End, town ?? "" };
        var categorySql = string.Empty;
        if (codes != null)
        {
            parameters.Add(codes);
            categorySql = " AND a.classification_result = ANY($4::text[])";
        }

        // 增减方向仅由布尔分支硬编码，不拼接外部输入 → 无 SQL 注入
        var directionSql = increase
            ? "cr.new_guarantee_amount > cr.old_guarantee_amount"
            : "cr.new_guarantee_amount < cr.old_guarantee_amount";

        var sql = $@"SELECT cr.id AS change_id, a.applicant_name AS head_name, a.applicant_id_card AS head_id_card,
                        {MemberCountSql("a")},
                        a.address, a.classification_result, a.classified_subsidy_amount, cr.change_date,
                        cr.old_guarantee_amount, cr.new_guarantee_amount, cr.change_reason,
                        cr.change_reason_type,
                        a.district, a.town, a.community
                     FROM nc_biz_change_records cr
                     JOIN nc_biz_applications a ON cr.application_id = a.id
                     WHERE cr.change_type = 'FundChange'
                       AND cr.old_guarantee_amount IS DISTINCT FROM cr.new_guarantee_amount
                       AND cr.changed_at >= $1 AND cr.changed_at < $2
                       AND a.deleted_at IS NULL
                       AND cr.deleted_at IS NULL
                       -- 增发/减发仅统计同类型变更（低保→低保等）；跨类（如低保→低收入/特困）走停保+新增明细，不在此表
                       AND cr.old_classification IS NOT DISTINCT FROM cr.new_classification
                       AND ({directionSql})
                       AND ($3 = '' OR a.town = $3)
                       {categorySql}
                     ORDER BY cr.changed_at ASC";

        var result = await _db.QueryAsync<AmountChangeRow>(sql, ct, parameters.ToArray());
        if (result.IsFailure)
            return Result.Failure<List<MonthlyAmountChangeRow>>(result.ErrorCode!, result.Message!);

        var rows = result.Value ?? new List<AmountChangeRow>();
        // 减发人口数=变更前后快照 FamilySize 差（批量加载避免 N+1）
        var diffMapResult = await LoadFamilySizeDiffMapAsync(rows.Select(r => r.ChangeId), ct);
        var diffMap = diffMapResult.IsSuccess ? diffMapResult.Value : new Dictionary<long, int>();
        var dateMapResult = await LoadApprovalDateMapAsync(rows.Select(r => r.HeadIdCard), ct);
        if (dateMapResult.IsFailure)
            return Result.Failure<List<MonthlyAmountChangeRow>>(dateMapResult.ErrorCode!, dateMapResult.Message!);
        var dateMap = dateMapResult.Value;
        // 死亡人员姓名 + 原家庭人口（从该户死亡/变更 Before 快照批量查，减发理由/原家庭人口用）
        var deceasedMapResult = await LoadDeceasedInfoMapAsync(rows.Select(r => r.HeadIdCard), ct);
        if (deceasedMapResult.IsFailure)
            return Result.Failure<List<MonthlyAmountChangeRow>>(deceasedMapResult.ErrorCode!, deceasedMapResult.Message!);
        var deceasedMap = deceasedMapResult.Value;
        var cycleEnd = c.End.AddDays(-1);

        var outRows = new List<MonthlyAmountChangeRow>();
        foreach (var item in rows)
        {
            var oldA = item.OldGuaranteeAmount ?? 0;
            var newA = item.NewGuaranteeAmount ?? 0;
            var changeAmt = Math.Abs(newA - oldA);
            var birth = string.IsNullOrEmpty(item.HeadIdCard)
                ? ""
                : IdCardValidator.ExtractBirthDate(item.HeadIdCard)?.ToString("yyyy-MM-dd") ?? "";
            var categoryCode = item.ClassificationResult ?? "";
            var display = ClassificationConstants.ConvertToMajorCategoryName(categoryCode);
            var enjoy = dateMap.TryGetValue(item.HeadIdCard ?? "", out var dt) ? dt : cycleEnd.ToString("yyyy-MM-dd");
            outRows.Add(new MonthlyAmountChangeRow
            {
                HeadName = item.HeadName ?? "",
                HeadBirth = birth,
                FamilySize = (item.FamilySize ?? 0).ToString(),
                Address = JoinFullAddress(item.District, item.Town, item.Community, item.Address),
                Category = display,
                EnjoyDate = enjoy,
                OldAmount = oldA.ToString("F2"),
                NewAmount = newA.ToString("F2"),
                Classification = display,
                ChangeAmount = changeAmt.ToString("F2"),
                // 减发人口=快照差；现分类施保=户当前分类施保金额（减发表专用列）
                DecreasePerson = item.ChangeId.HasValue && diffMap.TryGetValue(item.ChangeId.Value, out var d) ? d : 0,
                ClassifiedSubsidyAmount = item.ClassifiedSubsidyAmount ?? 0,
                // 减发理由按变更类型区分：经济复核=收入变化（增发=收入减少/减发=收入增加）；
                // 有死亡成员的减发，组合"家庭人员死亡，死亡人员{姓名}，经过经济复核收入增加"；
                // 户主死亡=成员死亡；其他/空保留原文
                Reason = item.ChangeReasonType switch
                {
                    ChangeReasonTypeConstants.EconomicReview when !increase
                        && deceasedMap.TryGetValue(item.HeadIdCard ?? "", out var di) && !string.IsNullOrEmpty(di.Names)
                        => $"家庭人员死亡，死亡人员{di.Names}，经过经济复核收入增加",
                    ChangeReasonTypeConstants.EconomicReview => increase ? "收入减少" : "收入增加",
                    ChangeReasonTypeConstants.HeadDeceased => "成员死亡",
                    var other => string.IsNullOrWhiteSpace(other) ? item.ChangeReason ?? "" : other
                },
                // 原家庭人口：从老档案（死亡前 Before 快照）计算，含死亡成员；无则用当前剔除赡养人口
                OldFamilySize = deceasedMap.TryGetValue(item.HeadIdCard ?? "", out var di2) && di2.OldFamilySize > 0
                    ? di2.OldFamilySize.ToString()
                    : (item.FamilySize ?? 0).ToString(),
                CategoryCode = categoryCode,
                IsRural = ClassificationConstants.IsCodeRural(categoryCode)
            });
        }
        return Result.Success(outRows);
    }

    /// <summary>
    /// 批量加载变更前后人口快照（nc_biz_change_snapshots.Before/After 的 FamilySize 差），
    /// 用于保障金减发表的"减发人口数"列；无快照或差≤0 视为减少 0 人。
    /// </summary>
    private async Task<Result<Dictionary<long, int>>> LoadFamilySizeDiffMapAsync(IEnumerable<long?> changeIds, CancellationToken ct = default)
    {
        var ids = (changeIds ?? Enumerable.Empty<long?>())
            .Where(i => i.HasValue && i.Value > 0)
            .Select(i => i!.Value)
            .Distinct()
            .ToArray();
        var map = new Dictionary<long, int>();
        if (ids.Length == 0)
            return Result.Success(map);

        var sql = @"SELECT change_id, snapshot_type, snapshot_data
                    FROM nc_biz_change_snapshots
                    WHERE change_id = ANY($1) AND snapshot_type IN ('Before','After')";
        var result = await _db.QueryAsync<ChangeSnapshotRow>(sql, ct, ids);
        if (result.IsFailure)
            return Result.Failure<Dictionary<long, int>>(result.ErrorCode!, result.Message!);

        var beforeByChange = new Dictionary<long, int>();
        var afterByChange = new Dictionary<long, int>();
        foreach (var row in result.Value ?? new List<ChangeSnapshotRow>())
        {
            var size = TryParseFamilySize(row.SnapshotData);
            if (row.SnapshotType == "Before")
                beforeByChange[row.ChangeId] = size;
            else if (row.SnapshotType == "After")
                afterByChange[row.ChangeId] = size;
        }
        foreach (var id in ids)
        {
            if (beforeByChange.TryGetValue(id, out var before) && afterByChange.TryGetValue(id, out var after))
                map[id] = Math.Max(0, before - after);
        }
        return Result.Success(map);
    }

    /// <summary>
    /// 从快照 JSON 提取 FamilySize（快照字段可能存在缺失/更名，解析失败返回 0，全部失败不影响主流程）
    /// </summary>
    private static int TryParseFamilySize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return 0;
        try
        {
            using var doc = global::System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("FamilySize", out var el) && el.ValueKind == global::System.Text.Json.JsonValueKind.Number)
                return el.GetInt32();
        }
        catch (global::System.Text.Json.JsonException)
        {
            // 快照解析失败不计入减发人口（保持 0）
        }
        return 0;
    }

    private class ChangeSnapshotRow
    {
        public long ChangeId { get; set; }
        public string? SnapshotType { get; set; }
        public string? SnapshotData { get; set; }
    }

    /// <summary>
    /// 该户死亡/变更信息（减发理由/原家庭人口用）
    /// </summary>
    private sealed class DeceasedInfo
    {
        public string Names { get; set; } = string.Empty;      // 死亡人员姓名（顿号连接，去重）
        public int OldFamilySize { get; set; }                  // 原家庭人口（死亡前 Before 快照 FamilySize，含死亡成员）
    }

    /// <summary>
    /// 按户主身份证批量加载死亡人员姓名与原家庭人口：
    /// 从 HouseholdDeath/MemberDeath 变更的 Before 快照取 DeceasedHeadName/DeceasedName/FamilySize。
    /// 用于减发表"减发成员理由"（家庭人员死亡，死亡人员XXX…）与"原家庭人口"（从老数据档案计算，含死亡成员）。
    /// </summary>
    private async Task<Result<Dictionary<string, DeceasedInfo>>> LoadDeceasedInfoMapAsync(IEnumerable<string?> idCards, CancellationToken ct = default)
    {
        var cards = (idCards ?? Enumerable.Empty<string?>())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var map = new Dictionary<string, DeceasedInfo>(StringComparer.OrdinalIgnoreCase);
        if (cards.Length == 0)
            return Result.Success(map);

        var sql = @"SELECT a.applicant_id_card AS head_id_card,
                           COALESCE(after_cs.snapshot_data->>'DeceasedHeadName', after_cs.snapshot_data->>'DeceasedName') AS deceased_name,
                           (before_cs.snapshot_data->>'FamilySize')::int AS old_family_size,
                           cr.change_date
                    FROM nc_biz_change_records cr
                    LEFT JOIN nc_biz_change_snapshots before_cs ON before_cs.change_id = cr.id AND before_cs.snapshot_type = 'Before'
                    LEFT JOIN nc_biz_change_snapshots after_cs ON after_cs.change_id = cr.id AND after_cs.snapshot_type = 'After'
                    JOIN nc_biz_applications a ON cr.application_id = a.id AND a.deleted_at IS NULL
                    WHERE cr.change_type IN ('HouseholdDeath','MemberDeath')
                      AND cr.deleted_at IS NULL
                      AND a.applicant_id_card = ANY($1::text[])
                      AND COALESCE(after_cs.snapshot_data->>'DeceasedHeadName', after_cs.snapshot_data->>'DeceasedName') IS NOT NULL
                      AND COALESCE(after_cs.snapshot_data->>'DeceasedHeadName', after_cs.snapshot_data->>'DeceasedName') <> ''
                    ORDER BY cr.change_date DESC, cr.id DESC";
        var result = await _db.QueryAsync<DeceasedInfoRow>(sql, ct, new object[] { cards });
        if (result.IsFailure)
            return Result.Failure<Dictionary<string, DeceasedInfo>>(result.ErrorCode!, result.Message!);

        foreach (var row in result.Value ?? new List<DeceasedInfoRow>())
        {
            if (string.IsNullOrEmpty(row.HeadIdCard)) continue;
            if (!map.TryGetValue(row.HeadIdCard!, out var info))
            {
                // 首条（最近一次死亡）的原家庭人口作为基准；多条死亡合并姓名
                info = new DeceasedInfo { OldFamilySize = row.OldFamilySize };
                map[row.HeadIdCard!] = info;
            }
            if (!string.IsNullOrEmpty(row.DeceasedName)
                && !info.Names.Split('、', StringSplitOptions.RemoveEmptyEntries).Contains(row.DeceasedName, StringComparer.Ordinal))
            {
                if (info.Names.Length > 0) info.Names += "、";
                info.Names += row.DeceasedName;
            }
        }
        return Result.Success(map);
    }

    private class DeceasedInfoRow
    {
        public string? HeadIdCard { get; set; }
        public string? DeceasedName { get; set; }
        public int OldFamilySize { get; set; }
        public DateTime? ChangeDate { get; set; }
    }

    /// <summary>
    /// 分类施保金减发（低保导入库）：农村/城市低保导入表 person_classified_amount&gt;0 的户，
    /// 户内本月满 18 周岁成员（含户主）每人一行——即 18 周岁生日落在本月自然月
    /// [本月1日, 下月1日) 的成员。享受日期=纳入时间（imported_at）。
    /// 减发金额=人员分类施保金额；原/现分类施保金=0。
    /// </summary>
    public async Task<Result<List<MonthlyShiBaoRow>>> GetShiBaoReductionRowsAsync(int year, int month, CancellationToken ct = default)
    {
        var monthStart = new DateTime(year, month, 1);
        var monthEnd = monthStart.AddMonths(1); // 下月1号，不在本区间内
        var monthLast = monthEnd.AddDays(-1);    // 本月末日

        var sql = @"SELECT '最低生活保障对象' AS category, applicant_name, applicant_id_card,
                            member_names, member_id_cards,
                            COALESCE(person_classified_amount, 0) AS person_classified_amount,
                            COALESCE(monthly_guarantee_amount, 0) AS monthly_guarantee_amount,
                            address, district, street, community,
                            TO_CHAR(imported_at, 'YYYY-MM-DD') AS imported_date,
                            TRUE AS is_rural
                     FROM nc_biz_rural_subsistence_families
                     WHERE person_classified_amount > 0
                     UNION ALL
                     SELECT '最低生活保障对象' AS category, applicant_name, applicant_id_card,
                            member_names, member_id_cards,
                            COALESCE(person_classified_amount, 0) AS person_classified_amount,
                            COALESCE(monthly_guarantee_amount, 0) AS monthly_guarantee_amount,
                            address, district, street, community,
                            TO_CHAR(imported_at, 'YYYY-MM-DD') AS imported_date,
                            FALSE AS is_rural
                     FROM nc_biz_urban_subsistence_families
                     WHERE person_classified_amount > 0";
        var result = await _db.QueryAsync<ShiBaoFamilyRow>(sql, ct);
        if (result.IsFailure)
            return Result.Failure<List<MonthlyShiBaoRow>>(result.ErrorCode!, result.Message!);

        var rows = new List<MonthlyShiBaoRow>();
        var families = result.Value ?? new List<ShiBaoFamilyRow>();

        // 初次享受日期统一：导入库建档户取纳入时间、其余取首次审批时间（LoadApprovalDateMapAsync），与停保/增减发/自然减员同口径
        var dateMapResult = await LoadApprovalDateMapAsync(families.Select(f => f.ApplicantIdCard), ct);
        if (dateMapResult.IsFailure)
            return Result.Failure<List<MonthlyShiBaoRow>>(dateMapResult.ErrorCode!, dateMapResult.Message!);
        var dateMap = dateMapResult.Value;

        foreach (var fam in families)
        {
            foreach (var member in ParseShiBaoMembers(fam))
            {
                var birth = IdCardValidator.ExtractBirthDate(member.IdCard);
                if (birth == null) continue;
                var eighteen = birth.Value.AddYears(18);
                if (eighteen < monthStart || eighteen >= monthEnd) continue;

                var enjoy = dateMap.TryGetValue(fam.ApplicantIdCard ?? "", out var dt)
                    ? dt
                    : monthLast.ToString("yyyy-MM-dd");

                rows.Add(new MonthlyShiBaoRow
                {
                    HeadName = fam.ApplicantName ?? "",
                    MemberName = member.Name,
                    Address = JoinFullAddress(fam.District, fam.Street ?? fam.Town, fam.Community, fam.Address),
                    Category = fam.Category ?? "",
                    EnjoyDate = enjoy,
                    OriginalAmount = fam.MonthlyGuaranteeAmount.ToString("F2"),
                    OriginalClassified = "0.00",
                    Reason = "本月满18周岁",
                    DecreaseAmount = fam.PersonClassifiedAmount.ToString("F2"),
                    CurrentAmount = fam.MonthlyGuaranteeAmount.ToString("F2"),
                    CurrentClassified = "0.00",
                    IsRural = fam.IsRural
                });
            }
        }
        return Result.Success(rows);
    }

    /// <summary>
    /// 分类施保增发（低保导入库）：本月年满 60 周岁（身份证生日落在 [本月1日, 下月1日)）且
    /// 未享受分类施保（total_classified_amount=0）的成员所在家庭，户级每户一页。
    /// 户级入选判断 + Count/Amount/ClassifiedType 以"符合增发条件的成员"为准（保持月报补充设定口径）；
    /// 家庭成员明细（Members 1~6）展示该户非户主成员（户主信息已在表头上方单独填写，与档案模板15 同语义）。
    /// 加发额度 = 符合人数 × 分类施保标准（Rural/Urban，IStandardConfigService，失败回退 104/142）。
    /// 首次调用实时计算并落库快照（nc_biz_monthly_classified_adds）；当月已落库后直接读库返回，
    /// 锁死当月快照，保证重复打印一致且可历史溯源。
    /// </summary>
    public async Task<Result<List<MonthlyClassifiedAddRow>>> GetClassifiedSubsidyAddRowsAsync(int year, int month, CancellationToken ct = default)
    {
        // 已落库快照优先（当月确定后锁死，重复打印不重算、不受低保导入库后续变化影响）
        var saved = await GetClassifiedSubsidyAddsHistoryAsync(year, month, ct);
        if (saved.IsFailure)
            return Result.Failure<List<MonthlyClassifiedAddRow>>(saved.ErrorCode!, saved.Message!);
        if (saved.Value is { Count: > 0 })
            return saved;

        var computed = await ComputeClassifiedSubsidyAddRowsAsync(year, month, ct);
        if (computed.IsFailure)
            return computed;
        var rows = computed.Value ?? new List<MonthlyClassifiedAddRow>();
        if (rows.Count == 0)
            return Result.Success(rows);

        var saveResult = await SaveClassifiedSubsidyAddsAsync(year, month, "全部", rows, ct);
        if (saveResult.IsFailure)
            return Result.Failure<List<MonthlyClassifiedAddRow>>(saveResult.ErrorCode!, saveResult.Message!);

        LogInfo($"分类施保增发明细落库: {year}年{month}月 {rows.Count} 户");
        return Result.Success(rows);
    }

    /// <summary>
    /// 显式落库分类施保增发明细快照（幂等：同 year+month+applicant_id_card 冲突则忽略）。返回是否产生新写入。
    /// </summary>
    public async Task<Result<bool>> SaveClassifiedSubsidyAddsAsync(int year, int month, string town, List<MonthlyClassifiedAddRow> rows, CancellationToken ct = default)
    {
        if (rows == null || rows.Count == 0)
            return Result.Success(false);

        var inserted = 0;
        const int chunkSize = 400; // 18 参数/行 × 400 ≈ 7200，低于 PG 单语句 65535 参数上限

        for (var start = 0; start < rows.Count; start += chunkSize)
        {
            var chunk = rows.Skip(start).Take(chunkSize).ToList();
            var (valuesClause, args) = MultiRowValuesBuilder.Build(chunk.Count, 18, i =>
            {
                var r = chunk[i];
                var membersJson = JsonSerializer.Serialize(r.Members ?? new List<MonthlyClassifiedMember>());
                return new object?[]
                {
                    year, month, town,
                    r.Classification ?? "最低生活保障对象",
                    r.IsRural,
                    r.ApplicantIdCard ?? "",
                    r.ApplicantName ?? "",
                    r.ApplicantGender ?? "",
                    r.ApplicantBirthDate ?? "",
                    r.Nationality ?? "",
                    r.HealthStatus ?? "",
                    int.TryParse(r.FamilySize, out var fs) ? fs : 0,
                    r.Phone ?? "",
                    r.Address ?? "",
                    r.ClassifiedType ?? "",
                    r.Count,
                    r.Amount,
                    membersJson
                };
            }, o =>
            {
                var p = o;
                var sb = new StringBuilder("(");
                for (var k = 0; k < 18; k++)
                {
                    if (k > 0) sb.Append(", ");
                    sb.Append('$').Append(p + k);
                }
                sb.Append("::jsonb, NOW())");
                return sb.ToString();
            });

            var chunkSql = @"INSERT INTO nc_biz_monthly_classified_adds
                (year, month, town, category, is_rural, applicant_id_card, applicant_name, applicant_gender,
                 applicant_birth_date, nationality, health_status, family_size, phone, address,
                 classified_type, count, amount, members_json, created_at)
                VALUES " + valuesClause + @"
                ON CONFLICT (year, month, applicant_id_card) DO NOTHING
                RETURNING id";
            var result = await _db.QueryAsync<long>(chunkSql, ct, args);
            if (result.IsFailure)
                return Result.Failure<bool>(result.ErrorCode!, result.Message!);
            inserted += result.Value?.Count ?? 0;
        }

        return Result.Success(inserted > 0);
    }

    /// <summary>
    /// 分类施保增发明细历史溯源（读 nc_biz_monthly_classified_adds 快照，按 applicant_name 排序）
    /// </summary>
    public async Task<Result<List<MonthlyClassifiedAddRow>>> GetClassifiedSubsidyAddsHistoryAsync(int year, int month, CancellationToken ct = default)
    {
        var sql = @"SELECT category, is_rural, applicant_id_card, applicant_name, applicant_gender,
                           applicant_birth_date, nationality, health_status, family_size, phone, address,
                           classified_type, count, amount, members_json
                    FROM nc_biz_monthly_classified_adds
                    WHERE year = $1 AND month = $2
                    ORDER BY applicant_name";
        var result = await _db.QueryAsync<ClassifiedAddSnapshotRow>(sql, ct, year, month);
        if (result.IsFailure)
            return Result.Failure<List<MonthlyClassifiedAddRow>>(result.ErrorCode!, result.Message!);

        var rows = new List<MonthlyClassifiedAddRow>();
        foreach (var s in result.Value ?? new List<ClassifiedAddSnapshotRow>())
        {
            var members = new List<MonthlyClassifiedMember>();
            if (!string.IsNullOrWhiteSpace(s.MembersJson))
            {
                try
                {
                    members = JsonSerializer.Deserialize<List<MonthlyClassifiedMember>>(s.MembersJson)
                              ?? new List<MonthlyClassifiedMember>();
                }
                catch (JsonException)
                {
                    LogWarn($"分类施保增发快照 members_json 解析失败: Year={year} Month={month} Applicant={DataMasker.MaskIdCard(s.ApplicantIdCard ?? string.Empty)}");
                }
            }
            rows.Add(new MonthlyClassifiedAddRow
            {
                Classification = s.Category ?? "",
                ApplicantName = s.ApplicantName ?? "",
                ApplicantGender = s.ApplicantGender ?? "",
                ApplicantBirthDate = s.ApplicantBirthDate ?? "",
                Nationality = s.Nationality ?? "",
                HealthStatus = s.HealthStatus ?? "",
                FamilySize = s.FamilySize?.ToString() ?? "",
                Phone = s.Phone ?? "",
                Address = s.Address ?? "",
                ApplicantIdCard = s.ApplicantIdCard ?? "",
                ClassifiedType = s.ClassifiedType ?? "",
                Count = s.Count ?? 0,
                Amount = s.Amount ?? 0,
                Members = members
            });
        }
        return Result.Success(rows);
    }

    /// <summary>
    /// 实时计算分类施保增发明细（CTE 先定位符合条件户，再查出这些户的全部成员；满 60 周岁在内存精确判定）
    /// </summary>
    private async Task<Result<List<MonthlyClassifiedAddRow>>> ComputeClassifiedSubsidyAddRowsAsync(int year, int month, CancellationToken ct = default)
    {
        var monthStart = new DateTime(year, month, 1);               // 本月1日
        var monthEnd = monthStart.AddMonths(1);                      // 下月1日（满 60 周岁生日不在此区间）

        // [索引豁免] EXTRACT(MONTH FROM birth_date)=$1 是"生日月份匹配"（任意年份的该月）语义，
        // 不存在等价的时间范围改写；CTE 粗筛 + 内存精确判定满 60，低频且数据规模有限。
        var sql = @"WITH matched AS (
                         SELECT DISTINCT p.family_id
                         FROM nc_biz_rural_subsistence_persons p
                         JOIN nc_biz_rural_subsistence_families f ON f.id = p.family_id
                         WHERE COALESCE(p.total_classified_amount, 0) = 0
                           AND p.birth_date IS NOT NULL
                           AND EXTRACT(MONTH FROM p.birth_date) = $1::int
                         UNION
                         SELECT DISTINCT p.family_id
                         FROM nc_biz_urban_subsistence_persons p
                         JOIN nc_biz_urban_subsistence_families f ON f.id = p.family_id
                         WHERE COALESCE(p.total_classified_amount, 0) = 0
                           AND p.birth_date IS NOT NULL
                           AND EXTRACT(MONTH FROM p.birth_date) = $1::int
                     )
                     SELECT '最低生活保障对象' AS category,
                            f.applicant_name, f.applicant_id_card, f.phone, f.family_size,
                            f.address, f.district, f.street, f.community,
                            p.name AS member_name, p.gender, p.birth_date, p.ethnicity,
                            p.health_status, p.disability_type, p.disability_level, p.relationship, p.annual_income, p.id_card AS member_id_card,
                            COALESCE(p.total_classified_amount, 0) AS total_classified_amount,
                            TRUE AS is_rural
                     FROM nc_biz_rural_subsistence_families f
                     JOIN nc_biz_rural_subsistence_persons p ON p.family_id = f.id
                     WHERE f.id IN (SELECT family_id FROM matched)
                     UNION ALL
                     SELECT '最低生活保障对象' AS category,
                            f.applicant_name, f.applicant_id_card, f.phone, f.family_size,
                            f.address, f.district, f.street, f.community,
                            p.name AS member_name, p.gender, p.birth_date, p.ethnicity,
                            p.health_status, p.disability_type, p.disability_level, p.relationship, p.monthly_income * 12 AS annual_income, p.id_card AS member_id_card,
                            COALESCE(p.total_classified_amount, 0) AS total_classified_amount,
                            FALSE AS is_rural
                     FROM nc_biz_urban_subsistence_families f
                     JOIN nc_biz_urban_subsistence_persons p ON p.family_id = f.id
                     WHERE f.id IN (SELECT family_id FROM matched)";
        var result = await _db.QueryAsync<ClassifiedAddPersonRow>(sql, ct, month);
        if (result.IsFailure)
            return Result.Failure<List<MonthlyClassifiedAddRow>>(result.ErrorCode!, result.Message!);

        // 初次享受日期统一：导入库建档户取纳入时间、其余取首次审批时间（LoadApprovalDateMapAsync），与施保金减发同口径（不影响字段展示，仅留档）
        var persons = result.Value ?? new List<ClassifiedAddPersonRow>();

        // 符合增发条件成员：身份证生日（回退 birth_date）落在 [本月1日, 下月1日) 且未享受分类施保，
        // 与分类施保减发（满 18 周岁）同构；仅用于户级入选判断与 Count/Amount/ClassifiedType。
        var qualified = persons
            .Where(m => m.TotalClassifiedAmount == 0
                        && (IdCardValidator.ExtractBirthDate(m.MemberIdCard ?? "") ?? m.BirthDate) is { } birth
                        && birth.AddYears(60) >= monthStart
                        && birth.AddYears(60) < monthEnd)
            .ToList();
        if (qualified.Count == 0)
            return Result.Success(new List<MonthlyClassifiedAddRow>());

        var rows = new List<MonthlyClassifiedAddRow>();
        var grouped = persons.GroupBy(p => (Category: p.Category ?? "", ApplicantIdCard: p.ApplicantIdCard ?? "", IsRural: p.IsRural));

        // 分类施保标准（农村/城市，单点取自 IStandardConfigService，与 ClassificationService 同源；失败显式失败，不用过期回退值）
        var ruralStandardResult = await _standardConfigService.GetStandardValueAsync("ClassifiedSubsidyStandard", "Rural", ct: ct);
        var urbanStandardResult = await _standardConfigService.GetStandardValueAsync("ClassifiedSubsidyStandard", "Urban", ct: ct);
        if (ruralStandardResult.IsFailure || urbanStandardResult.IsFailure
            || ruralStandardResult.Value <= 0 || urbanStandardResult.Value <= 0)
        {
            var failMsg = ruralStandardResult.IsFailure ? ruralStandardResult.Message : urbanStandardResult.Message;
            LogError($"分类施保标准读取失败: {failMsg}");
            return Result.Failure<List<MonthlyClassifiedAddRow>>(ErrorCodes.CONFIG_NOT_FOUND,
                $"分类补贴标准配置缺失：{failMsg}。请在「数据中心-标准配置管理」中维护。");
        }
        var ruralStandard = ruralStandardResult.Value;
        var urbanStandard = urbanStandardResult.Value;

        foreach (var group in grouped)
        {
            var list = group.OrderBy(m => m.Relationship == "Head" ? 0 : 1)
                            .ThenBy(m => RelationOrder(m.Relationship ?? ""))
                            .ThenBy(m => m.BirthDate ?? DateTime.MaxValue)
                            .ThenBy(m => m.MemberIdCard ?? "")
                            .ToList();

            // 户级入选判断 + Count/Amount/ClassifiedType 以"符合增发条件成员"为准（月报补充设定口径）
            var qualifiedInFamily = qualified
                .Where(q => q.ApplicantIdCard == group.Key.ApplicantIdCard && q.IsRural == group.Key.IsRural)
                .ToList();
            if (qualifiedInFamily.Count == 0)
                continue;

            var head = list.FirstOrDefault(m => m.Relationship == "Head") ?? list.FirstOrDefault() ?? list[0];
            var isRural = group.Key.IsRural;
            var standard = isRural ? ruralStandard : urbanStandard;

            var row = new MonthlyClassifiedAddRow
            {
                ApplicantName = group.First().ApplicantName ?? "",
                ApplicantGender = head.Gender ?? "",
                ApplicantBirthDate = FormatChineseDate(head.BirthDate),
                Nationality = MapEthnicity(head.Ethnicity ?? ""),
                HealthStatus = MapHealthStatus(head.HealthStatus ?? ""),
                Classification = group.Key.Category ?? "",
                FamilySize = (group.First().FamilySize ?? 0).ToString(),
                Phone = group.First().Phone ?? "",
                Address = JoinFullAddress(group.First().District, group.First().Street ?? group.First().Town, group.First().Community, group.First().Address),
                ApplicantIdCard = head.MemberIdCard ?? group.Key.ApplicantIdCard ?? "",
                ClassifiedType = BuildClassifiedType(qualifiedInFamily, monthStart),
                Count = qualifiedInFamily.Count,
                Amount = qualifiedInFamily.Count * standard,
                IsRural = group.Key.IsRural,
                Members = list.Where(m => m.Relationship != "Head")
                              .Take(6)
                              .Select(m => new MonthlyClassifiedMember
                {
                    Name = m.MemberName ?? "",
                    Gender = m.Gender ?? "",
                    BirthDate = FormatChineseDate(m.BirthDate),
                    Relation = MapFamilyRelationship(m.Relationship ?? ""),
                    Health = MapHealthStatus(m.HealthStatus ?? ""),
                    MonthlyIncome = Math.Round((m.AnnualIncome ?? 0) / 12m, 2, MidpointRounding.AwayFromZero),
                    AnnualIncome = m.AnnualIncome ?? 0
                }).ToList()
            };
            rows.Add(row);
        }

        return Result.Success(rows.OrderBy(r => r.Classification).ThenBy(r => r.ApplicantName).ToList());
    }

    /// <summary>
    /// 民族码 → 中文（与 nc_dict_items Ethnicities 字典一致；未知回退原值）
    /// </summary>
    private static string MapEthnicity(string code)
        => code switch
        {
            "Han" => "汉族",
            "Mongol" => "蒙古族",
            "Hui" => "回族",
            "Tibetan" => "藏族",
            "Uyghur" => "维吾尔族",
            "Miao" => "苗族",
            "Yi" => "彝族",
            "Zhuang" => "壮族",
            "Bouyei" => "布依族",
            "Korean" => "朝鲜族",
            "Manchu" => "满族",
            "Dong" => "侗族",
            "Yao" => "瑶族",
            "Bai" => "白族",
            "Tujia" => "土家族",
            "Hani" => "哈尼族",
            "Dai" => "傣族",
            "Li" => "黎族",
            "Other" => "其他",
            _ => code
        };

    /// <summary>
    /// 与户主关系码 → 中文（与 nc_dict_items FamilyRelationships 字典一致；未知回退原值）
    /// </summary>
    private static string MapFamilyRelationship(string code)
        => code switch
        {
            "Head" => "本人/户主",
            "Spouse" => "配偶",
            "Son" => "子/婿",
            "Daughter" => "女/媳",
            "Grandchild" => "孙子女/外孙子女",
            "Parent" => "父母/岳父母/公婆",
            "Grandparent" => "祖父母/外祖父母",
            "Sibling" => "兄弟姐妹",
            "Other" => "其他",
            _ => code
        };

    /// <summary>
    /// 成员排序权重（户主之后：配偶→子女→父母→祖辈→孙辈→兄弟姐妹→其他）
    /// </summary>
    private static int RelationOrder(string relationship)
        => relationship switch
        {
            "Spouse" => 1,
            "Son" or "Daughter" => 2,
            "Parent" => 3,
            "Grandparent" => 4,
            "Grandchild" => 5,
            "Sibling" => 6,
            _ => 7
        };

    /// <summary>
    /// 日期 → yyyy年MM月dd日（空值回退空串）
    /// </summary>
    private static string FormatChineseDate(DateTime? date)
        => date.HasValue ? date.Value.ToString("yyyy年MM月dd日") : string.Empty;

    /// <summary>
    /// 拆分导入库成员（分号分隔；含户主本人，去重）
    /// </summary>
    private static List<(string Name, string IdCard)> ParseShiBaoMembers(ShiBaoFamilyRow family)
    {
        var members = new List<(string Name, string IdCard)>();
        if (!string.IsNullOrWhiteSpace(family.ApplicantIdCard))
            members.Add((family.ApplicantName?.Trim() ?? "", family.ApplicantIdCard.Trim()));

        var names = (family.MemberNames ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ids = (family.MemberIdCards ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (names.Length == 0 && ids.Length == 0) return members;

        for (var i = 0; i < Math.Max(names.Length, ids.Length); i++)
        {
            var name = i < names.Length ? names[i] : "";
            var id = i < ids.Length ? ids[i] : "";
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (members.Any(x => string.Equals(x.IdCard, id, StringComparison.OrdinalIgnoreCase))) continue;
            members.Add((name, id));
        }
        return members;
    }

    public async Task<Result<List<MonthlyDeathRow>>> GetDeathRowsAsync(int year, int month, string town, string? category = null, CancellationToken ct = default)
    {
        var cycle = await GetCycleRangeAsync(year, month, ct);
        if (cycle.IsFailure) return Result.Failure<List<MonthlyDeathRow>>(cycle.ErrorCode!, cycle.Message!);
        var c = cycle.Value;

        var codes = CategoryCodes(category);
        var parameters = new List<object> { c.Start, c.End, town ?? "" };
        var categorySql = string.Empty;
        if (codes != null)
        {
            parameters.Add(codes);
            categorySql = " AND a.classification_result = ANY($4::text[])";
        }

        // 死亡记录表无乡镇列，乡镇过滤借道 nc_biz_applications.town（join application_id）
        var sql = $@"SELECT a.applicant_name AS head_name, a.applicant_id_card AS head_id_card,
                        a.address, d.member_name AS deceased_name, d.member_id_card,
                        d.death_date, d.new_head_name,
                        a.classification_result, a.district, a.town, a.community
                     FROM nc_biz_death_records d
                     LEFT JOIN nc_biz_applications a ON d.application_id = a.id
                     WHERE d.death_date >= $1 AND d.death_date < $2
                       AND ($3 = '' OR a.town = $3)
                       {categorySql}
                     ORDER BY d.death_date ASC";
        var result = await _db.QueryAsync<DeathRow>(sql, ct, parameters.ToArray());
        if (result.IsFailure)
            return Result.Failure<List<MonthlyDeathRow>>(result.ErrorCode!, result.Message!);

        var rows = result.Value ?? new List<DeathRow>();
        var dateMapResult = await LoadApprovalDateMapAsync(rows.Select(r => r.HeadIdCard), ct);
        if (dateMapResult.IsFailure)
            return Result.Failure<List<MonthlyDeathRow>>(dateMapResult.ErrorCode!, dateMapResult.Message!);
        var dateMap = dateMapResult.Value;
        var cycleEnd = c.End.AddDays(-1);

        var outRows = new List<MonthlyDeathRow>();
        foreach (var item in rows)
        {
            var enjoy = dateMap.TryGetValue(item.HeadIdCard ?? "", out var dt) ? dt : cycleEnd.ToString("yyyy-MM-dd");
            outRows.Add(new MonthlyDeathRow
            {
                HeadName = item.HeadName ?? "",
                HeadIdCard = item.HeadIdCard ?? "",
                Address = JoinFullAddress(item.District, item.Town, item.Community, item.Address),
                DeceasedName = item.DeceasedName ?? "",
                DeceasedDate = item.DeathDate?.ToString("yyyy-MM-dd") ?? "",
                EnjoyDate = enjoy,
                NewHeadName = item.NewHeadName ?? "",
                CategoryCode = item.ClassificationResult ?? "",
                IsRural = ClassificationConstants.IsCodeRural(item.ClassificationResult ?? "")
            });
        }
        return Result.Success(outRows);
    }

    /// <summary>
    /// 退出对象兜底纠治表（表 7）。
    /// 退出对象识别（本月周期）：停保(status=ApplicationStatusCodes.STOPPED) + 渐退期满(grace_periods.end_date，仅已生效档案) + 经济复核降档(change_records.triggered_grace_period，排除户主死亡/分类施保减除)。
    /// 核查列：给予渐退期(事件日期落在渐退期内)；纳入低保/特困/低保边缘/刚性支出(本户原档案当前生效分类)；直接退出(均否)。
    /// </summary>
    public async Task<Result<List<MonthlyExitRectificationRow>>> GetExitRectificationRowsAsync(int year, int month, string town, CancellationToken ct = default)
    {
        var cycle = await GetCycleRangeAsync(year, month, ct);
        if (cycle.IsFailure) return Result.Failure<List<MonthlyExitRectificationRow>>(cycle.ErrorCode!, cycle.Message!);
        var c = cycle.Value;

        // 渐退期满按"期满所在自然月"判定（期满落在哪个月就进哪个月的表）：
        // 停保/经济复核降档仍走 B 线周期 [7/16, 8/16)，渐退期满走当月 1 日~次月 1 日。
        // 否则 8/30 期满的户会被排到 9 月表，8 月纠治表缺漏渐退期满对象。
        // 渐退期满事件的"退出前类别"取进入渐退期时类别（change_records 反推），
        // 而非当前分类——渐退期内经济复核降档（如 低保→边缘）属退出后变化，类别列仍显原类别。
        var monthStart = new DateTime(year, month, 1);
        var monthEnd = monthStart.AddMonths(1);

        // 1. 识别本月退出事件（三源 UNION）
        var eventSql = $@"
            SELECT x.application_id, x.event_date, x.event_type, x.exit_reason, x.old_classification
            FROM (
                SELECT a.id AS application_id, a.stop_date AS event_date, '停保' AS event_type,
                       COALESCE(a.stop_reason, '') AS exit_reason,
                       a.classification_result AS old_classification
                FROM nc_biz_applications a
                WHERE a.status = '{ApplicationStatusCodes.STOPPED}' AND a.stop_date >= $1 AND a.stop_date < $2
                  AND a.deleted_at IS NULL AND ($3 = '' OR a.town = $3)
                  -- 停旧建新接续（同大类继续享受）不算退出对象
                  {StoppedArchiveFilter.NotRebuildContinuationSql("a")}
                UNION ALL
                SELECT c.application_id, c.change_date AS event_date, '经济复核降档' AS event_type,
                       '经济复核转低收入' AS exit_reason, c.old_classification
                FROM nc_biz_change_records c
                WHERE c.triggered_grace_period = TRUE AND c.change_date >= $1 AND c.change_date < $2
                  AND c.deleted_at IS NULL
                  -- 排除户主死亡/分类施保减除（死亡已有停保事件行，避免误标经济复核降档）
                  AND c.change_type NOT IN ('{DictionaryConstants.ChangeType.HOUSEHOLD_DEATH}', '{DictionaryConstants.ChangeType.CLASSIFIED_SUBSIDY_REDUCE}')
                UNION ALL
                SELECT g.application_id, g.end_date AS event_date, '渐退期满' AS event_type,
                       '渐退期满重新核算' AS exit_reason,
                       COALESCE(
                           (SELECT c.new_classification FROM nc_biz_change_records c
                            WHERE c.application_id = g.application_id AND c.deleted_at IS NULL
                              AND c.change_date < g.start_date
                              AND c.new_classification IS NOT NULL AND c.new_classification <> ''
                            ORDER BY c.change_date DESC, c.id DESC LIMIT 1),
                           (SELECT c.old_classification FROM nc_biz_change_records c
                            WHERE c.application_id = g.application_id AND c.deleted_at IS NULL
                              AND c.change_date <= g.end_date
                              AND c.old_classification IS NOT NULL AND c.old_classification <> ''
                            ORDER BY c.change_date ASC, c.id ASC LIMIT 1),
                           a.classification_result) AS old_classification
                FROM nc_biz_grace_periods g
                LEFT JOIN nc_biz_applications a ON g.application_id = a.id
                WHERE g.end_date >= $4 AND g.end_date < $5 AND g.deleted_at IS NULL
                  -- 仅统计已生效档案：Draft 新档（户主死亡停旧建新待认定）渐退期满不构成退出事件
                  AND a.status IN ('{ApplicationStatusCodes.APPROVED}', '{ApplicationStatusCodes.COMPLETED}')
            ) x
            ORDER BY x.event_date, x.application_id;
";
        var eventResult = await _db.QueryAsync<ExitEventRow>(eventSql, ct, c.Start, c.End, town ?? "", monthStart, monthEnd);
        if (eventResult.IsFailure)
            return Result.Failure<List<MonthlyExitRectificationRow>>(eventResult.ErrorCode!, eventResult.Message!);

        var events = eventResult.Value ?? new List<ExitEventRow>();
        if (events.Count == 0)
            return Result.Success(new List<MonthlyExitRectificationRow>());

        // 同 application_id 去重：停保 > 渐退期满 > 经济复核降档
        var distinctEvents = events
            .GroupBy(e => e.ApplicationId)
            .Select(g => g.OrderByDescending(e => e.EventType == "停保")
                           .ThenByDescending(e => e.EventType == "渐退期满")
                           .ThenBy(e => e.EventDate)
                           .First())
            .ToList();
        var ids = distinctEvents.Select(e => e.ApplicationId).ToArray();

        // 2. 批量查申请详情（户主信息/当前分类/当前状态）
        // 注意：MemberCountSql 自带 AS family_size 别名，勿再追加别名（重复 AS 会触发 42601）
        var detailSql = $@"SELECT a.id, a.applicant_name, a.applicant_id_card,
                                  {MemberCountSql("a")},
                                  a.classification_result, a.status
                           FROM nc_biz_applications a
                           WHERE a.id = ANY($1) AND a.deleted_at IS NULL";
        var detailResult = await _db.QueryAsync<ExitDetailRow>(detailSql, ct, ids);
        if (detailResult.IsFailure)
            return Result.Failure<List<MonthlyExitRectificationRow>>(detailResult.ErrorCode!, detailResult.Message!);
        var details = (detailResult.Value ?? new List<ExitDetailRow>())
            .ToDictionary(d => d.Id, d => d);

        // 3. 批量查渐退期（判定"给予渐退期"）
        var graceSql = @"SELECT application_id, start_date, end_date
                         FROM nc_biz_grace_periods
                         WHERE deleted_at IS NULL AND application_id = ANY($1)";
        var graceResult = await _db.QueryAsync<ExitGraceRow>(graceSql, ct, ids);
        if (graceResult.IsFailure)
            return Result.Failure<List<MonthlyExitRectificationRow>>(graceResult.ErrorCode!, graceResult.Message!);
        var graces = graceResult.Value ?? new List<ExitGraceRow>();

        // 3.5 停旧建新：批量查停保户的下游新档案（original_application_id=本户），
        //     用于确定"退出后纳入类别"（解维平 44 → 新档案 45 特困）与退保原因。
        var rebuildMap = new Dictionary<long, string>();
        if (ids.Length > 0)
        {
            var rebuildSql = @"SELECT DISTINCT ON (original_application_id) original_application_id, classification_result
                               FROM nc_biz_applications
                               WHERE original_application_id = ANY($1)
                               ORDER BY original_application_id, (deleted_at IS NULL) DESC, created_at DESC";
            var rebuildResult = await _db.QueryAsync<RebuildTargetRow>(rebuildSql, ct, ids);
            if (rebuildResult.IsFailure)
                return Result.Failure<List<MonthlyExitRectificationRow>>(rebuildResult.ErrorCode!, rebuildResult.Message!);
            foreach (var row in rebuildResult.Value ?? new List<RebuildTargetRow>())
            {
                if (row.OriginalApplicationId.HasValue && !string.IsNullOrEmpty(row.ClassificationResult))
                    rebuildMap[row.OriginalApplicationId.Value] = row.ClassificationResult!;
            }
        }

        // 3.7 户主变更识别：该档案存在户主死亡/户主变更变更记录 → 退保原因描述为"停止"
        var headChangeApps = new HashSet<long>();
        if (ids.Length > 0)
        {
            var headChangeSql = @"SELECT DISTINCT application_id
                                  FROM nc_biz_change_records
                                  WHERE change_type = ANY($1) AND deleted_at IS NULL AND application_id = ANY($2)";
            var headChangeResult = await _db.QueryAsync<HeadChangeAppRow>(headChangeSql, ct,
                new[] { DictionaryConstants.ChangeType.HOUSEHOLD_DEATH, DictionaryConstants.ChangeType.HOUSEHOLD_HEAD_CHANGE }, ids);
            if (headChangeResult.IsFailure)
                return Result.Failure<List<MonthlyExitRectificationRow>>(headChangeResult.ErrorCode!, headChangeResult.Message!);
            foreach (var row in headChangeResult.Value ?? new List<HeadChangeAppRow>())
                if (row.ApplicationId.HasValue)
                    headChangeApps.Add(row.ApplicationId.Value);
        }

        // 3.8 跨类停保的变更原因类型（CategoryStop 记录）：退保原因前缀"经济复核后/成员变更后"
        var stopReasonTypeMap = new Dictionary<long, string>();
        if (ids.Length > 0)
        {
            var reasonTypeSql = @"SELECT DISTINCT ON (application_id) application_id, change_reason_type
                                  FROM nc_biz_change_records
                                  WHERE change_type = 'CategoryStop' AND deleted_at IS NULL AND application_id = ANY($1)
                                  ORDER BY application_id, changed_at DESC";
            var reasonTypeResult = await _db.QueryAsync<ChangeReasonTypeRow>(reasonTypeSql, ct, ids);
            if (reasonTypeResult.IsFailure)
                return Result.Failure<List<MonthlyExitRectificationRow>>(reasonTypeResult.ErrorCode!, reasonTypeResult.Message!);
            foreach (var row in reasonTypeResult.Value ?? new List<ChangeReasonTypeRow>())
                if (row.ApplicationId.HasValue && !string.IsNullOrWhiteSpace(row.ChangeReasonType))
                    stopReasonTypeMap[row.ApplicationId.Value] = row.ChangeReasonType!;
        }

        // 3.9 死亡识别：周期内户主死亡记录（死亡日期落在本月周期内）→ 死亡户直接退出。
        // 仅认"周期内死亡"：佟淑华户 2 月前户主吴克学死亡但家庭继续保障、8 月渐退期满重新核算纳入低保，
        // 该户死亡不在本月周期，不会被误判为死亡退出。
        var deathAppIds = new HashSet<long>();
        if (ids.Length > 0)
        {
            var deathSql = @"SELECT DISTINCT application_id
                             FROM nc_biz_death_records
                             WHERE is_household_head = true
                               AND death_date >= $1 AND death_date < $2
                               AND application_id = ANY($3)";
            var deathResult = await _db.QueryAsync<HeadChangeAppRow>(deathSql, ct, c.Start, c.End, ids);
            if (deathResult.IsFailure)
                return Result.Failure<List<MonthlyExitRectificationRow>>(deathResult.ErrorCode!, deathResult.Message!);
            foreach (var row in deathResult.Value ?? new List<HeadChangeAppRow>())
                if (row.ApplicationId.HasValue)
                    deathAppIds.Add(row.ApplicationId.Value);
        }

        var dibaoCodes = CategoryCodes("最低生活保障");
        var tekuCodes = CategoryCodes("特困人员");
        var edgeCodes = CategoryCodes("最低生活保障边缘家庭");
        var rigidCodes = CategoryCodes("刚性支出困难家庭");

        var outRows = new List<MonthlyExitRectificationRow>();
        foreach (var ev in distinctEvents)
        {
            var appId = ev.ApplicationId ?? 0;
            if (!details.TryGetValue(appId, out var d))
                continue;

            var categoryCode = string.IsNullOrEmpty(ev.OldClassification) ? d.ClassificationResult ?? "" : ev.OldClassification;

            // 死亡户短路：周期内户主死亡 → 人员死亡直接退出，不参与渐退期/纳入类别核查。
            // 死亡人不可能转入低保/特困等类别，新户主建档属新申请，不构成"退出后纳入"。
            // 优先级最高，覆盖 headChangeApps（"停止"）与停旧建新（纳入类别）判定。
            if (deathAppIds.Contains(appId))
            {
                outRows.Add(new MonthlyExitRectificationRow
                {
                    Name = d.ApplicantName ?? "",
                    IdCard = d.ApplicantIdCard ?? "",
                    Category = ClassificationConstants.ConvertToMajorCategoryName(categoryCode),
                    FamilySize = (d.FamilySize ?? 0).ToString(),
                    StopMonth = ev.EventDate?.ToString("yyyy年M月") ?? "",
                    StopReason = "人员死亡",
                    GracePeriod = "否",
                    IntoDibao = "否",
                    IntoTeku = "否",
                    IntoEdge = "否",
                    IntoRigid = "否",
                    DirectExit = "是"
                });
                continue;
            }

            var isActive = d.Status is ApplicationStatusCodes.APPROVED or ApplicationStatusCodes.COMPLETED;

            // 给予渐退期：档案生效期=办理月次月 1 日（提前办理档案），
            // 渐退期在某月结束，次月新开档案不算在渐退期内。
            // 判定口径 = 渐退期是否覆盖"生效日"（事件日期所在月的次月 1 日）。
            var eventDate = ev.EventDate;
            var effectiveDate = eventDate.HasValue
                ? new DateTime(eventDate.Value.Year, eventDate.Value.Month, 1).AddMonths(1)
                : (DateTime?)null;
            var hasGrace = effectiveDate.HasValue && graces.Any(g =>
                g.ApplicationId == appId && g.StartDate.HasValue && g.EndDate.HasValue
                && g.StartDate.Value <= effectiveDate.Value
                && g.EndDate.Value >= effectiveDate.Value);
            var graceText = hasGrace ? "是" : "否";

            // 纳入类别：停旧建新（有下游新档案）优先用新档案分类（如解维平 44→45 特困）；
            // 否则用当前档案分类（isActive Approved/Completed 才视为纳入）。
            var intoCategory = rebuildMap.TryGetValue(appId, out var rebuildCode) && !string.IsNullOrEmpty(rebuildCode)
                ? rebuildCode
                : (isActive ? d.ClassificationResult ?? "" : "");

            var hasDibao = intoCategory != "" && dibaoCodes != null && dibaoCodes.Contains(intoCategory) ? "是" : "否";
            var hasTeku = intoCategory != "" && tekuCodes != null && tekuCodes.Contains(intoCategory) ? "是" : "否";
            var hasEdge = intoCategory != "" && edgeCodes != null && edgeCodes.Contains(intoCategory) ? "是" : "否";
            var hasRigid = intoCategory != "" && rigidCodes != null && rigidCodes.Contains(intoCategory) ? "是" : "否";
            var directExit = !hasGrace && hasDibao == "否" && hasTeku == "否" && hasEdge == "否" && hasRigid == "否" ? "是" : "否";

            // 退保原因（事件类型 + 纳入类别，标准化）：
            // 停保 + 户主变更（户主死亡/户主变更）→ "停止"
            // 停保 + 停旧建新（经济复核跨类）→ "经济复核后"；停保 + 纯停保 → 保留 stop_reason/停保退出
            // 经济复核降档（低保→低收入，进渐退期）→ "渐退期满，经经济复核转低收入"
            // 渐退期满 → "渐退期满，经经济复核转{纳入类别}"
            var stopReason = ev.EventType switch
            {
                "停保" when headChangeApps.Contains(appId) => "停止",
                "停保" when !string.IsNullOrEmpty(intoCategory) && !string.IsNullOrEmpty(rebuildCode)
                    => $"{(stopReasonTypeMap.TryGetValue(appId, out var reasonType) ? reasonType : ChangeReasonTypeConstants.EconomicReview)}后 由 {ClassificationConstants.ConvertToMajorCategoryName(categoryCode)}转入{ClassificationConstants.ConvertToMajorCategoryName(intoCategory)}",
                "停保" => string.IsNullOrWhiteSpace(ev.ExitReason) ? "停保退出" : ev.ExitReason!,
                "经济复核降档" => "渐退期满，经经济复核转低收入",
                "渐退期满" when !string.IsNullOrEmpty(intoCategory)
                    => $"渐退期满，经经济复核转{ClassificationConstants.ConvertFromCode(intoCategory)}",
                _ => string.IsNullOrWhiteSpace(ev.ExitReason) ? "停保退出" : ev.ExitReason!
            };

            outRows.Add(new MonthlyExitRectificationRow
            {
                Name = d.ApplicantName ?? "",
                IdCard = d.ApplicantIdCard ?? "",
                Category = ClassificationConstants.ConvertToMajorCategoryName(categoryCode),
                FamilySize = (d.FamilySize ?? 0).ToString(),
                StopMonth = ev.EventDate?.ToString("yyyy年M月") ?? "",
                StopReason = stopReason,
                GracePeriod = graceText,
                IntoDibao = hasDibao,
                IntoTeku = hasTeku,
                IntoEdge = hasEdge,
                IntoRigid = hasRigid,
                DirectExit = directExit
            });
        }
        return Result.Success(outRows);
    }

    // ─────────────────── 临时救助新增汇总 ───────────────────

    public async Task<Result<List<MonthlyTempReliefRow>>> GetTempReliefSummaryRowsAsync(int year, int month, string town, CancellationToken ct = default)
    {
        var townSafe = string.IsNullOrEmpty(town) ? "" : town;

        var cycle = await GetCycleRangeAsync(year, month, ct);
        if (cycle.IsFailure) return Result.Failure<List<MonthlyTempReliefRow>>(cycle.ErrorCode!, cycle.Message!);
        var c = cycle.Value;

        // 主查询：周期内已确认（Confirmed）的临时救助申请；
        // 一卡通账号取临时救助档案自身的 bank_account（来源台账 one_card_account 优先，见预填逻辑）。
        var sql = @"SELECT t.id AS relief_id,
                           t.relief_type,
                           t.applicant_name, t.applicant_id_card, t.gender, t.age, t.phone,
                           t.family_size, t.family_address, t.family_category,
                           t.difficulty_type, t.confirm_amount, t.confirmed_at,
                           t.bank_account
                    FROM nc_biz_temp_relief_applications t
                    WHERE t.status = $4
                      AND t.confirmed_at >= $1 AND t.confirmed_at < $2
                      AND t.deleted_at IS NULL
                      AND ($3 = '' OR t.town = $3)
                    ORDER BY t.confirmed_at, t.id";
        var result = await _db.QueryAsync<TempReliefSummaryRowData>(sql, ct, c.Start, c.End, townSafe, TempReliefConstants.StatusConfirmed);
        if (result.IsFailure)
            return Result.Failure<List<MonthlyTempReliefRow>>(result.ErrorCode!, result.Message!);

        var main = result.Value ?? new List<TempReliefSummaryRowData>();
        if (main.Count == 0)
            return Result.Success(new List<MonthlyTempReliefRow>());

        var ids = main.Select(r => r.ReliefId).Distinct().ToArray();

        // 明细批量加载（避免 N+1），供自付金额合计与精简句成员名使用
        var diseasesResult = await _db.QueryAsync<TempReliefDisease>(
            "SELECT * FROM nc_biz_temp_relief_diseases WHERE application_id = ANY($1) AND deleted_at IS NULL ORDER BY id", ct, ids);
        if (diseasesResult.IsFailure)
            return Result.Failure<List<MonthlyTempReliefRow>>(diseasesResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                diseasesResult.Message ?? "临时救助疾病明细查询失败");
        var accidentsResult = await _db.QueryAsync<TempReliefAccident>(
            "SELECT * FROM nc_biz_temp_relief_accidents WHERE application_id = ANY($1) AND deleted_at IS NULL ORDER BY id", ct, ids);
        if (accidentsResult.IsFailure)
            return Result.Failure<List<MonthlyTempReliefRow>>(accidentsResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                accidentsResult.Message ?? "临时救助意外灾害明细查询失败");
        var educationsResult = await _db.QueryAsync<TempReliefEducation>(
            "SELECT * FROM nc_biz_temp_relief_educations WHERE application_id = ANY($1) AND deleted_at IS NULL ORDER BY id", ct, ids);
        if (educationsResult.IsFailure)
            return Result.Failure<List<MonthlyTempReliefRow>>(educationsResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                educationsResult.Message ?? "临时救助教育支出明细查询失败");

        var diseasesByApp = (diseasesResult.Value ?? new()).GroupBy(d => d.ApplicationId).ToDictionary(g => g.Key, g => g.ToList());
        var accidentsByApp = (accidentsResult.Value ?? new()).GroupBy(a => a.ApplicationId).ToDictionary(g => g.Key, g => g.ToList());
        var educationsByApp = (educationsResult.Value ?? new()).GroupBy(e => e.ApplicationId).ToDictionary(g => g.Key, g => g.ToList());

        var rows = new List<MonthlyTempReliefRow>(main.Count);
        foreach (var r in main)
        {
            var diseases = diseasesByApp.GetValueOrDefault(r.ReliefId) ?? new List<TempReliefDisease>();
            var accidents = accidentsByApp.GetValueOrDefault(r.ReliefId) ?? new List<TempReliefAccident>();
            var educations = educationsByApp.GetValueOrDefault(r.ReliefId) ?? new List<TempReliefEducation>();

            var isSmall = string.Equals(r.ReliefType, TempReliefConstants.ReliefTypeSmall, StringComparison.Ordinal);
            rows.Add(new MonthlyTempReliefRow
            {
                Name = r.ApplicantName ?? "",
                IdCard = r.ApplicantIdCard ?? "",
                Age = r.Age?.ToString() ?? "",
                Gender = r.Gender ?? "",
                FamilySize = r.FamilySize?.ToString() ?? "",
                Address = r.FamilyAddress ?? "",
                // 个人类别用缩写（最低生活保障家庭→低保 等）
                Category = TempReliefConstants.GetFamilyCategoryShortName(r.FamilyCategory),
                Phone = r.Phone ?? "",
                // 大额救助：救助金额 / 审批时间两列一律留空
                Amount = isSmall && r.ConfirmAmount.HasValue ? r.ConfirmAmount.Value.ToString("F2") : "",
                AuditDate = isSmall && r.ConfirmedAt.HasValue ? r.ConfirmedAt.Value.ToString("yyyy-MM-dd") : "",
                Reason = BuildTempReliefSummaryReason(r.DifficultyType ?? "", diseases, accidents, educations),
                SelfPay = CalcTempReliefSelfPay(diseases, accidents, educations),
                BankAccount = r.BankAccount ?? ""
            });
        }
        return Result.Success(rows);
    }

    /// <summary>
    /// 汇总表"申请理由"文本（每条明细一句，块间换行，不截断）：
    /// 因病→"因病，住院时间为{A}到{B}。患病为{C}。"；因学→"在{年}被{学校}录取。"；
    /// 因灾→"因{类型}，发生于{时间}，地点{地点}。财产损失{金额}元。"；
    /// 其他困难→固定句。缺失字段的子句整体省略。
    /// </summary>
    private static string BuildTempReliefSummaryReason(
        string difficultyType,
        List<TempReliefDisease> diseases, List<TempReliefAccident> accidents, List<TempReliefEducation> educations)
    {
        var type = (difficultyType ?? string.Empty).Trim();

        if (type.Contains("教育", StringComparison.Ordinal))
            return BuildEducationSummaryReason(educations);
        if (type.Contains("灾害", StringComparison.Ordinal) || type.Contains("意外", StringComparison.Ordinal))
            return BuildAccidentSummaryReason(accidents);
        if (type.Contains("疾病", StringComparison.Ordinal))
            return BuildDiseaseSummaryReason(diseases);

        return "因家庭突发困难导致基本生活暂时陷入困境，特申请临时救助。";
    }

    /// <summary>因病：逐条"因病，住院时间为{A}到{B}。患病为{C}。"，块间换行；无日期/病名则省略对应子句</summary>
    private static string BuildDiseaseSummaryReason(List<TempReliefDisease> diseases)
    {
        if (diseases.Count == 0) return "因病。";

        var blocks = new List<string>(diseases.Count);
        foreach (var d in diseases)
        {
            var range = FormatSummaryDateRange(d.TreatStartDate, d.TreatEndDate);
            var block = string.IsNullOrWhiteSpace(range) ? "因病" : $"因病，住院时间为{range}";
            block += "。";
            if (!string.IsNullOrWhiteSpace(d.DiseaseName))
                block += $"患病为{d.DiseaseName.Trim()}。";
            blocks.Add(block);
        }
        return string.Join(Environment.NewLine, blocks);
    }

    /// <summary>因学：逐条"在{缴费年份}被{学校}录取。"，块间换行；缺年份/学校则省略对应部分</summary>
    private static string BuildEducationSummaryReason(List<TempReliefEducation> educations)
    {
        if (educations.Count == 0) return "因学。";

        var blocks = new List<string>(educations.Count);
        foreach (var e in educations)
        {
            var hasYear = e.FeeDate != DateTime.MinValue;
            var school = (e.SchoolName ?? string.Empty).Trim();
            blocks.Add((hasYear, !string.IsNullOrWhiteSpace(school)) switch
            {
                (true, true) => $"在{e.FeeDate:yyyy年}被{school}录取。",
                (false, true) => $"被{school}录取。",
                (true, false) => $"在{e.FeeDate:yyyy年}被录取。",
                _ => "因学。"
            });
        }
        return string.Join(Environment.NewLine, blocks);
    }

    /// <summary>因灾：逐条"因{类型}，发生于{时间}，地点{地点}。财产损失{金额}元。"，块间换行；缺字段省略对应子句</summary>
    private static string BuildAccidentSummaryReason(List<TempReliefAccident> accidents)
    {
        if (accidents.Count == 0) return "因灾。";

        var blocks = new List<string>(accidents.Count);
        foreach (var a in accidents)
        {
            var typeText = string.IsNullOrWhiteSpace(a.AccidentType) ? "意外灾害" : a.AccidentType.Trim();
            // 类型值可能自带"因"前缀，重复前置会拼出"因因…"（与 FamilySituationTextBuilder 同类防护）
            var clauses = new List<string>
            {
                typeText.StartsWith("因", StringComparison.Ordinal) ? typeText : $"因{typeText}"
            };
            if (a.HappenDate != DateTime.MinValue) clauses.Add($"发生于{a.HappenDate:yyyy年M月d日}");
            if (!string.IsNullOrWhiteSpace(a.HappenPlace)) clauses.Add($"地点{a.HappenPlace.Trim()}");

            var block = string.Join("，", clauses) + "。";
            if (a.PropertyLoss is > 0) block += $"财产损失{a.PropertyLoss.Value:F2}元。";
            blocks.Add(block);
        }
        return string.Join(Environment.NewLine, blocks);
    }

    /// <summary>日期区间文本：起止均空返回空串；单端仅显示该日；同日仅显示一天；跨日用"到"</summary>
    private static string FormatSummaryDateRange(DateTime start, DateTime end)
    {
        if (start == DateTime.MinValue && end == DateTime.MinValue) return string.Empty;
        if (start == DateTime.MinValue) return end.ToString("yyyy年M月d日");
        if (end == DateTime.MinValue) return start.ToString("yyyy年M月d日");
        if (start.Date == end.Date) return start.ToString("yyyy年M月d日");
        return $"{start:yyyy年M月d日}到{end:yyyy年M月d日}";
    }

    /// <summary>
    /// 临时救助自付金额合计 = 疾病 self_paid（按"同一次医疗事件"去重，单人多病例只计一次住院）
    /// + 教育 tuition_fee + 意外(property_loss − compensation_paid，负数按 0)；为 0 显示空。
    /// </summary>
    private static string CalcTempReliefSelfPay(
        List<TempReliefDisease> diseases, List<TempReliefAccident> accidents, List<TempReliefEducation> educations)
    {
        // 去重口径与临时救助打印 builder 共用（单点实现）
        var diseaseSelf = TempReliefPrintDataBuilder.DistinctDiseasesByExpense(diseases)
            .Where(d => d.SelfPaid.HasValue)
            .Sum(d => d.SelfPaid!.Value);

        var accidentSelf = accidents.Sum(a =>
        {
            var net = (a.PropertyLoss ?? 0m) - (a.CompensationPaid ?? 0m);
            return net > 0 ? net : 0m;
        });

        var educationSelf = educations.Sum(e => e.TuitionFee ?? 0m);

        var total = diseaseSelf + accidentSelf + educationSelf;
        return total > 0 ? total.ToString("F2") : string.Empty;
    }

    private class TempReliefSummaryRowData
    {
        public long ReliefId { get; set; }
        public string? ReliefType { get; set; }
        public string? ApplicantName { get; set; }
        public string? ApplicantIdCard { get; set; }
        public string? Gender { get; set; }
        public int? Age { get; set; }
        public string? Phone { get; set; }
        public int? FamilySize { get; set; }
        public string? FamilyAddress { get; set; }
        public string? FamilyCategory { get; set; }
        public string? DifficultyType { get; set; }
        public decimal? ConfirmAmount { get; set; }
        public DateTime? ConfirmedAt { get; set; }
        public string? BankAccount { get; set; }
    }

    // ─────────────────── 会议记录 ───────────────────

    public async Task<Result<MonthlyMeeting>> GetMeetingAsync(int year, int month, string town, bool specialApproval = false, CancellationToken ct = default)
    {
        var t = string.IsNullOrEmpty(town) ? "全部" : town;
        var result = await _db.QuerySingleAsync<MonthlyMeeting>(
            "SELECT * FROM nc_biz_monthly_meetings WHERE year = $1 AND month = $2 AND town = $3", ct, year, month, t);
        if (result.IsSuccess && result.Value != null)
        {
            // 库中记录已保存但新增成员信息为空时：自动带出，避免用户看不到新增成员
            var autoResult = await BuildMeetingAutoFieldsAsync(year, month, t, specialApproval, ct);
            if (autoResult.IsSuccess)
            {
                if (string.IsNullOrWhiteSpace(result.Value.MemberInfo))
                {
                    result.Value.MemberInfo = autoResult.Value.MemberInfo;
                    result.Value.ApplyCategory = autoResult.Value.ApplyCategory;
                }
                // 决议段姓名列表始终以周期内新增明细为准（与自动带出的成员信息同源）
                result.Value.MemberNames = autoResult.Value.NameList;
            }
            return Result.Success(result.Value);
        }

        // 不存在：返回空记录，新增成员信息/申请分类自动从周期内新增明细带出
        var meeting = new MonthlyMeeting { Year = year, Month = month, Town = t };
        var added = await BuildMeetingAutoFieldsAsync(year, month, t, specialApproval, ct);
        if (added.IsSuccess)
        {
            meeting.MemberInfo = added.Value.MemberInfo;
            meeting.ApplyCategory = added.Value.ApplyCategory;
            meeting.MemberNames = added.Value.NameList;
        }
        return Result.Success(meeting);
    }

    /// <summary>
    /// 自动带出会议新增成员信息/申请分类：周期内新增明细 + 一事一议分流。
    /// onlySpecialApproval=true：仅保留特殊审批（special_approvals 关联申请，一事一议模板专用）；
    /// false：普通会议记录 = 非一事一议申请。
    /// member_info 每行格式：柳树镇X村村民 姓名 家庭情况说明：{申请原因详情}（分类）。
    /// </summary>
    private async Task<Result<(string MemberInfo, string ApplyCategory, string NameList)>> BuildMeetingAutoFieldsAsync(
        int year, int month, string town, bool onlySpecialApproval, CancellationToken ct = default)
    {
        // 会议记录表用 "全部" 存库，而 GetAddedRowsAsync 以 $3='' 表示不过滤乡镇——须归一化
        var queryTown = town == "全部" ? "" : town;
        var addedResult = await GetAddedRowsAsync(year, month, queryTown, null, ct);
        if (addedResult.IsFailure)
            return Result.Failure<(string, string, string)>(addedResult.ErrorCode!, addedResult.Message!);

        // 一事一议标记：关联 nc_biz_special_approvals（申请层面标记），命中则分流到一事一议模板
        var specialIds = new HashSet<long>();
        var sp = await _db.QueryAsync<SpecialApprovalIdRow>(
            @"SELECT application_id FROM nc_biz_special_approvals WHERE deleted_at IS NULL", ct);
        if (sp.IsSuccess && sp.Value != null)
            foreach (var s in sp.Value)
                if (s.ApplicationId.HasValue)
                    specialIds.Add(s.ApplicationId.Value);

        var candidates = addedResult.Value.Where(r =>
            onlySpecialApproval
                ? r.ApplicationId > 0 && specialIds.Contains(r.ApplicationId)
                : !(r.ApplicationId > 0 && specialIds.Contains(r.ApplicationId))).ToList();

        var memberInfo = string.Join(Environment.NewLine, candidates.Select(BuildMemberInfoLine));
        var applyCategory = string.Join("、", candidates.Select(r => r.Classification).Distinct());
        var nameList = string.Join("、", candidates.Select(r => r.Name));
        return Result.Success((memberInfo, applyCategory, nameList));
    }

    /// <summary>
    /// 拼装会议记录"家庭情况说明"整段：柳树镇{村}村民 {姓名} 家庭情况说明：{申请原因详情}（分类）。
    /// 村名取 community 去"村委会/村民委员会/社区"后缀；申请原因详情去尾句号避免双句号。
    /// </summary>
    private static string BuildMemberInfoLine(MonthlyAddedRow r)
    {
        var village = TrimVillageSuffix(r.Community);
        var townPrefix = string.IsNullOrWhiteSpace(r.Town) ? string.Empty : r.Town;
        // 村：柳树镇柳树村村民；社区/居委：柳树镇XX社区（不加"村民"）
        var isCommunity = village.Contains("社区", StringComparison.Ordinal)
                          || village.Contains("街道", StringComparison.Ordinal)
                          || village.Contains("居委会", StringComparison.Ordinal);
        var head = string.IsNullOrWhiteSpace(village)
            ? $"{r.Name} 家庭情况说明："
            : isCommunity
                ? $"{townPrefix}{village} 家庭情况说明："
                : $"{townPrefix}{village}村民 {r.Name} 家庭情况说明：";
        // 家庭情况说明优先按最新申请明细现场重建（每次生成即更新，不用库中旧文本）；无快照时回退库中旧文本
        var reasonDetail = r.FamilyContext != null
            ? FamilySituationTextBuilder.Build(r.FamilyContext).TrimEnd('。')
            : (string.IsNullOrWhiteSpace(r.ReasonDetail) ? string.Empty : r.ReasonDetail.TrimEnd('。'));
        var category = ClassificationConstants.ConvertFromCode(r.CategoryCode);
        if (string.IsNullOrWhiteSpace(category))
            return string.IsNullOrWhiteSpace(reasonDetail) ? r.Name : $"{head}{reasonDetail}。";
        if (string.IsNullOrWhiteSpace(reasonDetail))
            return head;
        return $"{head}{reasonDetail}（{category}）。";
    }

    /// <summary>
    /// 社区/村名规范化：去"村民委员会/村委会"后缀并补"村"（柳树村委会→柳树村）；
    /// 社区/居委会保留原名（作"社区居民"表述，不加"村民"）
    /// </summary>
    private static string TrimVillageSuffix(string? community)
    {
        var c = (community ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(c)) return string.Empty;
        foreach (var suffix in new[] { "村民委员会", "村委会" })
        {
            if (c.EndsWith(suffix, StringComparison.Ordinal) && c.Length > suffix.Length)
            {
                var v = c[..^suffix.Length];
                return v.EndsWith("村", StringComparison.Ordinal) ? v : v + "村";
            }
        }
        return c;
    }

    public async Task<Result<MonthlyMeeting>> SaveMeetingAsync(MonthlyMeeting meeting, CancellationToken ct = default)
    {
        meeting.Town = string.IsNullOrEmpty(meeting.Town) ? "全部" : meeting.Town;

        var exist = await _db.QuerySingleAsync<MonthlyMeeting>(
            "SELECT id FROM nc_biz_monthly_meetings WHERE year = $1 AND month = $2 AND town = $3",
            ct, meeting.Year, meeting.Month, meeting.Town);
        if (exist.IsSuccess && exist.Value != null)
        {
            // member_info 为空时保留库中原值（避免覆盖自动带出的新增成员列表）
            var upd = @"UPDATE nc_biz_monthly_meetings SET meeting_time=$4, host=$5, recorder=$6, attendees=$7,
                        absentees=$8, member_info=COALESCE(NULLIF($9, ''), member_info), apply_category=$10, updated_at=NOW()
                        WHERE year=$1 AND month=$2 AND town=$3";
            var r = await _db.ExecuteNonQueryAsync(upd, ct, meeting.Year, meeting.Month, meeting.Town,
                meeting.MeetingTime, meeting.Host, meeting.Recorder, meeting.Attendees,
                meeting.Absentees, meeting.MemberInfo, meeting.ApplyCategory);
            if (r.IsFailure) return Result.Failure<MonthlyMeeting>(r.ErrorCode!, r.Message!);
            Logger.LogBusiness("更新会议记录", ("Year", meeting.Year), ("Month", meeting.Month), ("Town", meeting.Town));
            return Result.Success(meeting);
        }

        var sql = @"INSERT INTO nc_biz_monthly_meetings
            (year, month, town, meeting_time, host, recorder, attendees, absentees, member_info, apply_category, created_by, created_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, NOW())
            RETURNING id";
        var ins = await _db.ExecuteScalarAsync(sql, ct, meeting.Year, meeting.Month, meeting.Town,
            meeting.MeetingTime, meeting.Host, meeting.Recorder, meeting.Attendees, meeting.Absentees,
            meeting.MemberInfo, meeting.ApplyCategory, meeting.CreatedBy);
        if (ins.IsFailure) return Result.Failure<MonthlyMeeting>(ins.ErrorCode!, ins.Message!);
        meeting.Id = ins.Value;
        Logger.LogBusiness("保存会议记录", ("Year", meeting.Year), ("Month", meeting.Month), ("Town", meeting.Town));
        return Result.Success(meeting);
    }

    public async Task<Result<bool>> SaveMeetingAttendanceHistoryAsync(
        int year, int month, string town, string attendees, string absentees, string createdBy, CancellationToken ct = default)
    {
        var t = string.IsNullOrEmpty(town) ? "全部" : town;
        var sql = @"INSERT INTO nc_biz_meeting_attendance_history
            (year, month, town, attendees, absentees, created_by, created_at)
            VALUES ($1, $2, $3, $4, $5, $6, NOW())
            RETURNING id";
        var ins = await _db.ExecuteScalarAsync(sql, ct, year, month, t, attendees, absentees, createdBy);
        if (ins.IsFailure)
            return Result.Failure<bool>(ins.ErrorCode!, ins.Message!);
        Logger.LogBusiness("保存会议出席/缺席历史", ("Year", year), ("Month", month), ("Town", t));
        return Result.Success(true);
    }

    public async Task<Result<MonthlyMeetingAttendance>> GetLatestMeetingAttendanceAsync(
        int year, int month, string town, CancellationToken ct = default)
    {
        var t = string.IsNullOrEmpty(town) ? "全部" : town;
        var result = await _db.QuerySingleAsync<MonthlyMeetingAttendance>(
            @"SELECT id, year, month, town, attendees, absentees, created_by, created_at
              FROM nc_biz_meeting_attendance_history
              WHERE year = $1 AND month = $2 AND town = $3
              ORDER BY created_at DESC, id DESC
              LIMIT 1", ct, year, month, t);
        if (result.IsFailure)
            return Result.Failure<MonthlyMeetingAttendance>(result.ErrorCode!, result.Message!);
        return Result.Success(result.Value ?? new MonthlyMeetingAttendance { Year = year, Month = month, Town = t });
    }

    // ─────────────────── 导出 ───────────────────

    public async Task<Result<byte[]>> ExportAsync(int year, int month, string format = "Excel", CancellationToken ct = default)
    {
        var reportResult = await GetAsync(year, month, ct);
        if (reportResult.IsFailure)
            return Result.Failure<byte[]>(reportResult.ErrorCode!, reportResult.Message!);
        var report = reportResult.Value;
        if (report == null)
            return Result.Failure<byte[]>(ErrorCodes.NOT_FOUND, "报表不存在");

        byte[] bytes;
        if (string.Equals(format, "Excel", StringComparison.OrdinalIgnoreCase))
        {
            OfficeOpenXml.ExcelPackage.License.SetNonCommercialOrganization("民政社会救助管理系统");
            using var package = new OfficeOpenXml.ExcelPackage();
            var ws = package.Workbook.Worksheets.Add($"{year}年{month}月月报表");
            var headers = new[] { "年份", "月份", "总档案数", "新增档案", "停止档案", "总金额" };
            for (var i = 0; i < headers.Length; i++)
            {
                ws.Cells[1, i + 1].Value = headers[i];
                ws.Cells[1, i + 1].Style.Font.Bold = true;
            }
            ws.Cells[2, 1].Value = year;
            ws.Cells[2, 2].Value = month;
            ws.Cells[2, 3].Value = report.TotalArchives;
            ws.Cells[2, 4].Value = report.NewArchives;
            ws.Cells[2, 5].Value = report.StoppedArchives;
            ws.Cells[2, 6].Value = report.TotalAmount;
            for (var i = 1; i <= headers.Length; i++)
                ws.Column(i).Width = 14;
            bytes = package.GetAsByteArray();
        }
        else
        {
            var csv = $"年份,月份,总档案数,新增档案,停止档案,总金额\n{year},{month},{report.TotalArchives},{report.NewArchives},{report.StoppedArchives},{report.TotalAmount}";
            bytes = global::System.Text.Encoding.UTF8.GetBytes(csv);
        }
        LogInfo($"报表导出成功: {year}年{month}月");
        return Result.Success(bytes);
    }

    // ─────────────────── 私有映射实体 ───────────────────

    private class MonthlyAggregate
    {
        public int TotalArchives { get; set; }
        public int NewArchives { get; set; }
        public int StoppedArchives { get; set; }
        public decimal TotalAmount { get; set; }
    }

    private class ClassificationCount
    {
        public string ClassificationResult { get; set; } = string.Empty;
        public int Count { get; set; }
        public decimal TotalAmount { get; set; }
    }

    private class AddedRowData
    {
        public long? ApplicationId { get; set; }
        public string? ApplicantName { get; set; }
        public string? ApplicantIdCard { get; set; }
        public string? Gender { get; set; }
        public string? Address { get; set; }
        public int? FamilySize { get; set; }
        public string? ClassificationResult { get; set; }
        public DateTime? ApprovalAt { get; set; }
        public string? District { get; set; }
        public string? Town { get; set; }
        public string? Community { get; set; }
        public string? HukouType { get; set; }
        public string? ApplicationReason { get; set; }
        public string? ApplicationReasonDetail { get; set; }
        public string? MaritalStatus { get; set; }
        public string? HealthStatus { get; set; }
        public string? BankAccount { get; set; }
        public decimal? TotalFamilyIncome { get; set; }
        public decimal? WorkIncomeTotal { get; set; }
        public decimal? BusinessIncomeTotal { get; set; }
        public decimal? PropertyIncomeTotal { get; set; }
        public decimal? TransferIncomeTotal { get; set; }
        public decimal? OtherIncomeTotal { get; set; }
        public decimal? PerCapitaIncome { get; set; }
        public decimal? TotalAnnualIncome { get; set; }
        public decimal? PerCapitaAnnualIncome { get; set; }
        public decimal? RigidExpenditure { get; set; }
        public decimal? AlimonyIncome { get; set; }
        public decimal? FamilyLandArea { get; set; }
        public decimal? LandIncomeTotal { get; set; }
        public decimal? SubsidyTotal { get; set; }
    }

    private class SpecialApprovalIdRow
    {
        public long? ApplicationId { get; set; }
    }

    private class SickMemberRow
    {
        public long? ApplicationId { get; set; }
        public string? Name { get; set; }
        public string? HealthStatus { get; set; }
    }

    private class SupporterGroupRow
    {
        public long? ApplicationId { get; set; }
        public string? PersonType { get; set; }
        public long? Cnt { get; set; }
        public decimal? TotalFee { get; set; }
    }

    private class AssetCountRow
    {
        public long? ApplicationId { get; set; }
        public long? Cnt { get; set; }
    }

    private class HeadRelationRow
    {
        public long? ApplicationId { get; set; }
        public string? RelationshipToHead { get; set; }
    }

    private class StoppedRowData
    {
        public string? HeadName { get; set; }
        public string? HeadIdCard { get; set; }
        public int? FamilySize { get; set; }
        public string? Address { get; set; }
        public string? ClassificationResult { get; set; }
        public decimal? TotalGuaranteeAmount { get; set; }
        public string? StopReason { get; set; }
        public DateTime? StopDate { get; set; }
        public DateTime? FirstApprovedAt { get; set; }
        public decimal? ClassifiedSubsidyAmount { get; set; }
        public string? District { get; set; }
        public string? Town { get; set; }
        public string? Community { get; set; }
        public string? SourceTable { get; set; }   // 跨类停止行：导入库表名（追溯旧户月保障金/分类施保）
        public long? SourceId { get; set; }        // 跨类停止行：导入库家庭表 ID
    }

    /// <summary>成员减员行（边缘家庭"停止、减员汇总表"——减员类别；来源：成员变更/成员死亡变更记录）</summary>
    private class MemberReductionRowData
    {
        public long ChangeId { get; set; }
        public long ApplicationId { get; set; }
        public DateTime? ChangeDate { get; set; }
        public string? OldClassification { get; set; }
        public string? HeadName { get; set; }
        public string? HeadIdCard { get; set; }
        public int? FamilySize { get; set; }
        public string? District { get; set; }
        public string? Town { get; set; }
        public string? Community { get; set; }
        public string? Address { get; set; }

        /// <summary>减员明细（多行：姓名|原因名|yyyy-MM-dd|备注，来源 nc_biz_change_details）</summary>
        public string? MemberDetail { get; set; }

        /// <summary>死亡记录（多行：姓名|死亡日期，来源 nc_biz_death_records）</summary>
        public string? DeathInfo { get; set; }

        /// <summary>明细缺失时的兜底原因（变更记录 change_reason 原文）</summary>
        public string? FallbackReason { get; set; }
    }

    /// <summary>导入库（农村/城市低保）家庭表追溯行：旧户月保障金 + 分类施保</summary>
    private sealed class ImportedStopAmountRow
    {
        public string? SourceTable { get; set; }
        public long? SourceId { get; set; }
        public decimal? MonthlyGuaranteeAmount { get; set; }
        public decimal? FamilyClassifiedAmount { get; set; }
        public decimal? PersonClassifiedAmount { get; set; }
    }

    /// <summary>
    /// 按 (source_table, source_id) 批量追溯导入库农村/城市低保家庭表的旧户月保障金与分类施保。
    /// 跨类停止（CategoryStop，原为低保）的旧金额在变类后被主表覆盖，只能从导入库原档案追溯。
    /// 表名走白名单（农村/城市低保家庭表），杜绝动态表名 SQL 注入。
    /// </summary>
    private async Task<Result<Dictionary<(string Table, long Id), (decimal Monthly, decimal Classified)>>> LoadImportedStopAmountMapAsync(
        IEnumerable<(string Table, long Id)> sourceKeys, CancellationToken ct = default)
    {
        var map = new Dictionary<(string, long), (decimal, decimal)>();
        var keys = (sourceKeys ?? Enumerable.Empty<(string, long)>())
            .Where(k => !string.IsNullOrWhiteSpace(k.Table) && k.Id > 0)
            .Distinct()
            .ToArray();
        if (keys.Length == 0)
            return Result.Success(map);

        foreach (var grp in keys.GroupBy(k => k.Table))
        {
            // 白名单：仅允许农村/城市低保家庭表（跨类停止的原享受类别必为低保）
            var table = grp.Key switch
            {
                "nc_biz_rural_subsistence_families" => "nc_biz_rural_subsistence_families",
                "nc_biz_urban_subsistence_families" => "nc_biz_urban_subsistence_families",
                _ => null
            };
            if (table == null) continue;
            var ids = grp.Select(g => g.Id).Distinct().ToArray();
            var sql = $@"SELECT '{table}'::text AS source_table, id AS source_id,
                                monthly_guarantee_amount, family_classified_amount, person_classified_amount
                         FROM {table}
                         WHERE id = ANY($1::bigint[])";
            var result = await _db.QueryAsync<ImportedStopAmountRow>(sql, ct, ids);
            if (result.IsFailure)
                return Result.Failure<Dictionary<(string, long), (decimal, decimal)>>(result.ErrorCode!, result.Message!);
            foreach (var row in result.Value ?? new List<ImportedStopAmountRow>())
            {
                if (row.SourceId == null) continue;
                map[(table, row.SourceId.Value)] = (
                    row.MonthlyGuaranteeAmount ?? 0,
                    (row.FamilyClassifiedAmount ?? 0) + (row.PersonClassifiedAmount ?? 0));
            }
        }
        return Result.Success(map);
    }

    private class AmountChangeRow
    {
        public long? ChangeId { get; set; }
        public string? HeadName { get; set; }
        public string? HeadIdCard { get; set; }
        public int? FamilySize { get; set; }
        public string? Address { get; set; }
        public string? ClassificationResult { get; set; }
        public DateTime? ChangeDate { get; set; }
        public decimal? OldGuaranteeAmount { get; set; }
        public decimal? NewGuaranteeAmount { get; set; }
        public string? ChangeReason { get; set; }
        public string? ChangeReasonType { get; set; }
        public decimal? ClassifiedSubsidyAmount { get; set; }
        public string? District { get; set; }
        public string? Town { get; set; }
        public string? Community { get; set; }
    }

    private class DeathRow
    {
        public string? HeadName { get; set; }
        public string? HeadIdCard { get; set; }
        public string? Address { get; set; }
        public string? DeceasedName { get; set; }
        public string? MemberIdCard { get; set; }
        public DateTime? DeathDate { get; set; }
        public string? NewHeadName { get; set; }
        public string? ClassificationResult { get; set; }
        public string? District { get; set; }
        public string? Town { get; set; }
        public string? Community { get; set; }
    }

    /// <summary>退出事件行（三源 UNION 识别退出对象）</summary>
    private class ExitEventRow
    {
        public long? ApplicationId { get; set; }
        public DateTime? EventDate { get; set; }
        public string? EventType { get; set; }
        public string? ExitReason { get; set; }
        public string? OldClassification { get; set; }
    }

    /// <summary>退出对象申请详情行</summary>
    private class ExitDetailRow
    {
        public long Id { get; set; }
        public string? ApplicantName { get; set; }
        public string? ApplicantIdCard { get; set; }
        public int? FamilySize { get; set; }
        public string? ClassificationResult { get; set; }
        public string? Status { get; set; }
    }

    /// <summary>渐退期判定行</summary>
    private class ExitGraceRow
    {
        public long? ApplicationId { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }

    /// <summary>停旧建新目标档案行（原档案 → 新档案分类）</summary>
    private class RebuildTargetRow
    {
        public long? OriginalApplicationId { get; set; }
        public string? ClassificationResult { get; set; }
    }

    /// <summary>户主变更档案行（户主死亡/户主变更变更记录所在档案）</summary>
    private class HeadChangeAppRow
    {
        public long? ApplicationId { get; set; }
    }

    /// <summary>跨类停保记录的变更原因类型行（经济复核/成员变更）</summary>
    private class ChangeReasonTypeRow
    {
        public long? ApplicationId { get; set; }
        public string? ChangeReasonType { get; set; }
    }

    /// <summary>
    /// 导入库纳入时间（享受日期来源）映射行
    /// </summary>
    private class ImportedDateRow
    {
        public string? ApplicantIdCard { get; set; }
        public string? ImportedDate { get; set; }
    }

    /// <summary>
    /// 低保导入库户行（施保金减发数据源）
    /// </summary>
    private class ShiBaoFamilyRow
    {
        public string? Category { get; set; }
        public string? ApplicantName { get; set; }
        public string? ApplicantIdCard { get; set; }
        public string? MemberNames { get; set; }
        public string? MemberIdCards { get; set; }
        public decimal PersonClassifiedAmount { get; set; }
        public decimal MonthlyGuaranteeAmount { get; set; }
        public string? Address { get; set; }
        public string? District { get; set; }
        public string? Street { get; set; }
        public string? Town { get; set; }
        public string? Community { get; set; }
        public string? ImportedDate { get; set; }
        public bool IsRural { get; set; }
    }

    /// <summary>
    /// 分类施保增发候选户成员行（低保导入库 persons JOIN families，含户内全部成员；
    /// TotalClassifiedAmount 用于区分符合增发条件成员与户内其他成员）
    /// </summary>
    private class ClassifiedAddPersonRow
    {
        public string? Category { get; set; }
        public string? ApplicantName { get; set; }
        public string? ApplicantIdCard { get; set; }
        public string? Phone { get; set; }
        public int? FamilySize { get; set; }
        public string? Address { get; set; }
        public string? District { get; set; }
        public string? Street { get; set; }
        public string? Town { get; set; }
        public string? Community { get; set; }
        public string? MemberName { get; set; }
        public string? Gender { get; set; }
        public DateTime? BirthDate { get; set; }
        public string? Ethnicity { get; set; }
        public string? HealthStatus { get; set; }
        public string? DisabilityType { get; set; }
        public string? DisabilityLevel { get; set; }
        public string? Relationship { get; set; }
        public decimal? AnnualIncome { get; set; }
        public string? MemberIdCard { get; set; }
        public decimal TotalClassifiedAmount { get; set; }
        public bool IsRural { get; set; }
    }

    /// <summary>
    /// 分类施保增发落库快照行（nc_biz_monthly_classified_adds，members_json 为 jsonb → string）
    /// </summary>
    private class ClassifiedAddSnapshotRow
    {
        public string? Category { get; set; }
        public bool IsRural { get; set; }
        public string? ApplicantIdCard { get; set; }
        public string? ApplicantName { get; set; }
        public string? ApplicantGender { get; set; }
        public string? ApplicantBirthDate { get; set; }
        public string? Nationality { get; set; }
        public string? HealthStatus { get; set; }
        public int? FamilySize { get; set; }
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? ClassifiedType { get; set; }
        public int? Count { get; set; }
        public decimal? Amount { get; set; }
        public string? MembersJson { get; set; }
    }
}