using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>
/// 家庭成员服务实现
/// </summary>
public class FamilyMemberService : BaseService, IFamilyMemberService
{
    protected override string ServiceName => "FamilyMemberService";
    private readonly IDatabaseService _db;

    public FamilyMemberService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
    }

    public async Task<Result<List<FamilyMember>>> GetByApplicationIdAsync(long applicationId, CancellationToken ct = default)
    {
        LogInfo($"执行操作");
        var sql = "SELECT * FROM nc_biz_family_members WHERE application_id = $1 AND deleted_at IS NULL ORDER BY is_applicant DESC, created_at";
        var result = await _db.QueryAsync<FamilyMember>(sql, ct, applicationId);

        // 归一化 member_category（历史/导入数据可能存中文显示值或空值），并推导户主内存标记。
        // 不归一化会导致分类不一致的成员在界面被静默过滤、被用户重复录入（曾造成整单保存失败）。
        if (result.IsSuccess && result.Value != null)
        {
            foreach (var member in result.Value)
            {
                member.MemberCategory = MemberCategoryHelper.Normalize(
                    member.MemberCategory, member.IsApplicant, member.RelationshipToHead);
                // IsHouseholdHead 是内存计算属性（DB 无 is_household_head 列），按申请人/户主分类推导
                member.IsHouseholdHead = member.IsApplicant
                    || string.Equals(member.MemberCategory, MemberCategoryConstants.HOUSEHOLD_HEAD, StringComparison.OrdinalIgnoreCase);
            }
        }

        return result;
    }

    public async Task<Result<FamilyMember>> GetByIdAsync(long id, CancellationToken ct = default)
    {
        LogInfo($"执行操作");
        var sql = "SELECT * FROM nc_biz_family_members WHERE id = $1 AND deleted_at IS null";
        return await _db.QuerySingleAsync<FamilyMember>(sql, ct, id);
    }

    public async Task<Result<long>> AddAsync(FamilyMemberCreateRequest request, CancellationToken ct = default)
    {
        ValidateNotNull(request, nameof(request));
        ValidateNotNullOrEmpty(request.Name, nameof(request.Name));
        ValidateNotNullOrEmpty(request.IdCard, nameof(request.IdCard));

        LogInfo($"执行操作");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            var existsSql = "SELECT COUNT(*) FROM nc_biz_family_members WHERE application_id = $1 AND id_card = $2 AND deleted_at IS null";
            var existsResult = await _db.ExecuteScalarAsync(existsSql, ct, request.ApplicationId, request.IdCard);
            if (existsResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<long>(existsResult.ErrorCode!, existsResult.Message!);
            }

            if (existsResult.Value > 0)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<long>(ErrorCodes.DUPLICATE_ID_CARD, "该身份证号已在此申请中存在");
            }

            if (request.IsHouseholdHead)
            {
                var clearHeadSql = "UPDATE nc_biz_family_members SET is_applicant = false WHERE application_id = $1";
                await ExecOrThrowAsync(_db, clearHeadSql, ct, request.ApplicationId);
            }

            var sql = @"INSERT INTO nc_biz_family_members
            (application_id, name, id_card, relationship_to_head, gender, age, ethnicity, phone,
            hukou_type, hukou_address, home_province, home_city, home_district, home_town, home_village, home_address,
            hukou_province, hukou_city, hukou_district, hukou_town,
            marital_status, education_level, political_status,
            health_status, is_severe_disability, is_disabled, disability_type, disability_level,
            disability_certificate_no, disease_category, disease_name, secondary_disease,
            disease_code, is_severe_disease, is_labor_exempt,
            is_applicant, member_category, employment_status, work_unit, main_income_source, annual_income, work_capacity,
            monthly_income_capacity, family_size, person_type, annual_support_fee, monthly_support_fee,
            support_months, is_support_ability, birth_date,
            created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10,
            $11, $12, $13, $14, $15, $16, $17, $18, $19, $20,
            $21, $22, $23, $24, $25, $26, $27, $28, $29, $30,
            $31, $32, $33, $34, $35, $36, $37, $38, $39, $40,
            $41, $42, $43, $44, $45, $46, $47, $48, $49, $50,
            NOW(), NOW())
            RETURNING id";

            var insertResult = await _db.ExecuteScalarAsync(sql, ct,
                request.ApplicationId, request.Name, request.IdCard, request.Relation,
                request.Gender, (object)request.Age!, request.Ethnicity, request.Phone,
                request.HukouType, request.HukouAddress,
                request.HomeProvince, request.HomeCity, request.HomeDistrict, request.HomeTown, request.HomeVillage, request.HomeAddress,
                request.HukouProvince, request.HukouCity, request.HukouDistrict, request.HukouTown,
                request.MaritalStatus, request.EducationLevel, request.PoliticalStatus,
                request.HealthStatus, request.HasSevereIllness, request.HasDisability,
                request.DisabilityType, request.DisabilityLevel,
                request.DisabilityCertificateNo, request.DiseaseCategory, request.DiseaseName, request.SecondaryDiseaseName,
                request.DiseaseCode, request.IsSevereDisease, request.IsLaborExempt,
                request.IsHouseholdHead, request.MemberCategory,
                request.EmploymentStatus, request.WorkUnit, request.MainIncomeSource, request.AnnualIncome,
                request.WorkCapacity,
                request.MonthlyIncomeCapacity, (object?)request.FamilySize, request.PersonType,
                request.AnnualSupportFee, request.MonthlySupportFee, (object?)request.SupportMonths,
                request.IsSupportAbility, (object?)request.BirthDate);

            if (insertResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<long>(insertResult.ErrorCode!, insertResult.Message!);
            }

            await tx.CommitAsync(ct);

            LogInfo($"执行操作");
            Logger.LogBusiness("添加家庭成员", ("MemberId", insertResult.Value), ("Name", DataMasker.MaskName(request.Name)));
            return Result.Success(insertResult.Value);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "失败");
            return Result.FromException<long>(ex);
        }
    }

    public async Task<Result> UpdateAsync(FamilyMemberUpdateRequest request, CancellationToken ct = default)
    {
        ValidateNotNull(request, nameof(request));

        LogInfo($"执行操作");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            var memberResult = await GetByIdAsync(request.MemberId, ct);
            if (memberResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure(memberResult.ErrorCode!, memberResult.Message!);
            }

            var member = memberResult.Value;
            if (member == null)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure(ErrorCodes.FAMILY_MEMBER_NOT_FOUND, "家庭成员不存在");
            }

            if (request.IsHouseholdHead && !member.IsHouseholdHead)
            {
                var clearHeadSql = "UPDATE nc_biz_family_members SET is_applicant = false WHERE application_id = $1";
                await ExecOrThrowAsync(_db, clearHeadSql, ct, member.ApplicationId);
            }

            var sql = @"UPDATE nc_biz_family_members SET
            name = $1, relationship_to_head = $2, gender = $3, age = $4, ethnicity = $5,
            phone = $6, hukou_type = $7, hukou_address = $8, marital_status = $9,
            education_level = $10, political_status = $11,
            home_province = $12, home_city = $13, home_district = $14, home_town = $15, home_village = $16, home_address = $17,
            hukou_province = $18, hukou_city = $19, hukou_district = $20, hukou_town = $21,
            health_status = $22, is_severe_disability = $23, is_disabled = $24,
            disability_type = $25, disability_level = $26, disability_certificate_no = $27,
            disease_category = $28, disease_name = $29, secondary_disease = $30,
            disease_code = $31, is_severe_disease = $32, is_labor_exempt = $33,
            is_applicant = $34,
            member_category = $35, employment_status = $36, work_unit = $37,
            main_income_source = $38, annual_income = $39, work_capacity = $40,
            monthly_income_capacity = $41, family_size = $42, person_type = $43,
            annual_support_fee = $44, monthly_support_fee = $45, support_months = $46,
            is_support_ability = $47,
            birth_date = $48,
            updated_at = NOW()
            WHERE id = $49";

            var updateResult = await _db.ExecuteNonQueryAsync(sql, ct,
                request.Name, request.Relation,
                request.Gender, (object)request.Age!, request.Ethnicity, request.Phone,
                request.HukouType, request.HukouAddress, request.MaritalStatus, request.EducationLevel,
                request.PoliticalStatus,
                request.HomeProvince, request.HomeCity, request.HomeDistrict, request.HomeTown, request.HomeVillage, request.HomeAddress,
                request.HukouProvince, request.HukouCity, request.HukouDistrict, request.HukouTown,
                request.HealthStatus, request.HasSevereIllness, request.HasDisability,
                request.DisabilityType, request.DisabilityLevel, request.DisabilityCertificateNo,
                request.DiseaseCategory, request.DiseaseName, request.SecondaryDiseaseName,
                request.DiseaseCode, request.IsSevereDisease, request.IsLaborExempt,
                request.IsHouseholdHead, request.MemberCategory,
                request.EmploymentStatus, request.WorkUnit, request.MainIncomeSource, request.AnnualIncome,
                request.WorkCapacity,
                request.MonthlyIncomeCapacity, (object?)request.FamilySize, request.PersonType,
                request.AnnualSupportFee, request.MonthlySupportFee, (object?)request.SupportMonths,
                request.IsSupportAbility,
                (object?)request.BirthDate,
                request.MemberId);

            if (updateResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure(updateResult.ErrorCode!, updateResult.Message!);
            }

            await tx.CommitAsync(ct);

            LogInfo($"执行操作");
            return Result.Success();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "失败");
            return Result.FromException(ex);
        }
    }

    public async Task<Result> DeleteAsync(long id, CancellationToken ct = default)
    {
        LogInfo($"执行操作");

        var memberResult = await GetByIdAsync(id, ct);
        if (memberResult.IsFailure)
            return Result.Failure(memberResult.ErrorCode!, memberResult.Message!);

        var member = memberResult.Value;
        if (member == null)
            return Result.Failure(ErrorCodes.FAMILY_MEMBER_NOT_FOUND, "家庭成员不存在");

        if (member.IsHouseholdHead)
            return Result.Failure(ErrorCodes.MEMBER_IS_HEAD, "户主不能直接删除，请先变更户主");

        var canDeleteResult = await CanDeleteMemberAsync(member.ApplicationId, id, ct);
        if (canDeleteResult.IsFailure)
            return Result.Failure(canDeleteResult.ErrorCode!, canDeleteResult.Message!);

        if (!canDeleteResult.Value)
            return Result.Failure(ErrorCodes.LAST_MEMBER_CANNOT_DELETE, "最后一个家庭成员不能删除");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            // 1. 软删除家庭成员
            var sql = "UPDATE nc_biz_family_members SET deleted_at = NOW() WHERE id = $1";
            var deleteResult = await _db.ExecuteNonQueryAsync(sql, ct, id);

            if (deleteResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure(deleteResult.ErrorCode!, deleteResult.Message!);
            }

            // 2. 同时标记关联的赡养抚养扶养记录（通过身份证号匹配）
            // 注意：nc_biz_supporters 表已废弃，赡养人数据现在存储在 nc_biz_family_members 中
            // 删除 family_member 时，对应的 member_category='赡养抚养扶养' 记录也会被软删除
            // 因为它们是同一条记录

            await tx.CommitAsync(ct);

            LogInfo($"执行操作");
            return Result.Success();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogError($"删除家庭成员失败: {ex.Message}");
            return Result.Failure(ErrorCodes.DB_CONNECTION_FAILED, ex.Message);
        }
    }

    /// <summary>
    /// 删除申请的所有家庭成员（软删除），返回删除行数
    /// </summary>
    public async Task<Result<int>> DeleteByApplicationIdAsync(long applicationId, CancellationToken ct = default)
    {
        LogInfo($"执行操作");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            // 1. 软删除所有家庭成员
            var sql = "UPDATE nc_biz_family_members SET deleted_at = NOW() WHERE application_id = $1 AND deleted_at IS null";
            var deleteResult = await _db.ExecuteNonQueryAsync(sql, ct, applicationId);

            if (deleteResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<int>(deleteResult.ErrorCode!, deleteResult.Message!);
            }

            // 2. 同时标记所有关联的赡养抚养扶养记录
            // 注意：nc_biz_supporters 表已废弃，赡养人数据现在存储在 nc_biz_family_members 中
            // 删除所有 family_member 时，member_category='赡养抚养扶养' 的记录也会被上面的 SQL 一并软删除

            await tx.CommitAsync(ct);

            LogInfo($"执行操作: 删除行数={deleteResult.Value}");
            return Result.Success(deleteResult.Value);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogError($"删除家庭成员失败: {ex.Message}");
            return Result.Failure<int>(ErrorCodes.DB_CONNECTION_FAILED, ex.Message);
        }
    }

    /// <summary>
    /// 查询申请下已登记死亡的成员身份证（历史数据未软删，展示/生成档案时须过滤）
    /// </summary>
    public async Task<Result<List<string>>> GetDeadIdCardsByApplicationIdAsync(long applicationId, CancellationToken ct = default)
    {
        var sql = "SELECT member_id_card FROM nc_biz_death_records WHERE application_id = $1 AND member_id_card IS NOT NULL";
        var result = await _db.QueryAsync<string>(sql, ct, applicationId);
        return result.IsSuccess
            ? Result.Success(result.Value ?? new List<string>())
            : Result.Failure<List<string>>(result.ErrorCode!, result.Message!);
    }

    public async Task<Result> SetHouseholdHeadAsync(long applicationId, long memberId, CancellationToken ct = default)
    {
        LogInfo($"设置户主: ApplicationId={applicationId}");

        var memberResult = await GetByIdAsync(memberId, ct);
        if (memberResult.IsFailure)
            return Result.Failure(memberResult.ErrorCode!, memberResult.Message!);

        var member = memberResult.Value;
        if (member == null)
            return Result.Failure(ErrorCodes.FAMILY_MEMBER_NOT_FOUND, "家庭成员不存在");

        if (member.ApplicationId != applicationId)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "确定");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            var clearSql = "UPDATE nc_biz_family_members SET is_applicant = false WHERE application_id = $1";
            await ExecOrThrowAsync(_db, clearSql, ct, applicationId);

            var setSql = "UPDATE nc_biz_family_members SET is_applicant = true, updated_at = NOW() WHERE id = $1";
            await ExecOrThrowAsync(_db, setSql, ct, memberId);

            await tx.CommitAsync(ct);

            LogInfo($"执行操作");
            return Result.Success();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "失败");
            return Result.FromException(ex);
        }
    }

    public async Task<Result<bool>> CanDeleteMemberAsync(long applicationId, long memberId, CancellationToken ct = default)
    {
        var countResult = await GetCountByApplicationIdAsync(applicationId, ct);
        if (countResult.IsFailure)
            return Result.Failure<bool>(countResult.ErrorCode!, countResult.Message!);

        return Result.Success(countResult.Value > 1);
    }

    public async Task<Result<int>> GetCountByApplicationIdAsync(long applicationId, CancellationToken ct = default)
    {
        var sql = "SELECT COUNT(*) FROM nc_biz_family_members WHERE application_id = $1 AND deleted_at IS null";
        var result = await _db.ExecuteScalarAsync(sql, ct, applicationId);
        if (result.IsFailure)
            return Result.Failure<int>(result.ErrorCode!, result.Message!);

        return Result.Success(Convert.ToInt32(result.Value));
    }

    public async Task<Result> BatchUpdateIncomeAsync(long applicationId, Dictionary<long, decimal> incomes, CancellationToken ct = default)
    {
        ValidateNotNull(incomes, nameof(incomes));

        LogInfo($"批量更新收入: ApplicationId={applicationId}");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            // unnest 数组批改（原逐行 UPDATE 为 N+1）；数组实参传 List<T>（勿传 T[]，防 params 协变）
            var sql = @"UPDATE nc_biz_family_members SET annual_income = v.annual_income, updated_at = NOW()
                FROM (SELECT * FROM unnest($1::bigint[], $2::numeric[]) AS t(id, annual_income)) v
                WHERE nc_biz_family_members.id = v.id AND nc_biz_family_members.application_id = $3";
            await ExecOrThrowAsync(_db, sql, ct,
                incomes.Keys.ToList(), incomes.Values.ToList(), applicationId);

            await tx.CommitAsync(ct);

            LogInfo($"执行操作");
            return Result.Success();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "失败");
            return Result.FromException(ex);
        }
    }
}
