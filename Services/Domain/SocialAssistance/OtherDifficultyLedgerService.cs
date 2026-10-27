using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <inheritdoc cref="IOtherDifficultyLedgerService" />
public class OtherDifficultyLedgerService : BaseService, IOtherDifficultyLedgerService
{
    protected override string ServiceName => "OtherDifficultyLedgerService";

    private readonly IDatabaseService _db;

    public OtherDifficultyLedgerService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
    }

    private sealed class StopAppRow
    {
        public long Id { get; set; }
        public string? ApplicantName { get; set; }
        public string? ApplicantIdCard { get; set; }
        public DateTime? FirstApprovedAt { get; set; }
        public string? ClassificationResult { get; set; }
        public string? HukouType { get; set; }
        public string? ApplicantPhone { get; set; }
        public string? Address { get; set; }
        public string? Province { get; set; }
        public string? City { get; set; }
        public string? District { get; set; }
        public string? Town { get; set; }
        public string? Community { get; set; }
        public int FamilySize { get; set; }
        public string? StopReason { get; set; }
        public DateTime? StopDate { get; set; }
    }

    /// <inheritdoc />
    public async Task<Result> TransferOnStopAsync(long applicationId, string? stopReason, DateTime? stopDate, CancellationToken ct = default)
    {
        try
        {
            var appResult = await _db.QuerySingleAsync<StopAppRow>(
                @"SELECT id, applicant_name, applicant_id_card, first_approved_at, classification_result,
                         hukou_type, applicant_phone, address, province, city, district, town, community,
                         family_size, stop_reason, stop_date
                  FROM nc_biz_applications
                  WHERE id = $1 AND deleted_at IS NULL", ct, applicationId);
            // 「查询失败」与「档案不存在」必须分开（§7）：
            //   失败   → 如实上抛；调用方 ApplicationStatusService.TryTransferToOtherDifficultyAsync
            //            已有 IsFailure 分支，只告警、不阻断已落库的停保主流程。
            //            折叠成 Success 会静默漏写台账，事后无从发现。
            //   不存在 → 读不到档案不是停保失败的理由：仅告警后放行。
            if (appResult.IsFailure)
                return Result.Failure(appResult.ErrorCode!, appResult.Message!);
            if (appResult.Value == null)
            {
                LogWarn($"停保转入其他困难人员跳过：档案不存在 ApplicationId={applicationId}");
                return Result.Success();
            }

            var app = appResult.Value;
            var effectiveReason = stopReason ?? app.StopReason ?? string.Empty;
            var effectiveStopDate = stopDate ?? app.StopDate ?? DateTime.Today;

            // 1) 死亡性停保不转入（户主死亡 / 最后成员死亡 / 档案已登记户主死亡记录）
            if (effectiveReason.Contains("死亡", StringComparison.Ordinal))
            {
                LogInfo($"停保转入其他困难人员跳过：死亡性停保 ApplicationId={applicationId}, Reason={effectiveReason}");
                return Result.Success();
            }

            var deathResult = await _db.ExecuteScalarAsync(
                "SELECT COUNT(*) FROM nc_biz_death_records WHERE application_id = $1 AND is_household_head = true",
                ct, applicationId);
            if (deathResult.IsFailure)
                return Result.Failure(deathResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    deathResult.Message ?? "检查死亡记录失败");
            if (Convert.ToInt64(deathResult.Value) > 0)
            {
                LogInfo($"停保转入其他困难人员跳过：存在户主死亡记录 ApplicationId={applicationId}");
                return Result.Success();
            }

            // 2) 从未获得保障（首次判不合格/从未审批）不属于"停保"
            if (app.FirstApprovedAt == null)
            {
                LogInfo($"停保转入其他困难人员跳过：档案从未获得保障 ApplicationId={applicationId}");
                return Result.Success();
            }

            // 3) 该户仍有其他在保/在途且非停保类的档案 → 属于停旧建新/跨类接续，不属于真正退出
            if (string.IsNullOrWhiteSpace(app.ApplicantIdCard))
                return Result.Success();

            var coveredResult = await _db.ExecuteScalarAsync(
                $@"SELECT COUNT(*)
                   FROM nc_biz_applications a2
                   WHERE a2.id <> $1 AND a2.deleted_at IS NULL
                     AND a2.status IN ($2, $3, $4, $5)
                     AND (a2.classification_result IS NULL OR a2.classification_result = ''
                          OR a2.classification_result NOT IN ({StopCodeLiterals()}))
                     AND (a2.applicant_id_card = $6
                          OR EXISTS (SELECT 1 FROM nc_biz_family_members m2
                                     WHERE m2.application_id = a2.id AND m2.deleted_at IS NULL
                                       AND m2.id_card IN (SELECT m1.id_card FROM nc_biz_family_members m1
                                                          WHERE m1.application_id = $1 AND m1.deleted_at IS NULL)))",
                ct,
                applicationId,
                ApplicationStatusCodes.DRAFT,
                ApplicationStatusCodes.SUBMITTED,
                ApplicationStatusCodes.APPROVED,
                ApplicationStatusCodes.COMPLETED,
                app.ApplicantIdCard);
            if (coveredResult.IsFailure)
                return Result.Failure(coveredResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    coveredResult.Message ?? "检查在保接续失败");
            if (Convert.ToInt64(coveredResult.Value) > 0)
            {
                LogInfo($"停保转入其他困难人员跳过：该户仍有在保/在途档案 ApplicationId={applicationId}");
                return Result.Success();
            }

            // 4) 台账已有同户行（导入或既往转入）→ 不重复写入
            var existingResult = await _db.ExecuteScalarAsync<long?>(
                "SELECT id FROM nc_biz_other_difficulty_families WHERE applicant_id_card = $1 LIMIT 1",
                ct, app.ApplicantIdCard);
            if (existingResult.IsFailure)
                return Result.Failure(existingResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    existingResult.Message ?? "查询其他困难人员台账失败");
            if (existingResult.Value is > 0)
            {
                LogInfo($"停保转入其他困难人员跳过：台账已存在户主 ApplicationId={applicationId}");
                return Result.Success();
            }

            var includeMonth = effectiveStopDate.ToString("yyyy-MM");
            var region = string.Concat(app.Province ?? string.Empty, app.City ?? string.Empty,
                app.District ?? string.Empty, app.Town ?? string.Empty, app.Community ?? string.Empty);

            var familyInsert = await _db.ExecuteScalarAsync<long?>(
                @"INSERT INTO nc_biz_other_difficulty_families
                      (applicant_name, applicant_id_card, guarantee_size, apply_reason, hukou_type, phone,
                       address, province, city, district, street, community, include_month,
                       member_names, member_id_cards, source, source_application_id,
                       imported_at, updated_at)
                  VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13,
                      COALESCE((SELECT string_agg(m.name, ';' ORDER BY m.id)
                                FROM nc_biz_family_members m
                                WHERE m.application_id = $14 AND m.deleted_at IS NULL
                                  AND COALESCE(m.member_category, '') <> 'Support'), ''),
                      COALESCE((SELECT string_agg(m.id_card, ';' ORDER BY m.id)
                                FROM nc_biz_family_members m
                                WHERE m.application_id = $14 AND m.deleted_at IS NULL
                                  AND COALESCE(m.member_category, '') <> 'Support'), ''),
                      'stop', $14, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP)
                  RETURNING id",
                ct,
                app.ApplicantName ?? string.Empty,
                app.ApplicantIdCard,
                app.FamilySize,
                effectiveReason,
                app.HukouType ?? string.Empty,
                app.ApplicantPhone ?? string.Empty,
                app.Address ?? string.Empty,
                app.Province ?? string.Empty,
                app.City ?? string.Empty,
                app.District ?? string.Empty,
                app.Town ?? string.Empty,
                app.Community ?? string.Empty,
                includeMonth,
                applicationId);
            if (familyInsert.IsFailure || familyInsert.Value is not > 0)
                return Result.Failure(familyInsert.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    familyInsert.Message ?? "写入其他困难人员家庭台账失败");

            var familyId = familyInsert.Value!.Value;

            var personInsert = await _db.ExecuteNonQueryAsync(
                @"INSERT INTO nc_biz_other_difficulty_persons
                      (family_id, head_id_card, name, id_card, relationship, gender, ethnicity,
                       marital_status, education_level, political_status, employment_status,
                       monthly_income, is_disabled, disability_certificate, disability_type,
                       disability_level, work_capacity, health_status, hukou_type,
                       is_poverty_policy_population, region, address, include_month,
                       source, source_application_id, imported_at, updated_at)
                  SELECT $1, $2, m.name, m.id_card, COALESCE(m.relationship_to_head, ''),
                         COALESCE(m.gender, ''), COALESCE(m.ethnicity, ''), COALESCE(m.marital_status, ''),
                         COALESCE(m.education_level, ''), COALESCE(m.political_status, ''),
                         COALESCE(m.employment_status, ''),
                         ROUND(COALESCE(m.annual_income, 0) / 12.0, 2),
                         COALESCE(m.is_disabled, false),
                         COALESCE(m.disability_certificate_no, ''),
                         COALESCE(m.disability_type, ''), COALESCE(m.disability_level, ''),
                         COALESCE(m.work_capacity, ''), COALESCE(m.health_status, ''),
                         COALESCE(NULLIF(m.hukou_type, ''), $3),
                         false, $4, COALESCE(NULLIF(m.home_address, ''), $5), $6,
                         'stop', $7, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP
                  FROM nc_biz_family_members m
                  WHERE m.application_id = $7 AND m.deleted_at IS NULL
                    AND COALESCE(m.member_category, '') <> 'Support'
                  ON CONFLICT (id_card) DO NOTHING",
                ct,
                familyId,
                app.ApplicantIdCard,
                app.HukouType ?? string.Empty,
                region,
                app.Address ?? string.Empty,
                includeMonth,
                applicationId);
            if (personInsert.IsFailure)
                return Result.Failure(personInsert.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    personInsert.Message ?? "写入其他困难人员人员台账失败");

            LogInfo($"停保转入其他困难人员台账完成: ApplicationId={applicationId}, 人员={personInsert.Value}");
            Logger.LogBusiness("停保转入其他困难人员",
                ("ApplicationId", applicationId.ToString()),
                ("ApplicantIdCard", DataMasker.MaskIdCard(app.ApplicantIdCard)),
                ("StopReason", effectiveReason),
                ("Persons", personInsert.Value.ToString()));
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "停保转入其他困难人员");
            return Result.FromException(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result> RemoveOnResumeAsync(long applicationId, CancellationToken ct = default)
    {
        try
        {
            var persons = await _db.ExecuteNonQueryAsync(
                "DELETE FROM nc_biz_other_difficulty_persons WHERE source = 'stop' AND source_application_id = $1",
                ct, applicationId);
            if (persons.IsFailure)
                return Result.Failure(persons.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    persons.Message ?? "撤回其他困难人员人员台账失败");

            var families = await _db.ExecuteNonQueryAsync(
                "DELETE FROM nc_biz_other_difficulty_families WHERE source = 'stop' AND source_application_id = $1",
                ct, applicationId);
            if (families.IsFailure)
                return Result.Failure(families.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    families.Message ?? "撤回其他困难人员家庭台账失败");

            if (persons.Value > 0 || families.Value > 0)
            {
                LogInfo($"恢复保障撤回其他困难人员台账: ApplicationId={applicationId}, 家庭={families.Value}, 人员={persons.Value}");
                Logger.LogBusiness("恢复保障撤回其他困难人员台账",
                    ("ApplicationId", applicationId.ToString()),
                    ("Families", families.Value.ToString()),
                    ("Persons", persons.Value.ToString()));
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "恢复保障撤回其他困难人员台账");
            return Result.FromException(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result> RecordTransferOutAsync(long applicationId, CancellationToken ct = default)
    {
        try
        {
            var appResult = await _db.QuerySingleAsync<TransferOutAppRow>(
                @"SELECT id, applicant_name, applicant_id_card, first_approved_at, classification_result,
                         family_size, hukou_type, district, town, community, address
                  FROM nc_biz_applications
                  WHERE id = $1 AND deleted_at IS NULL", ct, applicationId);
            // 「查询失败」与「档案不存在」必须分开（§7）：
            //   失败     → 如实上抛；调用方 ApplicationStatusService.TryRecordOtherDifficultyTransferOutAsync
            //              已有 IsFailure 分支，只告警、不影响审批主流程。折叠成 Success 会静默漏记转出台账。
            //   不存在   → 按原设计放行，不阻断审批。
            if (appResult.IsFailure)
                return Result.Failure(appResult.ErrorCode!, appResult.Message!);
            if (appResult.Value == null)
                return Result.Success();

            var app = appResult.Value;
            if (app.FirstApprovedAt == null)
                return Result.Success(); // 未真正获得保障，不构成转出

            var memberResult = await _db.QueryAsync<IdCardRow>(
                @"SELECT id_card FROM nc_biz_family_members
                  WHERE application_id = $1 AND deleted_at IS NULL
                    AND (is_applicant = TRUE OR COALESCE(member_category, '') <> 'Support')",
                ct, applicationId);
            if (memberResult.IsFailure)
                return Result.Failure(memberResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    memberResult.Message ?? "读取保障成员失败");

            var idCards = (memberResult.Value ?? new List<IdCardRow>())
                .Select(r => r.IdCard)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (!string.IsNullOrWhiteSpace(app.ApplicantIdCard)
                && !idCards.Contains(app.ApplicantIdCard.Trim(), StringComparer.OrdinalIgnoreCase))
                idCards.Add(app.ApplicantIdCard.Trim());
            if (idCards.Count == 0)
                return Result.Success();

            // 一次查出命中的台账人员 + 其家庭剩余未转出人数（决定 停止/减员）
            var odResult = await _db.QueryAsync<TransferOutOdRow>(
                @"SELECT p.id AS person_id, p.family_id, p.name AS person_name, p.id_card AS person_id_card,
                         f.applicant_name AS head_name, f.applicant_id_card AS head_id_card,
                         f.guarantee_size, f.hukou_type, f.district, f.street, f.community, f.address,
                         f.include_month,
                         (SELECT COUNT(*) FROM nc_biz_other_difficulty_persons p2
                          WHERE p2.family_id = p.family_id AND NOT (p2.id_card = ANY($1)))::int AS remaining_count
                  FROM nc_biz_other_difficulty_persons p
                  JOIN nc_biz_other_difficulty_families f ON f.id = p.family_id
                  WHERE p.id_card = ANY($1)", ct, new object[] { idCards.ToArray() });
            if (odResult.IsFailure)
                return Result.Failure(odResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    odResult.Message ?? "读取其他困难人员台账失败");

            var rows = odResult.Value ?? new List<TransferOutOdRow>();
            if (rows.Count == 0)
                return Result.Success();

            // 同一人只记一次转出（防止停旧建新/复核再次审批时按新日期重复写）；停保撤回后允许再写。
            var existingResult = await _db.QueryAsync<IdCardRow>(
                "SELECT person_id_card AS id_card FROM nc_biz_other_difficulty_stops WHERE person_id_card = ANY($1)",
                ct, new object[] { rows.Select(r => r.PersonIdCard ?? string.Empty).Where(x => x.Length > 0).Distinct().ToArray() });
            if (existingResult.IsFailure)
                return Result.Failure(existingResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    existingResult.Message ?? "检查其他困难人员转出记录失败");
            var alreadyStopped = new HashSet<string>(
                (existingResult.Value ?? new List<IdCardRow>())
                    .Select(r => r.IdCard ?? string.Empty)
                    .Where(x => x.Length > 0),
                StringComparer.OrdinalIgnoreCase);

            // 转出日期 = 本次审批/恢复的实际日期（不能用 first_approved_at：导入建档档案的首次享受
            // 时间是历史纳入月，会把转出事件错误地排到历史月份）
            var stopDate = DateTime.Today;
            var targetName = string.IsNullOrWhiteSpace(app.ClassificationResult)
                ? "其他类别"
                : ClassificationConstants.ConvertFromCode(app.ClassificationResult);
            var targetClass = app.ClassificationResult ?? string.Empty;
            var inserted = 0;

            foreach (var od in rows)
            {
                if (!string.IsNullOrWhiteSpace(od.PersonIdCard)
                    && alreadyStopped.Contains(od.PersonIdCard.Trim()))
                    continue;

                var stopType = (od.RemainingCount ?? 0) > 0 ? "减员" : "停止";
                // 减员行按"转出后剩余人数"展示（与边缘停止、减员汇总表口径一致）；停止行保留原保障人口
                var familySize = stopType == "减员"
                    ? (od.RemainingCount ?? 0)
                    : od.GuaranteeSize;
                var reason = string.IsNullOrWhiteSpace(targetName)
                    ? "转出其他困难人员"
                    : $"转出至{targetName}";
                var insert = await _db.ExecuteNonQueryAsync(
                    @"INSERT INTO nc_biz_other_difficulty_stops
                          (family_id, person_id, person_name, person_id_card, head_name, head_id_card,
                           family_size, hukou_type, district, street, community, address, include_month,
                           old_category, stop_type, stop_date, stop_reason, target_application_id,
                           target_classification, source, created_at)
                      VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,
                              '其他困难人员',$14,$15,$16,$17,$18,'approval',NOW())
                      ON CONFLICT (person_id_card, stop_date) DO NOTHING",
                    ct, od.FamilyId, od.PersonId, od.PersonName ?? string.Empty, od.PersonIdCard ?? string.Empty,
                    od.HeadName ?? string.Empty, od.HeadIdCard ?? string.Empty, familySize,
                    od.HukouType ?? string.Empty, od.District ?? string.Empty, od.Street ?? string.Empty,
                    od.Community ?? string.Empty, od.Address ?? string.Empty, od.IncludeMonth ?? string.Empty,
                    stopType, stopDate, reason, applicationId, targetClass);
                if (insert.IsFailure)
                    return Result.Failure(insert.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        insert.Message ?? "写入其他困难人员转出记录失败");
                inserted += insert.Value;
            }

            if (inserted > 0)
            {
                LogInfo($"其他困难人员转出登记: ApplicationId={applicationId}, 人员={inserted}");
                Logger.LogBusiness("其他困难人员转出登记",
                    ("ApplicationId", applicationId.ToString()),
                    ("Persons", inserted.ToString()),
                    ("StopDate", stopDate.ToString("yyyy-MM-dd")),
                    ("Target", targetName));
            }
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "其他困难人员转出登记");
            return Result.FromException(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result> RemoveTransferOutAsync(long applicationId, CancellationToken ct = default)
    {
        try
        {
            var result = await _db.ExecuteNonQueryAsync(
                "DELETE FROM nc_biz_other_difficulty_stops WHERE source = 'approval' AND target_application_id = $1",
                ct, applicationId);
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    result.Message ?? "撤回其他困难人员转出记录失败");
            if (result.Value > 0)
            {
                LogInfo($"撤回其他困难人员转出记录: ApplicationId={applicationId}, 行={result.Value}");
                Logger.LogBusiness("撤回其他困难人员转出记录",
                    ("ApplicationId", applicationId.ToString()), ("Rows", result.Value.ToString()));
            }
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "撤回其他困难人员转出记录");
            return Result.FromException(ex);
        }
    }

    private sealed class TransferOutAppRow
    {
        public long Id { get; set; }
        public string? ApplicantName { get; set; }
        public string? ApplicantIdCard { get; set; }
        public DateTime? FirstApprovedAt { get; set; }
        public string? ClassificationResult { get; set; }
        public int? FamilySize { get; set; }
        public string? HukouType { get; set; }
        public string? District { get; set; }
        public string? Town { get; set; }
        public string? Community { get; set; }
        public string? Address { get; set; }
    }

    private sealed class IdCardRow
    {
        public string? IdCard { get; set; }
    }

    private sealed class TransferOutOdRow
    {
        public long? PersonId { get; set; }
        public long? FamilyId { get; set; }
        public string? PersonName { get; set; }
        public string? PersonIdCard { get; set; }
        public string? HeadName { get; set; }
        public string? HeadIdCard { get; set; }
        public int? GuaranteeSize { get; set; }
        public string? HukouType { get; set; }
        public string? District { get; set; }
        public string? Street { get; set; }
        public string? Community { get; set; }
        public string? Address { get; set; }
        public string? IncludeMonth { get; set; }
        public int? RemainingCount { get; set; }
    }

    /// <summary>停保类分类码 SQL 字面量（编译期常量，无注入风险；与 ClassificationConstants.StopCategoryCodes 同源）。</summary>
    private static string StopCodeLiterals() =>
        string.Join(",", ClassificationConstants.StopCategoryCodes.Select(c => $"'{c.Replace("'", "''")}'"));
}
