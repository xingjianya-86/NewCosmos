using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.SocialAssistance;

public class CaregiverService : BaseService, ICaregiverService
{
    protected override string ServiceName => "CaregiverService";
    private readonly IDatabaseService _db;

    public CaregiverService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
    }

    public async Task<Result<List<Caregiver>>> GetByApplicationIdAsync(long applicationId, CancellationToken ct = default)
    {
        LogInfo($"查询照料人: ApplicationId={applicationId}");
        var sql = "SELECT * FROM nc_biz_caregivers WHERE application_id = $1 AND deleted_at IS NULL ORDER BY created_at";
        return await _db.QueryAsync<Caregiver>(sql, ct, applicationId);
    }

    public async Task<Result> SaveAsync(long applicationId, List<Caregiver> caregivers, CancellationToken ct = default)
    {
        ValidateNotNull(caregivers, nameof(caregivers));

        LogInfo($"保存照料人: ApplicationId={applicationId}, Count={caregivers.Count}");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            var delSql = "DELETE FROM nc_biz_caregivers WHERE application_id = $1";
            var delResult = await _db.ExecuteNonQueryAsync(delSql, ct, applicationId);
            if (delResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return delResult;
            }

            foreach (var caregiver in caregivers)
                caregiver.ApplicationId = applicationId;

            // 多行 VALUES 批写（原逐行 INSERT 为 N+1）
            const int chunkSize = 500;
            for (var chunkStart = 0; chunkStart < caregivers.Count; chunkStart += chunkSize)
            {
                var chunk = caregivers.Skip(chunkStart).Take(chunkSize).ToList();
                var (valuesClause, insertArgs) = NewCosmos.Helpers.MultiRowValuesBuilder.Build(chunk.Count, 19, i =>
                {
                    var c = chunk[i];
                    return new object?[] { c.ApplicationId, c.CaredMemberId, c.Name, c.IdCard, c.Phone,
                        c.Relationship, c.Gender, c.Age, c.Ethnicity, c.MaritalStatus,
                        c.HukouType, c.EducationLevel, c.PoliticalStatus,
                        c.HealthStatus, c.EmploymentStatus, c.MainIncomeSource,
                        c.WorkUnit, c.Position, c.Address };
                }, s => $"({string.Join(",", Enumerable.Range(0, 19).Select(k => $"${s + k}"))},NOW())");

                var insSql = $@"INSERT INTO nc_biz_caregivers
                    (application_id, cared_member_id, name, id_card, phone,
                     relationship, gender, age, ethnicity, marital_status,
                     hukou_type, education_level, political_status,
                     health_status, employment_status, main_income_source,
                     work_unit, position, address, created_at)
                    VALUES {valuesClause}";
                var insResult = await _db.ExecuteNonQueryAsync(insSql, ct, insertArgs);
                if (insResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return insResult;
                }
            }

            await tx.CommitAsync(ct);

            LogInfo("照料人保存成功");
            return Result.Success();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogError("照料人保存失败");
            return Result.Failure(ErrorCodes.DB_CONNECTION_FAILED, ex.Message);
        }
    }

    public async Task<Result> DeleteByApplicationIdAsync(long applicationId, CancellationToken ct = default)
    {
        LogInfo($"删除照料人: ApplicationId={applicationId}");
        var sql = "DELETE FROM nc_biz_caregivers WHERE application_id = $1";
        return await _db.ExecuteNonQueryAsync(sql, ct, applicationId);
    }
}
