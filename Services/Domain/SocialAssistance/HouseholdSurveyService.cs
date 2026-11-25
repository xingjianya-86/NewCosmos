using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>
/// 入户调查服务实现
/// </summary>
public class HouseholdSurveyService : BaseService, IHouseholdSurveyService
{
    protected override string ServiceName => "HouseholdSurveyService";
    private readonly IDatabaseService _db;

    public HouseholdSurveyService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
    }

    public async Task<Result<HouseholdSurvey?>> GetByApplicationIdAsync(long applicationId, CancellationToken ct = default)
    {
        // LIMIT 1 必须配 ORDER BY：若同一申请存在多条未删记录，无序 LIMIT 1 取哪条
        // 由执行计划决定，前后两次可能读到不同的调查记录。按 id DESC 固定取最新一条。
        var sql = "SELECT * FROM nc_biz_household_surveys WHERE application_id = $1 AND deleted_at IS NULL ORDER BY id DESC LIMIT 1";
        return await _db.QuerySingleAsync<HouseholdSurvey?>(sql, ct, applicationId);
    }

    public async Task<Result<IReadOnlyDictionary<long, DateTime>>> GetLatestSurveyDateMapByApplicationIdsAsync(
        IReadOnlyCollection<long> applicationIds, CancellationToken ct = default)
    {
        try
        {
            if (applicationIds.Count == 0)
                return Result.Success<IReadOnlyDictionary<long, DateTime>>(new Dictionary<long, DateTime>());

            // DISTINCT ON + ORDER BY id DESC：同一申请取最新一条未删记录，口径与 GetByApplicationIdAsync 一致
            var sql = @"SELECT DISTINCT ON (application_id) application_id, survey_date
                FROM nc_biz_household_surveys
                WHERE application_id = ANY($1) AND deleted_at IS NULL
                ORDER BY application_id, id DESC";
            var result = await _db.QueryAsync<SurveyDateRecord>(sql, ct, applicationIds.ToArray());
            if (result.IsFailure)
                return Result.Failure<IReadOnlyDictionary<long, DateTime>>(result.ErrorCode!, result.Message!);

            var map = new Dictionary<long, DateTime>();
            foreach (var row in result.Value ?? new List<SurveyDateRecord>())
                map.TryAdd(row.ApplicationId, row.SurveyDate.Date);

            return Result.Success<IReadOnlyDictionary<long, DateTime>>(map);
        }
        catch (Exception ex)
        {
            LogException(ex, "批量查询入户调查日期失败");
            return Result.FromException<IReadOnlyDictionary<long, DateTime>>(ex);
        }
    }

    public async Task<Result<long>> SaveAsync(long applicationId, HouseholdSurvey survey, CancellationToken ct = default)
    {
        ValidateNotNull(survey, nameof(survey));

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            // 检查是否已存在
            var existing = await GetByApplicationIdAsync(applicationId, ct);
            if (existing.IsFailure)
            {
                // 查询失败不能走 INSERT：会产生同一申请重复入户调查行。
                await tx.RollbackAsync(ct);
                LogError($"查询已有入户调查失败: {existing.ErrorCode} {existing.Message}");
                return Result.Failure<long>(
                    existing.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    $"查询已有入户调查失败: {existing.Message}");
            }
            if (existing.Value != null)
            {
                // 更新
                var updateSql = @"UPDATE nc_biz_household_surveys SET 
                    survey_date = $1, surveyor_name = $2, surveyor_organization = $3,
                    respondent_name = $4, respondent_relation = $5,
                    application_reason = $6, application_reason_detail = $7,
                    survey_conclusion = $8, survey_notes = $9,
                    updated_at = NOW()
                    WHERE application_id = $10 AND deleted_at IS NULL";

                var updateResult = await _db.ExecuteNonQueryAsync(updateSql, ct,
                    survey.SurveyDate, survey.SurveyorName, survey.SurveyorOrganization,
                    survey.RespondentName, survey.RespondentRelation,
                    survey.ApplicationReason, survey.ApplicationReasonDetail,
                    survey.SurveyConclusion, survey.SurveyNotes,
                    applicationId);

                if (updateResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<long>(updateResult.ErrorCode!, updateResult.Message!);
                }

                await tx.CommitAsync(ct);
                LogInfo("入户调查更新成功");
                return Result.Success(existing.Value.Id);
            }
            else
            {
                // 创建
                var insertSql = @"INSERT INTO nc_biz_household_surveys 
                    (application_id, survey_date, surveyor_name, surveyor_organization,
                     respondent_name, respondent_relation,
                     application_reason, application_reason_detail,
                     survey_conclusion, survey_notes,
                     created_at, updated_at)
                    VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, NOW(), NOW())
                    RETURNING id";

                var insertResult = await _db.ExecuteScalarAsync(insertSql, ct,
                    applicationId, survey.SurveyDate, survey.SurveyorName, survey.SurveyorOrganization,
                    survey.RespondentName, survey.RespondentRelation,
                    survey.ApplicationReason, survey.ApplicationReasonDetail,
                    survey.SurveyConclusion, survey.SurveyNotes);

                if (insertResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<long>(insertResult.ErrorCode!, insertResult.Message!);
                }

                await tx.CommitAsync(ct);
                LogInfo("入户调查创建成功");
                return Result.Success(insertResult.Value);
            }
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "失败");
            return Result.FromException<long>(ex);
        }
    }

    public async Task<Result> DeleteByApplicationIdAsync(long applicationId, CancellationToken ct = default)
    {
        var sql = "UPDATE nc_biz_household_surveys SET deleted_at = NOW() WHERE application_id = $1 AND deleted_at IS NULL";
        return await _db.ExecuteNonQueryAsync(sql, ct, applicationId);
    }
}
