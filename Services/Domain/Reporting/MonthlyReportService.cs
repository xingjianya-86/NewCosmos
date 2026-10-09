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
public partial class MonthlyReportService : BaseService, IMonthlyReportService
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
            sql = "SELECT * FROM nc_biz_monthly_reports WHERE year = $1 ORDER BY month DESC LIMIT 1000";
            parameters = new object[] { year.Value };
        }
        else
        {
            sql = "SELECT * FROM nc_biz_monthly_reports ORDER BY year DESC, month DESC LIMIT 1000";
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
                COUNT(*) FILTER (WHERE status = $4 AND (stop_date IS NULL OR stop_date >= $3::date)) AS total_archives,
                COUNT(*) FILTER (WHERE status = $4 AND original_application_id IS NULL AND first_approved_at >= $1::timestamp AND first_approved_at < $2::timestamp) AS new_archives,
                COUNT(*) FILTER (WHERE status = $5 AND stop_date >= $1::date AND stop_date < $2::date
                    {StoppedArchiveFilter.NotRebuildContinuationSql("nc_biz_applications")}) AS stopped_archives,
                COALESCE(SUM(total_guarantee_amount) FILTER (WHERE status = $4 AND (stop_date IS NULL OR stop_date >= $3::date)), 0) AS total_amount
            FROM nc_biz_applications
            WHERE deleted_at IS NULL;
";

        // 查询失败必须作为 Failure 向上传播，绝不允许"吞异常返回空集合"：
        // 全 0 的"成功"会被 GenerateAsync 写成一份全 0 的正式月报，且环境问题会被静默掩盖。
        var aggregateResult = await _db.QuerySingleAsync<MonthlyAggregate>(aggregateSql, ct, start, end, end,
            ApplicationStatusCodes.APPROVED, ApplicationStatusCodes.STOPPED);
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

        var classificationSql = @"SELECT classification_result, COUNT(*) AS classification_count, COALESCE(SUM(total_guarantee_amount), 0) AS total_amount
                                  FROM nc_biz_applications
                                  WHERE status = $2 AND (stop_date IS NULL OR stop_date >= $1::date) AND deleted_at IS NULL
                                  GROUP BY classification_result;
";
        var classificationResult = await _db.QueryAsync<ClassificationCount>(classificationSql, ct, end, ApplicationStatusCodes.APPROVED);
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
        var parameters = new List<object> { c.Start, c.End, town ?? "", ApplicationStatusCodes.APPROVED };
        var categorySql = string.Empty;
        if (codes != null)
        {
            parameters.Add(codes);
            categorySql = " AND a.classification_result = ANY($5::text[])";
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
                     WHERE a.status = $4 AND a.first_approved_at >= $1 AND a.first_approved_at < $2 AND a.deleted_at IS NULL
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
                // [历史数据兼容豁免] 年值列缺失（total_annual_income IS NULL 的老数据）时按月值×12 近似，
                // 一次性舍入到分；新数据一律走年值列（§9 禁止月值反推，此为无年值可用时的唯一兜底）
                AnnualIncome = item.TotalAnnualIncome ?? Math.Round((item.TotalFamilyIncome ?? 0) * 12, 2),
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
        var parameters = new List<object> { c.Start, c.End, town ?? "", ApplicationStatusCodes.STOPPED };
        var categorySql = string.Empty;
        if (codes != null)
        {
            parameters.Add(codes);
            categorySql = " AND a.classification_result = ANY($5::text[])";
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
                     WHERE a.status = $4 AND a.stop_date >= $1 AND a.stop_date < $2 AND a.deleted_at IS NULL
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
        var crossParams = new List<object> { c.Start, c.End, town ?? "", ApplicationStatusCodes.STOPPED };
        var crossCategorySql = string.Empty;
        if (codes != null)
        {
            crossParams.Add(codes);
            crossCategorySql = " AND cr.old_classification = ANY($5::text[])";
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
                       AND a.status <> $4
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
    /// 分类施保金减发（两个数据源）：
    /// ① 低保导入库：农村/城市低保导入表 person_classified_amount&gt;0 的户，户内本月满 18 周岁成员
    ///    （含户主）每人一行——即 18 周岁生日落在本月自然月 [本月1日, 下月1日) 的成员，
    ///    享受日期=纳入时间（imported_at），减发金额=人员分类施保金额，原/现分类施保金=0。
    /// ② 户主死亡进入渐退期的分类施保减发（nc_biz_change_records，change_type=ClassifiedSubsidyReduce，
    ///    挂旧档）：changed_at 落在本月 B 线周期（与保障金增减发同口径）；户主/地址/类别/原现户月取旧档，
    ///    减发成员取死亡记录（缺失回退旧档户主=死亡户主本人），原/现分类施保取记录 old/new_guarantee_amount。
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

        // ② 户主死亡渐退分类施保减发（变更记录口径；B 线周期内 changed_at，先于享受日期映射取出）
        var cycleResult = await GetCycleRangeAsync(year, month, ct);
        if (cycleResult.IsFailure)
            return Result.Failure<List<MonthlyShiBaoRow>>(cycleResult.ErrorCode!, cycleResult.Message!);
        var cycle = cycleResult.Value;

        var reduceSql = @"SELECT cr.id, cr.change_date, cr.old_guarantee_amount, cr.new_guarantee_amount,
                                 a.applicant_name, a.applicant_id_card, a.classification_result,
                                 a.address, a.district, a.town, a.community,
                                 a.household_monthly_guarantee_amount AS old_monthly,
                                 n.household_monthly_guarantee_amount AS new_monthly,
                                 d.member_name AS deceased_name
                          FROM nc_biz_change_records cr
                          JOIN nc_biz_applications a ON a.id = cr.application_id AND a.deleted_at IS NULL
                          LEFT JOIN nc_biz_applications n ON n.id = cr.new_application_id AND n.deleted_at IS NULL
                          LEFT JOIN LATERAL (
                              SELECT member_name FROM nc_biz_death_records d
                              WHERE d.application_id = cr.application_id AND d.is_household_head
                                AND (cr.new_application_id IS NULL OR d.new_application_id = cr.new_application_id)
                              ORDER BY d.id DESC LIMIT 1
                          ) d ON TRUE
                          WHERE cr.change_type = $1 AND cr.deleted_at IS NULL
                            AND cr.old_guarantee_amount > cr.new_guarantee_amount
                            AND cr.changed_at >= $2 AND cr.changed_at < $3
                          ORDER BY cr.id";
        var reduceResult = await _db.QueryAsync<ShiBaoReduceRow>(reduceSql, ct,
            DictionaryConstants.ChangeType.CLASSIFIED_SUBSIDY_REDUCE, cycle.Start, cycle.End);
        if (reduceResult.IsFailure)
            return Result.Failure<List<MonthlyShiBaoRow>>(reduceResult.ErrorCode!, reduceResult.Message!);
        var reduceRows = reduceResult.Value ?? new List<ShiBaoReduceRow>();

        // 初次享受日期统一：导入库建档户取纳入时间、其余取首次审批时间（LoadApprovalDateMapAsync），与停保/增减发/自然减员同口径
        var dateMapResult = await LoadApprovalDateMapAsync(
            families.Select(f => f.ApplicantIdCard).Concat(reduceRows.Select(r => r.ApplicantIdCard)), ct);
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

        // ② 减发行装配：户主=旧档户主（樊万河户），减发成员=死亡记录户主姓名，低保金原/现=旧/新档户月
        foreach (var red in reduceRows)
        {
            var enjoy = dateMap.TryGetValue(red.ApplicantIdCard ?? "", out var rdt)
                ? rdt
                : monthLast.ToString("yyyy-MM-dd");

            rows.Add(new MonthlyShiBaoRow
            {
                HeadName = red.ApplicantName ?? "",
                MemberName = string.IsNullOrEmpty(red.DeceasedName) ? red.ApplicantName ?? "" : red.DeceasedName,
                Address = JoinFullAddress(red.District, red.Town, red.Community, red.Address),
                Category = "最低生活保障对象",
                EnjoyDate = enjoy,
                OriginalAmount = red.OldMonthly.ToString("F2"),
                OriginalClassified = red.OldGuaranteeAmount.ToString("F2"),
                Reason = ChangeReasonTypeConstants.HeadDeceased,
                DecreaseAmount = (red.OldGuaranteeAmount - red.NewGuaranteeAmount).ToString("F2"),
                CurrentAmount = (red.NewMonthly ?? red.OldMonthly).ToString("F2"),
                CurrentClassified = red.NewGuaranteeAmount.ToString("F2"),
                IsRural = ClassificationConstants.IsCodeRural(red.ClassificationResult ?? "")
            });
        }

        return Result.Success(rows);
    }

}