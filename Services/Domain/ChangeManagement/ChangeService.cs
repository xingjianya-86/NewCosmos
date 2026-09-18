using System.Text;
using System.Text.Json;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Enums;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.StateMachine;

namespace NewCosmos.Services.Domain.ChangeManagement;

using ApplicationEntity = NewCosmos.Models.Entities.Application;

/// <summary>
/// 变更上下文
/// </summary>
public class ChangeContext
{
    public long ApplicationId { get; set; }
    public string ChangeType { get; set; } = string.Empty;

    /// <summary>变更类别（DictionaryConstants.ChangeCategory.*；默认 null 不写）</summary>
    public string? ChangeCategory { get; set; }

    public string ChangeReason { get; set; } = string.Empty;
    public string ChangeReasonType { get; set; } = string.Empty;
    public DateTime ChangeDate { get; set; } = DateTime.Today;
    public string OperatorName { get; set; } = string.Empty;
}

/// <summary>
/// 经济复核上下文
/// </summary>
public class EconomicReviewContext
{
    public long ApplicationId { get; set; }
    public decimal NewTotalFamilyIncome { get; set; }
    public decimal NewPerCapitaIncome { get; set; }
    public decimal NewTotalAnnualIncome { get; set; }
    public decimal NewPerCapitaAnnualIncome { get; set; }
    public decimal NewRigidExpenditure { get; set; }
    public int NewFamilySize { get; set; }
    public string ReviewReason { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;

    /// <summary>
    /// 是否为家庭信息修正模式（需要额外校验周期限制）
    /// </summary>
    public bool IsFamilyCorrection { get; set; }
}

/// <summary>
/// 家庭成员变更上下文（增员/减员后重新认定，停旧建新）
/// </summary>
public class MemberChangeContext
{
    public long ApplicationId { get; set; }

    /// <summary>主变更类型（DictionaryConstants.ChangeType.MEMBER_ADD/MEMBER_REMOVE/MEMBER_MODIFY）</summary>
    public string ChangeType { get; set; } = DictionaryConstants.ChangeType.MEMBER_MODIFY;

    /// <summary>变更摘要（如 "新增 张三；减员 李四"），并入变更原因</summary>
    public string ChangeSummary { get; set; } = string.Empty;

    /// <summary>变更原因（表单"原因详情"，必填）</summary>
    public string ChangeReason { get; set; } = string.Empty;

    /// <summary>逐人变更明细（增员/减员原因登记；写入快照与 nc_biz_change_details，死亡减员联动死亡记录）</summary>
    public List<MemberChangeEntry> Entries { get; set; } = new();

    /// <summary>变更前家庭人数（表单加载时口径，用于 Before 快照）</summary>
    public int OldFamilySize { get; set; }

    public decimal NewTotalFamilyIncome { get; set; }
    public decimal NewPerCapitaIncome { get; set; }
    public decimal NewTotalAnnualIncome { get; set; }
    public decimal NewPerCapitaAnnualIncome { get; set; }
    public decimal NewRigidExpenditure { get; set; }
    public int NewFamilySize { get; set; }
    public string OperatorName { get; set; } = string.Empty;
}

/// <summary>
/// 家庭成员变更明细（增员/减员逐人原因登记）
/// </summary>
public class MemberChangeEntry
{
    /// <summary>方向：DictionaryConstants.ChangeType.MEMBER_ADD / MEMBER_REMOVE</summary>
    public string Direction { get; set; } = DictionaryConstants.ChangeType.MEMBER_ADD;

    /// <summary>成员ID（减员为旧档案成员行ID；增员为 0）</summary>
    public long MemberId { get; set; }

    public string Name { get; set; } = string.Empty;
    public string IdCard { get; set; } = string.Empty;
    public string RelationshipToHead { get; set; } = string.Empty;
    public string MemberCategory { get; set; } = string.Empty;

    /// <summary>变更原因代码（MemberChangeReasonConstants）</summary>
    public string ReasonCode { get; set; } = string.Empty;

    /// <summary>变更原因名称（冗余存储，供输出与审计直读）</summary>
    public string ReasonName { get; set; } = string.Empty;

    /// <summary>事由日期（死亡减员=死亡日期，其余=生效日期）</summary>
    public DateTime EventDate { get; set; } = DateTime.Today;

    public string Remark { get; set; } = string.Empty;
}

/// <summary>
/// 成员死亡上下文
/// </summary>
public class MemberDeathContext
{
    public long ApplicationId { get; set; }
    public long MemberId { get; set; }
    public string MemberName { get; set; } = string.Empty;
    public string MemberIdCard { get; set; } = string.Empty;
    public DateTime DeathDate { get; set; } = DateTime.Today;
    public string DeathReason { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
}

/// <summary>
/// 户主变更上下文
/// </summary>
public class HeadChangeContext
{
    public long ApplicationId { get; set; }
    public long OldHeadMemberId { get; set; }
    public long NewHeadMemberId { get; set; }
    public string OldHeadName { get; set; } = string.Empty;
    public string NewHeadName { get; set; } = string.Empty;
    public string ChangeReason { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
}

/// <summary>
/// 户主死亡变更上下文（停旧建新）
/// </summary>
public class HouseholdDeathContext
{
    /// <summary>
    /// 旧档案申请ID
    /// </summary>
    public long ApplicationId { get; set; }

    /// <summary>
    /// 死亡原户主成员ID（0 时由服务端按"申请人/户主"自动定位）
    /// </summary>
    public long DeceasedHeadMemberId { get; set; }

    /// <summary>
    /// 死亡原户主姓名
    /// </summary>
    public string DeceasedHeadName { get; set; } = string.Empty;

    /// <summary>
    /// 死亡原户主身份证号
    /// </summary>
    public string DeceasedHeadIdCard { get; set; } = string.Empty;

    /// <summary>
    /// 新户主成员ID（在原档案中的 member id）
    /// </summary>
    public long NewHeadMemberId { get; set; }

    /// <summary>
    /// 新户主姓名
    /// </summary>
    public string NewHeadName { get; set; } = string.Empty;

    /// <summary>
    /// 新户主身份证号
    /// </summary>
    public string NewHeadIdCard { get; set; } = string.Empty;

    /// <summary>
    /// 死亡日期
    /// </summary>
    public DateTime DeathDate { get; set; } = DateTime.Today;

    /// <summary>
    /// 死亡原因
    /// </summary>
    public string DeathReason { get; set; } = string.Empty;

    /// <summary>
    /// 死亡证明编号
    /// </summary>
    public string DeathCertificateNo { get; set; } = string.Empty;

    /// <summary>
    /// 操作人
    /// </summary>
    public string OperatorName { get; set; } = string.Empty;
}

/// <summary>
/// 变更结果
/// </summary>
public class ChangeResult
{
    public long ChangeId { get; set; }
    public bool IsSuccess { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public string OldClassification { get; set; } = string.Empty;
    public string NewClassification { get; set; } = string.Empty;
    public decimal OldGuaranteeAmount { get; set; }
    public decimal NewGuaranteeAmount { get; set; }
    public bool TriggeredGracePeriod { get; set; }
    public bool TriggeredStop { get; set; }

    /// <summary>
    /// 户主死亡停旧建新后生成的新档案申请ID（其余变更场景为 0）
    /// </summary>
    public long NewApplicationId { get; set; }
}

/// <summary>
/// 变更服务接口
/// </summary>
public interface IChangeService
{
    /// <summary>
    /// 创建变更记录
    /// </summary>
    Task<Result<long>> CreateChangeAsync(ChangeContext context, string beforeJson, string afterJson, CancellationToken ct = default);

    /// <summary>
    /// 保存快照
    /// </summary>
    Task SaveSnapshotAsync(long changeId, string snapshotType, string dataJson, CancellationToken ct = default);

    /// <summary>
    /// 保存变更明细
    /// </summary>
    Task SaveDetailAsync(long changeId, string fieldName, string oldValue, string newValue, CancellationToken ct = default);

    /// <summary>
    /// 执行经济复核
    /// </summary>
    Task<Result<ChangeResult>> ExecuteEconomicReviewAsync(EconomicReviewContext context, CancellationToken ct = default);

    /// <summary>
    /// 执行家庭成员变更（增员/减员后重新认定，停旧建新）
    /// </summary>
    Task<Result<ChangeResult>> ExecuteMemberChangeAsync(MemberChangeContext context, CancellationToken ct = default);

    /// <summary>
    /// 处理成员死亡
    /// </summary>
    Task<Result<ChangeResult>> ProcessMemberDeathAsync(MemberDeathContext context, CancellationToken ct = default);

    /// <summary>
    /// 执行户主变更
    /// </summary>
    Task<Result<ChangeResult>> ExecuteHeadChangeAsync(HeadChangeContext context, CancellationToken ct = default);

    /// <summary>
    /// 执行户主死亡变更（停旧建新：旧档案停止，以新户主建立新档案并继承全部子表数据）
    /// </summary>
    Task<Result<ChangeResult>> ExecuteHouseholdDeathAsync(HouseholdDeathContext context, CancellationToken ct = default);

    /// <summary>
    /// 获取变更历史
    /// </summary>
    Task<List<ChangeRecord>> GetChangeHistoryAsync(long applicationId, CancellationToken ct = default);
}

/// <summary>
/// 变更记录
/// </summary>
public class ChangeRecord
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }
    public string ChangeNo { get; set; } = string.Empty;
    public string ChangeType { get; set; } = string.Empty;
    public string ChangeReason { get; set; } = string.Empty;
    public DateTime ChangeDate { get; set; }
    public string OperatorName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ChangedAt { get; set; }
}

/// <summary>
/// 变更服务实现
/// </summary>
public class ChangeService : BaseService, IChangeService
{
    protected override string ServiceName => "ChangeService";
    private readonly IDatabaseService _db;
    private readonly IClassificationService _classificationService;
    private readonly IGracePeriodService _gracePeriodService;
    private readonly ISupporterService _supporterService;
    private readonly IApplicationService _applicationService;
    private readonly IEconomicDetailService _economicDetailService;
    private readonly Services.Utilities.IBusinessTimelineService _businessTimelineService;
    private readonly Services.Domain.ElderlyBenefits.IElderlyApplicationService _elderlyApplicationService;

    public ChangeService(
        IDatabaseService db,
        IClassificationService classificationService,
        IGracePeriodService gracePeriodService,
        ISupporterService supporterService,
        IApplicationService applicationService,
        IEconomicDetailService economicDetailService,
        ILoggerService logger,
        Services.Utilities.IBusinessTimelineService businessTimelineService,
        Services.Domain.ElderlyBenefits.IElderlyApplicationService elderlyApplicationService) : base(logger)
    {
        _db = db;
        _classificationService = classificationService;
        _gracePeriodService = gracePeriodService;
        _supporterService = supporterService;
        _applicationService = applicationService;
        _economicDetailService = economicDetailService;
        _businessTimelineService = businessTimelineService;
        _elderlyApplicationService = elderlyApplicationService;
    }

    public async Task<Result<long>> CreateChangeAsync(ChangeContext context, string beforeJson, string afterJson, CancellationToken ct = default)
    {
        var changeNo = $"CHG{DateTime.Now:yyyyMMddHHmmssfff}";

        var shouldManageTransaction = !_db.HasTransaction;
        if (shouldManageTransaction)
            await _db.BeginTransactionAsync();

        try
        {
            var sql = @"INSERT INTO nc_biz_change_records 
                       (application_id, change_no, change_type, change_category, change_reason, change_reason_type, change_date, operator_name, changed_at)
                       VALUES ($1,$2,$3,$8,$4,$5,$6,$7, NOW()) RETURNING id;";

            var result = await _db.ExecuteScalarAsync(sql, ct,
                context.ApplicationId, changeNo, context.ChangeType,
                context.ChangeReason, context.ChangeReasonType,
                context.ChangeDate, context.OperatorName,
                (object?)context.ChangeCategory);

            // 变更主记录写入失败必须中断：静默返回 0 会让调用方带着无效 ChangeId 提交事务，
            // 审计链条就断了。Result 化后由调用方显式检查并回滚。
            if (result.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<long>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    result.Message ?? "创建变更记录失败");
            }

            var changeId = result.Value;

            try
            {
                if (!string.IsNullOrEmpty(beforeJson))
                    await SaveSnapshotAsync(changeId, "Before", beforeJson, ct);
                if (!string.IsNullOrEmpty(afterJson))
                    await SaveSnapshotAsync(changeId, "After", afterJson, ct);
            }
            catch (Exception snapEx)
            {
                // 快照是变更审计的核心证据：写入失败即整体失败（避免"变更成功但无快照"断档）
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                LogException(snapEx, "保存变更快照");
                return Result.Failure<long>(ErrorCodes.DB_QUERY_ERROR,
                    $"保存变更快照失败: {snapEx.Message}");
            }

            if (shouldManageTransaction)
                await _db.CommitTransactionAsync();

            LogInfo($"创建变更记录: ChangeId={changeId}");
            return Result.Success(changeId);
        }
        catch (Exception ex)
        {
            if (shouldManageTransaction) await _db.RollbackTransactionAsync();
            LogException(ex, "创建变更记录");
            return Result.FromException<long>(ex);
        }
    }

