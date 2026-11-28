using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.ElderlyBenefits;

/// <summary>
/// 普惠高龄年度认证领域服务。
/// 只读写认证域表（campaigns/tasks/evidence），不直接改申请主表；停发仍走现有 ElderlyApplicationService.StopAsync。
/// </summary>
public class ElderlyCertificationService : BaseService, IElderlyCertificationService
{
    private const string TaskSelectColumns = @"
            id, campaign_id, id_card, name, application_id, application_no, source_kind,
            gender, birth_date, age_snapshot, category, identity_flag, issue_amount,
            hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id, hukou_village, hukou_address,
            family_city_id, family_county_id, family_town_id, family_village_id, family_address,
            phone, bank_name, bank_account, agent_name, agent_relation,
            snapshot_at, cert_status, verify_method, verified_at, verified_by, verifier_org, verifier_phone,
            result_note, death_date, expected_evidence_count, evidence_count, evidence_status,
            stop_decision, stop_reason, stop_note, follow_up_date, due_stop_date, actual_stop_date,
            stop_application_id, stopped_at, source_batch_no, source_file, source_row, source_submitted_at,
            last_imported_at, created_at, updated_at";

    /// <summary>停发预览允许的认证状态（与 <see cref="ElderlyCertificationConstants.IsStopCandidate"/> 一致）。</summary>
    private static readonly string[] StopCandidateStatuses =
    {
        ElderlyCertificationConstants.CertStatusPending,
        ElderlyCertificationConstants.CertStatusRefused,
        ElderlyCertificationConstants.CertStatusFailed,
        ElderlyCertificationConstants.CertStatusDeceased,
        ElderlyCertificationConstants.CertStatusMovedOut,
        ElderlyCertificationConstants.CertStatusNotLocated
    };

    private static readonly string[] AllowedStopDecisions =
    {
        ElderlyCertificationConstants.StopDecisionDeferred,
        ElderlyCertificationConstants.StopDecisionExcluded,
        ElderlyCertificationConstants.StopDecisionPending
    };

    private readonly IDatabaseService _db;

    protected override string ServiceName => "ElderlyCertificationService";

