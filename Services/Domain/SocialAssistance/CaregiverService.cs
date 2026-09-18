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

        var shouldManageTransaction = !_db.HasTransaction;
        if (shouldManageTransaction)
            await _db.BeginTransactionAsync();

        try
        {
            var delSql = "DELETE FROM nc_biz_caregivers WHERE application_id = $1";
            var delResult = await _db.ExecuteNonQueryAsync(delSql, ct, applicationId);
            if (delResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return delResult;
            }

            foreach (var caregiver in caregivers)
            {
                caregiver.ApplicationId = applicationId;
                var insSql = @"INSERT INTO nc_biz_caregivers
                    (application_id, cared_member_id, name, id_card, phone,
                     relationship, gender, age, ethnicity, marital_status,
                     hukou_type, education_level, political_status,
                     health_status, employment_status, main_income_source,
                     work_unit, position, address, created_at)
                    VALUES ($1, $2, $3, $4, $5,
                            $6, $7, $8, $9, $10,
                            $11, $12, $13,
                            $14, $15, $16,
                            $17, $18, $19, NOW());";
                var insResult = await _db.ExecuteNonQueryAsync(insSql, ct,
                    caregiver.ApplicationId, caregiver.CaredMemberId,
                    caregiver.Name, caregiver.IdCard, caregiver.Phone,
                    caregiver.Relationship, caregiver.Gender, caregiver.Age,
                    caregiver.Ethnicity, caregiver.MaritalStatus,
                    caregiver.HukouType, caregiver.EducationLevel, caregiver.PoliticalStatus,
                    caregiver.HealthStatus, caregiver.EmploymentStatus, caregiver.MainIncomeSource,
                    caregiver.WorkUnit, caregiver.Position, caregiver.Address);
                if (insResult.IsFailure)
                {
                    if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                    return insResult;
                }
            }

            if (shouldManageTransaction)
                await _db.CommitTransactionAsync();

            LogInfo("照料人保存成功");
            return Result.Success();
        }
        catch (Exception ex)
        {
            if (shouldManageTransaction)
                await _db.RollbackTransactionAsync();
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