    public async Task SaveSnapshotAsync(long changeId, string snapshotType, string dataJson, CancellationToken ct = default)
    {
        var sql = @"INSERT INTO nc_biz_change_snapshots (change_id, snapshot_type, snapshot_data) VALUES ($1,$2,$3::jsonb);";
        var result = await _db.ExecuteNonQueryAsync(sql, ct, changeId, snapshotType, dataJson);
        // 快照是变更审计的核心证据，写入失败不能吞掉（否则出现"变更成功但无快照"的断档）
        if (result.IsFailure)
            throw new BusinessException(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? $"保存变更快照失败: ChangeId={changeId}, Type={snapshotType}");
    }

    public async Task SaveDetailAsync(long changeId, string fieldName, string oldValue, string newValue, CancellationToken ct = default)
    {
        var sql = @"INSERT INTO nc_biz_change_details (change_id, field_name, old_value, new_value) VALUES ($1,$2,$3,$4);";
        var result = await _db.ExecuteNonQueryAsync(sql, ct, changeId, fieldName, oldValue, newValue);
        if (result.IsFailure)
            throw new BusinessException(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? $"保存变更明细失败: ChangeId={changeId}, Field={fieldName}");
    }

    /// <summary>
    /// 渐退期状态重建（复核/变更后按新分类决定去留）：
    /// - 新分类仍属低收入（IsCodeLowIncome）→ 保留：已激活维持现状（含"渐退期中间政策变动仍符合"的情形），
    ///   判定触发新渐退期则 ActivateAsync 覆盖续接（UPSERT）；
    /// - 新分类非低收入（回低保/特困/刚性/停保等）→ 结清既有渐退期。
    /// 必须在调用方已开启的环境事务内执行。
    /// </summary>
    private async Task<Result> ReconcileGracePeriodAsync(
        long applicationId, string newClassification, GracePeriodCheckResult graceCheck,
        string? oldClassification, decimal? oldGuaranteeAmount, bool forceClear = false,
        CancellationToken ct = default)
    {
        var stillLowIncome = !forceClear && ClassificationConstants.IsCodeLowIncome(newClassification);

        if (!stillLowIncome)
        {
            var clearRes = await _gracePeriodService.ClearAsync(applicationId, ct);
            if (clearRes.IsFailure)
                return Result.Failure(clearRes.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    clearRes.Message ?? "结清渐退期失败");
            return Result.Success();
        }

        if (graceCheck.IsEligible)
        {
            var activateRes = await _gracePeriodService.ActivateAsync(
                applicationId, graceCheck.Months,
                graceCheck.StartDate, graceCheck.EndDate,
                oldClassification, oldGuaranteeAmount, ct);
            if (activateRes.IsFailure)
                return Result.Failure(activateRes.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    activateRes.Message ?? "激活渐退期失败");
        }

        return Result.Success();
    }