    public ElderlyCertificationService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
    }

    public async Task<Result<List<ElderlyCertCampaign>>> GetCampaignsAsync(CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, campaign_no, year, round, name, period_start, period_end, valid_until,
                   status, require_evidence, deadline_stop_offset, roster_generated_at,
                   closed_at, closed_by, remark, created_at, updated_at, created_by, updated_by, deleted_at
            FROM nc_biz_elderly_cert_campaigns
            WHERE deleted_at IS NULL
            ORDER BY year DESC, round DESC, id DESC";

        var result = await _db.QueryAsync<ElderlyCertCampaign>(sql, ct);
        if (result.IsFailure)
            return Result.Failure<List<ElderlyCertCampaign>>(result.ErrorCode!, result.Message!);

        return Result.Success(result.Value ?? new List<ElderlyCertCampaign>());
    }

    public async Task<Result<ElderlyCertCampaign?>> GetCampaignAsync(long id, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, campaign_no, year, round, name, period_start, period_end, valid_until,
                   status, require_evidence, deadline_stop_offset, roster_generated_at,
                   closed_at, closed_by, remark, created_at, updated_at, created_by, updated_by, deleted_at
            FROM nc_biz_elderly_cert_campaigns
            WHERE id = $1 AND deleted_at IS NULL
            LIMIT 1";

        var result = await _db.QueryAsync<ElderlyCertCampaign>(sql, ct, id);
        if (result.IsFailure)
            return Result.Failure<ElderlyCertCampaign?>(result.ErrorCode!, result.Message!);

        return Result.Success<ElderlyCertCampaign?>(result.Value?.FirstOrDefault());
    }

    public async Task<Result<long>> CreateCampaignAsync(
        int year,
        int round,
        string name,
        DateTime periodStart,
        DateTime periodEnd,
        bool requireEvidence,
        string operatorName,
        CancellationToken ct = default)
    {
        if (year is < 2000 or > 2100)
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "认证年度不合法");
        if (round < 1)
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "轮次必须大于 0");
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "批次名称不能为空");
        if (periodStart.Date > periodEnd.Date)
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "认证开始日期不能晚于截止日期");

        var campaignNo = round <= 1 ? $"RZ{year}" : $"RZ{year}-{round}";

        const string sql = @"
            INSERT INTO nc_biz_elderly_cert_campaigns
                (campaign_no, year, round, name, period_start, period_end, valid_until,
                 status, require_evidence, deadline_stop_offset, created_at, updated_at, created_by, updated_by)
            VALUES ($1, $2, $3, $4, $5, $6, $6, $7, $8, 1, NOW(), NOW(), $9, $9)
            RETURNING id";

        try
        {
            var result = await _db.ExecuteScalarAsync<long>(sql, ct,
                campaignNo, year, round, name.Trim(), periodStart.Date, periodEnd.Date,
                ElderlyCertificationConstants.CampaignStatusDraft, requireEvidence, operatorName);

            if (result.IsFailure)
            {
                if (result.ErrorCode == ErrorCodes.DB_UNIQUE_VIOLATION
                    || result.Message?.Contains("23505", StringComparison.Ordinal) == true)
                    return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "同年度同轮次的认证批次已存在");
                return Result.Failure<long>(result.ErrorCode!, result.Message!);
            }

            Logger.LogBusiness("创建高龄年度认证批次", ("CampaignNo", campaignNo), ("Year", year), ("Round", round));
            return Result.Success(result.Value);
        }
        catch (Exception ex)
        {
            LogException(ex, "CreateCampaignAsync");
            return Result.FromException<long>(ex);
        }
    }

    public async Task<Result<int>> GenerateRosterAsync(long campaignId, string operatorName, CancellationToken ct = default)
    {
        var campaignResult = await GetCampaignAsync(campaignId, ct);
        if (campaignResult.IsFailure)
            return Result.Failure<int>(campaignResult.ErrorCode!, campaignResult.Message!);

        var campaign = campaignResult.Value;
        if (campaign == null)
            return Result.Failure<int>(ErrorCodes.NOT_FOUND, "认证批次不存在");
        if (campaign.Status == ElderlyCertificationConstants.CampaignStatusClosed)
            return Result.Failure<int>(ErrorCodes.VALIDATION_FAILED, "批次已关闭，不能刷新名单");

        // 名单快照：在享 Confirmed 档案优先；名册 Active 且未建档的补充。
        // 年龄按批次截止日计算；历史名册无出生日期时回退其 age 字段。
        const string sql = @"
            WITH apps AS (
                SELECT a.id AS application_id,
                       a.application_no,
                       a.id_card,
                       LEFT(a.name, 50) AS name,
                       a.gender,
                       a.birth_date,
                       CASE WHEN a.birth_date IS NULL THEN NULL
                            ELSE DATE_PART('year', AGE($2::date, a.birth_date))::int END AS age_snapshot,
                       a.category,
                       a.identity_flag,
                       a.issue_amount,
                       a.hukou_city_id, a.hukou_county_id, a.hukou_town_id, a.hukou_village_id,
                       a.hukou_village, LEFT(a.hukou_address, 200) AS hukou_address,
                       a.family_city_id, a.family_county_id, a.family_town_id, a.family_village_id,
                       LEFT(a.family_address, 200) AS family_address,
                       a.phone, a.bank_name, a.bank_account, a.agent_name, a.agent_relation,
                       'Application'::varchar(20) AS source_kind
                FROM nc_biz_elderly_applications a
                WHERE a.status = $3 AND a.deleted_at IS NULL
            ),
            hist AS (
                SELECT NULL::bigint AS application_id,
                       NULL::varchar(50) AS application_no,
                       h.id_card,
                       LEFT(h.name, 50) AS name,
                       h.gender,
                       h.birth_date,
                       COALESCE(DATE_PART('year', AGE($2::date, h.birth_date))::int, h.age) AS age_snapshot,
                       NULL::varchar(20) AS category,
                       NULL::varchar(20) AS identity_flag,
                       h.subsidy_amount AS issue_amount,
                       NULL::integer AS hukou_city_id, NULL::integer AS hukou_county_id,
                       NULL::integer AS hukou_town_id, NULL::integer AS hukou_village_id,
                       NULL::varchar(100) AS hukou_village, LEFT(h.address, 200) AS hukou_address,
                       NULL::integer AS family_city_id, NULL::integer AS family_county_id,
                       NULL::integer AS family_town_id, NULL::integer AS family_village_id,
                       NULL::varchar(200) AS family_address,
                       h.phone, NULL::varchar(100) AS bank_name, h.bank_account,
                       NULL::varchar(50) AS agent_name, NULL::varchar(50) AS agent_relation,
                       'History'::varchar(20) AS source_kind
                FROM nc_biz_elderly_subsidy_history h
                WHERE h.status = $4
                  AND NOT EXISTS (
                      SELECT 1 FROM nc_biz_elderly_applications a2
                      WHERE a2.id_card = h.id_card AND a2.status = $3 AND a2.deleted_at IS NULL)
            ),
            roster AS (
                SELECT DISTINCT ON (id_card)
                       application_id, application_no, id_card, name, gender, birth_date, age_snapshot,
                       category, identity_flag, issue_amount,
                       hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id, hukou_village, hukou_address,
                       family_city_id, family_county_id, family_town_id, family_village_id, family_address,
                       phone, bank_name, bank_account, agent_name, agent_relation, source_kind
                FROM (
                    SELECT application_id, application_no, id_card, name, gender, birth_date, age_snapshot,
                           category, identity_flag, issue_amount,
                           hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id, hukou_village, hukou_address,
                           family_city_id, family_county_id, family_town_id, family_village_id, family_address,
                           phone, bank_name, bank_account, agent_name, agent_relation, source_kind
                    FROM apps
                    UNION ALL
                    SELECT application_id, application_no, id_card, name, gender, birth_date, age_snapshot,
                           category, identity_flag, issue_amount,
                           hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id, hukou_village, hukou_address,
                           family_city_id, family_county_id, family_town_id, family_village_id, family_address,
                           phone, bank_name, bank_account, agent_name, agent_relation, source_kind
                    FROM hist
                ) x
                ORDER BY id_card, (source_kind = 'Application') DESC, application_id DESC NULLS LAST
            )
            INSERT INTO nc_biz_elderly_cert_tasks
                (campaign_id, id_card, name, application_id, application_no, source_kind,
                 gender, birth_date, age_snapshot, category, identity_flag, issue_amount,
                 hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id, hukou_village, hukou_address,
                 family_city_id, family_county_id, family_town_id, family_village_id, family_address,
                 phone, bank_name, bank_account, agent_name, agent_relation,
                 snapshot_at, cert_status, evidence_status, stop_decision, created_at, updated_at)
            SELECT $1, id_card, name, application_id, application_no, source_kind,
                   gender, birth_date, age_snapshot, category, identity_flag, issue_amount,
                   hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id, hukou_village, hukou_address,
                   family_city_id, family_county_id, family_town_id, family_village_id, family_address,
                   phone, bank_name, bank_account, agent_name, agent_relation,
                   NOW(), $5, $6, $7, NOW(), NOW()
            FROM roster
            ON CONFLICT (campaign_id, id_card) WHERE deleted_at IS NULL DO NOTHING";

        try
        {
            await using var tx = await _db.BeginTransactionScopeAsync(ct);

            var insertResult = await _db.ExecuteNonQueryAsync(sql, ct,
                campaignId,
                campaign.PeriodEnd.Date,
                ElderlyBenefitConstants.StatusConfirmed,
                ElderlyBenefitConstants.StatusActive,
                ElderlyCertificationConstants.CertStatusPending,
                ElderlyCertificationConstants.EvidenceStatusNone,
                ElderlyCertificationConstants.StopDecisionNone);

            if (insertResult.IsFailure)
                return Result.Failure<int>(insertResult.ErrorCode!, insertResult.Message!);

            const string updateSql = @"
                UPDATE nc_biz_elderly_cert_campaigns
                SET roster_generated_at = NOW(),
                    status = CASE WHEN status = $2 THEN $3 ELSE status END,
                    updated_at = NOW(),
                    updated_by = $4
                WHERE id = $1 AND deleted_at IS NULL";
            var updateResult = await _db.ExecuteNonQueryAsync(updateSql, ct,
                campaignId,
                ElderlyCertificationConstants.CampaignStatusDraft,
                ElderlyCertificationConstants.CampaignStatusCollecting,
                operatorName);
            if (updateResult.IsFailure)
                return Result.Failure<int>(updateResult.ErrorCode!, updateResult.Message!);

            await tx.CommitAsync(ct);
            Logger.LogBusiness("生成高龄年度认证名单", ("CampaignId", campaignId), ("Added", insertResult.Value), ("Operator", operatorName));
            return Result.Success(insertResult.Value);
        }
        catch (Exception ex)
        {
            LogException(ex, "GenerateRosterAsync");
            return Result.FromException<int>(ex);
        }
    }

    public async Task<Result<PagedResult<ElderlyCertTask>>> GetTasksPagedAsync(
        long campaignId,
        string? keyword,
        string? certStatus,
        string? evidenceStatus,
        int pageIndex,
        int pageSize,
        CancellationToken ct = default)
    {
        if (pageIndex < 1) pageIndex = 1;
        if (pageSize < 1) pageSize = 20;

        var conditions = new List<string> { "campaign_id = $1", "deleted_at IS NULL" };
        var parameters = new List<object?> { campaignId };
        var paramIndex = 2;

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            conditions.Add($"(name ILIKE ${paramIndex} OR id_card ILIKE ${paramIndex})");
            parameters.Add($"%{keyword.Trim()}%");
            paramIndex++;
        }
        if (!string.IsNullOrWhiteSpace(certStatus))
        {
            conditions.Add($"cert_status = ${paramIndex}");
            parameters.Add(certStatus);
            paramIndex++;
        }
        if (!string.IsNullOrWhiteSpace(evidenceStatus))
        {
            conditions.Add($"evidence_status = ${paramIndex}");
            parameters.Add(evidenceStatus);
            paramIndex++;
        }

        var where = string.Join(" AND ", conditions);
        var countSql = $"SELECT COUNT(*) FROM nc_biz_elderly_cert_tasks WHERE {where}";

        var querySql = $@"
            SELECT {TaskSelectColumns}
            FROM nc_biz_elderly_cert_tasks
            WHERE {where}
            ORDER BY CASE cert_status WHEN '{ElderlyCertificationConstants.CertStatusPending}' THEN 0 ELSE 1 END,
                     hukou_town_id NULLS LAST, hukou_village_id NULLS LAST, name, id_card
            LIMIT ${paramIndex} OFFSET ${paramIndex + 1}";
        parameters.Add(pageSize);
        parameters.Add((pageIndex - 1) * pageSize);

        try
        {
            var countResult = await _db.ExecuteScalarAsync(countSql, ct, parameters.Take(paramIndex - 1).ToArray());
            if (countResult.IsFailure)
                return Result.Failure<PagedResult<ElderlyCertTask>>(countResult.ErrorCode!, countResult.Message!);

            var listResult = await _db.QueryAsync<ElderlyCertTask>(querySql, ct, parameters.ToArray());
            if (listResult.IsFailure)
                return Result.Failure<PagedResult<ElderlyCertTask>>(listResult.ErrorCode!, listResult.Message!);

            return Result.Success(PagedResult<ElderlyCertTask>.FromList(
                listResult.Value ?? new List<ElderlyCertTask>(),
                pageIndex, pageSize, Convert.ToInt32(countResult.Value)));
        }
        catch (Exception ex)
        {
            LogException(ex, "GetTasksPagedAsync");
            return Result.FromException<PagedResult<ElderlyCertTask>>(ex);
        }
    }

    public async Task<Result<PagedResult<ElderlyCertTask>>> GetStopPreviewPagedAsync(
        long campaignId,
        string? keyword,
        string? status,
        int pageIndex,
        int pageSize,
        CancellationToken ct = default)
    {
        if (pageIndex < 1) pageIndex = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 200) pageSize = 200;

        var conditions = new List<string>
        {
            "campaign_id = $1",
            "deleted_at IS NULL",
            "cert_status = ANY($2)"
        };
        // $2 传候选状态数组：只允许常量清单内的状态，避免调用方传入任意值绕过停发预览口径。
        var parameters = new List<object?> { campaignId, StopCandidateStatuses };
        var paramIndex = 3;

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            conditions.Add($"(name ILIKE ${paramIndex} OR id_card ILIKE ${paramIndex})");
            parameters.Add($"%{keyword.Trim()}%");
            paramIndex++;
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!StopCandidateStatuses.Contains(status, StringComparer.Ordinal))
                return Result.Failure<PagedResult<ElderlyCertTask>>(
                    ErrorCodes.VALIDATION_FAILED, "认证状态不在停发预览范围内");
            conditions.Add($"cert_status = ${paramIndex}");
            parameters.Add(status);
            paramIndex++;
        }

        var where = string.Join(" AND ", conditions);
        var countSql = $"SELECT COUNT(*) FROM nc_biz_elderly_cert_tasks WHERE {where}";
        var querySql = $@"
            SELECT {TaskSelectColumns}
            FROM nc_biz_elderly_cert_tasks
            WHERE {where}
            ORDER BY hukou_village NULLS LAST, hukou_town_id NULLS LAST, name, id_card
            LIMIT ${paramIndex} OFFSET ${paramIndex + 1}";
        parameters.Add(pageSize);
        parameters.Add((pageIndex - 1) * pageSize);

        try
        {
            var countResult = await _db.ExecuteScalarAsync(countSql, ct, parameters.Take(paramIndex - 1).ToArray());
            if (countResult.IsFailure)
                return Result.Failure<PagedResult<ElderlyCertTask>>(countResult.ErrorCode!, countResult.Message!);

            var listResult = await _db.QueryAsync<ElderlyCertTask>(querySql, ct, parameters.ToArray());
            if (listResult.IsFailure)
                return Result.Failure<PagedResult<ElderlyCertTask>>(listResult.ErrorCode!, listResult.Message!);

            return Result.Success(PagedResult<ElderlyCertTask>.FromList(
                listResult.Value ?? new List<ElderlyCertTask>(),
                pageIndex, pageSize, Convert.ToInt32(countResult.Value)));
        }
        catch (Exception ex)
        {
            LogException(ex, "GetStopPreviewPagedAsync");
            return Result.FromException<PagedResult<ElderlyCertTask>>(ex);
        }
    }

    public async Task<Result> SetStopDecisionAsync(
        long taskId,
        string decision,
        string? note,
        DateTime? followUpDate,
        string operatorName,
        CancellationToken ct = default)
    {
        if (!AllowedStopDecisions.Contains(decision, StringComparer.Ordinal))
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "停发决定不合法（仅支持暂缓/排除/待停发）");

        var trimmedNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (trimmedNote?.Length > 500)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "停发决定备注不能超过 500 字");

        // 暂缓需要跟进日期；排除/重新进入待停发清空跟进日期，避免遗留过期提醒。
        var effectiveFollowUp = decision == ElderlyCertificationConstants.StopDecisionDeferred
            ? followUpDate?.Date
            : null;
        if (decision == ElderlyCertificationConstants.StopDecisionDeferred && effectiveFollowUp == null)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "暂缓停发必须填写跟进日期");

        // 已停发任务不允许回退，防止绕过停发回写把 Approved 改回待停发。
        const string sql = @"
            UPDATE nc_biz_elderly_cert_tasks
            SET stop_decision = $2,
                stop_note = $3,
                follow_up_date = $4,
                updated_at = NOW()
            WHERE id = $1
              AND deleted_at IS NULL
              AND stop_decision IS DISTINCT FROM $5";

        try
        {
            var result = await _db.ExecuteNonQueryAsync(sql, ct,
                taskId,
                decision,
                trimmedNote,
                effectiveFollowUp,
                ElderlyCertificationConstants.StopDecisionApproved);
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);
            if (result.Value == 0)
                return Result.Failure(ErrorCodes.NOT_FOUND, "认证任务不存在、已删除或已停发，不能变更停发决定");

            Logger.LogBusiness("高龄认证停发决定",
                ("TaskId", taskId),
                ("Decision", decision),
                ("FollowUpDate", effectiveFollowUp?.ToString("yyyy-MM-dd") ?? "-"),
                ("Operator", operatorName));
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "SetStopDecisionAsync");
            return Result.FromException(ex);
        }
    }

    public async Task<Result<ElderlyCertTask?>> GetTaskAsync(long id, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT id, campaign_id, id_card, name, application_id, application_no, source_kind,
                   gender, birth_date, age_snapshot, category, identity_flag, issue_amount,
                   hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id, hukou_village, hukou_address,
                   family_city_id, family_county_id, family_town_id, family_village_id, family_address,
                   phone, bank_name, bank_account, agent_name, agent_relation,
                   snapshot_at, cert_status, verify_method, verified_at, verified_by, verifier_org, verifier_phone,
                   result_note, death_date, expected_evidence_count, evidence_count, evidence_status,
                   stop_decision, stop_reason, stop_note, follow_up_date, due_stop_date, actual_stop_date,
                   stop_application_id, stopped_at, source_batch_no, source_file, source_row, source_submitted_at,
                   last_imported_at, created_at, updated_at
            FROM nc_biz_elderly_cert_tasks
            WHERE id = $1 AND deleted_at IS NULL
            LIMIT 1";

        var result = await _db.QueryAsync<ElderlyCertTask>(sql, ct, id);
        if (result.IsFailure)
            return Result.Failure<ElderlyCertTask?>(result.ErrorCode!, result.Message!);

        return Result.Success<ElderlyCertTask?>(result.Value?.FirstOrDefault());
    }

    public async Task<Result> UpdateTaskResultAsync(
        long taskId,
        string certStatus,
        string? verifyMethod,
        DateTime? verifiedAt,
        string? verifiedBy,
        string? note,
        DateTime? deathDate,
        string operatorName,
        CancellationToken ct = default)
    {
        if (!ElderlyCertificationConstants.CertStatusOptions.Any(o => o.Key == certStatus))
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "认证状态不合法");
        if (!string.IsNullOrWhiteSpace(verifyMethod)
            && !ElderlyCertificationConstants.VerifyMethodOptions.Any(o => o.Key == verifyMethod))
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "认证方式不合法");

        const string sql = @"
            UPDATE nc_biz_elderly_cert_tasks
            SET cert_status = $2,
                verify_method = $3,
                verified_at = $4,
                verified_by = $5,
                result_note = $6,
                death_date = $7,
                updated_at = NOW()
            WHERE id = $1 AND deleted_at IS NULL";

        try
        {
            var result = await _db.ExecuteNonQueryAsync(sql, ct,
                taskId, certStatus, verifyMethod, verifiedAt?.Date, verifiedBy, note, deathDate?.Date);
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);
            if (result.Value == 0)
                return Result.Failure(ErrorCodes.NOT_FOUND, "认证任务不存在或已删除");

            Logger.LogBusiness("手工更新高龄认证结果", ("TaskId", taskId), ("Status", certStatus), ("Operator", operatorName));
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "UpdateTaskResultAsync");
            return Result.FromException(ex);
        }
    }

    public async Task<Result<ElderlyCertStats>> GetStatsAsync(long campaignId, CancellationToken ct = default)
    {
        const string sql = @"
            SELECT
                COUNT(*)::int AS total,
                COUNT(*) FILTER (WHERE cert_status = $2)::int AS pending,
                COUNT(*) FILTER (WHERE cert_status = $3)::int AS verified,
                COUNT(*) FILTER (WHERE cert_status = $4)::int AS refused,
                COUNT(*) FILTER (WHERE cert_status = $5)::int AS failed,
                COUNT(*) FILTER (WHERE cert_status = $6)::int AS deceased,
                COUNT(*) FILTER (WHERE cert_status = $7)::int AS moved_out,
                COUNT(*) FILTER (WHERE cert_status = $8)::int AS not_located,
                COUNT(*) FILTER (WHERE cert_status = $9)::int AS exempt,
                COUNT(*) FILTER (WHERE cert_status = $10)::int AS stopped,
                COUNT(*) FILTER (WHERE cert_status = $11)::int AS invalid,
                COUNT(*) FILTER (WHERE evidence_status IN ($12, $13, $14))::int AS evidence_issue,
                COUNT(*) FILTER (WHERE cert_status IN ($2, $4, $5, $6, $7, $8))::int AS stop_candidate
            FROM nc_biz_elderly_cert_tasks
            WHERE campaign_id = $1 AND deleted_at IS NULL";

        var result = await _db.QueryAsync<ElderlyCertStats>(sql, ct,
            campaignId,
            ElderlyCertificationConstants.CertStatusPending,
            ElderlyCertificationConstants.CertStatusVerified,
            ElderlyCertificationConstants.CertStatusRefused,
            ElderlyCertificationConstants.CertStatusFailed,
            ElderlyCertificationConstants.CertStatusDeceased,
            ElderlyCertificationConstants.CertStatusMovedOut,
            ElderlyCertificationConstants.CertStatusNotLocated,
            ElderlyCertificationConstants.CertStatusExempt,
            ElderlyCertificationConstants.CertStatusStopped,
            ElderlyCertificationConstants.CertStatusInvalid,
            ElderlyCertificationConstants.EvidenceStatusMissing,
            ElderlyCertificationConstants.EvidenceStatusPartial,
            ElderlyCertificationConstants.EvidenceStatusAmbiguous);

        if (result.IsFailure)
            return Result.Failure<ElderlyCertStats>(result.ErrorCode!, result.Message!);

        return Result.Success(result.Value?.FirstOrDefault() ?? new ElderlyCertStats());
    }

    public async Task<Result<List<ElderlyCertEvidence>>> GetEvidenceAsync(long taskId, bool includeData = false, CancellationToken ct = default)
    {
        var columns = includeData
            ? @"id, task_id, batch_id, seq, evidence_type, original_name, stored_name, display_name,
                source_zip, source_entry, match_status, match_method, photo_data, image_format, file_size,
                sha256, created_at, created_by, deleted_at"
            : @"id, task_id, batch_id, seq, evidence_type, original_name, stored_name, display_name,
                source_zip, source_entry, match_status, match_method, image_format, file_size,
                sha256, created_at, created_by, deleted_at";

        var sql = $@"
            SELECT {columns}
            FROM nc_biz_elderly_cert_evidence
            WHERE task_id = $1 AND deleted_at IS NULL
            ORDER BY seq, id";

        var result = await _db.QueryAsync<ElderlyCertEvidence>(sql, ct, taskId);
        if (result.IsFailure)
            return Result.Failure<List<ElderlyCertEvidence>>(result.ErrorCode!, result.Message!);

        return Result.Success(result.Value ?? new List<ElderlyCertEvidence>());
    }

    public async Task<Result<List<ElderlyCertEvidence>>> GetUnmatchedEvidenceAsync(long campaignId, int limit = 200, CancellationToken ct = default)
    {
        if (limit is < 1 or > 1000) limit = 200;
        const string sql = @"
            SELECT e.id, e.task_id, e.batch_id, e.seq, e.evidence_type, e.original_name, e.stored_name,
                   e.display_name, e.source_zip, e.source_entry, e.match_status, e.match_method,
                   e.image_format, e.file_size, e.sha256, e.created_at, e.created_by, e.deleted_at
            FROM nc_biz_elderly_cert_evidence e
            JOIN nc_biz_elderly_cert_import_batches b ON b.id = e.batch_id
            WHERE b.campaign_id = $1 AND e.task_id IS NULL AND e.deleted_at IS NULL
            ORDER BY e.id DESC
            LIMIT $2";

        var result = await _db.QueryAsync<ElderlyCertEvidence>(sql, ct, campaignId, limit);
        if (result.IsFailure)
            return Result.Failure<List<ElderlyCertEvidence>>(result.ErrorCode!, result.Message!);

        return Result.Success(result.Value ?? new List<ElderlyCertEvidence>());
    }

    public async Task<Result> AssignEvidenceAsync(long evidenceId, long taskId, int seq, string operatorName, CancellationToken ct = default)
    {
        if (seq < 1) seq = 1;

        try
        {
            const string sql = @"
                UPDATE nc_biz_elderly_cert_evidence
                SET task_id = $2, seq = $3, match_status = $4, match_method = $5
                WHERE id = $1 AND task_id IS NULL AND deleted_at IS NULL";
            var result = await _db.ExecuteNonQueryAsync(sql, ct,
                evidenceId, taskId, seq,
                ElderlyCertificationConstants.EvidenceMatchManual,
                ElderlyCertificationConstants.EvidenceMatchMethodManual);
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);
            if (result.Value == 0)
                return Result.Failure(ErrorCodes.NOT_FOUND, "凭证不存在、已挂接或已删除");

            await RefreshEvidenceStatsAsync(taskId, ct);
            Logger.LogBusiness("手工挂接高龄认证凭证", ("EvidenceId", evidenceId), ("TaskId", taskId), ("Operator", operatorName));
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "AssignEvidenceAsync");
            return Result.FromException(ex);
        }
    }

    public async Task<Result> MarkStoppedAsync(
        long taskId,
        long applicationId,
        string stopReason,
        DateTime? dueStopDate,
        DateTime? actualStopDate,
        string operatorName,
        CancellationToken ct = default)
    {
        const string sql = @"
            UPDATE nc_biz_elderly_cert_tasks
            SET stop_decision = $2,
                stop_reason = $3,
                due_stop_date = $4,
                actual_stop_date = $5,
                stop_application_id = $6,
                cert_status = $7,
                stopped_at = NOW(),
                updated_at = NOW()
            WHERE id = $1 AND deleted_at IS NULL";

        try
        {
            var result = await _db.ExecuteNonQueryAsync(sql, ct,
                taskId,
                ElderlyCertificationConstants.StopDecisionApproved,
                stopReason,
                dueStopDate?.Date,
                actualStopDate?.Date,
                applicationId,
                ElderlyCertificationConstants.CertStatusStopped);
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);
            if (result.Value == 0)
                return Result.Failure(ErrorCodes.NOT_FOUND, "认证任务不存在或已删除");

            Logger.LogBusiness("高龄认证停发回写", ("TaskId", taskId), ("ApplicationId", applicationId), ("Operator", operatorName));
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "MarkStoppedAsync");
            return Result.FromException(ex);
        }
    }

    private async Task RefreshEvidenceStatsAsync(long taskId, CancellationToken ct)
    {
        const string sql = @"
            UPDATE nc_biz_elderly_cert_tasks t
            SET evidence_count = c.cnt,
                evidence_status = CASE
                    WHEN t.expected_evidence_count <= 0 THEN
                        CASE WHEN c.cnt > 0 THEN $2 ELSE $3 END
                    WHEN c.cnt >= t.expected_evidence_count THEN $2
                    WHEN c.cnt > 0 THEN $4
                    ELSE $5
                END,
                updated_at = NOW()
            FROM (
                SELECT COUNT(*)::int AS cnt
                FROM nc_biz_elderly_cert_evidence
                WHERE task_id = $1 AND deleted_at IS NULL
            ) c
            WHERE t.id = $1";

        await _db.ExecuteNonQueryAsync(sql, ct,
            taskId,
            ElderlyCertificationConstants.EvidenceStatusComplete,
            ElderlyCertificationConstants.EvidenceStatusNone,
            ElderlyCertificationConstants.EvidenceStatusPartial,
            ElderlyCertificationConstants.EvidenceStatusMissing);
    }
}
