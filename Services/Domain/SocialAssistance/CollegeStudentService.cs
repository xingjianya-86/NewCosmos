using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>
/// 大学生档案服务实现（家庭成员→大学生关联）
/// </summary>
public class CollegeStudentService : BaseService, ICollegeStudentService
{
    private readonly IDatabaseService _db;

    protected override string ServiceName => "CollegeStudentService";

    public CollegeStudentService(ILoggerService logger, IDatabaseService db) : base(logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    private const string SelectColumns =
        "id, application_id, family_member_id, student_name, id_card, education_level, " +
        "school_name, school_duration, enrollment_year, graduation_year, status, remark, created_at, updated_at";

    /// <summary>
    /// 「保障已终止」判定子查询表达式（关联列 = 传入的身份证列表达式）：
    /// 有停保档案 且 无在保/在途续档——停旧建新续档（旧 Stopped + 新 Draft）不算终止，不误剔。
    /// 关联键只用身份证（family_member_id 混语义必悬空、application_id 无回写回路，均不可作依据）。
    /// 状态字面量与 ApplicationStatusCodes 同步：Stopped=停保；Draft/Submitted/Approved/Completed=在保在途。
    /// 消费方：今年毕业过滤、GetAllAsync 的 is_household_stopped 计算列（已关联/已退出分栏）。
    /// </summary>
    private static string HouseholdStoppedExpr(string idCardExpr) => $@"
(
    EXISTS (SELECT 1 FROM nc_biz_family_members m
            JOIN nc_biz_applications a ON a.id = m.application_id
            WHERE m.id_card = {idCardExpr} AND a.deleted_at IS NULL AND a.status = 'Stopped')
    AND NOT EXISTS (SELECT 1 FROM nc_biz_family_members m2
            JOIN nc_biz_applications a2 ON a2.id = m2.application_id
            WHERE m2.id_card = {idCardExpr} AND a2.deleted_at IS NULL
              AND a2.status IN ('Draft','Submitted','Approved','Completed'))
)";

    /// <inheritdoc/>
    public async Task<Result<List<CollegeStudent>>> GetByApplicationIdAsync(long applicationId, CancellationToken ct = default)
    {
        try
        {
            LogInfo($"获取大学生档案: applicationId={applicationId}");
            var sql = $"SELECT {SelectColumns} FROM nc_biz_college_students WHERE application_id = $1 AND deleted_at IS NULL ORDER BY student_name";
            var result = await _db.QueryAsync<CollegeStudent>(sql, ct, applicationId);
            if (result.IsSuccess) LogInfo($"获取到 {result.Value.Count} 条大学生档案");
            return result;
        }
        catch (Exception ex)
        {
            LogException(ex, "获取大学生档案");
            return Result.FromException<List<CollegeStudent>>(ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<List<CollegeStudent>>> GetAllAsync(string? keyword = null, CancellationToken ct = default)
    {
        try
        {
            var kw = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
            LogInfo($"查询大学生档案列表: keywordLength={(kw ?? string.Empty).Length}");

            // is_household_stopped：保障已终止计算列（非库列）——「已关联/已退出」Tab 分栏依据
            var sql = $@"
                SELECT {SelectColumns},
                       {HouseholdStoppedExpr("id_card")} AS is_household_stopped
                FROM nc_biz_college_students
                WHERE deleted_at IS NULL
                  AND ($1::text IS NULL OR student_name LIKE '%' || $1 || '%' OR id_card = $1)
                ORDER BY student_name
                LIMIT 500";

            var result = await _db.QueryAsync<CollegeStudent>(sql, ct, (object?)kw ?? DBNull.Value);
            if (result.IsSuccess) LogInfo($"查询到 {result.Value.Count} 条大学生档案");
            return result;
        }
        catch (Exception ex)
        {
            LogException(ex, "查询大学生档案列表");
            return Result.FromException<List<CollegeStudent>>(ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<List<FamilyMember>>> SearchEligibleMembersAsync(string? keyword = null, CancellationToken ct = default)
    {
        try
        {
            var kw = string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim();
            LogInfo($"搜索可关联的18-23岁家庭成员: keywordLength={(kw ?? string.Empty).Length}");

            // 从已归档档案的 family_members + 6 张导入台账的 persons 中搜索18-23岁成员
            // 统一用身份证第7-14位提取出生日期计算周岁（family_members.birth_date 大量为 NULL）
            // 增加户主姓名、联系方式、所在村屯字段
            // 导入台账人员通过 head_id_card 关联家庭表获取户主信息
            // [索引豁免] EXTRACT(YEAR FROM AGE(...TO_DATE(SUBSTRING(id_card)))) 由身份证推算周岁，
            // 无原生列可索引（除非另建生成列）；CTE 内作候选集过滤，结果集小。
            var sql = @"
                WITH family_members AS (
                    SELECT m.id, m.name, m.id_card, m.gender, m.birth_date, m.application_id,
                           m.phone, m.home_town, m.home_village,
                           NULL::text AS head_id_card,
                           'family' AS src
                    FROM nc_biz_family_members m
                    WHERE m.deleted_at IS NULL
                      AND m.id_card IS NOT NULL AND LENGTH(m.id_card) = 18
                      AND SUBSTRING(m.id_card FROM 7 FOR 8) ~ '^[0-9]{8}$'
                      AND EXTRACT(YEAR FROM AGE(CURRENT_DATE,
                          TO_DATE(SUBSTRING(m.id_card FROM 7 FOR 8), 'YYYYMMDD'))) BETWEEN $2 AND $3
                ),
                import_persons AS (
                    SELECT p.id, p.name, p.id_card, p.gender,
                           NULL::date AS birth_date, NULL::bigint AS application_id,
                           NULL::text AS phone, NULL::text AS home_town, NULL::text AS home_village,
                           p.head_id_card,
                           'import' AS src
                    FROM nc_biz_rural_subsistence_persons p
                    WHERE p.id_card ~ '^[0-9]{17}[0-9X]$'
                      AND SUBSTRING(p.id_card FROM 7 FOR 8) ~ '^[0-9]{8}$'
                      AND EXTRACT(YEAR FROM AGE(CURRENT_DATE,
                          TO_DATE(SUBSTRING(p.id_card FROM 7 FOR 8), 'YYYYMMDD'))) BETWEEN $2 AND $3
                    UNION ALL
                    SELECT p.id, p.name, p.id_card, p.gender,
                           NULL::date, NULL::bigint, NULL::text, NULL::text, NULL::text,
                           p.head_id_card, 'import'
                    FROM nc_biz_urban_subsistence_persons p
                    WHERE p.id_card ~ '^[0-9]{17}[0-9X]$'
                      AND SUBSTRING(p.id_card FROM 7 FOR 8) ~ '^[0-9]{8}$'
                      AND EXTRACT(YEAR FROM AGE(CURRENT_DATE,
                          TO_DATE(SUBSTRING(p.id_card FROM 7 FOR 8), 'YYYYMMDD'))) BETWEEN $2 AND $3
                    UNION ALL
                    SELECT p.id, p.name, p.id_card, p.gender,
                           NULL::date, NULL::bigint, NULL::text, NULL::text, NULL::text,
                           NULL::text AS head_id_card, 'import'
                    FROM nc_biz_low_income_persons p
                    WHERE p.id_card ~ '^[0-9]{17}[0-9X]$'
                      AND SUBSTRING(p.id_card FROM 7 FOR 8) ~ '^[0-9]{8}$'
                      AND EXTRACT(YEAR FROM AGE(CURRENT_DATE,
                          TO_DATE(SUBSTRING(p.id_card FROM 7 FOR 8), 'YYYYMMDD'))) BETWEEN $2 AND $3
                    UNION ALL
                    SELECT p.id, p.name, p.id_card, p.gender,
                           NULL::date, NULL::bigint, NULL::text, NULL::text, NULL::text,
                           p.head_id_card, 'import'
                    FROM nc_biz_destitute_persons p
                    WHERE p.id_card ~ '^[0-9]{17}[0-9X]$'
                      AND SUBSTRING(p.id_card FROM 7 FOR 8) ~ '^[0-9]{8}$'
                      AND EXTRACT(YEAR FROM AGE(CURRENT_DATE,
                          TO_DATE(SUBSTRING(p.id_card FROM 7 FOR 8), 'YYYYMMDD'))) BETWEEN $2 AND $3
                    UNION ALL
                    SELECT p.id, p.name, p.id_card, p.gender,
                           NULL::date, NULL::bigint, NULL::text, NULL::text, NULL::text,
                           p.head_id_card, 'import'
                    FROM nc_biz_low_income_edge_persons p
                    WHERE p.id_card ~ '^[0-9]{17}[0-9X]$'
                      AND SUBSTRING(p.id_card FROM 7 FOR 8) ~ '^[0-9]{8}$'
                      AND EXTRACT(YEAR FROM AGE(CURRENT_DATE,
                          TO_DATE(SUBSTRING(p.id_card FROM 7 FOR 8), 'YYYYMMDD'))) BETWEEN $2 AND $3
                    UNION ALL
                    SELECT p.id, p.name, p.id_card, p.gender,
                           NULL::date, NULL::bigint, NULL::text, NULL::text, NULL::text,
                           p.head_id_card, 'import'
                    FROM nc_biz_rigid_expenditure_persons p
                    WHERE p.id_card ~ '^[0-9]{17}[0-9X]$'
                      AND SUBSTRING(p.id_card FROM 7 FOR 8) ~ '^[0-9]{8}$'
                      AND EXTRACT(YEAR FROM AGE(CURRENT_DATE,
                          TO_DATE(SUBSTRING(p.id_card FROM 7 FOR 8), 'YYYYMMDD'))) BETWEEN $2 AND $3
                ),
                all_families AS (
                    SELECT applicant_id_card, applicant_name, phone,
                           COALESCE(district, '') || COALESCE(street, '') || COALESCE(community, '') AS location,
                           'rural' AS src
                    FROM nc_biz_rural_subsistence_families
                    UNION ALL
                    SELECT applicant_id_card, applicant_name, phone,
                           COALESCE(district, '') || COALESCE(street, '') || COALESCE(community, ''),
                           'urban'
                    FROM nc_biz_urban_subsistence_families
                    UNION ALL
                    SELECT applicant_id_card, applicant_name, phone,
                           COALESCE(township, '') || COALESCE(village, ''),
                           'low_income'
                    FROM nc_biz_low_income_families
                    UNION ALL
                    SELECT applicant_id_card, applicant_name, phone,
                           COALESCE(district, '') || COALESCE(street, '') || COALESCE(community, ''),
                           'destitute'
                    FROM nc_biz_destitute_families
                    UNION ALL
                    SELECT applicant_id_card, applicant_name, phone,
                           COALESCE(district, '') || COALESCE(street, '') || COALESCE(community, ''),
                           'low_income_edge'
                    FROM nc_biz_low_income_edge_families
                    UNION ALL
                    SELECT applicant_id_card, applicant_name, phone,
                           COALESCE(district, '') || COALESCE(street, '') || COALESCE(community, ''),
                           'rigid_expenditure'
                    FROM nc_biz_rigid_expenditure_families
                ),
                all_candidates AS (
                    SELECT * FROM family_members
                    UNION ALL
                    SELECT * FROM import_persons
                ),
                dedup AS (
                    SELECT DISTINCT ON (id_card) *
                    FROM all_candidates
                    ORDER BY id_card, src
                )
                SELECT d.id, d.name, d.id_card, d.gender, d.birth_date, d.application_id,
                       COALESCE(d.phone, af.phone, '') AS phone,
                       COALESCE(d.home_town, '') AS home_town,
                       COALESCE(d.home_village, '') AS home_village,
                       COALESCE(af.applicant_name, app.applicant_name, '') AS household_head_name,
                       COALESCE(af.location, '') AS home_location_display
                FROM dedup d
                LEFT JOIN all_families af ON af.applicant_id_card = d.head_id_card
                LEFT JOIN nc_biz_applications app ON app.id = d.application_id AND app.deleted_at IS NULL
                WHERE NOT EXISTS (
                    SELECT 1 FROM nc_biz_college_students cs
                    WHERE cs.id_card = d.id_card AND cs.deleted_at IS NULL
                )
                  AND ($1::text IS NULL OR d.name LIKE '%' || $1 || '%' OR d.id_card = $1)
                ORDER BY d.name
                LIMIT 100";

            var result = await _db.QueryAsync<FamilyMember>(sql, ct,
                (object?)kw ?? DBNull.Value,
                CollegeStudentConstants.MinAge,
                CollegeStudentConstants.MaxAge);

            if (result.IsSuccess) LogInfo($"搜索到 {result.Value.Count} 名可关联成员");
            return result;
        }
        catch (Exception ex)
        {
            LogException(ex, "搜索可关联的18-23岁家庭成员");
            return Result.FromException<List<FamilyMember>>(ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<long>> SaveAsync(CollegeStudent student, CancellationToken ct = default)
    {
        try
        {
            var graduationYear = (student.EnrollmentYear ?? DateTime.Today.Year) + (student.SchoolDuration ?? 0);
            LogInfo($"新增大学生档案: studentName={student.StudentName}, familyMemberId={student.FamilyMemberId}");

            var sql = @"
                INSERT INTO nc_biz_college_students
                (application_id, family_member_id, student_name, id_card, education_level,
                 school_name, school_duration, enrollment_year, graduation_year, status, remark, created_at, updated_at)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,NOW(),NOW())
                RETURNING id";

            var result = await _db.ExecuteScalarAsync<long>(sql, ct,
                student.ApplicationId,
                student.FamilyMemberId,
                student.StudentName,
                student.IdCard,
                student.EducationLevel,
                student.SchoolName,
                student.SchoolDuration,
                student.EnrollmentYear,
                graduationYear,
                student.Status,
                student.Remark);

            if (result.IsSuccess) LogInfo($"大学生档案保存成功，ID: {result.Value}");
            return result;
        }
        catch (Exception ex)
        {
            LogException(ex, "新增大学生档案");
            return Result.FromException<long>(ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> UpdateAsync(CollegeStudent student, CancellationToken ct = default)
    {
        try
        {
            var graduationYear = (student.EnrollmentYear ?? DateTime.Today.Year) + (student.SchoolDuration ?? 0);
            LogInfo($"更新大学生档案: id={student.Id}, studentName={student.StudentName}");

            var sql = @"
                UPDATE nc_biz_college_students SET
                    student_name = $1, id_card = $2, education_level = $3, school_name = $4,
                    school_duration = $5, enrollment_year = $6, graduation_year = $7,
                    status = $8, remark = $9, updated_at = NOW()
                WHERE id = $10 AND deleted_at IS NULL
                RETURNING id";

            var result = await _db.ExecuteScalarAsync<long?>(sql, ct,
                student.StudentName,
                student.IdCard,
                student.EducationLevel,
                student.SchoolName,
                student.SchoolDuration,
                student.EnrollmentYear,
                graduationYear,
                student.Status,
                student.Remark,
                student.Id);

            if (result.IsSuccess && result.Value.HasValue)
            {
                LogInfo($"大学生档案更新成功，ID: {result.Value}");
                return Result.Success(true);
            }

            LogWarn($"大学生档案更新失败或不存在: id={student.Id}");
            return Result.Failure<bool>(ErrorCodes.NOT_FOUND, "大学生档案不存在");
        }
        catch (Exception ex)
        {
            LogException(ex, "更新大学生档案");
            return Result.FromException<bool>(ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<bool>> DeleteAsync(long id, CancellationToken ct = default)
    {
        try
        {
            LogInfo($"删除大学生档案: id={id}");
            var sql = "UPDATE nc_biz_college_students SET deleted_at = NOW(), updated_at = NOW() WHERE id = $1";
            var result = await _db.ExecuteNonQueryAsync(sql, ct, id);
            if (result.IsSuccess && result.Value > 0)
            {
                LogInfo($"大学生档案删除成功，ID: {id}");
                return Result.Success(true);
            }
            return Result.Failure<bool>(ErrorCodes.NOT_FOUND, "大学生档案不存在");
        }
        catch (Exception ex)
        {
            LogException(ex, "删除大学生档案");
            return Result.FromException<bool>(ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<List<CollegeStudent>>> GetGraduatingStudentsAsync(int year, CancellationToken ct = default)
    {
        try
        {
            LogInfo($"获取{year}年毕业的在读/已毕业大学生");
            // 停保过滤（与已关联/已退出分栏同一判定，HouseholdStoppedExpr 单点）：
            // 保障已终止的学生不再提醒核查退出；Tab3「今年毕业」与首页提醒横幅共用本方法，改一处双处生效。
            var sql = $@"
                SELECT {SelectColumns} FROM nc_biz_college_students cs
                WHERE cs.graduation_year = $1 AND cs.deleted_at IS NULL AND cs.status IN ($2, $3)
                  AND NOT {HouseholdStoppedExpr("cs.id_card")}
                ORDER BY cs.student_name
                LIMIT 500";

            var result = await _db.QueryAsync<CollegeStudent>(sql, ct,
                year,
                CollegeStudentConstants.StatusStudying,
                CollegeStudentConstants.StatusGraduated);
            if (result.IsSuccess) LogInfo($"今年毕业大学生 {result.Value.Count} 人");
            return result;
        }
        catch (Exception ex)
        {
            LogException(ex, "获取毕业年大学生");
            return Result.FromException<List<CollegeStudent>>(ex);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<List<string>>> GetStudyingIdCardsAsync(List<string> idCards, CancellationToken ct = default)
    {
        if (idCards == null || idCards.Count == 0)
            return Result.Success(new List<string>());

        try
        {
            // 身份证列表以单个 text[] 参数传入（= ANY），参数个数与 N 无关；
            // 只按身份证关联（application_id 无回写回路、family_member_id 混语义，均不可作依据）。
            // 注意：string[] 必须包一层 object[]，否则会被 params object[] 展开成多个参数，
            // $2 将变成单个字符串而非数组，导致 ANY($2) 解析失败（42809/22P02）。
            const string sql = @"SELECT DISTINCT UPPER(TRIM(id_card)) AS id_card
                                 FROM nc_biz_college_students
                                 WHERE status = $1 AND deleted_at IS NULL
                                   AND id_card = ANY($2::text[])";

            LogInfo($"批量查在读大学生身份证: 输入 {idCards.Count} 张");
            var result = await _db.QueryAsync<CollegeStudent>(sql, ct,
                new object[] { CollegeStudentConstants.StatusStudying, idCards.ToArray() });

            if (result.IsSuccess)
            {
                var cards = result.Value
                    .Select(s => s.IdCard?.Trim() ?? string.Empty)
                    .Where(c => c.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                LogInfo($"在读大学生命中 {cards.Count} 张身份证");
                return Result.Success(cards);
            }
            return Result.Failure<List<string>>(result.ErrorCode!, result.Message!);
        }
        catch (Exception ex)
        {
            LogException(ex, "批量查在读大学生身份证");
            return Result.FromException<List<string>>(ex);
        }
    }
}
