using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>
/// 赡养人服务（底层数据存储在 nc_biz_family_members 表，member_category = "Support"）
/// </summary>
public class SupporterService : BaseService, ISupporterService
{
    protected override string ServiceName => "SupporterService";
    private readonly IDatabaseService _db;

    public SupporterService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
    }

    public async Task<Result<List<Supporter>>> GetByApplicationIdAsync(long applicationId, CancellationToken ct = default)
    {
        LogInfo($"查询赡养人信息: ApplicationId={applicationId}");
        var sql = @"SELECT id, application_id, name, id_card, person_type, relationship_to_head AS relationship,
                           annual_support_fee, is_support_ability,
                           monthly_support_fee, support_months, family_size AS supporter_family_size,
                           gender, age, ethnicity, phone, marital_status, hukou_type, education_level,
                           political_status, health_status, work_unit,
                           employment_status, main_income_source, work_capacity, annual_income,
                           home_province, home_city, home_district, home_town, home_village, home_address,
                           hukou_province, hukou_city, hukou_district, hukou_town
                    FROM nc_biz_family_members
                    WHERE application_id = $1 AND member_category = 'Support' AND deleted_at IS NULL
                    ORDER BY created_at";
        return await _db.QueryAsync<Supporter>(sql, ct, applicationId);
    }

    public async Task<Result> SaveAsync(long applicationId, List<Supporter> supporters, CancellationToken ct = default)
    {
        ValidateNotNull(supporters, nameof(supporters));

        LogInfo($"保存赡养人信息: ApplicationId={applicationId}");

        var shouldManageTransaction = !_db.HasTransaction;
        if (shouldManageTransaction)
            await _db.BeginTransactionAsync();

        try
        {
            // 软删除该申请的所有赡养抚养扶养成员
            var delSql = "UPDATE nc_biz_family_members SET deleted_at = NOW() WHERE application_id = $1 AND member_category = 'Support' AND deleted_at IS NULL";
            var delResult = await _db.ExecuteNonQueryAsync(delSql, ct, applicationId);
            if (delResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return delResult;
            }

            foreach (var supporter in supporters)
            {
                supporter.ApplicationId = applicationId;

                if (supporter.Id > 0)
                {
                    // 更新已有记录：写入赡养人字段（不覆盖 member_category，保留原有分类）
                    var updSql = @"UPDATE nc_biz_family_members SET
                        name = $2, id_card = $3, person_type = $4, relationship_to_head = $5,
                        annual_support_fee = $6, is_support_ability = $7,
                        monthly_support_fee = $8, support_months = $9, family_size = $10,
                        gender = $11, age = $12, ethnicity = $13, phone = $14,
                        marital_status = $15, hukou_type = $16, education_level = $17, political_status = $18,
                        health_status = $19, work_unit = $20,
                        employment_status = $21, main_income_source = $22, work_capacity = $23, annual_income = $24,
                        home_province = $25, home_city = $26, home_district = $27, home_town = $28,
                        home_village = $29, home_address = $30,
                        hukou_province = $31, hukou_city = $32, hukou_district = $33, hukou_town = $34,
                        deleted_at = NULL, updated_at = NOW()
                        WHERE id = $1 AND application_id = $35";
                    var updResult = await _db.ExecuteNonQueryAsync(updSql, ct,
                        supporter.Id, supporter.Name, supporter.IdCard, supporter.PersonType,
                        supporter.Relationship, supporter.AnnualSupportFee, supporter.IsSupportAbility,
                        supporter.MonthlySupportFee, supporter.SupportMonths, supporter.SupporterFamilySize,
                        supporter.Gender, supporter.Age, supporter.Ethnicity, supporter.Phone,
                        supporter.MaritalStatus, supporter.HukouType, supporter.EducationLevel,
                        supporter.PoliticalStatus, supporter.HealthStatus, supporter.WorkUnit,
                        supporter.EmploymentStatus, supporter.MainIncomeSource, supporter.WorkCapacity, supporter.AnnualIncome,
                        supporter.HomeProvince, supporter.HomeCity, supporter.HomeDistrict, supporter.HomeTown,
                        supporter.HomeVillage, supporter.HomeAddress,
                        supporter.HukouProvince, supporter.HukouCity, supporter.HukouDistrict, supporter.HukouTown,
                        applicationId);
                    if (updResult.IsFailure)
                    {
                        if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                        return updResult;
                    }
                }
                else
                {
                    // 新增记录：插入 nc_biz_family_members 并设置 member_category
                    var insSql = @"INSERT INTO nc_biz_family_members
                        (application_id, name, id_card, person_type, relationship_to_head,
                         annual_support_fee, is_support_ability,
                         monthly_support_fee, support_months, family_size,
                         gender, age, ethnicity, phone, marital_status, hukou_type, education_level,
                         political_status, health_status, work_unit,
                         employment_status, main_income_source, work_capacity, annual_income,
                         home_province, home_city, home_district, home_town, home_village, home_address,
                         hukou_province, hukou_city, hukou_district, hukou_town,
                         member_category, created_at)
                        VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10,
                                $11, $12, $13, $14, $15, $16, $17, $18, $19, $20,
                                $21, $22, $23, $24, $25, $26, $27, $28, $29, $30,
                                $31, $32, $33, $34,
                                'Support', NOW())";
                    var insResult = await _db.ExecuteNonQueryAsync(insSql, ct,
                        applicationId, supporter.Name, supporter.IdCard, supporter.PersonType,
                        supporter.Relationship, supporter.AnnualSupportFee, supporter.IsSupportAbility,
                        supporter.MonthlySupportFee, supporter.SupportMonths, supporter.SupporterFamilySize,
                        supporter.Gender, supporter.Age, supporter.Ethnicity, supporter.Phone,
                        supporter.MaritalStatus, supporter.HukouType, supporter.EducationLevel,
                        supporter.PoliticalStatus, supporter.HealthStatus, supporter.WorkUnit,
                        supporter.EmploymentStatus, supporter.MainIncomeSource, supporter.WorkCapacity, supporter.AnnualIncome,
                        supporter.HomeProvince, supporter.HomeCity, supporter.HomeDistrict, supporter.HomeTown,
                        supporter.HomeVillage, supporter.HomeAddress,
                        supporter.HukouProvince, supporter.HukouCity, supporter.HukouDistrict, supporter.HukouTown);
                    if (insResult.IsFailure)
                    {
                        if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                        return insResult;
                    }
                }
            }

            if (shouldManageTransaction)
                await _db.CommitTransactionAsync();

            LogInfo("保存赡养人信息成功");
            return Result.Success();
        }
        catch (Exception ex)
        {
            if (shouldManageTransaction)
                await _db.RollbackTransactionAsync();
            LogError("保存赡养人信息失败");
            return Result.Failure(ErrorCodes.DB_CONNECTION_FAILED, ex.Message);
        }
    }

    public async Task<Result> DeleteByApplicationIdAsync(long applicationId, CancellationToken ct = default)
    {
        LogInfo($"删除赡养人信息: ApplicationId={applicationId}");
        var sql = "UPDATE nc_biz_family_members SET deleted_at = NOW() WHERE application_id = $1 AND member_category = 'Support' AND deleted_at IS NULL";
        return await _db.ExecuteNonQueryAsync(sql, ct, applicationId);
    }
}
