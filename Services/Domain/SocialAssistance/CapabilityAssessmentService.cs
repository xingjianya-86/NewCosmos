using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>
/// 能力鉴定服务实现
/// </summary>
public class CapabilityAssessmentService : BaseService, ICapabilityAssessmentService
{
    protected override string ServiceName => "CapabilityAssessmentService";
    private readonly IDatabaseService _db;
    private readonly ILoggerService _logger;

    public CapabilityAssessmentService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// 根据申请ID获取能力鉴定
    /// </summary>
    public async Task<Result<CapabilityAssessment?>> GetByApplicationIdAsync(long applicationId, CancellationToken ct = default)
    {
        LogInfo($"获取能力鉴定: ApplicationId={applicationId}");
        try
        {
            var sql = @"SELECT * FROM nc_biz_capability_assessments
                       WHERE application_id = $1 AND deleted_at IS NULL
                       ORDER BY created_at DESC LIMIT 1";
            var result = await _db.QuerySingleAsync<CapabilityAssessment>(sql, ct, applicationId);

            if (result.IsSuccess)
            {
                LogInfo($"获取能力鉴定成功: Id={result.Value?.Id}");
                return Result.Success(result.Value);
            }

            return Result.Failure<CapabilityAssessment?>(result.ErrorCode!, result.Message!);
        }
        catch (Exception ex)
        {
            LogException(ex, "获取能力鉴定失败");
            return Result.FromException<CapabilityAssessment?>(ex);
        }
    }

    /// <summary>
    /// 保存能力鉴定
    /// </summary>
    public async Task<Result<long>> SaveAsync(CapabilityAssessment assessment, CancellationToken ct = default)
    {
        LogInfo($"保存能力鉴定: ApplicationId={assessment.ApplicationId}");
        try
        {
            if (assessment.Id > 0)
            {
                // 更新
                var sql = @"UPDATE nc_biz_capability_assessments SET
                           application_id = $1, assessment_date = $2, assessor_name = $3,
                           eating = $4, dressing = $5, getting_in_out_bed = $6,
                           using_toilet = $7, indoor_walking = $8, bathing = $9,
                           completed_items = $10, self_care_level = $11, care_level = $12,
                           remark = $13, updated_at = NOW()
                           WHERE id = $14 AND deleted_at IS NULL";

                // 更新失败必须如实上报（此前结果被丢弃，无条件返回成功）
                await ExecOrThrowAsync(_db, sql, ct,
                    assessment.ApplicationId, assessment.AssessmentDate, assessment.AssessorName,
                    assessment.Eating, assessment.Dressing, assessment.GettingInOutOfBed,
                    assessment.UsingToilet, assessment.IndoorWalking, assessment.Bathing,
                    assessment.CompletedItems, assessment.SelfCareLevel, assessment.CareLevel,
                    assessment.Remark, assessment.Id);

                LogInfo($"能力鉴定更新成功: Id={assessment.Id}");
                return Result.Success(assessment.Id);
            }
            else
            {
                // 新增
                var sql = @"INSERT INTO nc_biz_capability_assessments
                           (application_id, assessment_date, assessor_name,
                            eating, dressing, getting_in_out_bed,
                            using_toilet, indoor_walking, bathing,
                            completed_items, self_care_level, care_level,
                            remark, created_at, updated_at)
                           VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,NOW(),NOW())
                           RETURNING id";

                var result = await _db.ExecuteScalarAsync(sql, ct,
                    assessment.ApplicationId, assessment.AssessmentDate, assessment.AssessorName,
                    assessment.Eating, assessment.Dressing, assessment.GettingInOutOfBed,
                    assessment.UsingToilet, assessment.IndoorWalking, assessment.Bathing,
                    assessment.CompletedItems, assessment.SelfCareLevel, assessment.CareLevel,
                    assessment.Remark);

                if (result.IsSuccess)
                {
                    LogInfo($"能力鉴定保存成功: Id={result.Value}");
                    return Result.Success(result.Value);
                }

                return Result.Failure<long>(result.ErrorCode!, result.Message!);
            }
        }
        catch (Exception ex)
        {
            LogException(ex, "保存能力鉴定失败");
            return Result.FromException<long>(ex);
        }
    }

    /// <summary>
    /// 删除能力鉴定
    /// </summary>
    public async Task<Result> DeleteAsync(long id, CancellationToken ct = default)
    {
        LogInfo($"删除能力鉴定: Id={id}");
        try
        {
            var sql = "UPDATE nc_biz_capability_assessments SET deleted_at = NOW() WHERE id = $1";
            // 删除失败必须如实上报（此前结果被丢弃，无条件返回成功）
            await ExecOrThrowAsync(_db, sql, ct, id);

            LogInfo($"能力鉴定删除成功: Id={id}");
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "删除能力鉴定失败");
            return Result.FromException(ex);
        }
    }
}