    /// <summary>
    /// 补写"停保"伴随变更记录（CategoryStop + triggered_stop=true）：
    /// 档案输出的《档案_变更告知书》按该记录判定（application_id 或 new_application_id 匹配），
    /// 收入超标停保（经济复核/家庭成员变更）必须落这条记录。
    /// 必须在调用方已开启的环境事务内执行。
    /// </summary>
    private async Task<Result> WriteCategoryStopRecordAsync(
        long applicationId, string? oldClassification, string? newClassification,
        decimal oldGuaranteeAmount, decimal newGuaranteeAmount,
        bool triggeredGracePeriod, string reason, string reasonType,
        string operatorName, long newApplicationId, CancellationToken ct)
    {
        var sql = @"INSERT INTO nc_biz_change_records
            (application_id, change_no, change_type, change_reason, change_reason_type, change_date,
             old_classification, new_classification, old_guarantee_amount, new_guarantee_amount,
             triggered_grace_period, triggered_stop, operator_name, new_application_id, changed_at)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,NOW());";
        var result = await _db.ExecuteNonQueryAsync(sql, ct,
            applicationId,
            $"CHG{DateTime.Now:yyyyMMddHHmmssfff}",
            DictionaryConstants.ChangeType.CATEGORY_STOP,
            reason, reasonType, DateTime.Today,
            (object?)oldClassification, (object?)newClassification,
            (object?)oldGuaranteeAmount, (object?)newGuaranteeAmount,
            triggeredGracePeriod, true,
            operatorName,
            newApplicationId);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "写入停保变更记录失败");
        return Result.Success();
    }

    /// <summary>
    /// 停旧建新：复制旧档案（主表+家庭成员+经济明细+入户调查+照料人）→ 新档案（original_application_id=旧ID、Draft），
    /// 旧档案置 Stopped 并结清渐退期；支持对复制后的新档案 UPDATE 覆盖字段（分类/金额/收入等，如经济复核结果）。
    /// 返回新档案 ID 与旧成员ID→新成员ID 映射。同一事务内调用（应处于已开启事务的上下文）。
    /// </summary>
    private async Task<Result<(long NewApplicationId, Dictionary<long, long> MemberIdMap)>> StopAndRebuildAsync(
        long sourceApplicationId, string sourceType, string chainType, string changeReason, string? operatorName,
        Action<ApplicationEntity>? applyOverrides = null, CancellationToken ct = default)
    {
        // 1. 生成新申请编号
        var appNoResult = await _applicationService.GetNextApplicationNoAsync(ct);
        if (appNoResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(appNoResult.ErrorCode!, appNoResult.Message ?? "生成申请编号失败");

        // 2. 读取源档案与成员（供附属复制 + 成员ID映射）
        var srcResult = await _db.QuerySingleAsync<ApplicationEntity>(
            "SELECT * FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL", ct, sourceApplicationId);
        if (srcResult.IsFailure || srcResult.Value == null)
            return Result.Failure<(long, Dictionary<long, long>)>(ErrorCodes.APPLICATION_NOT_FOUND, "源档案不存在");

        // 3. 复制主表 → 新档案（application_no=新、status=Draft、original_application_id=旧ID、source_type=给定类型、
        //    清空停保/渐退期字段、data_completed_at=NULL，其余复制源值）
        var newAppInsertSql = @"INSERT INTO nc_biz_applications (
                application_no, applicant_name, applicant_id_card, applicant_phone,
                gender, ethnicity, marital_status, hukou_type, education_level, political_status,
                hukou_address, disability_card_no,
                province, city, district, town, community, address,
                physical_condition, disease_name, secondary_disease_name,
                disability_type, disability_level, health_status,
                employment_status, work_unit, income_source, bank_name, bank_account,
                family_size, confirmed_family_size,
                is_single_rescue, support_mode,
                application_reason, application_reason_detail, caregiver_type, destitute_support_type, support_institution_id,
                work_income_total, business_income_total, property_income_total,
                transfer_income_total, other_income_total, total_family_income, per_capita_income, rigid_expenditure, alimony_income,
                is_eligible, classification_result,
                classified_subsidy_type, classified_subsidy_amount,
                household_monthly_guarantee_amount, person_category_protection_total_amount,
                caregiver_subsidy_amount, total_guarantee_amount,
                status, current_step,
                source_type, source_table, source_id,
                chain_type,
                original_application_id,
                first_approved_at,
                created_at, updated_at, created_by, updated_by,
                city_id, county_id, town_id, village_id,
                hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id,
                family_land_area, self_farmed_land_area, subleased_land_area, contracted_land_area,
                land_income_total, subsidy_total, total_confirmed_land_area, confirmed_person_count
            )
            SELECT $1, applicant_name, applicant_id_card, applicant_phone,
                gender, ethnicity, marital_status, hukou_type, education_level, political_status,
                hukou_address, disability_card_no,
                province, city, district, town, community, address,
                physical_condition, disease_name, secondary_disease_name,
                disability_type, disability_level, health_status,
                employment_status, work_unit, income_source, bank_name, bank_account,
                family_size, confirmed_family_size,
                is_single_rescue, support_mode,
                application_reason, application_reason_detail, caregiver_type, destitute_support_type, support_institution_id,
                work_income_total, business_income_total, property_income_total,
                transfer_income_total, other_income_total, total_family_income, per_capita_income, rigid_expenditure, alimony_income,
                is_eligible, classification_result,
                classified_subsidy_type, classified_subsidy_amount,
                household_monthly_guarantee_amount, person_category_protection_total_amount,
                caregiver_subsidy_amount, total_guarantee_amount,
                'Draft', 1,
                $2, source_table, source_id,
                $6,
                $3,
                first_approved_at,
                NOW(), NOW(), created_by, $4,
                city_id, county_id, town_id, village_id,
                hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id,
                family_land_area, self_farmed_land_area, subleased_land_area, contracted_land_area,
                land_income_total, subsidy_total, total_confirmed_land_area, confirmed_person_count
            FROM nc_biz_applications WHERE id = $5
            RETURNING id;";
        var newAppResult = await _db.ExecuteScalarAsync<long?>(newAppInsertSql, ct,
            appNoResult.Value, sourceType, sourceApplicationId, operatorName ?? "System", sourceApplicationId, chainType);
        if (newAppResult.IsFailure || newAppResult.Value is not > 0)
            return Result.Failure<(long, Dictionary<long, long>)>(newAppResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                newAppResult.Message ?? "创建新档案失败");
        var newApplicationId = newAppResult.Value.Value;

        // 4. 复制家庭成员（member_id 映射）
        var oldMembersResult = await _db.QueryAsync<FamilyMember>(
            "SELECT * FROM nc_biz_family_members WHERE application_id = $1 AND deleted_at IS NULL", ct, sourceApplicationId);
        if (oldMembersResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(oldMembersResult.ErrorCode!, oldMembersResult.Message ?? "源成员查询失败");
        var oldMembers = oldMembersResult.Value ?? new List<FamilyMember>();

        var memberCopySql = @"INSERT INTO nc_biz_family_members (
                application_id, name, id_card, is_applicant, gender, birth_date, age, ethnicity, phone,
                hukou_type, hukou_address, marital_status, education_level, political_status,
                relationship_to_head, health_status, work_capacity, is_disabled,
                disability_type, disability_level, is_severe_disability, employment_status, annual_income,
                member_category, disability_certificate_no, disease_category, disease_name,
                home_province, home_city, home_district, home_town, home_address,
                hukou_province, hukou_city, hukou_district, hukou_town, home_village, secondary_disease,
                person_type, annual_support_fee, is_support_ability, monthly_income_capacity,
                family_size, work_unit, monthly_support_fee,
                support_months, main_income_source, is_severe_disease, is_labor_exempt, created_at, updated_at, deleted_at
            )
            SELECT $1, name, id_card, is_applicant, gender, birth_date, age, ethnicity, phone,
                hukou_type, hukou_address, marital_status, education_level, political_status,
                relationship_to_head, health_status, work_capacity, is_disabled,
                disability_type, disability_level, is_severe_disability, employment_status, annual_income,
                member_category, disability_certificate_no, disease_category, disease_name,
                home_province, home_city, home_district, home_town, home_address,
                hukou_province, hukou_city, hukou_district, hukou_town, home_village, secondary_disease,
                person_type, annual_support_fee, is_support_ability, monthly_income_capacity,
                family_size, work_unit, monthly_support_fee,
                support_months, main_income_source, is_severe_disease, is_labor_exempt, NOW(), NOW(), NULL
            FROM nc_biz_family_members WHERE application_id = $2 AND deleted_at IS NULL;";
        var memberCopyResult = await _db.ExecuteNonQueryAsync(memberCopySql, ct, newApplicationId, sourceApplicationId);
        if (memberCopyResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(memberCopyResult.ErrorCode!, memberCopyResult.Message ?? "复制家庭成员失败");

        // 成员ID映射（按身份证/姓名匹配）
        var newMembersResult = await _db.QueryAsync<FamilyMember>(
            "SELECT * FROM nc_biz_family_members WHERE application_id = $1 AND deleted_at IS NULL", ct, newApplicationId);
        if (newMembersResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(newMembersResult.ErrorCode!, newMembersResult.Message ?? "新档案成员查询失败");
        var newMembers = newMembersResult.Value ?? new List<FamilyMember>();
        var memberIdMap = new Dictionary<long, long>();
        foreach (var oldMember in oldMembers)
        {
            var match = newMembers.FirstOrDefault(m => !string.IsNullOrEmpty(m.IdCard)
                && string.Equals(m.IdCard, oldMember.IdCard, StringComparison.OrdinalIgnoreCase))
                ?? newMembers.FirstOrDefault(m => string.Equals(m.Name, oldMember.Name, StringComparison.Ordinal));
            if (match != null)
                memberIdMap[oldMember.Id] = match.Id;
        }
        long MapMemberId(long oldId) => oldId > 0 && memberIdMap.TryGetValue(oldId, out var nid) ? nid : 0;

        // 5. 复制经济明细（LoadAll → 重映射 member_id → SaveAll）
        var econLoadResult = await _economicDetailService.LoadAllAsync(sourceApplicationId, ct);
        if (econLoadResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(econLoadResult.ErrorCode!, econLoadResult.Message ?? "经济明细加载失败");
        var econ = econLoadResult.Value;
        foreach (var it in econ.LaborIncomes) it.MemberId = MapMemberId(it.MemberId);
        foreach (var it in econ.BusinessIncomes) it.MemberId = MapMemberId(it.MemberId);
        foreach (var it in econ.PropertyIncomes) it.MemberId = MapMemberId(it.MemberId);
        foreach (var it in econ.TransferIncomes) it.MemberId = MapMemberId(it.MemberId);
        foreach (var it in econ.RigidExpenditures) it.MemberId = MapMemberId(it.MemberId);
        foreach (var it in econ.FamilyProperties) it.MemberId = MapMemberId(it.MemberId);
        foreach (var it in econ.Vehicles) it.MemberId = MapMemberId(it.MemberId);
        foreach (var it in econ.Machineries) it.MemberId = MapMemberId(it.MemberId);
        var econSaveResult = await _economicDetailService.SaveAllAsync(newApplicationId,
            econ.LaborIncomes, econ.BusinessIncomes, econ.PropertyIncomes, econ.TransferIncomes,
            econ.OtherIncomes, econ.Subsidies, econ.BreedingIncomes, econ.RigidExpenditures,
            econ.FamilyProperties, econ.Vehicles, econ.Machineries, econ.FinancialAssets, econ.LandRegistrations,
            econ.LandConfirmationGroups, ct);
        if (econSaveResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(econSaveResult.ErrorCode!, econSaveResult.Message ?? "经济明细复制失败");

        // 6. 复制入户调查
        var surveyCopySql = @"INSERT INTO nc_biz_household_surveys
            (application_id, survey_date, surveyor_name, surveyor_organization,
             respondent_name, respondent_relation, survey_notes,
             created_at, updated_at, deleted_at,
             application_reason, application_reason_detail, survey_conclusion)
            SELECT $1, survey_date, surveyor_name, surveyor_organization,
                   respondent_name, respondent_relation, survey_notes,
                   NOW(), NOW(), NULL,
                   application_reason, application_reason_detail, survey_conclusion
            FROM nc_biz_household_surveys
            WHERE application_id = $2 AND deleted_at IS NULL;";
        var surveyCopyResult = await _db.ExecuteNonQueryAsync(surveyCopySql, ct, newApplicationId, sourceApplicationId);
        if (surveyCopyResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(surveyCopyResult.ErrorCode!, surveyCopyResult.Message ?? "复制入户调查失败");

        // 7. 复制照料人（cared_member_id 迁移）
        var caregiversResult = await _db.QueryAsync<Caregiver>(
            "SELECT * FROM nc_biz_caregivers WHERE application_id = $1 AND deleted_at IS NULL", ct, sourceApplicationId);
        if (caregiversResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(caregiversResult.ErrorCode!, caregiversResult.Message ?? "照料人查询失败");
        var caregivers = caregiversResult.Value ?? new List<Caregiver>();
        if (caregivers.Count > 0)
        {
            var caregiverSql = @"INSERT INTO nc_biz_caregivers
                (application_id, cared_member_id, name, id_card, phone, relationship,
                 gender, age, ethnicity, health_status, employment_status, main_income_source,
                 work_unit, position, address,
                 marital_status, hukou_type, education_level, political_status,
                 created_at, deleted_at)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,NOW(),NULL);";
            foreach (var cg in caregivers)
            {
                var insertCaregiverResult = await _db.ExecuteNonQueryAsync(caregiverSql, ct,
                    newApplicationId, MapMemberId(cg.CaredMemberId),
                    cg.Name, cg.IdCard, cg.Phone, cg.Relationship,
                    cg.Gender, cg.Age, cg.Ethnicity, cg.HealthStatus,
                    cg.EmploymentStatus, cg.MainIncomeSource,
                    cg.WorkUnit, cg.Position, cg.Address,
                    cg.MaritalStatus, cg.HukouType, cg.EducationLevel, cg.PoliticalStatus);
                if (insertCaregiverResult.IsFailure)
                    return Result.Failure<(long, Dictionary<long, long>)>(insertCaregiverResult.ErrorCode!, insertCaregiverResult.Message ?? "复制照料人失败");
            }
        }

        // 8. 应用覆盖字段（经济复核结果等）：UPDATE 新档案
        if (applyOverrides != null)
        {
            var newAppEntity = new ApplicationEntity { Id = newApplicationId };
            applyOverrides(newAppEntity);
            var updateSql = @"UPDATE nc_biz_applications SET
                total_family_income = $1, per_capita_income = $2, rigid_expenditure = $3,
                family_size = $4, classification_result = $5, is_eligible = $6,
                household_monthly_guarantee_amount = $7, total_guarantee_amount = $8,
                classified_subsidy_type = $9, classified_subsidy_amount = $10,
                total_annual_income = $11, per_capita_annual_income = $12,
                updated_at = NOW(), updated_by = $13
                WHERE id = $14;";
            var updResult = await _db.ExecuteNonQueryAsync(updateSql, ct,
                newAppEntity.TotalFamilyIncome, newAppEntity.PerCapitaIncome, newAppEntity.RigidExpenditure,
                newAppEntity.FamilySize, newAppEntity.ClassificationResult, newAppEntity.IsEligible,
                newAppEntity.HouseholdMonthlyGuaranteeAmount, newAppEntity.TotalGuaranteeAmount,
                newAppEntity.ClassifiedSubsidyType, newAppEntity.ClassifiedSubsidyAmount,
                newAppEntity.TotalAnnualIncome, newAppEntity.PerCapitaAnnualIncome,
                operatorName ?? "System", newApplicationId);
            if (updResult.IsFailure)
                return Result.Failure<(long, Dictionary<long, long>)>(updResult.ErrorCode!, updResult.Message ?? "更新新档案失败");
        }

        // 9. 停止旧档案 + 结清渐退期
        var stopSql = @"UPDATE nc_biz_applications SET
            status = $1, stop_reason = $2, stop_date = $3,
            updated_at = NOW(), updated_by = $4
            WHERE id = $5;";
        var stopResult = await _db.ExecuteNonQueryAsync(stopSql, ct,
            ApplicationStatus.Stopped.GetCode(), changeReason, DateTime.Today,
            operatorName ?? "System", sourceApplicationId);
        if (stopResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(stopResult.ErrorCode!, stopResult.Message ?? "停止旧档案失败");
        var graceClearResult = await _gracePeriodService.ClearAsync(sourceApplicationId, ct);
        if (graceClearResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(graceClearResult.ErrorCode!, graceClearResult.Message ?? "结清旧档案渐退期失败");

        return Result.Success((newApplicationId, memberIdMap));
    }

    /// <summary>
    /// 执行经济复核
    /// </summary>
    public async Task<Result<ChangeResult>> ExecuteEconomicReviewAsync(EconomicReviewContext context, CancellationToken ct = default)
    {
        LogInfo("执行经济复核");

        var shouldManageTransaction = !_db.HasTransaction;
        if (shouldManageTransaction)
            await _db.BeginTransactionAsync();

        try
        {
            // 1. 获取当前申请数据（已软删的申请不允许再做经济复核）
            var appSql = "SELECT * FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL";
            var appResult = await _db.QuerySingleAsync<ApplicationEntity>(appSql, ct, context.ApplicationId);

            if (!appResult.IsSuccess || appResult.Value == null)
            {
                // 提前返回必须先回滚：环境事务的连接由作用域持有，
                // 不回滚会泄漏连接并在数据库端留下 idle in transaction 会话
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>("APPLICATION_NOT_FOUND", "申请不存在");
            }

            var application = appResult.Value;
            var oldClassification = application.ClassificationResult ?? string.Empty;
            var oldGuaranteeAmount = application.TotalGuaranteeAmount;

            // 家庭信息修正模式：校验档案必须属于当前经济复核周期
            if (context.IsFamilyCorrection)
            {
                var currentTimeline = await _businessTimelineService.GetCurrentTimelineAsync(TimelineType.EconomicReview);
                var cycleStart = currentTimeline.CycleStartDate;
                var cycleEnd = currentTimeline.CycleEndDate;

                // 检查档案状态：必须是已批准状态
                if (application.Status != ApplicationStatusCodes.APPROVED)
                {
                    if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                    return Result.Failure<ChangeResult>("INVALID_STATUS", "只能修正已批准状态的档案");
                }

                // 检查档案的最近变更记录是否在当前周期内
                var changeHistory = await GetChangeHistoryAsync(context.ApplicationId, ct);
                if (changeHistory != null && changeHistory.Count > 0)
                {
                    var latestChange = changeHistory.OrderByDescending(c => c.ChangedAt).First();
                    if (latestChange.ChangedAt < cycleStart || latestChange.ChangedAt > cycleEnd)
                    {
                        if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                        return Result.Failure<ChangeResult>("OUT_OF_PERIOD",
                            $"该档案最近一次变更不在当前复核周期内（{cycleStart:MM月dd日}-{cycleEnd:MM月dd日}）");
                    }
                }
            }

            // 变更链新建档案（户主死亡停旧建新等，original_application_id 非空）：
            // 注入上游档案原分类，供渐退期"低保→低收入"判定识别渐变前原分类；
            // 判定/落库使用上游分类，审计快照仍用本档案变更前分类
            var graceOldClassification = oldClassification;
            if (application.OriginalApplicationId > 0)
            {
                var upstreamResult = await _applicationService.GetByIdAsync(application.OriginalApplicationId, ct);
                if (upstreamResult.IsSuccess && upstreamResult.Value != null
                    && !string.IsNullOrEmpty(upstreamResult.Value.ClassificationResult))
                {
                    graceOldClassification = upstreamResult.Value.ClassificationResult!;
                    application.OriginalClassificationResult = graceOldClassification;
                }
            }

            // 变更前值必须在改写 application 之前落到局部变量，
            // 否则 Before 快照序列化的是改写后的对象，before==after，审计快照失去意义
            var oldTotalIncome = application.TotalFamilyIncome;
            var oldPerCapitaIncome = application.PerCapitaIncome;
            var oldRigidExpenditure = application.RigidExpenditure;
            var oldFamilySize = application.FamilySize;

            // 2. 获取家庭成员（查询失败不能按"空成员"继续——分类判定会漏掉
            //    残疾/疾病等成员型补贴，算出错误金额并写库）
            var membersSql = "SELECT * FROM nc_biz_family_members WHERE application_id = $1 AND deleted_at IS null";
            var membersResult = await _db.QueryAsync<FamilyMember>(membersSql, ct, context.ApplicationId);
            if (membersResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(membersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    membersResult.Message ?? "家庭成员查询失败");
            }
            var members = membersResult.Value ?? new List<FamilyMember>();

            // 3. 更新经济信息（月值 = 年值÷12 分解显示；年值为权威口径）
            application.TotalAnnualIncome = context.NewTotalAnnualIncome;
            application.PerCapitaAnnualIncome = context.NewPerCapitaAnnualIncome;
            application.TotalFamilyIncome = context.NewTotalFamilyIncome;
            application.PerCapitaIncome = context.NewPerCapitaIncome;
            application.RigidExpenditure = context.NewRigidExpenditure;
            application.FamilySize = context.NewFamilySize;

            // 4. 重新执行分类判定（需传真实赡养抚养扶养义务人，特困判定依赖"无义务人"条件）
            var supportersResult = await _supporterService.GetByApplicationIdAsync(context.ApplicationId, ct);
            if (supportersResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(supportersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    supportersResult.Message ?? "赡养抚养扶养人查询失败");
            }
            var supporters = supportersResult.Value ?? new List<Supporter>();
            var classificationResult = await _classificationService.DetermineClassificationAsync(
                application, members, supporters, new List<Caregiver>(), ct);

            if (!classificationResult.IsSuccess)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>("CLASSIFICATION_FAILED", "分类判定失败");
            }

            var newClassification = classificationResult.Value.Classification;
            var newGuaranteeAmount = classificationResult.Value.GuaranteeAmount;

            // 5. 渐退期状态重建（同事务）：按复核后新分类决定去留——
            //    新分类仍属低收入 → 保留/续接（含"渐退期中间政策变动仍符合"的情形）；
            //    非低收入（回低保/停保等）→ 结清既有渐退期。
            var reconcileResult = await ReconcileGracePeriodAsync(
                application.Id, newClassification, classificationResult.Value.GracePeriod,
                graceOldClassification, oldGuaranteeAmount, ct: ct);
            if (reconcileResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(reconcileResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    reconcileResult.Message ?? "渐退期状态重建失败");
            }
            var triggeredGracePeriod = classificationResult.Value.GracePeriod.IsEligible == true;
            if (triggeredGracePeriod)
            {
                _gracePeriodService.ApplyGracePeriod(application, new GracePeriodInfo
                {
                    IsInGracePeriod = true,
                    GracePeriodMonths = classificationResult.Value.GracePeriod.Months,
                    GracePeriodStartDate = classificationResult.Value.GracePeriod.StartDate,
                    GracePeriodEndDate = classificationResult.Value.GracePeriod.EndDate,
                    OriginalClassificationResult = graceOldClassification,
                    OriginalGuaranteeAmount = oldGuaranteeAmount
                });
            }

            // 6. 检查是否停保
            bool triggeredStop = ClassificationConstants.IsCodeStop(newClassification);

            // 7. 停旧建新：经济复核属信息变更，一律复制为新档案（original_application_id=旧ID、Draft），
            //    旧档案置 Stopped。新档案应用复核后的收入/分类/金额/保障金。
            var rebuildResult = await StopAndRebuildAsync(
                context.ApplicationId,
                DictionaryConstants.ChangeType.FUND_CHANGE,
                ChainTypeConstants.CATEGORY_REBUILD,
                $"经济复核后 由 {ClassificationConstants.ConvertToMajorCategoryName(oldClassification ?? "")}转入{ClassificationConstants.ConvertToMajorCategoryName(newClassification ?? "")}",
                context.OperatorName,
                applyOverrides: target =>
                {
                    target.TotalFamilyIncome = application.TotalFamilyIncome;
                    target.PerCapitaIncome = application.PerCapitaIncome;
                    target.TotalAnnualIncome = application.TotalAnnualIncome;
                    target.PerCapitaAnnualIncome = application.PerCapitaAnnualIncome;
                    target.RigidExpenditure = application.RigidExpenditure;
                    target.FamilySize = application.FamilySize;
                    target.ClassificationResult = newClassification;
                    target.IsEligible = classificationResult.Value.IsEligible;
                    target.HouseholdMonthlyGuaranteeAmount = newGuaranteeAmount;
                    target.TotalGuaranteeAmount = newGuaranteeAmount +
                        classificationResult.Value.ClassifiedSubsidy.TotalAmount +
                        application.CaregiverSubsidyAmount;
                    target.ClassifiedSubsidyType = classificationResult.Value.ClassifiedSubsidy.Types;
                    target.ClassifiedSubsidyAmount = classificationResult.Value.ClassifiedSubsidy.TotalAmount;
                },
                ct);
            if (rebuildResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(rebuildResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    rebuildResult.Message ?? "经济复核停旧建新失败");
            }
            var newApplicationId = rebuildResult.Value.NewApplicationId;

            // 8. 创建变更记录（挂在旧档案上，After 快照含新档案ID）
            var changeContext = new ChangeContext
            {
                ApplicationId = context.ApplicationId,
                ChangeType = DictionaryConstants.ChangeType.FUND_CHANGE,
                ChangeReason = context.ReviewReason ?? "经济复核后",
                ChangeDate = DateTime.Today,
                OperatorName = context.OperatorName
            };

            // Before 快照用改写前捕获的局部变量
            var beforeJson = JsonSerializer.Serialize(new
            {
                Classification = oldClassification,
                GuaranteeAmount = oldGuaranteeAmount,
                TotalIncome = oldTotalIncome,
                PerCapitaIncome = oldPerCapitaIncome,
                RigidExpenditure = oldRigidExpenditure,
                FamilySize = oldFamilySize
            });

            var afterJson = JsonSerializer.Serialize(new
            {
                Classification = newClassification,
                GuaranteeAmount = newGuaranteeAmount,
                TotalIncome = application.TotalFamilyIncome,
                PerCapitaIncome = application.PerCapitaIncome,
                RigidExpenditure = application.RigidExpenditure,
                FamilySize = application.FamilySize,
                NewApplicationId = newApplicationId
            });

            var changeIdResult = await CreateChangeAsync(changeContext, beforeJson, afterJson, ct);
            if (changeIdResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(changeIdResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    changeIdResult.Message ?? "创建变更记录失败");
            }
            var changeId = changeIdResult.Value;

            // 填充 nc_biz_change_records 的结构化对比字段（与经济复核审计对应）
            var updateChangeSql = @"UPDATE nc_biz_change_records SET 
                change_reason_type = $1,
                old_classification = $2, new_classification = $3,
                old_per_capita_income = $4, new_per_capita_income = $5,
                old_guarantee_amount = $6, new_guarantee_amount = $7,
                triggered_grace_period = $8, triggered_stop = $9,
                new_application_id = $11,
                changed_at = NOW()
                WHERE id = $10;";
            var updateChangeResult = await _db.ExecuteNonQueryAsync(updateChangeSql, ct,
                ChangeReasonTypeConstants.EconomicReview, oldClassification, newClassification,
                oldPerCapitaIncome, application.PerCapitaIncome,
                oldGuaranteeAmount, application.TotalGuaranteeAmount,
                triggeredGracePeriod, triggeredStop,
                changeId, newApplicationId);
            if (updateChangeResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(updateChangeResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    updateChangeResult.Message ?? "更新变更记录对比字段失败");
            }

            // 跨大类变更（低保↔低收入/特困/刚性支出 等）：补写"原类别停止 + 新类别新增"两条记录，
            // 供月报"停保汇总表 / 新增救助明细"正确体现。
            if (IsCrossCategoryChange(oldClassification, newClassification))
            {
                var categoryReason = $"经济复核后 由 {ClassificationConstants.ConvertToMajorCategoryName(oldClassification ?? "")}转入{ClassificationConstants.ConvertToMajorCategoryName(newClassification ?? "")}";
                var crossSql = @"INSERT INTO nc_biz_change_records
                    (application_id, change_no, change_type, change_reason, change_reason_type, change_date,
                     old_classification, new_classification, old_guarantee_amount, new_guarantee_amount,
                     triggered_grace_period, triggered_stop, operator_name, new_application_id, changed_at)
                    VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$20,NOW()),
                           ($14,$2,$15,$4,$5,$6,$16,$17,$18,$19,$11,$12,$13,$21,NOW());";
                var crossResult = await _db.ExecuteNonQueryAsync(crossSql, ct,
                    context.ApplicationId,
                    $"CHG{DateTime.Now:yyyyMMddHHmmssfff}",
                    DictionaryConstants.ChangeType.CATEGORY_STOP,
                    categoryReason, ChangeReasonTypeConstants.EconomicReview, DateTime.Today,
                    (object?)oldClassification, (object?)newClassification,
                    (object?)oldGuaranteeAmount, (object?)application.TotalGuaranteeAmount,
                    triggeredGracePeriod, triggeredStop,
                    context.OperatorName,
                    newApplicationId,
                    DictionaryConstants.ChangeType.CATEGORY_ADD,
                    (object?)null, (object?)newClassification,
                    (object?)null, (object?)application.TotalGuaranteeAmount,
                    (object?)newApplicationId, (object?)null);
                if (crossResult.IsFailure)
                {
                    if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                    return Result.Failure<ChangeResult>(crossResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        crossResult.Message ?? "写入跨类变更记录失败");
                }
                LogInfo($"跨大类变更记录: {oldClassification} → {newClassification}（Stop/Add，新档案ID={newApplicationId}）");
            }
            else if (triggeredStop)
            {
                // 同大类转停保（如低保→收入超标）：补写 CategoryStop，供《档案_变更告知书》输出
                var stopWrite = await WriteCategoryStopRecordAsync(
                    context.ApplicationId, oldClassification, newClassification,
                    oldGuaranteeAmount, newGuaranteeAmount, triggeredGracePeriod,
                    context.ReviewReason ?? "经济复核后", ChangeReasonTypeConstants.EconomicReview,
                    context.OperatorName, newApplicationId, ct);
                if (stopWrite.IsFailure)
                {
                    if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                    return Result.Failure<ChangeResult>(stopWrite.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        stopWrite.Message ?? "写入停保变更记录失败");
                }
                LogInfo($"经济复核停保记录: {oldClassification} → {newClassification}（新档案ID={newApplicationId}）");
            }

            if (shouldManageTransaction)
                await _db.CommitTransactionAsync();

            LogInfo($"经济复核完成: 原分类={oldClassification} 新分类={newClassification}");

            // 联动：家庭经济/身份变化后，对已建高龄档案/在册发放成员写入"待复核"队列（失败不阻断）
            try
            {
                var reviewResult = await _elderlyApplicationService.TriggerReviewsForHouseholdAsync(
                    context.ApplicationId, NewCosmos.Constants.ElderlyBenefitConstants.ReviewTriggerLowIncomeChange, ct);
                if (reviewResult.IsSuccess && reviewResult.Value > 0)
                    LogInfo($"经济复核联动写入高龄待复核: ApplicationId={context.ApplicationId}, Count={reviewResult.Value}");
            }
            catch (Exception ex)
            {
                LogWarn($"高龄待复核联动失败（不阻断）: ApplicationId={context.ApplicationId}, {ex.Message}");
            }

            return Result.Success(new ChangeResult
            {
                ChangeId = changeId,
                IsSuccess = true,
                OldClassification = oldClassification ?? string.Empty,
                NewClassification = newClassification ?? string.Empty,
                OldGuaranteeAmount = oldGuaranteeAmount,
                NewGuaranteeAmount = newGuaranteeAmount,
                TriggeredGracePeriod = triggeredGracePeriod,
                TriggeredStop = triggeredStop
            });
        }
        catch (Exception ex)
        {
            if (shouldManageTransaction) await _db.RollbackTransactionAsync();
            LogException(ex, ChangeReasonTypeConstants.EconomicReview);
            return Result.FromException<ChangeResult>(ex);
        }
    }

    /// <summary>
    /// 执行家庭成员变更（增员/减员后重新认定）：重判分类 → 渐退期重建 → 停旧建新（chain_type=MemberChange）
    /// → 变更记录（change_category=MemberChange，供"增减员调整表"输出）→ 跨类/停保补记 CategoryStop
    /// （"停保告知书"依赖 triggered_stop=true 的 CategoryStop 记录）
    /// </summary>
    public async Task<Result<ChangeResult>> ExecuteMemberChangeAsync(MemberChangeContext context, CancellationToken ct = default)
    {
        LogInfo("执行家庭成员变更");

        var shouldManageTransaction = !_db.HasTransaction;
        if (shouldManageTransaction)
            await _db.BeginTransactionAsync();

        try
        {
            // 1. 当前申请（已软删的申请不允许再做成员变更）
            var appSql = "SELECT * FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL";
            var appResult = await _db.QuerySingleAsync<ApplicationEntity>(appSql, ct, context.ApplicationId);
            if (!appResult.IsSuccess || appResult.Value == null)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>("APPLICATION_NOT_FOUND", "申请不存在");
            }

            var application = appResult.Value;
            var oldClassification = application.ClassificationResult ?? string.Empty;
            var oldGuaranteeAmount = application.TotalGuaranteeAmount;

            // 变更链新建档案（户主死亡/既往复核停旧建新）：注入上游分类供渐退期"低保→低收入"判定
            var graceOldClassification = oldClassification;
            if (application.OriginalApplicationId > 0)
            {
                var upstreamResult = await _applicationService.GetByIdAsync(application.OriginalApplicationId, ct);
                if (upstreamResult.IsSuccess && upstreamResult.Value != null
                    && !string.IsNullOrEmpty(upstreamResult.Value.ClassificationResult))
                {
                    graceOldClassification = upstreamResult.Value.ClassificationResult!;
                    application.OriginalClassificationResult = graceOldClassification;
                }
            }

            // 变更前值必须在改写 application 之前落到局部变量（Before 快照用）
            var oldTotalIncome = application.TotalFamilyIncome;
            var oldPerCapitaIncome = application.PerCapitaIncome;
            var oldRigidExpenditure = application.RigidExpenditure;
            var oldFamilySize = context.OldFamilySize > 0 ? context.OldFamilySize : application.FamilySize;

            // 2. 家庭成员（表单已保存增删结果；查询失败不能按"空成员"继续，否则漏算成员型补贴）
            var membersSql = "SELECT * FROM nc_biz_family_members WHERE application_id = $1 AND deleted_at IS null";
            var membersResult = await _db.QueryAsync<FamilyMember>(membersSql, ct, context.ApplicationId);
            if (membersResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(membersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    membersResult.Message ?? "家庭成员查询失败");
            }
            var members = membersResult.Value ?? new List<FamilyMember>();

            // 3. 应用新经济/人数口径（年值权威，月值为分解显示）
            application.TotalAnnualIncome = context.NewTotalAnnualIncome;
            application.PerCapitaAnnualIncome = context.NewPerCapitaAnnualIncome;
            application.TotalFamilyIncome = context.NewTotalFamilyIncome;
            application.PerCapitaIncome = context.NewPerCapitaIncome;
            application.RigidExpenditure = context.NewRigidExpenditure;
            application.FamilySize = context.NewFamilySize;

            // 4. 重新执行分类判定（需传真实赡养抚养扶养义务人，特困判定依赖"无义务人"条件）
            var supportersResult = await _supporterService.GetByApplicationIdAsync(context.ApplicationId, ct);
            if (supportersResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(supportersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    supportersResult.Message ?? "赡养抚养扶养人查询失败");
            }
            var supporters = supportersResult.Value ?? new List<Supporter>();
            var classificationResult = await _classificationService.DetermineClassificationAsync(
                application, members, supporters, new List<Caregiver>(), ct);
            if (!classificationResult.IsSuccess)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>("CLASSIFICATION_FAILED", "分类判定失败");
            }

            var newClassification = classificationResult.Value.Classification;
            var newGuaranteeAmount = classificationResult.Value.GuaranteeAmount;

            // 5. 渐退期状态重建（同复核：新分类仍低收入 → 保留/续接；否则结清）
            var reconcileResult = await ReconcileGracePeriodAsync(
                application.Id, newClassification, classificationResult.Value.GracePeriod,
                graceOldClassification, oldGuaranteeAmount, ct: ct);
            if (reconcileResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(reconcileResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    reconcileResult.Message ?? "渐退期状态重建失败");
            }
            var triggeredGracePeriod = classificationResult.Value.GracePeriod.IsEligible == true;
            if (triggeredGracePeriod)
            {
                _gracePeriodService.ApplyGracePeriod(application, new GracePeriodInfo
                {
                    IsInGracePeriod = true,
                    GracePeriodMonths = classificationResult.Value.GracePeriod.Months,
                    GracePeriodStartDate = classificationResult.Value.GracePeriod.StartDate,
                    GracePeriodEndDate = classificationResult.Value.GracePeriod.EndDate,
                    OriginalClassificationResult = graceOldClassification,
                    OriginalGuaranteeAmount = oldGuaranteeAmount
                });
            }

            // 6. 是否停保
            bool triggeredStop = ClassificationConstants.IsCodeStop(newClassification);

            var changeType = string.IsNullOrEmpty(context.ChangeType)
                ? DictionaryConstants.ChangeType.MEMBER_MODIFY
                : context.ChangeType;
            var reasonText = string.IsNullOrWhiteSpace(context.ChangeSummary)
                ? (string.IsNullOrWhiteSpace(context.ChangeReason) ? "家庭成员变更" : context.ChangeReason)
                : $"{context.ChangeReason}（{context.ChangeSummary}）";

            // 7. 停旧建新：旧档案 Stopped，新档案 Draft 承载变更后成员/经济/分类/金额
            var rebuiltReason = $"家庭成员变更后 由 {ClassificationConstants.ConvertToMajorCategoryName(oldClassification ?? "")}转入{ClassificationConstants.ConvertToMajorCategoryName(newClassification ?? "")}";
            var rebuildResult = await StopAndRebuildAsync(
                context.ApplicationId,
                changeType,
                ChainTypeConstants.MEMBER_CHANGE,
                rebuiltReason,
                context.OperatorName,
                applyOverrides: target =>
                {
                    target.TotalFamilyIncome = application.TotalFamilyIncome;
                    target.PerCapitaIncome = application.PerCapitaIncome;
                    target.TotalAnnualIncome = application.TotalAnnualIncome;
                    target.PerCapitaAnnualIncome = application.PerCapitaAnnualIncome;
                    target.RigidExpenditure = application.RigidExpenditure;
                    target.FamilySize = application.FamilySize;
                    target.ClassificationResult = newClassification;
                    target.IsEligible = classificationResult.Value.IsEligible;
                    target.HouseholdMonthlyGuaranteeAmount = newGuaranteeAmount;
                    target.TotalGuaranteeAmount = newGuaranteeAmount +
                        classificationResult.Value.ClassifiedSubsidy.TotalAmount +
                        application.CaregiverSubsidyAmount;
                    target.ClassifiedSubsidyType = classificationResult.Value.ClassifiedSubsidy.Types;
                    target.ClassifiedSubsidyAmount = classificationResult.Value.ClassifiedSubsidy.TotalAmount;
                },
                ct);
            if (rebuildResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(rebuildResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    rebuildResult.Message ?? "家庭成员变更停旧建新失败");
            }
            var newApplicationId = rebuildResult.Value.NewApplicationId;

            // 8. 变更记录（挂在旧档案上；After 快照含新档案ID）
            var changeContext = new ChangeContext
            {
                ApplicationId = context.ApplicationId,
                ChangeType = changeType,
                ChangeCategory = DictionaryConstants.ChangeCategory.MEMBER_CHANGE,
                ChangeReason = reasonText,
                ChangeReasonType = ChangeReasonTypeConstants.MemberChange,
                ChangeDate = DateTime.Today,
                OperatorName = context.OperatorName
            };

            var beforeJson = JsonSerializer.Serialize(new
            {
                Classification = oldClassification,
                GuaranteeAmount = oldGuaranteeAmount,
                TotalIncome = oldTotalIncome,
                PerCapitaIncome = oldPerCapitaIncome,
                RigidExpenditure = oldRigidExpenditure,
                FamilySize = oldFamilySize
            });

            var entries = context.Entries ?? new List<MemberChangeEntry>();
            var addedDetails = entries
                .Where(e => string.Equals(e.Direction, DictionaryConstants.ChangeType.MEMBER_ADD, StringComparison.OrdinalIgnoreCase))
                .Select(e => new
                {
                    e.Name, e.IdCard, e.RelationshipToHead, e.MemberCategory,
                    e.ReasonCode, e.ReasonName,
                    EventDate = e.EventDate.ToString("yyyy-MM-dd"),
                    e.Remark
                }).ToList();
            var removedDetails = entries
                .Where(e => string.Equals(e.Direction, DictionaryConstants.ChangeType.MEMBER_REMOVE, StringComparison.OrdinalIgnoreCase))
                .Select(e => new
                {
                    e.Name, e.IdCard, e.RelationshipToHead, e.MemberCategory,
                    e.ReasonCode, e.ReasonName,
                    EventDate = e.EventDate.ToString("yyyy-MM-dd"),
                    e.Remark
                }).ToList();

            var afterJson = JsonSerializer.Serialize(new
            {
                Classification = newClassification,
                GuaranteeAmount = newGuaranteeAmount,
                TotalIncome = application.TotalFamilyIncome,
                PerCapitaIncome = application.PerCapitaIncome,
                RigidExpenditure = application.RigidExpenditure,
                FamilySize = application.FamilySize,
                ChangeSummary = context.ChangeSummary,
                AddedMembers = addedDetails,
                RemovedMembers = removedDetails,
                NewApplicationId = newApplicationId
            });

            var changeIdResult = await CreateChangeAsync(changeContext, beforeJson, afterJson, ct);
            if (changeIdResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(changeIdResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    changeIdResult.Message ?? "创建变更记录失败");
            }
            var changeId = changeIdResult.Value;

            // 填充结构化对比字段（与经济复核审计对齐）
            var updateChangeSql = @"UPDATE nc_biz_change_records SET 
                change_reason_type = $1,
                old_classification = $2, new_classification = $3,
                old_per_capita_income = $4, new_per_capita_income = $5,
                old_guarantee_amount = $6, new_guarantee_amount = $7,
                triggered_grace_period = $8, triggered_stop = $9,
                new_application_id = $11,
                changed_at = NOW()
                WHERE id = $10;";
            var updateChangeResult = await _db.ExecuteNonQueryAsync(updateChangeSql, ct,
                ChangeReasonTypeConstants.MemberChange, oldClassification, newClassification,
                oldPerCapitaIncome, application.PerCapitaIncome,
                oldGuaranteeAmount, newGuaranteeAmount,
                triggeredGracePeriod, triggeredStop,
                changeId, newApplicationId);
            if (updateChangeResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(updateChangeResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    updateChangeResult.Message ?? "更新变更记录对比字段失败");
            }

            // 逐人变更明细：结构化留痕（nc_biz_change_details），增/减员原因可查询审计
            if (entries.Count > 0)
            {
                var detailSb = new StringBuilder();
                var detailParams = new List<object>(entries.Count * 5);
                for (var i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    if (i > 0) detailSb.Append(',');
                    var p = i * 5;
                    detailSb.Append($"(${p + 1},${p + 2},${p + 3},${p + 4},${p + 5})");
                    detailParams.Add(changeId);
                    detailParams.Add(e.Direction);
                    detailParams.Add(e.Name);
                    detailParams.Add($"{e.Name}({e.IdCard}) {e.MemberCategory} {e.RelationshipToHead}".Trim());
                    detailParams.Add($"{e.ReasonName}|{e.EventDate:yyyy-MM-dd}|{e.Remark}");
                }
                var detailSql = $"INSERT INTO nc_biz_change_details (change_id, field_name, field_label, old_value, new_value) VALUES {detailSb};";
                var detailResult = await _db.ExecuteNonQueryAsync(detailSql, ct, detailParams.ToArray());
                if (detailResult.IsFailure)
                {
                    if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                    return Result.Failure<ChangeResult>(detailResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        detailResult.Message ?? "写入变更明细失败");
                }
            }

            // 死亡减员联动：减员原因为"人员死亡"时写死亡记录（与户主死亡同表，供统计/公示退出识别）
            foreach (var e in entries.Where(e =>
                string.Equals(e.Direction, DictionaryConstants.ChangeType.MEMBER_REMOVE, StringComparison.OrdinalIgnoreCase)
                && MemberChangeReasonConstants.IsDeath(e.ReasonCode)))
            {
                var deathSql = @"INSERT INTO nc_biz_death_records
                    (application_id, member_id, member_name, member_id_card, relationship_to_head,
                     death_date, death_reason, is_household_head, remark, operator_name, created_at)
                    SELECT $1,$2,$3,$4,$5,$6,$7,false,$8,$9,NOW()
                    WHERE NOT EXISTS (
                        SELECT 1 FROM nc_biz_death_records d
                        WHERE d.application_id = $1 AND d.member_id_card = $4)";
                var deathResult = await _db.ExecuteNonQueryAsync(deathSql, ct,
                    context.ApplicationId, e.MemberId, e.Name, e.IdCard, e.RelationshipToHead,
                    e.EventDate,
                    string.IsNullOrWhiteSpace(e.Remark) ? "成员变更-人员死亡" : e.Remark,
                    e.Remark, context.OperatorName);
                if (deathResult.IsFailure)
                {
                    if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                    return Result.Failure<ChangeResult>(deathResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        deathResult.Message ?? "写入死亡记录失败");
                }
            }

            // 9. 跨大类变更补写"原类别停止+新类别新增"；同大类但触发停保时补写 CategoryStop
            if (IsCrossCategoryChange(oldClassification, newClassification))
            {
                var categoryReason = $"家庭成员变更后 由 {ClassificationConstants.ConvertToMajorCategoryName(oldClassification ?? "")}转入{ClassificationConstants.ConvertToMajorCategoryName(newClassification ?? "")}";
                var crossSql = @"INSERT INTO nc_biz_change_records
                    (application_id, change_no, change_type, change_reason, change_reason_type, change_date,
                     old_classification, new_classification, old_guarantee_amount, new_guarantee_amount,
                     triggered_grace_period, triggered_stop, operator_name, new_application_id, changed_at)
                    VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$20,NOW()),
                           ($14,$2,$15,$4,$5,$6,$16,$17,$18,$19,$11,$12,$13,$21,NOW());";
                var crossResult = await _db.ExecuteNonQueryAsync(crossSql, ct,
                    context.ApplicationId,
                    $"CHG{DateTime.Now:yyyyMMddHHmmssfff}",
                    DictionaryConstants.ChangeType.CATEGORY_STOP,
                    categoryReason, ChangeReasonTypeConstants.MemberChange, DateTime.Today,
                    (object?)oldClassification, (object?)newClassification,
                    (object?)oldGuaranteeAmount, (object?)newGuaranteeAmount,
                    triggeredGracePeriod, triggeredStop,
                    context.OperatorName,
                    newApplicationId,
                    DictionaryConstants.ChangeType.CATEGORY_ADD,
                    (object?)null, (object?)newClassification,
                    (object?)null, (object?)newGuaranteeAmount,
                    (object?)newApplicationId, (object?)null);
                if (crossResult.IsFailure)
                {
                    if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                    return Result.Failure<ChangeResult>(crossResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        crossResult.Message ?? "写入跨类变更记录失败");
                }
                LogInfo($"家庭成员变更跨类记录: {oldClassification} → {newClassification}（Stop/Add，新档案ID={newApplicationId}）");
            }
            else if (triggeredStop)
            {
                var stopWrite = await WriteCategoryStopRecordAsync(
                    context.ApplicationId, oldClassification, newClassification,
                    oldGuaranteeAmount, newGuaranteeAmount, triggeredGracePeriod,
                    reasonText, ChangeReasonTypeConstants.MemberChange,
                    context.OperatorName, newApplicationId, ct);
                if (stopWrite.IsFailure)
                {
                    if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                    return Result.Failure<ChangeResult>(stopWrite.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        stopWrite.Message ?? "写入停保变更记录失败");
                }
                LogInfo($"家庭成员变更停保记录: {oldClassification} → {newClassification}（新档案ID={newApplicationId}）");
            }

            if (shouldManageTransaction)
                await _db.CommitTransactionAsync();

            LogInfo($"家庭成员变更完成: 原分类={oldClassification} 新分类={newClassification} 新档案ID={newApplicationId}");

            // 联动：家庭人口变化后，对已建高龄档案/在册发放成员写入"待复核"队列（失败不阻断）
            try
            {
                var reviewResult = await _elderlyApplicationService.TriggerReviewsForHouseholdAsync(
                    context.ApplicationId, NewCosmos.Constants.ElderlyBenefitConstants.ReviewTriggerLowIncomeChange, ct);
                if (reviewResult.IsSuccess && reviewResult.Value > 0)
                    LogInfo($"家庭成员变更联动写入高龄待复核: ApplicationId={context.ApplicationId}, Count={reviewResult.Value}");
            }
            catch (Exception ex)
            {
                LogWarn($"高龄待复核联动失败（不阻断）: ApplicationId={context.ApplicationId}, {ex.Message}");
            }

            return Result.Success(new ChangeResult
            {
                ChangeId = changeId,
                IsSuccess = true,
                OldClassification = oldClassification ?? string.Empty,
                NewClassification = newClassification ?? string.Empty,
                OldGuaranteeAmount = oldGuaranteeAmount,
                NewGuaranteeAmount = newGuaranteeAmount,
                TriggeredGracePeriod = triggeredGracePeriod,
                TriggeredStop = triggeredStop,
                NewApplicationId = newApplicationId
            });
        }
        catch (Exception ex)
        {
            if (shouldManageTransaction) await _db.RollbackTransactionAsync();
            LogException(ex, ChangeReasonTypeConstants.MemberChange);
            return Result.FromException<ChangeResult>(ex);
        }
    }

    /// <summary>
    /// 判断是否为跨大类变更（低保/低收入/特困/刚性支出 四大类之间跨越）。
    /// 与月报 CategoryCodes 分组一致：低保含单人保（RuralLowIncomeSingle/UrbanLowIncomeSingle）。
    /// 同大类内调整（如低保→低保单人）不算跨类；任一分组无法识别（空/无效码）不算跨类。
    /// </summary>
    private static bool IsCrossCategoryChange(string? oldClassification, string? newClassification)
    {
        var oldGroup = GetCategoryGroup(oldClassification);
        var newGroup = GetCategoryGroup(newClassification);
        return !string.IsNullOrEmpty(oldGroup) && !string.IsNullOrEmpty(newGroup)
            && !string.Equals(oldGroup, newGroup, StringComparison.Ordinal);
    }

    /// <summary>
    /// 分类码 → 四大类分组（与 MonthlyReportService.CategoryCodes 口径一致）。
    /// </summary>
    private static string? GetCategoryGroup(string? classification)
    {
        if (string.IsNullOrEmpty(classification)) return null;
        if (ClassificationConstants.IsCodeSubsistence(classification)) return "最低生活保障";
        if (ClassificationConstants.IsCodeLowIncome(classification)) return "最低生活保障边缘家庭";
        if (ClassificationConstants.IsCodeDestitute(classification)) return "特困人员";
        if (ClassificationConstants.IsCodeRigidExpenditure(classification)) return "刚性支出困难家庭";
        return null;
    }

    /// <summary>
    /// 处理成员死亡
    /// </summary>
    public async Task<Result<ChangeResult>> ProcessMemberDeathAsync(MemberDeathContext context, CancellationToken ct = default)
    {
        LogInfo($"处理成员死亡: ApplicationId={context.ApplicationId}");

        var shouldManageTransaction = !_db.HasTransaction;
        if (shouldManageTransaction)
            await _db.BeginTransactionAsync();

        try
        {
            // 1. 获取当前申请数据（已软删的申请不允许再做成员死亡处理）
            var appSql = "SELECT * FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL";
            var appResult = await _db.QuerySingleAsync<ApplicationEntity>(appSql, ct, context.ApplicationId);

            if (!appResult.IsSuccess || appResult.Value == null)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>("APPLICATION_NOT_FOUND", "申请不存在");
            }

            var application = appResult.Value;
            var oldClassification = application.ClassificationResult ?? string.Empty;
            var oldGuaranteeAmount = application.TotalGuaranteeAmount;
            // 变更前家庭人数在任何改写发生前捕获，Before 快照直接用它，
            // 不再用 FamilySize + 1 反推（改写后反推得到的是错的）
            var oldFamilySize = application.FamilySize;

            // 2. 标记成员死亡（nc_biz_family_members 无 deleted_by 列，仅置 deleted_at）
            var updateMemberSql = @"UPDATE nc_biz_family_members
                                   SET deleted_at = NOW()
                                   WHERE id = $1 AND application_id = $2;";
            var markResult = await _db.ExecuteNonQueryAsync(updateMemberSql, ct, context.MemberId, context.ApplicationId);
            if (markResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(markResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    markResult.Message ?? "标记成员死亡失败");
            }

            // 3. 创建死亡记录
            var deathSql = @"INSERT INTO nc_biz_death_records
                            (application_id, member_id, member_name, member_id_card, death_date, death_reason, operator_name)
                            VALUES ($1,$2,$3,$4,$5,$6,$7);";
            var deathInsertResult = await _db.ExecuteNonQueryAsync(deathSql, ct,
                context.ApplicationId, context.MemberId, context.MemberName,
                context.MemberIdCard, context.DeathDate, context.DeathReason, context.OperatorName);
            if (deathInsertResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(deathInsertResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    deathInsertResult.Message ?? "创建死亡记录失败");
            }

            // 4. 重新计算家庭人数。
            //    统计失败绝不能回退为"1 人"继续算：家庭人数直接决定保障金额，
            //    按错误人数算出的补贴会写进数据库，必须回滚并失败返回。
            //    剔除赡养抚养扶养人（member_category='Support'），该人群不计入共同生活家庭人数。
            var countSql = @"SELECT COUNT(*) FROM nc_biz_family_members 
                            WHERE application_id = $1 AND deleted_at IS null
                              AND (member_category IS NULL OR member_category <> 'Support')";
            var countResult = await _db.ExecuteScalarAsync(countSql, ct, context.ApplicationId);
            if (countResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(countResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    countResult.Message ?? "家庭人数统计失败");
            }
            var newFamilySize = (int)countResult.Value;

            // 5. 获取剩余成员（同经济复核：查询失败不能按空成员继续做分类判定）
            var membersSql = "SELECT * FROM nc_biz_family_members WHERE application_id = $1 AND deleted_at IS null";
            var membersResult = await _db.QueryAsync<FamilyMember>(membersSql, ct, context.ApplicationId);
            if (membersResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(membersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    membersResult.Message ?? "家庭成员查询失败");
            }
            var members = membersResult.Value ?? new List<FamilyMember>();

            // 6. 更新家庭人数
            application.FamilySize = newFamilySize;

            // 7. 重新执行分类判定（需传真实赡养抚养扶养义务人，特困判定依赖"无义务人"条件）
            var supportersResult = await _supporterService.GetByApplicationIdAsync(context.ApplicationId, ct);
            if (supportersResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(supportersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    supportersResult.Message ?? "赡养抚养扶养人查询失败");
            }
            var supporters = supportersResult.Value ?? new List<Supporter>();
            var classificationResult = await _classificationService.DetermineClassificationAsync(
                application, members, supporters, new List<Caregiver>(), ct);

            string newClassification = oldClassification;
            decimal newGuaranteeAmount = oldGuaranteeAmount;
            bool triggeredStop = false;

            if (classificationResult.IsSuccess)
            {
                newClassification = classificationResult.Value.Classification;
                newGuaranteeAmount = classificationResult.Value.GuaranteeAmount;
                triggeredStop = ClassificationConstants.IsCodeStop(newClassification);

                // 更新申请
                application.ClassificationResult = newClassification;
                application.IsEligible = classificationResult.Value.IsEligible;
                application.HouseholdMonthlyGuaranteeAmount = newGuaranteeAmount;
                application.TotalGuaranteeAmount = newGuaranteeAmount;
                application.FamilySize = newFamilySize;
                application.UpdatedAt = DateTime.UtcNow;
                application.UpdatedBy = context.OperatorName;

                // 如果最后成员死亡，停止档案。
                // 状态变更必须过状态机，不能从任意状态直写 "Stopped"：
                // - Draft（草稿）/ Refused（不予受理）从未进入保障，不存在"停保"一说，
                //   直写 Stopped 属于绕过状态机产生脏状态 → 拒绝（INVALID_TRANSITION）并回滚整个死亡处理；
                // - 业务意图确实是"最后成员死亡 → 停保"（死亡是客观事件），因此仅对
                //   Draft/Refused 阻断：Approved/Completed→Stopped 状态机本就允许；
                //   Submitted→Stopped 严格状态机不允许，但阻断会让全员死亡的在途申请
                //   永远无法关闭，故放行并记录警告；
                // - 已是 Stopped 的申请无需重复转换，保持现状。
                if (newFamilySize <= 0)
                {
                    var currentStatus = ApplicationStatusExtensions.FromCode(application.Status ?? string.Empty);
                    if (currentStatus == ApplicationStatus.Stopped)
                    {
                        triggeredStop = true;
                    }
                    else
                    {
                        var transition = ApplicationStateMachine.ValidateTransition(currentStatus, ApplicationStatus.Stopped);
                        if (transition.IsFailure &&
                            currentStatus is ApplicationStatus.Draft or ApplicationStatus.Refused)
                        {
                            if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                            return Result.Failure<ChangeResult>(
                                ErrorCodes.INVALID_TRANSITION,
                                transition.Message ?? $"不允许从 {currentStatus.GetDescription()} 转换到 已停保");
                        }
                        if (transition.IsFailure)
                        {
                            LogWarn($"成员死亡触发停保: 状态 {application.Status} → Stopped 不在严格状态机允许范围内，按业务规则（最后成员死亡）放行");
                        }

                        application.Status = ApplicationStatus.Stopped.GetCode();
                        application.StopReason = "最后成员死亡";
                        application.StopDate = DateTime.Today;
                        triggeredStop = true;
                    }
                }

                // 渐退期状态重建（同事务）：按成员死亡后新分类决定去留；
                // 最后成员死亡触发停保 → 强制结清既有渐退期。
                var reconcileRes = await ReconcileGracePeriodAsync(
                    context.ApplicationId, newClassification, classificationResult.Value.GracePeriod,
                    oldClassification, oldGuaranteeAmount,
                    forceClear: newFamilySize <= 0, ct: ct);
                if (reconcileRes.IsFailure)
                {
                    if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                    return Result.Failure<ChangeResult>(reconcileRes.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        reconcileRes.Message ?? "渐退期状态重建失败");
                }

                var updateSql = @"UPDATE nc_biz_applications SET
                    family_size = $1, classification_result = $2, is_eligible = $3,
                    household_monthly_guarantee_amount = $4, total_guarantee_amount = $5,
                    status = $6, stop_reason = $7, stop_date = $8,
                    updated_at = $9, updated_by = $10
                    WHERE id = $11;";

                var appUpdateResult = await _db.ExecuteNonQueryAsync(updateSql, ct,
                    application.FamilySize, application.ClassificationResult, application.IsEligible,
                    application.HouseholdMonthlyGuaranteeAmount, application.TotalGuaranteeAmount,
                    application.Status, application.StopReason, application.StopDate,
                    application.UpdatedAt, application.UpdatedBy,
                    application.Id);
                if (appUpdateResult.IsFailure)
                {
                    if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                    return Result.Failure<ChangeResult>(appUpdateResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        appUpdateResult.Message ?? "保存申请更新失败");
                }
            }

            // 8. 创建变更记录
            var changeContext = new ChangeContext
            {
                ApplicationId = context.ApplicationId,
                ChangeType = DictionaryConstants.ChangeType.MEMBER_DEATH,
                ChangeReason = $"成员死亡: {context.MemberName}({context.MemberIdCard})",
                ChangeDate = context.DeathDate,
                OperatorName = context.OperatorName
            };

            // Before 快照使用改写前捕获的 oldFamilySize（此前用 FamilySize + 1 反推，
            // 一旦 FamilySize 与成员表行数不同步，反推值就是错的）
            var beforeJson = JsonSerializer.Serialize(new
            {
                FamilySize = oldFamilySize,
                Classification = oldClassification,
                GuaranteeAmount = oldGuaranteeAmount
            });

            var afterJson = JsonSerializer.Serialize(new
            {
                FamilySize = newFamilySize,
                Classification = newClassification,
                GuaranteeAmount = newGuaranteeAmount,
                DeceasedMember = context.MemberName
            });

            var changeIdResult = await CreateChangeAsync(changeContext, beforeJson, afterJson, ct);
            if (changeIdResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(changeIdResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    changeIdResult.Message ?? "创建变更记录失败");
            }
            var changeId = changeIdResult.Value;

            if (shouldManageTransaction)
                await _db.CommitTransactionAsync();

            LogInfo($"成员死亡处理完成: 新家庭人数={newFamilySize}");

            return Result.Success(new ChangeResult
            {
                ChangeId = changeId,
                IsSuccess = true,
                OldClassification = oldClassification,
                NewClassification = newClassification,
                OldGuaranteeAmount = oldGuaranteeAmount,
                NewGuaranteeAmount = newGuaranteeAmount,
                TriggeredStop = triggeredStop
            });
        }
        catch (Exception ex)
        {
            if (shouldManageTransaction) await _db.RollbackTransactionAsync();
            LogException(ex, "成员死亡处理");
            return Result.FromException<ChangeResult>(ex);
        }
    }

    /// <summary>
    /// 执行户主变更（停旧建新：所有信息变更建新档案，旧档案停止，便于追踪变更记录）
    /// </summary>
    public async Task<Result<ChangeResult>> ExecuteHeadChangeAsync(HeadChangeContext context, CancellationToken ct = default)
    {
        LogInfo("执行户主变更（停旧建新）");

        var shouldManageTransaction = !_db.HasTransaction;
        if (shouldManageTransaction)
            await _db.BeginTransactionAsync();

        try
        {
            // 1. 获取新户主信息（必须限定归属本申请且未删除——否则传入别户的成员 ID
            //    会把别户成员的姓名/身份证写进本申请的户主字段，数据归属错乱）
            var memberSql = "SELECT * FROM nc_biz_family_members WHERE id = $1 AND application_id = $2 AND deleted_at IS NULL";
            var memberResult = await _db.QuerySingleAsync<FamilyMember>(memberSql, ct, context.NewHeadMemberId, context.ApplicationId);
            if (!memberResult.IsSuccess || memberResult.Value == null)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(ErrorCodes.APPLICATION_NOT_FOUND, "新户主成员不存在");
            }
            var newHead = memberResult.Value;

            // 2. 停旧建新：复制旧档案 → 新档案（original_application_id=旧ID、Draft），旧档案停止
            var rebuildResult = await StopAndRebuildAsync(
                context.ApplicationId,
                DictionaryConstants.ChangeType.HOUSEHOLD_HEAD_CHANGE,
                ChainTypeConstants.HEAD_CHANGE,
                "停止",
                context.OperatorName,
                ct: ct);
            if (rebuildResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(rebuildResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    rebuildResult.Message ?? "户主变更停旧建新失败");
            }
            var newApplicationId = rebuildResult.Value.NewApplicationId;

            // 3. 更新新档案申请户主信息（applicant_name 等）
            var updateAppSql = @"UPDATE nc_biz_applications SET
                applicant_name = $1, applicant_id_card = $2, gender = $3,
                ethnicity = $4, marital_status = $5, updated_at = NOW()
                WHERE id = $6;";
            var appHeadResult = await _db.ExecuteNonQueryAsync(updateAppSql, ct,
                newHead.Name, newHead.IdCard, newHead.Gender,
                newHead.Ethnicity, newHead.MaritalStatus,
                newApplicationId);
            if (appHeadResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(appHeadResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    appHeadResult.Message ?? "更新新档案户主信息失败");
            }

            // 4. 更新新档案成员户主关系：新户主→本人/户主，旧户主→家庭成员
            long MapMember(long id) => rebuildResult.Value.MemberIdMap.TryGetValue(id, out var nid) ? nid : id;
            var newHeadMemberId = MapMember(context.NewHeadMemberId);
            var oldHeadMemberId = context.OldHeadMemberId > 0 ? MapMember(context.OldHeadMemberId) : 0;
            if (oldHeadMemberId > 0)
            {
                var updOld = await _db.ExecuteNonQueryAsync(
                    "UPDATE nc_biz_family_members SET relationship_to_head = '家庭成员', updated_at = NOW() WHERE id = $1 AND application_id = $2;",
                    ct, oldHeadMemberId, newApplicationId);
                if (updOld.IsFailure)
                {
                    if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                    return Result.Failure<ChangeResult>(updOld.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        updOld.Message ?? "更新旧户主关系失败");
                }
            }
            var updNew = await _db.ExecuteNonQueryAsync(
                "UPDATE nc_biz_family_members SET relationship_to_head = '本人/户主', is_applicant = true, updated_at = NOW() WHERE id = $1 AND application_id = $2;",
                ct, newHeadMemberId, newApplicationId);
            if (updNew.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(updNew.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    updNew.Message ?? "更新新户主关系失败");
            }

            // 5. 创建变更记录（挂在旧档案上，After 快照含新档案ID）
            var changeContext = new ChangeContext
            {
                ApplicationId = context.ApplicationId,
                ChangeType = DictionaryConstants.ChangeType.HOUSEHOLD_HEAD_CHANGE,
                ChangeReason = context.ChangeReason ?? $"户主变更: {context.OldHeadName} -> {context.NewHeadName}",
                ChangeDate = DateTime.Today,
                OperatorName = context.OperatorName
            };

            var beforeJson = JsonSerializer.Serialize(new { HeadName = context.OldHeadName });
            var afterJson = JsonSerializer.Serialize(new { HeadName = context.NewHeadName, NewApplicationId = newApplicationId });

            var changeIdResult = await CreateChangeAsync(changeContext, beforeJson, afterJson, ct);
            if (changeIdResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(changeIdResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    changeIdResult.Message ?? "创建变更记录失败");
            }
            var changeId = changeIdResult.Value;

            if (shouldManageTransaction)
                await _db.CommitTransactionAsync();

            LogInfo($"户主变更完成（停旧建新）: 新档案ID={newApplicationId}");

            return Result.Success(new ChangeResult
            {
                ChangeId = changeId,
                IsSuccess = true
            });
        }
        catch (Exception ex)
        {
            if (shouldManageTransaction) await _db.RollbackTransactionAsync();
            LogException(ex, "户主变更");
            return Result.FromException<ChangeResult>(ex);
        }
    }

    /// <summary>
    /// 执行户主死亡变更（停旧建新）
    /// 事务内：停旧 → 身份证查重 → 建新（Draft）→ 复制成员/赡养人/经济明细/入户调查/照料人 → 死亡记录 → 变更记录
    /// </summary>
    public async Task<Result<ChangeResult>> ExecuteHouseholdDeathAsync(HouseholdDeathContext context, CancellationToken ct = default)
    {
        LogInfo($"执行户主死亡变更: ApplicationId={context.ApplicationId}, 新户主={DataMasker.MaskName(context.NewHeadName)}");

        if (context.NewHeadMemberId <= 0)
            return Result.Failure<ChangeResult>(ErrorCodes.VALIDATION_FAILED, "请选择新户主");

        var shouldManageTransaction = !_db.HasTransaction;
        if (shouldManageTransaction)
            await _db.BeginTransactionAsync();

        try
        {
            // 1. 获取旧档案（已软删的申请不允许再变更）
            var appResult = await _db.QuerySingleAsync<ApplicationEntity>(
                "SELECT * FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL", ct, context.ApplicationId);
            if (!appResult.IsSuccess || appResult.Value == null)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>("APPLICATION_NOT_FOUND", "申请不存在");
            }
            var application = appResult.Value;
            var oldClassification = application.ClassificationResult ?? string.Empty;
            var oldGuaranteeAmount = application.TotalGuaranteeAmount;
            var oldFamilySize = application.FamilySize;
            var oldHeadName = application.ApplicantName;
            var oldHeadIdCard = application.ApplicantIdCard;

            // 2. 状态校验：仅在保/已提交等存量档案允许停旧，草稿/不予受理不允许停保建新。
            //    状态变更必须过状态机，不能从任意状态直写 "Stopped"（同 ProcessMemberDeathAsync 的语义）
            var currentStatus = ApplicationStatusExtensions.FromCode(application.Status ?? string.Empty);
            if (currentStatus == ApplicationStatus.Stopped)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(ErrorCodes.INVALID_TRANSITION, "该档案已停保，不能重复执行户主死亡变更");
            }
            var transition = ApplicationStateMachine.ValidateTransition(currentStatus, ApplicationStatus.Stopped);
            if (transition.IsFailure && currentStatus is ApplicationStatus.Draft or ApplicationStatus.Refused)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(
                    ErrorCodes.INVALID_TRANSITION,
                    transition.Message ?? $"不允许从 {currentStatus.GetDescription()} 转换到 已停保");
            }
            if (transition.IsFailure)
            {
                LogWarn($"户主死亡触发停保: 状态 {application.Status} → Stopped 不在严格状态机允许范围内，按业务规则（户主死亡）放行");
            }

            // 3. 加载全部家庭成员，定位死亡原户主成员。
            //    未显式传入时按 优先级：is_applicant=true / member_category='HouseholdHead' /
            //    身份证号或姓名与旧档案申请人一致（存量数据常存在 is_applicant 未置位的申请人成员）
            var membersResult = await _db.QueryAsync<FamilyMember>(
                "SELECT * FROM nc_biz_family_members WHERE application_id = $1 AND deleted_at IS NULL", ct, context.ApplicationId);
            if (membersResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(membersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    membersResult.Message ?? "家庭成员查询失败");
            }
            var allMembers = membersResult.Value ?? new List<FamilyMember>();
            var deceasedHead = context.DeceasedHeadMemberId > 0
                ? allMembers.FirstOrDefault(m => m.Id == context.DeceasedHeadMemberId)
                : allMembers.FirstOrDefault(m => m.IsApplicant
                    || string.Equals(m.MemberCategory, MemberCategoryConstants.HOUSEHOLD_HEAD, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrEmpty(oldHeadIdCard)
                        && string.Equals(m.IdCard, oldHeadIdCard, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrEmpty(oldHeadName)
                        && string.Equals(m.Name, oldHeadName, StringComparison.Ordinal)));
            if (deceasedHead == null)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>("FAMILY_MEMBER_NOT_FOUND", "未找到死亡原户主成员记录");
            }

            // 4. 校验新户主成员（必须属于本申请、未删除、且不是死亡户主本人）
            var newHeadMember = allMembers.FirstOrDefault(m => m.Id == context.NewHeadMemberId);
            if (newHeadMember == null)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>("FAMILY_MEMBER_NOT_FOUND", "新户主成员不存在或已删除");
            }
            if (newHeadMember.Id == deceasedHead.Id)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(ErrorCodes.VALIDATION_FAILED, "新户主不能是死亡人员本人");
            }
            if (string.IsNullOrWhiteSpace(newHeadMember.IdCard))
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(ErrorCodes.VALIDATION_FAILED,
                    $"新户主 {DataMasker.MaskName(newHeadMember.Name)} 缺少身份证号，无法建立新档案");
            }

            // 5. 身份证查重（新户主已是其他档案的申请人则明确报错，不绕过——多户挂靠同一身份会污染在保名单）
            var dupResult = await _applicationService.CheckIdCardExistsAsync(newHeadMember.IdCard ?? string.Empty, null, ct);
            if (dupResult.IsSuccess && dupResult.Value)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(ErrorCodes.DUPLICATE_ID_CARD,
                    $"新户主 {DataMasker.MaskName(newHeadMember.Name)} 的身份证号已存在于其他申请档案");
            }

            // 6. 计算新家庭人数（剔除死亡原户主，且不计赡养抚养扶养人 member_category='Support'）
            var newFamilySize = allMembers.Count(m => m.Id != deceasedHead.Id
                && !string.Equals(m.MemberCategory, MemberCategoryConstants.SUPPORT, StringComparison.OrdinalIgnoreCase));
            if (newFamilySize <= 0)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(ErrorCodes.VALIDATION_FAILED, "户主死亡后无共同生活成员，无法建立新档案");
            }

            // 7. 生成新申请编号
            var appNoResult = await _applicationService.GetNextApplicationNoAsync(ct);
            if (appNoResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(appNoResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    appNoResult.Message ?? "生成申请编号失败");
            }

            // 8. 复制主表 → 新档案（Draft、换户主、family_size 重算、original_application_id=旧ID、
            //    清空分类/渐退/停保/银行卡字段——新档案重走审批，旧账号不能沿用给新户主）
            var newAppInsertSql = @"INSERT INTO nc_biz_applications (
                application_no, applicant_name, applicant_id_card, applicant_phone,
                gender, ethnicity, marital_status, hukou_type, education_level, political_status,
                hukou_address, disability_card_no,
                province, city, district, town, community, address,
                physical_condition, disease_name, secondary_disease_name,
                disability_type, disability_level, health_status,
                employment_status, work_unit, income_source,
                family_size, confirmed_family_size,
                is_single_rescue, support_mode,
                application_reason, application_reason_detail, caregiver_type, destitute_support_type, support_institution_id,
                work_income_total, business_income_total, property_income_total,
                transfer_income_total, other_income_total, total_family_income, per_capita_income, rigid_expenditure, alimony_income,
                is_eligible, classification_result,
                classified_subsidy_type, classified_subsidy_amount,
                household_monthly_guarantee_amount, person_category_protection_total_amount,
                caregiver_subsidy_amount, total_guarantee_amount,
                status, current_step,
                source_type, source_table, source_id,
                chain_type,
                original_application_id,
                first_approved_at,
                created_at, updated_at, created_by, updated_by,
                city_id, county_id, town_id, village_id,
                hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id,
                family_land_area, self_farmed_land_area, subleased_land_area, contracted_land_area,
                land_income_total, subsidy_total, total_confirmed_land_area, confirmed_person_count
            )
            SELECT $1, $2, $3, $4,
                $5, $6, $7, $8, $9, $10,
                hukou_address, disability_card_no,
                province, city, district, town, community, address,
                physical_condition, disease_name, secondary_disease_name,
                disability_type, disability_level, health_status,
                employment_status, work_unit, income_source,
                $11, $12,
                is_single_rescue, support_mode,
                application_reason, application_reason_detail, caregiver_type, destitute_support_type, support_institution_id,
                work_income_total, business_income_total, property_income_total,
                transfer_income_total, other_income_total, total_family_income, per_capita_income, rigid_expenditure, alimony_income,
                false, NULL,
                NULL, 0,
                0, 0, 0, 0,
                'Draft', 1,
                'HouseholdDeath', 'nc_biz_applications', $13,
                'HouseholdDeath',
                $14,
                first_approved_at,
                NOW(), NOW(), $15, $16,
                city_id, county_id, town_id, village_id,
                hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id,
                family_land_area, self_farmed_land_area, subleased_land_area, contracted_land_area,
                land_income_total, subsidy_total, total_confirmed_land_area, confirmed_person_count
            FROM nc_biz_applications WHERE id = $17
            RETURNING id;";

            var newAppResult = await _db.ExecuteScalarAsync(newAppInsertSql, ct,
                appNoResult.Value,
                newHeadMember.Name, newHeadMember.IdCard, newHeadMember.Phone,
                newHeadMember.Gender, newHeadMember.Ethnicity, newHeadMember.MaritalStatus, newHeadMember.HukouType,
                newHeadMember.EducationLevel, newHeadMember.PoliticalStatus,
                newFamilySize, newFamilySize,
                context.ApplicationId,
                context.ApplicationId,
                context.OperatorName, context.OperatorName,
                context.ApplicationId);
            if (newAppResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(newAppResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    newAppResult.Message ?? "创建新档案失败");
            }
            var newApplicationId = newAppResult.Value;

            // 9. 复制家庭成员（剔除死亡原户主；新户主 is_applicant=true、关系='本人/户主'）
            var memberCopySql = @"INSERT INTO nc_biz_family_members (
                application_id, name, id_card, is_applicant, gender, birth_date, age, ethnicity, phone,
                hukou_type, hukou_address, marital_status, education_level, political_status,
                relationship_to_head, health_status, work_capacity, is_disabled,
                disability_type, disability_level, is_severe_disability, employment_status, annual_income,
                member_category, disability_certificate_no, disease_category, disease_name,
                home_province, home_city, home_district, home_town, home_address,
                hukou_province, hukou_city, hukou_district, hukou_town, home_village, secondary_disease,
                person_type, annual_support_fee, is_support_ability, monthly_income_capacity,
                family_size, work_unit, monthly_support_fee,
                support_months, main_income_source, is_severe_disease, is_labor_exempt, created_at, updated_at, deleted_at
            )
            SELECT $1, name, id_card,
                CASE WHEN id = $2 THEN true ELSE is_applicant END,
                gender, birth_date, age, ethnicity, phone,
                hukou_type, hukou_address, marital_status, education_level, political_status,
                CASE WHEN id = $2 THEN '本人/户主' ELSE relationship_to_head END,
                health_status, work_capacity, is_disabled,
                disability_type, disability_level, is_severe_disability, employment_status, annual_income,
                member_category, disability_certificate_no, disease_category, disease_name,
                home_province, home_city, home_district, home_town, home_address,
                hukou_province, hukou_city, hukou_district, hukou_town, home_village, secondary_disease,
                person_type, annual_support_fee, is_support_ability, monthly_income_capacity,
                family_size, work_unit, monthly_support_fee,
                support_months, main_income_source, is_severe_disease, is_labor_exempt, NOW(), NOW(), NULL
            FROM nc_biz_family_members
            WHERE application_id = $3 AND id != $4 AND deleted_at IS NULL;";
            var memberCopyResult = await _db.ExecuteNonQueryAsync(memberCopySql, ct,
                newApplicationId, context.NewHeadMemberId, context.ApplicationId, deceasedHead.Id);
            if (memberCopyResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(memberCopyResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    memberCopyResult.Message ?? "复制家庭成员失败");
            }

            // 构建 旧成员ID → 新成员ID 映射（按身份证号匹配，用于照料人被照料成员与经济明细 member_id 的指向迁移）
            var newMembersResult = await _db.QueryAsync<FamilyMember>(
                "SELECT * FROM nc_biz_family_members WHERE application_id = $1 AND deleted_at IS NULL", ct, newApplicationId);
            if (newMembersResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(newMembersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    newMembersResult.Message ?? "新档案成员查询失败");
            }
            var newMembers = newMembersResult.Value ?? new List<FamilyMember>();
            var memberIdMap = new Dictionary<long, long>();
            foreach (var oldMember in allMembers.Where(m => m.Id != deceasedHead.Id))
            {
                var match = newMembers.FirstOrDefault(m => !string.IsNullOrEmpty(m.IdCard)
                    && string.Equals(m.IdCard, oldMember.IdCard, StringComparison.OrdinalIgnoreCase))
                    ?? newMembers.FirstOrDefault(m => string.Equals(m.Name, oldMember.Name, StringComparison.Ordinal));
                if (match != null)
                    memberIdMap[oldMember.Id] = match.Id;
            }

            // 10. 复制经济明细（LoadAll → 重映射 member_id → SaveAll；死亡原户主的明细行 member_id 置 0，文本字段保留）
            var econLoadResult = await _economicDetailService.LoadAllAsync(context.ApplicationId, ct);
            if (econLoadResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(econLoadResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    econLoadResult.Message ?? "经济明细加载失败");
            }
            var econ = econLoadResult.Value;
            long MapMemberId(long oldId) => oldId > 0 && memberIdMap.TryGetValue(oldId, out var nid) ? nid : 0;
            foreach (var it in econ.LaborIncomes) it.MemberId = MapMemberId(it.MemberId);
            foreach (var it in econ.BusinessIncomes) it.MemberId = MapMemberId(it.MemberId);
            foreach (var it in econ.PropertyIncomes) it.MemberId = MapMemberId(it.MemberId);
            foreach (var it in econ.TransferIncomes) it.MemberId = MapMemberId(it.MemberId);
            foreach (var it in econ.RigidExpenditures) it.MemberId = MapMemberId(it.MemberId);
            foreach (var it in econ.FamilyProperties) it.MemberId = MapMemberId(it.MemberId);
            foreach (var it in econ.Vehicles) it.MemberId = MapMemberId(it.MemberId);
            foreach (var it in econ.Machineries) it.MemberId = MapMemberId(it.MemberId);

            var econSaveResult = await _economicDetailService.SaveAllAsync(newApplicationId,
                econ.LaborIncomes, econ.BusinessIncomes, econ.PropertyIncomes, econ.TransferIncomes,
                econ.OtherIncomes, econ.Subsidies, econ.BreedingIncomes, econ.RigidExpenditures,
                econ.FamilyProperties, econ.Vehicles, econ.Machineries, econ.FinancialAssets, econ.LandRegistrations,
                econ.LandConfirmationGroups, ct);
            if (econSaveResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(econSaveResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    econSaveResult.Message ?? "经济明细复制失败");
            }

            // 11. 复制入户调查（若存在）
            var surveyCopySql = @"INSERT INTO nc_biz_household_surveys
                (application_id, survey_date, surveyor_name, surveyor_organization,
                 respondent_name, respondent_relation, survey_notes,
                 created_at, updated_at, deleted_at,
                 application_reason, application_reason_detail, survey_conclusion)
                SELECT $1, survey_date, surveyor_name, surveyor_organization,
                       respondent_name, respondent_relation, survey_notes,
                       NOW(), NOW(), NULL,
                       application_reason, application_reason_detail, survey_conclusion
                FROM nc_biz_household_surveys
                WHERE application_id = $2 AND deleted_at IS NULL;";
            var surveyCopyResult = await _db.ExecuteNonQueryAsync(surveyCopySql, ct, newApplicationId, context.ApplicationId);
            if (surveyCopyResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(surveyCopyResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    surveyCopyResult.Message ?? "复制入户调查失败");
            }

            // 12. 复制照料人（cared_member_id 迁移到新成员ID，无法映射置 0）
            var caregiversResult = await _db.QueryAsync<Caregiver>(
                "SELECT * FROM nc_biz_caregivers WHERE application_id = $1 AND deleted_at IS NULL", ct, context.ApplicationId);
            if (caregiversResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(caregiversResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    caregiversResult.Message ?? "照料人查询失败");
            }
            var caregivers = caregiversResult.Value ?? new List<Caregiver>();
            if (caregivers.Count > 0)
            {
                var caregiverSql = @"INSERT INTO nc_biz_caregivers
                    (application_id, cared_member_id, name, id_card, phone, relationship,
                     gender, age, ethnicity, health_status, employment_status, main_income_source,
                     work_unit, position, address,
                     marital_status, hukou_type, education_level, political_status,
                     created_at, deleted_at)
                    VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,NOW(),NULL);";
                foreach (var cg in caregivers)
                {
                    var insertCaregiverResult = await _db.ExecuteNonQueryAsync(caregiverSql, ct,
                        newApplicationId, MapMemberId(cg.CaredMemberId),
                        cg.Name, cg.IdCard, cg.Phone, cg.Relationship,
                        cg.Gender, cg.Age, cg.Ethnicity, cg.HealthStatus,
                        cg.EmploymentStatus, cg.MainIncomeSource,
                        cg.WorkUnit, cg.Position, cg.Address,
                        cg.MaritalStatus, cg.HukouType, cg.EducationLevel, cg.PoliticalStatus);
                    if (insertCaregiverResult.IsFailure)
                    {
                        if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                        return Result.Failure<ChangeResult>(insertCaregiverResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                            insertCaregiverResult.Message ?? "复制照料人失败");
                    }
                }
            }

            // 13. 写死亡记录（is_household_head=true，回填新档案ID）
            var deathSql = @"INSERT INTO nc_biz_death_records
                (application_id, member_id, member_name, member_id_card, relationship_to_head,
                 death_date, death_reason, death_certificate_no,
                 is_household_head, new_head_id_card, new_head_name, new_application_id,
                 operator_name, created_by, created_at)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,true,$9,$10,$11,$12,$12,NOW());";
            var deathInsertResult = await _db.ExecuteNonQueryAsync(deathSql, ct,
                context.ApplicationId, deceasedHead.Id, deceasedHead.Name, deceasedHead.IdCard,
                string.IsNullOrEmpty(deceasedHead.RelationshipToHead) ? "本人/户主" : deceasedHead.RelationshipToHead,
                context.DeathDate, context.DeathReason, context.DeathCertificateNo,
                context.NewHeadIdCard, context.NewHeadName, newApplicationId,
                context.OperatorName);
            if (deathInsertResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(deathInsertResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    deathInsertResult.Message ?? "创建死亡记录失败");
            }

            // 14. 停止旧档案（户主死亡）
            var stopSql = @"UPDATE nc_biz_applications SET
                status = $1, stop_reason = $2, stop_date = $3,
                updated_at = NOW(), updated_by = $4
                WHERE id = $5;";
            var stopResult = await _db.ExecuteNonQueryAsync(stopSql, ct,
                ApplicationStatus.Stopped.GetCode(), ChangeReasonTypeConstants.HeadDeceased, context.DeathDate,
                context.OperatorName, context.ApplicationId);
            if (stopResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(stopResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    stopResult.Message ?? "停止旧档案失败");
            }

            // 14.1 旧档案停保，结清其渐退期（渐退期仅对在保的低收入降档户有意义）
            var graceClearResult = await _gracePeriodService.ClearAsync(context.ApplicationId, ct);
            if (graceClearResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(graceClearResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    graceClearResult.Message ?? "结清旧档案渐退期失败");
            }

            // 15. 创建变更记录（挂在旧档案上，After 快照含新档案ID）
            var changeContext = new ChangeContext
            {
                ApplicationId = context.ApplicationId,
                ChangeType = DictionaryConstants.ChangeType.HOUSEHOLD_DEATH,
                ChangeReason = $"户主死亡变更: {oldHeadName} → 新户主 {context.NewHeadName}",
                ChangeDate = context.DeathDate,
                OperatorName = context.OperatorName
            };
            var beforeJson = JsonSerializer.Serialize(new
            {
                HeadName = oldHeadName,
                HeadIdCard = oldHeadIdCard,
                FamilySize = oldFamilySize,
                Classification = oldClassification,
                GuaranteeAmount = oldGuaranteeAmount
            });
            var afterJson = JsonSerializer.Serialize(new
            {
                NewHeadName = context.NewHeadName,
                NewApplicationId = newApplicationId,
                FamilySize = newFamilySize,
                DeathDate = context.DeathDate,
                DeathReason = context.DeathReason,
                DeceasedHeadName = deceasedHead.Name
            });
            var changeIdResult = await CreateChangeAsync(changeContext, beforeJson, afterJson, ct);
            if (changeIdResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(changeIdResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    changeIdResult.Message ?? "创建变更记录失败");
            }
            var changeId = changeIdResult.Value;

            // 填充 nc_biz_change_records 的结构化对比字段（户主死亡审计对应）
            var updateChangeSql = @"UPDATE nc_biz_change_records SET
                change_reason_type = $1,
                old_classification = $2, old_guarantee_amount = $3,
                triggered_stop = $4,
                changed_at = NOW()
                WHERE id = $5;";
            var updateChangeResult = await _db.ExecuteNonQueryAsync(updateChangeSql, ct,
                ChangeReasonTypeConstants.HeadDeceased, oldClassification, oldGuaranteeAmount, true, changeId);
            if (updateChangeResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<ChangeResult>(updateChangeResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    updateChangeResult.Message ?? "更新变更记录对比字段失败");
            }

            if (shouldManageTransaction)
                await _db.CommitTransactionAsync();

            LogInfo($"户主死亡变更完成: 旧档案={context.ApplicationId} → 新档案={newApplicationId}, 新家庭人数={newFamilySize}");

            return Result.Success(new ChangeResult
            {
                ChangeId = changeId,
                IsSuccess = true,
                OldClassification = oldClassification,
                OldGuaranteeAmount = oldGuaranteeAmount,
                TriggeredStop = true,
                NewApplicationId = newApplicationId
            });
        }
        catch (Exception ex)
        {
            if (shouldManageTransaction) await _db.RollbackTransactionAsync();
            LogException(ex, "户主死亡变更");
            return Result.FromException<ChangeResult>(ex);
        }
    }

    /// <summary>
    /// 获取变更历史
    /// </summary>
    public async Task<List<ChangeRecord>> GetChangeHistoryAsync(long applicationId, CancellationToken ct = default)
    {
        // nc_biz_change_records 无 created_at 列（只有 changed_at）：
        // 查询改用 changed_at，避免 42703 使环境事务进入 aborted 状态而引发后续 25P02
        var sql = @"SELECT id, application_id, change_no, change_type, change_reason, 
                    change_date, operator_name, changed_at
                    FROM nc_biz_change_records 
                    WHERE application_id = $1 
                    ORDER BY changed_at DESC;";

        var result = await _db.QueryAsync<ChangeRecord>(sql, ct, applicationId);
        return result.IsSuccess && result.Value != null ? result.Value : new List<ChangeRecord>();
    }
}
