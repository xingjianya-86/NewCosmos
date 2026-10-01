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

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            // 软删除该申请的所有赡养抚养扶养成员
            var delSql = "UPDATE nc_biz_family_members SET deleted_at = NOW() WHERE application_id = $1 AND member_category = 'Support' AND deleted_at IS NULL";
            var delResult = await _db.ExecuteNonQueryAsync(delSql, ct, applicationId);
            if (delResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return delResult;
            }

            foreach (var supporter in supporters)
                supporter.ApplicationId = applicationId;

            // 新增/更新分流：新增走多行 VALUES 批插，更新走 unnest 数组批改（原逐行 SQL 为 N+1）
            var inserts = supporters.Where(s => s.Id <= 0).ToList();
            var updates = supporters.Where(s => s.Id > 0).ToList();

            if (updates.Count > 0)
            {
                var updSql = @"UPDATE nc_biz_family_members SET
                    name = v.name, id_card = v.id_card, person_type = v.person_type, relationship_to_head = v.relationship_to_head,
                    annual_support_fee = v.annual_support_fee, is_support_ability = v.is_support_ability,
                    monthly_support_fee = v.monthly_support_fee, support_months = v.support_months, family_size = v.family_size,
                    gender = v.gender, age = v.age, ethnicity = v.ethnicity, phone = v.phone,
                    marital_status = v.marital_status, hukou_type = v.hukou_type, education_level = v.education_level, political_status = v.political_status,
                    health_status = v.health_status, work_unit = v.work_unit,
                    employment_status = v.employment_status, main_income_source = v.main_income_source, work_capacity = v.work_capacity, annual_income = v.annual_income,
                    home_province = v.home_province, home_city = v.home_city, home_district = v.home_district, home_town = v.home_town,
                    home_village = v.home_village, home_address = v.home_address,
                    hukou_province = v.hukou_province, hukou_city = v.hukou_city, hukou_district = v.hukou_district, hukou_town = v.hukou_town,
                    deleted_at = NULL, updated_at = NOW()
                FROM (
                    SELECT * FROM unnest(
                        $1::bigint[], $2::text[], $3::text[], $4::text[], $5::text[], $6::numeric[], $7::boolean[],
                        $8::numeric[], $9::integer[], $10::integer[], $11::text[], $12::integer[], $13::text[],
                        $14::text[], $15::text[], $16::text[], $17::text[], $18::text[],
                        $19::text[], $20::text[], $21::text[], $22::text[], $23::text[], $24::numeric[], $25::text[],
                        $26::text[], $27::text[], $28::text[], $29::text[], $30::text[],
                        $31::text[], $32::text[], $33::text[], $34::text[]
                    ) AS t(id, name, id_card, person_type, relationship_to_head, annual_support_fee, is_support_ability,
                           monthly_support_fee, support_months, family_size, gender, age, ethnicity, phone,
                           marital_status, hukou_type, education_level, political_status,
                           health_status, work_unit, employment_status, main_income_source, work_capacity, annual_income,
                           home_province, home_city, home_district, home_town, home_village, home_address,
                           hukou_province, hukou_city, hukou_district, hukou_town)
                ) v
                WHERE nc_biz_family_members.id = v.id AND nc_biz_family_members.application_id = $35";
                // 数组实参传 List<T>（勿传 T[]——单数组协变会退化 params 形参，见 NewPermissionService 教训）
                var updResult = await _db.ExecuteNonQueryAsync(updSql, ct,
                    updates.Select(s => s.Id).ToList(),
                    updates.Select(s => s.Name).ToList(),
                    updates.Select(s => s.IdCard).ToList(),
                    updates.Select(s => s.PersonType).ToList(),
                    updates.Select(s => s.Relationship).ToList(),
                    updates.Select(s => s.AnnualSupportFee).ToList(),
                    updates.Select(s => s.IsSupportAbility).ToList(),
                    updates.Select(s => s.MonthlySupportFee).ToList(),
                    updates.Select(s => s.SupportMonths).ToList(),
                    updates.Select(s => s.SupporterFamilySize).ToList(),
                    updates.Select(s => s.Gender).ToList(),
                    updates.Select(s => s.Age).ToList(),
                    updates.Select(s => s.Ethnicity).ToList(),
                    updates.Select(s => s.Phone).ToList(),
                    updates.Select(s => s.MaritalStatus).ToList(),
                    updates.Select(s => s.HukouType).ToList(),
                    updates.Select(s => s.EducationLevel).ToList(),
                    updates.Select(s => s.PoliticalStatus).ToList(),
                    updates.Select(s => s.HealthStatus).ToList(),
                    updates.Select(s => s.WorkUnit).ToList(),
                    updates.Select(s => s.EmploymentStatus).ToList(),
                    updates.Select(s => s.MainIncomeSource).ToList(),
                    updates.Select(s => s.WorkCapacity).ToList(),
                    updates.Select(s => s.AnnualIncome).ToList(),
                    updates.Select(s => s.HomeProvince).ToList(),
                    updates.Select(s => s.HomeCity).ToList(),
                    updates.Select(s => s.HomeDistrict).ToList(),
                    updates.Select(s => s.HomeTown).ToList(),
                    updates.Select(s => s.HomeVillage).ToList(),
                    updates.Select(s => s.HomeAddress).ToList(),
                    updates.Select(s => s.HukouProvince).ToList(),
                    updates.Select(s => s.HukouCity).ToList(),
                    updates.Select(s => s.HukouDistrict).ToList(),
                    updates.Select(s => s.HukouTown).ToList(),
                    applicationId);
                if (updResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return updResult;
                }
            }

            const int chunkSize = 500;
            for (var chunkStart = 0; chunkStart < inserts.Count; chunkStart += chunkSize)
            {
                var chunk = inserts.Skip(chunkStart).Take(chunkSize).ToList();
                var (valuesClause, insertArgs) = NewCosmos.Helpers.MultiRowValuesBuilder.Build(chunk.Count, 34, i =>
                {
                    var s = chunk[i];
                    return new object?[] { s.ApplicationId, s.Name, s.IdCard, s.PersonType, s.Relationship,
                        s.AnnualSupportFee, s.IsSupportAbility, s.MonthlySupportFee, s.SupportMonths, s.SupporterFamilySize,
                        s.Gender, s.Age, s.Ethnicity, s.Phone, s.MaritalStatus, s.HukouType, s.EducationLevel,
                        s.PoliticalStatus, s.HealthStatus, s.WorkUnit, s.EmploymentStatus, s.MainIncomeSource,
                        s.WorkCapacity, s.AnnualIncome, s.HomeProvince, s.HomeCity, s.HomeDistrict, s.HomeTown,
                        s.HomeVillage, s.HomeAddress, s.HukouProvince, s.HukouCity, s.HukouDistrict, s.HukouTown };
                }, start => $"({string.Join(",", Enumerable.Range(0, 34).Select(k => $"${start + k}"))},'Support',NOW())");

                var insSql = $@"INSERT INTO nc_biz_family_members
                    (application_id, name, id_card, person_type, relationship_to_head,
                     annual_support_fee, is_support_ability,
                     monthly_support_fee, support_months, family_size,
                     gender, age, ethnicity, phone, marital_status, hukou_type, education_level,
                     political_status, health_status, work_unit,
                     employment_status, main_income_source, work_capacity, annual_income,
                     home_province, home_city, home_district, home_town, home_village, home_address,
                     hukou_province, hukou_city, hukou_district, hukou_town,
                     member_category, created_at)
                    VALUES {valuesClause}";
                var insResult = await _db.ExecuteNonQueryAsync(insSql, ct, insertArgs);
                if (insResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return insResult;
                }
            }

            await tx.CommitAsync(ct);

            LogInfo("保存赡养人信息成功");
            return Result.Success();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
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
