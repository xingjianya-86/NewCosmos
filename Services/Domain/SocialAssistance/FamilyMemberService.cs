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
        var sql = "SELECT " + WideTableColumns.FamilyMembersColumns + " FROM nc_biz_family_members WHERE application_id = $1 AND deleted_at IS NULL ORDER BY is_applicant DESC, created_at";
        var result = await _db.QueryAsync<FamilyMember>(sql, ct, applicationId);

        // 归一化 member_category（历史/导入数据可能存中文显示值或空值），并推导户主内存标记。
        // 不归一化会导致分类不一致的成员在界面被静默过滤、被用户重复录入（曾造成整单保存失败）。
        if (result.HasValue)
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
        var sql = "SELECT " + WideTableColumns.FamilyMembersColumns + " FROM nc_biz_family_members WHERE id = $1 AND deleted_at IS null";
        return await _db.QuerySingleAsync<FamilyMember>(sql, ct, id);
    }

    public async Task<Result<long>> AddAsync(FamilyMemberCreateRequest request, CancellationToken ct = default)
    {
        ValidateNotNull(request, nameof(request));
        ValidateNotNullOrEmpty(request.Name, nameof(request.Name));
        ValidateNotNullOrEmpty(request.IdCard, nameof(request.IdCard));


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
            support_months, is_support_ability, birth_date, medical_self_paid_12m,
            created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10,
            $11, $12, $13, $14, $15, $16, $17, $18, $19, $20,
            $21, $22, $23, $24, $25, $26, $27, $28, $29, $30,
            $31, $32, $33, $34, $35, $36, $37, $38, $39, $40,
            $41, $42, $43, $44, $45, $46, $47, $48, $49, $50, $51,
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
                request.IsSupportAbility, (object?)request.BirthDate,
                (object?)request.MedicalSelfPaid12m);

            if (insertResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<long>(insertResult.ErrorCode!, insertResult.Message!);
            }

            await tx.CommitAsync(ct);

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

    /// <summary>
    /// 多行 VALUES 批量插入家庭成员（保存草稿/提交的批写路径，替代逐人 AddAsync N+1：
    /// 6 口之家原 ~18 条串行 SQL（每人 COUNT 防重 + INSERT）→ 本方法 2 条）。
    /// 防重语义：① 批内去重由调用方负责（表单层已按身份证去重）；② 库内已存在同身份证
    /// 则整体 Failure DUPLICATE_ID_CARD（比逐人静默跳过更强的显式失败，§7）。
    /// 不做 IsHouseholdHead 清位（调用方先 DeleteByApplicationIdAsync 全删，无残留行可冲突）。
    /// 返回 id_card → 新 id 映射（RETURNING id, id_card，供赡养人落库引用）。
    /// </summary>
    public async Task<Result<IReadOnlyDictionary<string, long>>> AddRangeAsync(
        IReadOnlyList<FamilyMemberCreateRequest> requests, CancellationToken ct = default)
    {
        ValidateNotNull(requests, nameof(requests));
        if (requests.Count == 0)
            return Result.Success<IReadOnlyDictionary<string, long>>(
                new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase));

        foreach (var r in requests)
        {
            ValidateNotNull(r, nameof(requests));
            ValidateNotNullOrEmpty(r.Name, nameof(r.Name));
            ValidateNotNullOrEmpty(r.IdCard, nameof(r.IdCard));
        }

        LogInfo($"批量添加家庭成员: {requests.Count} 人");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);
        try
        {
            // 库内防重（一次批查替代逐人 COUNT）：任一身份证已存在则整体拒绝
            var existsSql = "SELECT id_card FROM nc_biz_family_members WHERE application_id = $1 AND id_card = ANY($2::text[]) AND deleted_at IS NULL";
            var existsResult = await _db.QueryAsync<string>(existsSql, ct,
                requests[0].ApplicationId, requests.Select(r => r.IdCard.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
            if (existsResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<IReadOnlyDictionary<string, long>>(existsResult.ErrorCode!, existsResult.Message!);
            }
            var existing = new HashSet<string>(existsResult.Value ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var conflict = requests.FirstOrDefault(r => existing.Contains(r.IdCard.Trim()));
            if (conflict != null)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<IReadOnlyDictionary<string, long>>(ErrorCodes.DUPLICATE_ID_CARD,
                    $"身份证号 {DataMasker.MaskIdCard(conflict.IdCard)} 已在此申请中存在");
            }

            // 多行 VALUES 批插：53 列（51 业务列 + created_at/updated_at）× N 行，$1..$51N 平铺（§8 鼓励的批写模式）
            // [SQL 白名单背书] cols=本地 const 列清单（硬编码 53 列，与 ApplicationColumns 同模式）；
            // values 由硬编码 rowTpl + 请求对象字段生成，无外部 SQL 输入（check_standards SQL1 豁免 cols/values）。
            const string cols = @"application_id, name, id_card, relationship_to_head, gender, age, ethnicity, phone,
                hukou_type, hukou_address, home_province, home_city, home_district, home_town, home_village, home_address,
                hukou_province, hukou_city, hukou_district, hukou_town,
                marital_status, education_level, political_status,
                health_status, is_severe_disability, is_disabled, disability_type, disability_level,
                disability_certificate_no, disease_category, disease_name, secondary_disease,
                disease_code, is_severe_disease, is_labor_exempt,
                is_applicant, member_category, employment_status, work_unit, main_income_source, annual_income, work_capacity,
                monthly_income_capacity, family_size, person_type, annual_support_fee, monthly_support_fee,
                support_months, is_support_ability, birth_date, medical_self_paid_12m,
                created_at, updated_at";
            // 占位符必须与 cols 的业务列数严格相等（51）：曾漏 ${48}/${49} → "INSERT has more target columns than expressions"。
            var rowTpl = @"(${0},${1},${2},${3},${4},${5},${6},${7},${8},${9},${10},${11},${12},${13},${14},${15},${16},${17},${18},${19},
                ${20},${21},${22},${23},${24},${25},${26},${27},${28},${29},${30},${31},${32},${33},${34},${35},${36},${37},${38},${39},
                ${40},${41},${42},${43},${44},${45},${46},${47},${48},${49},${50}, NOW(), NOW())";

            var values = new List<string>(requests.Count);
            var args = new List<object>(requests.Count * 51);
            foreach (var r in requests)
            {
                var baseIdx = args.Count;
                values.Add(string.Format(global::System.Globalization.CultureInfo.InvariantCulture, rowTpl,
                    baseIdx + 1, baseIdx + 2, baseIdx + 3, baseIdx + 4, baseIdx + 5, baseIdx + 6, baseIdx + 7, baseIdx + 8, baseIdx + 9, baseIdx + 10,
                    baseIdx + 11, baseIdx + 12, baseIdx + 13, baseIdx + 14, baseIdx + 15, baseIdx + 16, baseIdx + 17, baseIdx + 18, baseIdx + 19, baseIdx + 20,
                    baseIdx + 21, baseIdx + 22, baseIdx + 23, baseIdx + 24, baseIdx + 25, baseIdx + 26, baseIdx + 27, baseIdx + 28, baseIdx + 29, baseIdx + 30,
                    baseIdx + 31, baseIdx + 32, baseIdx + 33, baseIdx + 34, baseIdx + 35, baseIdx + 36, baseIdx + 37, baseIdx + 38, baseIdx + 39, baseIdx + 40,
                    baseIdx + 41, baseIdx + 42, baseIdx + 43, baseIdx + 44, baseIdx + 45, baseIdx + 46, baseIdx + 47, baseIdx + 48, baseIdx + 49,
                    baseIdx + 50, baseIdx + 51));

                args.AddRange(new object[]
                {
                    r.ApplicationId, r.Name, r.IdCard, r.Relation,
                    r.Gender, r.Age!, r.Ethnicity, r.Phone,
                    r.HukouType, r.HukouAddress,
                    r.HomeProvince, r.HomeCity, r.HomeDistrict, r.HomeTown, r.HomeVillage, r.HomeAddress,
                    r.HukouProvince, r.HukouCity, r.HukouDistrict, r.HukouTown,
                    r.MaritalStatus, r.EducationLevel, r.PoliticalStatus,
                    r.HealthStatus, r.HasSevereIllness, r.HasDisability,
                    r.DisabilityType, r.DisabilityLevel,
                    r.DisabilityCertificateNo, r.DiseaseCategory, r.DiseaseName, r.SecondaryDiseaseName,
                    r.DiseaseCode, r.IsSevereDisease, r.IsLaborExempt,
                    r.IsHouseholdHead, r.MemberCategory,
                    r.EmploymentStatus, r.WorkUnit, r.MainIncomeSource, r.AnnualIncome, r.WorkCapacity,
                    r.MonthlyIncomeCapacity, (object?)r.FamilySize!, r.PersonType,
                    r.AnnualSupportFee, r.MonthlySupportFee, (object?)r.SupportMonths!,
                    r.IsSupportAbility, (object?)r.BirthDate!,
                    (object?)r.MedicalSelfPaid12m!
                });
            }

            var sql = $"INSERT INTO nc_biz_family_members ({cols}) VALUES {string.Join(", ", values)} RETURNING id, id_card";
            var insertResult = await _db.QueryAsync<(long Id, string IdCard)>(sql, ct, args.ToArray());
            if (insertResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<IReadOnlyDictionary<string, long>>(insertResult.ErrorCode!, insertResult.Message!);
            }

            await tx.CommitAsync(ct);

            var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            foreach (var (id, idCard) in insertResult.Value ?? new List<(long, string)>())
                if (!string.IsNullOrWhiteSpace(idCard)) map[idCard.Trim()] = id;

            Logger.LogBusiness("批量添加家庭成员", ("Count", requests.Count), ("ApplicationId", requests[0].ApplicationId));
            return Result.Success<IReadOnlyDictionary<string, long>>(map);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "批量添加家庭成员失败");
            return Result.FromException<IReadOnlyDictionary<string, long>>(ex);
        }
    }

    public async Task<Result> UpdateAsync(FamilyMemberUpdateRequest request, CancellationToken ct = default)
    {
        ValidateNotNull(request, nameof(request));


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
            medical_self_paid_12m = $49,
            updated_at = NOW()
            WHERE id = $50";

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
                (object?)request.MedicalSelfPaid12m,
                request.MemberId);

            if (updateResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure(updateResult.ErrorCode!, updateResult.Message!);
            }

            await tx.CommitAsync(ct);

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
    /// 软删除申请的【非赡养】家庭成员（户主+共同生活），返回删除行数（2026-11-12）。
    /// 表单保存链专用：赡养行（member_category='Support'）由 SupporterService 独占写入
    ///（upsert + 差集删），本方法不得触碰——原"全删全插"与其互打会产生同刻创建即删除的孤儿行。
    /// </summary>
    public async Task<Result<int>> DeleteNonSupportByApplicationIdAsync(long applicationId, CancellationToken ct = default)
    {
        LogInfo($"软删非赡养成员: ApplicationId={applicationId}");

        // 2026-11-19 户主行不参与表单"全删全插"：户主由
        // UpsertHouseholdHeadFromApplicationAsync 从主表单向维护，删掉会导致户主行丢失/回退旧快照。
        var sql = @"UPDATE nc_biz_family_members SET deleted_at = NOW()
                    WHERE application_id = $1 AND deleted_at IS NULL
                      AND COALESCE(member_category, '') <> 'Support'
                      AND COALESCE(member_category, '') <> 'HouseholdHead'
                      AND COALESCE(is_applicant, FALSE) = FALSE";
        var deleteResult = await _db.ExecuteNonQueryAsync(sql, ct, applicationId);
        if (deleteResult.IsFailure)
            return Result.Failure<int>(deleteResult.ErrorCode!, deleteResult.Message!);

        LogInfo($"软删非赡养成员完成: 删除行数={deleteResult.Value}");
        return Result.Success(deleteResult.Value);
    }

    /// <summary>
    /// 按主表申请人字段同步户主成员行（2026-11-19）。
    /// 背景：表单建档只保存共同生活成员，户主权威在主表；缺失户主行会让户主死亡/月报/打印定位失败。
    /// 做法：① 更新身份证与主表申请人一致的户主候选行（唯一户主标记）；
    ///       ② 无户主候选行时从主表 INSERT；③ 清除非现任户主的 is_applicant 标记。
    /// 幂等，可在表单保存事务内反复调用；只覆盖主表有权威值的字段，成员表独有字段（户籍省市区镇等）不动。
    /// </summary>
    public async Task<Result> UpsertHouseholdHeadFromApplicationAsync(long applicationId, CancellationToken ct = default)
    {
        LogInfo($"同步户主成员行: ApplicationId={applicationId}");

        const string updateSql = @"UPDATE nc_biz_family_members m SET
            name = TRIM(a.applicant_name),
            id_card = TRIM(a.applicant_id_card),
            is_applicant = TRUE,
            relationship_to_head = '本人/户主',
            member_category = 'HouseholdHead',
            gender = COALESCE(NULLIF(a.gender, ''), m.gender),
            birth_date = CASE WHEN a.applicant_id_card ~ '^[0-9]{17}[0-9Xx]$'
                              THEN to_date(substr(a.applicant_id_card, 7, 8), 'YYYYMMDD') ELSE m.birth_date END,
            age = CASE WHEN a.applicant_id_card ~ '^[0-9]{17}[0-9Xx]$'
                       THEN date_part('year', age(to_date(substr(a.applicant_id_card, 7, 8), 'YYYYMMDD')))::int ELSE m.age END,
            ethnicity = COALESCE(NULLIF(a.ethnicity, ''), m.ethnicity),
            marital_status = COALESCE(NULLIF(a.marital_status, ''), m.marital_status),
            phone = COALESCE(NULLIF(a.applicant_phone, ''), m.phone),
            hukou_type = COALESCE(NULLIF(a.hukou_type, ''), m.hukou_type),
            hukou_address = COALESCE(NULLIF(a.hukou_address, ''), m.hukou_address),
            home_province = COALESCE(NULLIF(a.province, ''), m.home_province),
            home_city = COALESCE(NULLIF(a.city, ''), m.home_city),
            home_district = COALESCE(NULLIF(a.district, ''), m.home_district),
            home_town = COALESCE(NULLIF(a.town, ''), m.home_town),
            home_village = COALESCE(NULLIF(a.community, ''), m.home_village),
            home_address = COALESCE(NULLIF(a.address, ''), m.home_address),
            education_level = COALESCE(NULLIF(a.education_level, ''), m.education_level),
            political_status = COALESCE(NULLIF(a.political_status, ''), m.political_status),
            health_status = COALESCE(NULLIF(a.health_status, ''), m.health_status),
            employment_status = COALESCE(NULLIF(a.employment_status, ''), m.employment_status),
            work_unit = COALESCE(NULLIF(a.work_unit, ''), m.work_unit),
            main_income_source = COALESCE(NULLIF(a.income_source, ''), m.main_income_source),
            is_disabled = COALESCE(a.disability_type, '') <> '',
            disability_certificate_no = COALESCE(NULLIF(a.disability_card_no, ''), m.disability_certificate_no),
            disability_type = COALESCE(NULLIF(a.disability_type, ''), m.disability_type),
            disability_level = COALESCE(NULLIF(a.disability_level, ''), m.disability_level),
            disease_name = COALESCE(NULLIF(a.disease_name, ''), m.disease_name),
            secondary_disease = COALESCE(NULLIF(a.secondary_disease_name, ''), m.secondary_disease),
            is_severe_disease = COALESCE(a.is_severe_disease, m.is_severe_disease),
            family_size = COALESCE(a.family_size, m.family_size),
            updated_at = NOW()
        FROM nc_biz_applications a
        WHERE a.id = $1 AND a.deleted_at IS NULL
          AND m.application_id = $1 AND m.deleted_at IS NULL
          AND COALESCE(TRIM(a.applicant_id_card), '') <> ''
          AND m.id = (
              SELECT m2.id FROM nc_biz_family_members m2
              WHERE m2.application_id = $1 AND m2.deleted_at IS NULL
                AND (COALESCE(m2.is_applicant, FALSE)
                     OR m2.member_category = 'HouseholdHead'
                     OR TRIM(COALESCE(m2.id_card, '')) = TRIM(a.applicant_id_card))
              ORDER BY (TRIM(COALESCE(m2.id_card, '')) = TRIM(a.applicant_id_card)) DESC,
                       COALESCE(m2.is_applicant, FALSE) DESC,
                       (m2.member_category = 'HouseholdHead') DESC,
                       m2.id
              LIMIT 1)";

        var updateResult = await _db.ExecuteNonQueryAsync(updateSql, ct, applicationId);
        if (updateResult.IsFailure)
            return Result.Failure(updateResult.ErrorCode!, updateResult.Message!);

        // 无户主候选行（档案有成员但都不是户主）→ 从主表补一条
        const string insertSql = @"INSERT INTO nc_biz_family_members (
            application_id, name, id_card, is_applicant, relationship_to_head, member_category,
            gender, birth_date, age, ethnicity, marital_status, phone,
            hukou_type, hukou_address,
            home_province, home_city, home_district, home_town, home_village, home_address,
            education_level, political_status, health_status, employment_status, work_unit, main_income_source,
            is_disabled, disability_certificate_no, disability_type, disability_level,
            disease_name, secondary_disease, is_severe_disease, family_size,
            created_at, updated_at)
        SELECT a.id, TRIM(a.applicant_name), TRIM(a.applicant_id_card), TRUE, '本人/户主', 'HouseholdHead',
            a.gender,
            CASE WHEN a.applicant_id_card ~ '^[0-9]{17}[0-9Xx]$'
                 THEN to_date(substr(a.applicant_id_card, 7, 8), 'YYYYMMDD') END,
            CASE WHEN a.applicant_id_card ~ '^[0-9]{17}[0-9Xx]$'
                 THEN date_part('year', age(to_date(substr(a.applicant_id_card, 7, 8), 'YYYYMMDD')))::int END,
            a.ethnicity, a.marital_status, a.applicant_phone,
            a.hukou_type, a.hukou_address,
            a.province, a.city, a.district, a.town, a.community, a.address,
            a.education_level, a.political_status, a.health_status, a.employment_status, a.work_unit, a.income_source,
            COALESCE(a.disability_type, '') <> '', a.disability_card_no, a.disability_type, a.disability_level,
            a.disease_name, a.secondary_disease_name, COALESCE(a.is_severe_disease, FALSE), a.family_size,
            NOW(), NOW()
        FROM nc_biz_applications a
        WHERE a.id = $1 AND a.deleted_at IS NULL
          AND COALESCE(TRIM(a.applicant_name), '') <> ''
          AND COALESCE(TRIM(a.applicant_id_card), '') <> ''
          AND NOT EXISTS (
              SELECT 1 FROM nc_biz_family_members m
              WHERE m.application_id = a.id AND m.deleted_at IS NULL
                AND (COALESCE(m.is_applicant, FALSE)
                     OR m.member_category = 'HouseholdHead'
                     OR TRIM(COALESCE(m.id_card, '')) = TRIM(a.applicant_id_card)))";

        var insertResult = await _db.ExecuteNonQueryAsync(insertSql, ct, applicationId);
        if (insertResult.IsFailure)
            return Result.Failure(insertResult.ErrorCode!, insertResult.Message!);

        // 清除非现任户主的 is_applicant 标记（历史户主变更未清理会一档多户主）
        const string clearStaleSql = @"UPDATE nc_biz_family_members m SET is_applicant = FALSE, updated_at = NOW()
            FROM nc_biz_applications a
            WHERE a.id = $1 AND a.deleted_at IS NULL
              AND m.application_id = $1 AND m.deleted_at IS NULL
              AND COALESCE(TRIM(a.applicant_id_card), '') <> ''
              AND m.is_applicant
              AND TRIM(COALESCE(m.id_card, '')) <> TRIM(a.applicant_id_card)";
        var clearResult = await _db.ExecuteNonQueryAsync(clearStaleSql, ct, applicationId);
        if (clearResult.IsFailure)
            return Result.Failure(clearResult.ErrorCode!, clearResult.Message!);

        LogInfo($"户主成员行同步完成: 更新={updateResult.Value} 新增={insertResult.Value} 清旧标记={clearResult.Value}");
        return Result.Success();
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
