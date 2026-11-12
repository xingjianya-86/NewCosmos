using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Enums;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.AssetVerification;
using NewCosmos.Services.Domain.ElderlyBenefits;
using NewCosmos.Services.StateMachine;
using NewCosmos.Services.Utilities;

namespace NewCosmos.Services.Domain.SocialAssistance;

using ApplicationEntity = NewCosmos.Models.Entities.Application;

/// <summary>
/// 申请服务实现
/// </summary>
public class ApplicationService : BaseService, IApplicationService
{
    protected override string ServiceName => "ApplicationService";
    private readonly IDatabaseService _db;
    private readonly IPdfVerificationService _pdfVerificationService;
    private readonly IElderlyApplicationService _elderlyApplicationService;
    private readonly IClassificationService _classificationService;
    private readonly IGuaranteeAmountService _guaranteeAmountService;
    private readonly Services.Core.IApplicationStatusService _statusService;
    private readonly IBusinessTimelineService _timelineService;

    /// <summary>「已完结档案」状态过滤集（已出草稿态，防 Draft/Refused 异常行混入）</summary>
    private static readonly List<string> ArchivedStatusFilter = new()
    {
        ApplicationStatusCodes.APPROVED,
        ApplicationStatusCodes.COMPLETED,
        ApplicationStatusCodes.STOPPED
    };

    public ApplicationService(IDatabaseService db, ILoggerService logger, IPdfVerificationService pdfVerificationService,
        IElderlyApplicationService elderlyApplicationService,
        IClassificationService classificationService,
        IGuaranteeAmountService guaranteeAmountService,
        Services.Core.IApplicationStatusService statusService,
        IBusinessTimelineService timelineService) : base(logger)
    {
        _db = db;
        _pdfVerificationService = pdfVerificationService;
        _elderlyApplicationService = elderlyApplicationService;
        _classificationService = classificationService;
        _guaranteeAmountService = guaranteeAmountService;
        _statusService = statusService;
        _timelineService = timelineService;
    }

    /// <summary>
    /// 在数据库事务中执行工作单元：正常完成提交，异常自动回滚。
    /// 供 ViewModel 编排多服务保存流程使用（子服务经 HasTransaction 自动加入本事务）。
    /// </summary>
    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> work, CancellationToken ct = default)
    {
        await using var tx = await _db.BeginTransactionScopeAsync(ct);
        await work(ct);
        await tx.CommitAsync(ct);
    }

    /// <summary>
    /// 在数据库事务中执行返回 Result 的工作单元：Result 成功才提交，失败回滚并原样返回。
    /// </summary>
    public async Task<Result> ExecuteInTransactionForResultAsync(Func<CancellationToken, Task<Result>> work, CancellationToken ct = default)
    {
        await using var tx = await _db.BeginTransactionScopeAsync(ct);
        var result = await work(ct);
        if (result.IsFailure)
            return result;
        await tx.CommitAsync(ct);
        return result;
    }

    /// <summary>
    /// 建档/审批成为保障对象后，联动升级资产核查状态为"已纳入低收入人口"。
    /// 分类口径与 PdfVerificationService.HasLowIncomeIdentityAsync 共用
    /// ClassificationConstants.IsProtectedLowIncomeCode（低保/单人/边缘/特困，不含刚性支出）；
    /// 失败仅记日志不阻断主流程。处于环境事务中时自动随事务提交/回滚。
    /// </summary>
    private async Task TryMarkAssetChecksIncludedAsync(
        (long Id, string? ClassificationResult, string? ApplicantIdCard) app, CancellationToken ct)
    {
        try
        {
            var code = app.ClassificationResult;
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(app.ApplicantIdCard))
                return;

            if (!ClassificationConstants.IsProtectedLowIncomeCode(code))
                return;

            var markResult = await _pdfVerificationService.MarkIncludedByIdCardAsync(app.ApplicantIdCard, ct);
            if (markResult.IsFailure)
                LogWarn($"资产核查状态联动失败（不阻断）: ApplicationId={app.Id}, {markResult.Message}");
        }
        catch (Exception ex)
        {
            LogException(ex, "资产核查状态联动异常");
        }
    }

    public async Task<Result<ApplicationEntity>> GetByIdAsync(long id, CancellationToken ct = default)
    {
        LogInfo($"执行操作");
        var sql = $"SELECT {ApplicationColumns.Full} FROM nc_biz_applications WHERE id = $1 AND deleted_at IS null";
        return await _db.QuerySingleAsync<ApplicationEntity>(sql, ct, id);
    }

    public async Task<Result<List<ApplicationEntity>>> GetByIdCardAsync(string idCard, CancellationToken ct = default)
    {
        ValidateNotNullOrEmpty(idCard, nameof(idCard));
        LogInfo($"根据身份证查询: {DataMasker.MaskIdCard(idCard)}");
        var sql = $"SELECT {ApplicationColumns.Full} FROM nc_biz_applications WHERE applicant_id_card = $1 AND deleted_at IS NULL ORDER BY created_at DESC LIMIT 100";
        return await _db.QueryAsync<ApplicationEntity>(sql, ct, idCard);
    }

    public async Task<Result<Dictionary<string, string>>> GetClassificationsByIdCardsAsync(IReadOnlyCollection<string> idCards, CancellationToken ct = default)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (idCards == null || idCards.Count == 0)
            return Result.Success(map);

        LogInfo($"批量查询档案分类: 数量={idCards.Count}");
        var sql = "SELECT applicant_id_card, classification_result FROM nc_biz_applications WHERE applicant_id_card = ANY($1::text[]) AND deleted_at IS NULL";
        var result = await _db.QueryAsync<(string? IdCard, string? Classification)>(sql, ct, new object[] { idCards.ToArray() });
        if (result.IsFailure)
            return Result.Failure<Dictionary<string, string>>(result.ErrorCode!, result.Message!);

        foreach (var row in result.Value ?? new List<(string? IdCard, string? Classification)>())
        {
            // 同一身份证可能有多条档案，取先命中行
            if (!string.IsNullOrWhiteSpace(row.IdCard) && !string.IsNullOrWhiteSpace(row.Classification))
                map.TryAdd(row.IdCard.Trim(), row.Classification);
        }
        return Result.Success(map);
    }

    public async Task<Result<PagedResult<ApplicationEntity>>> GetPagedAsync(int pageIndex, int pageSize, string status = null, string keyword = null, CancellationToken ct = default)
    {
        LogInfo($"分页查询: 第{pageIndex}页 每页{pageSize}条");

        var conditions = new SqlConditionBuilder()
            .Add("deleted_at IS NULL")
            .AddIf(!string.IsNullOrEmpty(status), "status = {0}", status)
            .AddIf(!string.IsNullOrEmpty(keyword),
                "(applicant_name LIKE {0} OR applicant_id_card = {1}"
                + " OR EXISTS (SELECT 1 FROM nc_biz_family_members fm"
                + " WHERE fm.application_id = nc_biz_applications.id"
                + " AND (fm.name LIKE {0} OR fm.id_card = {1}) AND fm.deleted_at IS NULL))",
                $"%{keyword}%", keyword);

        var where = conditions.ToWhereClause();
        var countSql = $"SELECT COUNT(*) FROM nc_biz_applications{where}";
        var querySql = $"SELECT {ApplicationColumns.Full} FROM nc_biz_applications{where}";

        var countResult = await _db.ExecuteScalarAsync<long>(countSql, ct, conditions.GetParameters());
        if (countResult.IsFailure)
            return Result.Failure<PagedResult<ApplicationEntity>>(countResult.ErrorCode!, countResult.Message!);

        var offset = (pageIndex - 1) * pageSize;
        querySql += $" ORDER BY created_at DESC LIMIT ${conditions.ParamCount + 1} OFFSET ${conditions.ParamCount + 2}";
        var pageParams = new List<object?>(conditions.GetParameters()) { pageSize, offset };

        var listResult = await _db.QueryAsync<ApplicationEntity>(querySql, ct, pageParams.ToArray());
        if (listResult.IsFailure)
            return Result.Failure<PagedResult<ApplicationEntity>>(listResult.ErrorCode!, listResult.Message!);

        return Result.Success(PagedResult<ApplicationEntity>.FromList(listResult.Value, pageIndex, pageSize, Convert.ToInt32(countResult.Value)));
    }

    public async Task<Result<PagedResult<ApplicationEntity>>> GetByMonthPagedAsync(int year, int month, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        LogInfo($"按月查询: {year}年{month}月, 第{pageIndex}页");

        var monthStart = new DateTime(year, month, 1);
        var monthEnd = monthStart.AddMonths(1);

        // first_approved_at 范围比较（走索引），与名单"首次审批/新增"业务时间口径一致
        var conditions = new SqlConditionBuilder()
            .Add("deleted_at IS NULL")
            .Add("first_approved_at IS NOT NULL")
            .Add("first_approved_at >= {0}", monthStart)
            .Add("first_approved_at < {0}", monthEnd);

        var where = conditions.ToWhereClause();
        var countSql = $"SELECT COUNT(*) FROM nc_biz_applications{where}";
        var querySql = $"SELECT {ApplicationColumns.Full} FROM nc_biz_applications{where}";

        var countResult = await _db.ExecuteScalarAsync<long>(countSql, ct, conditions.GetParameters());
        if (countResult.IsFailure)
            return Result.Failure<PagedResult<ApplicationEntity>>(countResult.ErrorCode!, countResult.Message!);

        var offset = (pageIndex - 1) * pageSize;
        querySql += $" ORDER BY first_approved_at DESC LIMIT ${conditions.ParamCount + 1} OFFSET ${conditions.ParamCount + 2}";
        var pageParams = new List<object?>(conditions.GetParameters()) { pageSize, offset };

        var listResult = await _db.QueryAsync<ApplicationEntity>(querySql, ct, pageParams.ToArray());
        if (listResult.IsFailure)
            return Result.Failure<PagedResult<ApplicationEntity>>(listResult.ErrorCode!, listResult.Message!);

        return Result.Success(PagedResult<ApplicationEntity>.FromList(listResult.Value, pageIndex, pageSize, Convert.ToInt32(countResult.Value)));
    }

    public async Task<Result<long>> CreateAsync(ApplicationEntity application, CancellationToken ct = default)
    {
        ValidateNotNull(application, nameof(application));

        LogInfo($"创建申请(完整): {DataMasker.MaskName(application.ApplicantName)}");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            var existsResult = await CheckIdCardExistsAsync(application.ApplicantIdCard, null, ct);
            if (existsResult.IsSuccess && existsResult.Value)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<long>(ErrorCodes.DUPLICATE_ID_CARD, "该身份证号已存在");
            }

            var appNoResult = await GetNextApplicationNoAsync(ct);
            if (appNoResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<long>(appNoResult.ErrorCode!, appNoResult.Message!);
            }

            var sql = @"INSERT INTO nc_biz_applications 
            (application_no, applicant_name, applicant_id_card, applicant_phone,
             gender, ethnicity, marital_status, hukou_type, education_level, political_status,
             hukou_address, disability_card_no,
             province, city, district, town, community, address,
             city_id, county_id, town_id, village_id,
             hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id,
              physical_condition, disease_name, secondary_disease_name,
              disease_code, is_severe_disease,
              disability_type, disability_level, health_status,
             employment_status, work_unit, income_source,
             family_size, confirmed_family_size,
             is_single_rescue, support_mode,
             application_reason, application_reason_detail, caregiver_type, destitute_support_type, support_institution_id,
             work_income_total, business_income_total, property_income_total,
             transfer_income_total, other_income_total, alimony_income,
             total_family_income, per_capita_income, rigid_expenditure,
             family_land_area, self_farmed_land_area, subleased_land_area,
             contracted_land_area, land_income_total, subsidy_total,
             total_annual_income, per_capita_annual_income,
              is_eligible, classification_result,
              classified_subsidy_type, classified_subsidy_amount,
              household_monthly_guarantee_amount, person_category_protection_total_amount,
              caregiver_subsidy_amount, total_guarantee_amount,
              is_special_approval, special_approval_id,
              status, current_step, created_by, created_at, updated_at, bank_name, bank_account,
              medical_self_paid_12m)
             VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,
                    $11,$12,$13,$14,$15,$16,$17,$18,
                    $19,$20,$21,$22,$23,$24,$25,$26,
                    $27,$28,$29,$30,$31,$32,$33,$34,$35,
                    $36,$37,$38,$39,$40,$41,$42,$43,
                    $44,$45,$46,$47,$48,$49,$50,$51,$52,$53,
                    $54,$55,$56,$57,$58,$59,$60,$61,$62,$63,
                    $64,$65,$66,$67,$68,$69,$70,$71,$72,$73,
                    $74,$75,$76,$77,$78,$79,$80,$81)
            RETURNING id";

            var insertResult = await _db.ExecuteScalarAsync(sql, ct,
                appNoResult.Value,
                application.ApplicantName, application.ApplicantIdCard, application.ApplicantPhone,
                application.Gender, application.Ethnicity, application.MaritalStatus, application.HukouType,
                application.EducationLevel, application.PoliticalStatus,
                application.HukouAddress, application.DisabilityCardNo,
                application.Province, application.City, application.District, application.Town,
                application.Community, application.Address,
                (object?)application.CityId, (object?)application.CountyId,
                (object?)application.TownId, (object?)application.VillageId,
                (object?)application.HukouCityId, (object?)application.HukouCountyId,
                (object?)application.HukouTownId, (object?)application.HukouVillageId,
                application.PhysicalCondition, application.DiseaseName, application.SecondaryDiseaseName,
                application.DiseaseCode, application.IsSevereDisease,
                application.DisabilityType, application.DisabilityLevel, application.HealthStatus,
                application.EmploymentStatus, application.WorkUnit, application.IncomeSource,
                application.FamilySize, application.ConfirmedFamilySize,
                application.IsSingleRescue, application.SupportMode,
                application.ApplicationReason, application.ApplicationReasonDetail, application.CaregiverType, application.DestituteSupportType,
                application.SupportInstitutionId,
                application.WorkIncomeTotal, application.BusinessIncomeTotal, application.PropertyIncomeTotal,
                application.TransferIncomeTotal, application.OtherIncomeTotal, application.AlimonyIncome,
                application.TotalFamilyIncome, application.PerCapitaIncome, application.RigidExpenditure,
                application.FamilyLandArea, application.SelfFarmedLandArea, application.SubleasedLandArea,
                application.ContractedLandArea, application.LandIncomeTotal, application.SubsidyTotal,
                application.TotalAnnualIncome, application.PerCapitaAnnualIncome,
                application.IsEligible, application.ClassificationResult,
                application.ClassifiedSubsidyType, application.ClassifiedSubsidyAmount,
                application.HouseholdMonthlyGuaranteeAmount, application.PersonCategoryProtectionTotalAmount,
                application.CaregiverSubsidyAmount, application.TotalGuaranteeAmount,
                application.IsSpecialApproval, (object?)application.SpecialApprovalId,
                application.Status ?? "Draft", application.CurrentStep,
                application.CreatedBy ?? "System",
                application.CreatedAt == default ? DateTime.Now : application.CreatedAt,
                DateTime.Now,
                application.BankName ?? string.Empty, application.BankAccount ?? string.Empty,
                (object?)application.MedicalSelfPaid12m);

            if (insertResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<long>(insertResult.ErrorCode!, insertResult.Message!);
            }

            await tx.CommitAsync(ct);

            var newId = insertResult.Value;
            LogInfo($"创建申请成功: Id={newId}");
            return Result.Success(newId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogError($"创建申请失败: {ex.Message}");
            return Result.Failure<long>(ErrorCodes.DB_CONNECTION_FAILED, ex.Message);
        }
    }

    public async Task<Result> UpdateAsync(ApplicationUpdateRequest request, CancellationToken ct = default, bool allowNonEditable = false)
    {
        ValidateNotNull(request, nameof(request));

        LogInfo($"执行操作");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            var appResult = await GetByIdAsync(request.ApplicationId, ct);
            if (appResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure(appResult.ErrorCode!, appResult.Message!);
            }

            var app = appResult.Value;
            if (app == null)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");
            }

            // 补全模式（allowNonEditable=true）允许更新已建档的 Approved 档案；普通编辑仍只允许草稿
            if (!allowNonEditable && !ApplicationStateMachine.IsEditable(ApplicationStatusExtensions.FromCode(app.Status)))
            {
                await tx.RollbackAsync(ct);
                return Result.Failure(ErrorCodes.INVALID_TRANSITION, "当前状态不允许编辑");
            }

            // 动态 SET 构建：字段名→值 顺序一一对应，参数编号自动生成（WHERE 参数放最后）
            var sets = new List<string>();
            var args = new List<object?>();
            void Set(string col, object? v) { args.Add(v); sets.Add($"{col} = ${args.Count}"); }

            // 增量写（2026-11 A5）：与上方事务内已读行（app）逐列比较，只写变化列。
            // 与全量写【结果状态完全等价】——相等跳过 = 写回同值；不等写入 = 原样写入，
            // 故「未编辑列原样回写」语义（BuildApplication 回填契约）不受影响，且写放大显著减少。
            // 宁可多写不漏写：类型不匹配/无法比较时 Equals 返回 false → 写入（退化为原全量写行为）。
            void SetIfChanged(string col, object? newValue, object? currentValue)
            {
                if (ValuesEqual(newValue, currentValue)) return;
                args.Add(newValue);
                sets.Add($"{col} = ${args.Count}");
            }

            // 值相等判定：null/null、同型值、string Ordinal 精确比较（null ≠ ""——
            // 与原全量写逐字节等价：DB NULL→请求 "" 仍会写入，不静默改变 NULL/空串语义）。
            // 类型不匹配（int vs long 等）Equals 返回 false → 写入，退化为原全量写行为，宁可多写不漏写。
            static bool ValuesEqual(object? a, object? b)
            {
                if (a is null && b is null) return true;
                if (a is null || b is null) return false;
                if (a is string sa && b is string sb)
                    return string.Equals(sa, sb, StringComparison.Ordinal);
                return a.Equals(b);
            }

            SetIfChanged("applicant_name", request.ApplicantName, app.ApplicantName);
            SetIfChanged("applicant_id_card", request.ApplicantIdCard, app.ApplicantIdCard);
            SetIfChanged("applicant_phone", request.ApplicantPhone, app.ApplicantPhone);
            SetIfChanged("address", request.Address, app.Address);
            SetIfChanged("town", request.Town, app.Town);
            SetIfChanged("community", request.Community, app.Community);
            SetIfChanged("family_size", request.FamilySize, app.FamilySize);
            SetIfChanged("total_family_income", request.AnnualIncome, app.TotalFamilyIncome);
            SetIfChanged("gender", request.Gender, app.Gender);
            SetIfChanged("ethnicity", request.Ethnicity, app.Ethnicity);
            SetIfChanged("marital_status", request.MaritalStatus, app.MaritalStatus);
            SetIfChanged("hukou_type", request.HukouType, app.HukouType);
            SetIfChanged("education_level", request.EducationLevel, app.EducationLevel);
            SetIfChanged("political_status", request.PoliticalStatus, app.PoliticalStatus);
            SetIfChanged("hukou_address", request.HukouAddress, app.HukouAddress);
            SetIfChanged("disability_card_no", request.DisabilityCardNo, app.DisabilityCardNo);
            SetIfChanged("province", request.Province, app.Province);
            SetIfChanged("city", request.City, app.City);
            SetIfChanged("district", request.District, app.District);
            // 补齐历史缺失字段（与 CreateAsync INSERT 列对齐；注：nc_biz_applications 无 village 列，
            // 表单的村/社区数据走 community 列）
            SetIfChanged("city_id", request.CityId, app.CityId);
            SetIfChanged("county_id", request.CountyId, app.CountyId);
            SetIfChanged("town_id", request.TownId, app.TownId);
            SetIfChanged("village_id", request.VillageId, app.VillageId);
            SetIfChanged("hukou_city_id", request.HukouCityId, app.HukouCityId);
            SetIfChanged("hukou_county_id", request.HukouCountyId, app.HukouCountyId);
            SetIfChanged("hukou_town_id", request.HukouTownId, app.HukouTownId);
            SetIfChanged("hukou_village_id", request.HukouVillageId, app.HukouVillageId);
            SetIfChanged("physical_condition", request.PhysicalCondition, app.PhysicalCondition);
            SetIfChanged("disease_name", request.DiseaseName, app.DiseaseName);
            SetIfChanged("secondary_disease_name", request.SecondaryDiseaseName, app.SecondaryDiseaseName);
            SetIfChanged("disease_code", request.DiseaseCode, app.DiseaseCode);
            SetIfChanged("is_severe_disease", request.IsSevereDisease, app.IsSevereDisease);
            SetIfChanged("medical_self_paid_12m", request.MedicalSelfPaid12m, app.MedicalSelfPaid12m);
            SetIfChanged("disability_type", request.DisabilityType, app.DisabilityType);
            SetIfChanged("disability_level", request.DisabilityLevel, app.DisabilityLevel);
            SetIfChanged("health_status", request.HealthStatus, app.HealthStatus);
            SetIfChanged("employment_status", request.EmploymentStatus, app.EmploymentStatus);
            SetIfChanged("work_unit", request.WorkUnit, app.WorkUnit);
            SetIfChanged("income_source", request.IncomeSource, app.IncomeSource);
            SetIfChanged("bank_name", request.BankName, app.BankName);
            SetIfChanged("bank_account", request.BankAccount, app.BankAccount);
            SetIfChanged("application_reason", request.ApplicationReason, app.ApplicationReason);
            SetIfChanged("application_reason_detail", request.ApplicationReasonDetail, app.ApplicationReasonDetail);
            SetIfChanged("work_income_total", request.WorkIncomeTotal, app.WorkIncomeTotal);
            SetIfChanged("business_income_total", request.BusinessIncomeTotal, app.BusinessIncomeTotal);
            SetIfChanged("property_income_total", request.PropertyIncomeTotal, app.PropertyIncomeTotal);
            SetIfChanged("transfer_income_total", request.TransferIncomeTotal, app.TransferIncomeTotal);
            SetIfChanged("other_income_total", request.OtherIncomeTotal, app.OtherIncomeTotal);
            SetIfChanged("alimony_income", request.AlimonyIncome, app.AlimonyIncome);
            SetIfChanged("per_capita_income", request.PerCapitaIncome, app.PerCapitaIncome);
            SetIfChanged("total_annual_income", request.TotalAnnualIncome, app.TotalAnnualIncome);
            SetIfChanged("per_capita_annual_income", request.PerCapitaAnnualIncome, app.PerCapitaAnnualIncome);
            SetIfChanged("rigid_expenditure", request.RigidExpenditure, app.RigidExpenditure);
            SetIfChanged("family_land_area", request.FamilyLandArea, app.FamilyLandArea);
            SetIfChanged("self_farmed_land_area", request.SelfFarmedLandArea, app.SelfFarmedLandArea);
            SetIfChanged("subleased_land_area", request.SubleasedLandArea, app.SubleasedLandArea);
            SetIfChanged("contracted_land_area", request.ContractedLandArea, app.ContractedLandArea);
            SetIfChanged("land_income_total", request.LandIncomeTotal, app.LandIncomeTotal);
            SetIfChanged("subsidy_total", request.SubsidyTotal, app.SubsidyTotal);
            SetIfChanged("is_eligible", request.IsEligible, app.IsEligible);
            SetIfChanged("classification_result", request.ClassificationResult, app.ClassificationResult);
            SetIfChanged("classified_subsidy_type", request.ClassifiedSubsidyType, app.ClassifiedSubsidyType);
            SetIfChanged("classified_subsidy_amount", request.ClassifiedSubsidyAmount, app.ClassifiedSubsidyAmount);
            SetIfChanged("household_monthly_guarantee_amount", request.HouseholdMonthlyGuaranteeAmount, app.HouseholdMonthlyGuaranteeAmount);
            SetIfChanged("person_category_protection_total_amount", request.PersonCategoryProtectionTotalAmount, app.PersonCategoryProtectionTotalAmount);
            SetIfChanged("caregiver_subsidy_amount", request.CaregiverSubsidyAmount, app.CaregiverSubsidyAmount);
            SetIfChanged("total_guarantee_amount", request.TotalGuaranteeAmount, app.TotalGuaranteeAmount);
            SetIfChanged("is_special_approval", request.IsSpecialApproval, app.IsSpecialApproval);
            SetIfChanged("special_approval_id", request.SpecialApprovalId, app.SpecialApprovalId);
            SetIfChanged("caregiver_type", request.CaregiverType, app.CaregiverType);
            SetIfChanged("destitute_support_type", request.DestituteSupportType, app.DestituteSupportType);
            SetIfChanged("confirmed_family_size", request.ConfirmedFamilySize, app.ConfirmedFamilySize);
            SetIfChanged("is_single_rescue", request.IsSingleRescue, app.IsSingleRescue);
            SetIfChanged("support_mode", request.SupportMode, app.SupportMode);
            SetIfChanged("support_institution_id", request.SupportInstitutionId, app.SupportInstitutionId);
            SetIfChanged("current_step", request.CurrentStep, app.CurrentStep);
            // 审计与并发令牌列无条件写（updated_at 是乐观并发令牌 + 列表排序键，必须推进）
            Set("updated_by", request.UpdatedBy);
            sets.Add("updated_at = NOW()");

            // WHERE 参数（放在 SET 参数之后）
            args.Add(request.ApplicationId);
            var idIdx = args.Count;

            // 乐观并发守卫：仅携带令牌时附加 updated_at 条件（null 参数会触发
            // PostgreSQL 42P08 无法推断参数类型，故令牌为 null 时整条条件省略）
            var sql = $@"UPDATE nc_biz_applications SET
            {string.Join(",\n            ", sets)}
            WHERE id = ${idIdx} AND deleted_at IS NULL";
            if (request.LoadedUpdatedAt.HasValue)
            {
                args.Add(request.LoadedUpdatedAt.Value);
                var tokenIdx = args.Count;
                sql += $" AND updated_at = ${tokenIdx}";
            }

            var updateResult = await _db.ExecuteNonQueryAsync(sql, ct, args.ToArray()!);

            if (updateResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure(updateResult.ErrorCode!, updateResult.Message!);
            }

            if (updateResult.Value == 0)
            {
                await tx.RollbackAsync(ct);
                // 携带令牌时 0 行 = 并发冲突（前面 GetByIdAsync 已确认记录存在）；
                // 未携带令牌时 0 行 = 记录已被并发删除
                return request.LoadedUpdatedAt.HasValue
                    ? Result.Failure(ErrorCodes.CONCURRENCY_CONFLICT, "该申请已被他人修改，请刷新后重试")
                    : Result.Failure(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在或已被删除");
            }

            await tx.CommitAsync(ct);

            LogInfo($"执行操作");
            return Result.Success();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "更新申请失败");
            return Result.FromException(ex);
        }
    }

    public async Task<Result> UpdateAsync(ApplicationEntity application, CancellationToken ct = default, bool allowNonEditable = false)
    {
        ValidateNotNull(application, nameof(application));
        var request = new ApplicationUpdateRequest
        {
            ApplicationId = application.Id,
            ApplicantName = application.ApplicantName,
            ApplicantIdCard = application.ApplicantIdCard,
            ApplicantPhone = application.ApplicantPhone,
            Address = application.Address,
            Town = application.Town,
            Village = application.Village,
            FamilySize = application.FamilySize,
            AnnualIncome = application.TotalFamilyIncome,
            UpdatedBy = application.UpdatedBy ?? "System",
            Gender = application.Gender,
            Ethnicity = application.Ethnicity,
            MaritalStatus = application.MaritalStatus,
            HukouType = application.HukouType,
            EducationLevel = application.EducationLevel,
            PoliticalStatus = application.PoliticalStatus,
            HukouAddress = application.HukouAddress,
            DisabilityCardNo = application.DisabilityCardNo,
            Province = application.Province,
            City = application.City,
            District = application.District,
            Community = application.Community,
            DiseaseName = application.DiseaseName,
            DisabilityType = application.DisabilityType,
            DisabilityLevel = application.DisabilityLevel,
            HealthStatus = application.HealthStatus,
            ApplicationReason = application.ApplicationReason,
            ApplicationReasonDetail = application.ApplicationReasonDetail,
            WorkIncomeTotal = application.WorkIncomeTotal,
            BusinessIncomeTotal = application.BusinessIncomeTotal,
            PropertyIncomeTotal = application.PropertyIncomeTotal,
            TransferIncomeTotal = application.TransferIncomeTotal,
            OtherIncomeTotal = application.OtherIncomeTotal,
            AlimonyIncome = application.AlimonyIncome,
            PerCapitaIncome = application.PerCapitaIncome,
            TotalAnnualIncome = application.TotalAnnualIncome,
            PerCapitaAnnualIncome = application.PerCapitaAnnualIncome,
            RigidExpenditure = application.RigidExpenditure,
            FamilyLandArea = application.FamilyLandArea,
            SelfFarmedLandArea = application.SelfFarmedLandArea,
            SubleasedLandArea = application.SubleasedLandArea,
            ContractedLandArea = application.ContractedLandArea,
            LandIncomeTotal = application.LandIncomeTotal,
            SubsidyTotal = application.SubsidyTotal,
            IsEligible = application.IsEligible,
            ClassificationResult = application.ClassificationResult,
            ClassifiedSubsidyType = application.ClassifiedSubsidyType,
            ClassifiedSubsidyAmount = application.ClassifiedSubsidyAmount,
            HouseholdMonthlyGuaranteeAmount = application.HouseholdMonthlyGuaranteeAmount,
            CaregiverSubsidyAmount = application.CaregiverSubsidyAmount,
            TotalGuaranteeAmount = application.TotalGuaranteeAmount,
            IsSpecialApproval = application.IsSpecialApproval,
            SpecialApprovalId = application.SpecialApprovalId,
            IsInGracePeriod = application.IsInGracePeriod,
            GracePeriodMonths = application.GracePeriodMonths,
            GracePeriodStartDate = application.GracePeriodStartDate,
            GracePeriodEndDate = application.GracePeriodEndDate,
            OriginalClassificationResult = application.OriginalClassificationResult,
            OriginalGuaranteeAmount = application.OriginalGuaranteeAmount,
            CaregiverType = application.CaregiverType,
            DestituteSupportType = application.DestituteSupportType,
            CityId = application.CityId,
            CountyId = application.CountyId,
            TownId = application.TownId,
            VillageId = application.VillageId,
            HukouCityId = application.HukouCityId,
            HukouCountyId = application.HukouCountyId,
            HukouTownId = application.HukouTownId,
            HukouVillageId = application.HukouVillageId,
            PhysicalCondition = application.PhysicalCondition,
            SecondaryDiseaseName = application.SecondaryDiseaseName,
            DiseaseCode = application.DiseaseCode,
            IsSevereDisease = application.IsSevereDisease,
            MedicalSelfPaid12m = application.MedicalSelfPaid12m,
            EmploymentStatus = application.EmploymentStatus,
            WorkUnit = application.WorkUnit,
            IncomeSource = application.IncomeSource,
            BankName = application.BankName ?? string.Empty,
            BankAccount = application.BankAccount ?? string.Empty,
            ConfirmedFamilySize = application.ConfirmedFamilySize,
            IsSingleRescue = application.IsSingleRescue,
            SupportMode = application.SupportMode,
            SupportInstitutionId = application.SupportInstitutionId,
            PersonCategoryProtectionTotalAmount = application.PersonCategoryProtectionTotalAmount,
            CurrentStep = application.CurrentStep,
            // 实体路径的并发令牌约定：UpdatedAt == default 表示调用方未携带令牌（跳过并发检查）。
            // 表单在加载时保存 entity.UpdatedAt 并于保存时回填；新构造的实体请显式置 default。
            LoadedUpdatedAt = application.UpdatedAt == default ? null : application.UpdatedAt
        };
        return await UpdateAsync(request, ct, allowNonEditable);
    }

    /// <inheritdoc />
    public async Task<Result> MarkDataCompletedAsync(long id, CancellationToken ct = default)
    {
        try
        {
            // 导入库建档的档案补全即视为"已审批在保"：status=Approved + current_step=6（已归档完结）。
            // 导入库本就是已在享对象的历史档案，补全只是把导入数据补到当前库，不存在二次审批。
            // first_approved_at = 纳入时间（ImportedArchiveService 写入 created_at，取自
            // 导入库 first_receive_month；edge/rigid 库无纳入时间则取建档时刻），
            // 不用 NOW() —— 否则存量导入户会被月报"新增救助"口径计成当期新增。
            // COALESCE 保持只写一次语义（与 ApproveAsync/CompleteArchiveAsync 一致）。
            var sql = $@"UPDATE nc_biz_applications SET
                data_completed_at = NOW(),
                status = CASE WHEN status = ANY($3) THEN status ELSE $4 END,
                current_step = $2,
                first_approved_at = COALESCE(first_approved_at, created_at, NOW()),
                updated_at = NOW()
                WHERE id = $1 AND deleted_at IS NULL";
            var result = await _db.ExecuteNonQueryAsync(sql, ct, id, WorkflowSteps.ARCHIVED,
                new List<string> { ApplicationStatusCodes.APPROVED, ApplicationStatusCodes.COMPLETED },
                ApplicationStatusCodes.APPROVED);
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    result.Message ?? "标记数据补全完成失败");

            LogInfo($"标记数据补全完成: ApplicationId={id}");
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "标记数据补全完成失败");
            return Result.FromException(ex);
        }
    }

    public async Task<Result> DeleteAsync(long id, CancellationToken ct = default)
    {
        return await DeleteAsync(id, "System", ct);
    }

    public async Task<Result> DeleteAsync(long id, string deletedBy, CancellationToken ct = default)
    {
        LogInfo($"执行操作");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            var appResult = await GetByIdAsync(id, ct);
            if (appResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure(appResult.ErrorCode!, appResult.Message!);
            }

            var app = appResult.Value;
            if (app == null)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");
            }

            if (!ApplicationStateMachine.IsEditable(ApplicationStatusExtensions.FromCode(app.Status)))
            {
                await tx.RollbackAsync(ct);
                return Result.Failure(ErrorCodes.INVALID_TRANSITION, "当前状态不允许删除");
            }

            var sql = "UPDATE nc_biz_applications SET deleted_at = NOW(), updated_by = $1 WHERE id = $2";
            var deleteResult = await _db.ExecuteNonQueryAsync(sql, ct, deletedBy, id);

            if (deleteResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure(deleteResult.ErrorCode!, deleteResult.Message!);
            }

            await tx.CommitAsync(ct);

            LogInfo($"执行操作");
            return Result.Success();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "删除申请失败");
            return Result.FromException(ex);
        }
    }

    public async Task<Result> SubmitAsync(ApplicationSubmitRequest request, CancellationToken ct = default)
    {
        ValidateNotNull(request, nameof(request));

        LogInfo($"执行操作");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            var appResult = await GetByIdAsync(request.ApplicationId, ct);
            if (appResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure(appResult.ErrorCode!, appResult.Message!);
            }

            var app = appResult.Value;
            if (app == null)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");
            }

            var currentStatus = ApplicationStatusExtensions.FromCode(app.Status);
            var validateResult = ApplicationStateMachine.ValidateTransition(currentStatus, ApplicationStatus.Submitted);
            if (validateResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure(ErrorCodes.INVALID_TRANSITION, "当前状态不允许提交");
            }

            // 状态写入统一走 ApplicationStatusService（§9 状态机单一权威 + §8 审计留痕）
            var submitResult = await _statusService.SubmitAsync(request.ApplicationId, request.SubmittedBy, ct);

            if (submitResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure(submitResult.ErrorCode!, submitResult.Message!);
            }

            await tx.CommitAsync(ct);

            LogInfo($"执行操作");
            return Result.Success();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "提交申请失败");
            return Result.FromException(ex);
        }
    }

    public async Task<Result> SubmitAsync(long id, string submittedBy, CancellationToken ct = default)
    {
        var request = new ApplicationSubmitRequest
        {
            ApplicationId = id,
            SubmittedBy = submittedBy
        };
        return await SubmitAsync(request, ct);
    }

    public async Task<Result> ApproveAsync(ApplicationApproveRequest request, CancellationToken ct = default)
    {
        ValidateNotNull(request, nameof(request));

        LogInfo($"审批申请: {request.ApplicationId}, 结果: {(request.Approved ? "通过" : "拒绝")}");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            var appResult = await GetByIdAsync(request.ApplicationId, ct);
            if (appResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure(appResult.ErrorCode!, appResult.Message!);
            }

            var app = appResult.Value;
            if (app == null)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");
            }

            var currentStatus = ApplicationStatusExtensions.FromCode(app.Status);
            var targetStatus = request.Approved ? ApplicationStatus.Approved : ApplicationStatus.Refused;
            var validateResult = ApplicationStateMachine.ValidateTransition(currentStatus, targetStatus);
            if (validateResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure(ErrorCodes.INVALID_TRANSITION, "当前状态不允许审批");
            }

            // 状态写入统一走 ApplicationStatusService（§9 状态机单一权威 + §8 审计留痕）
            var approveResult = request.Approved
                ? await _statusService.ApproveAsync(request.ApplicationId, request.ApprovedBy, ct)
                : await _statusService.RefuseAsync(request.ApplicationId, request.ApprovedBy, ct);

            if (approveResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure(approveResult.ErrorCode!, approveResult.Message!);
            }

            // 审批通过成为保障对象 → 联动升级资产核查状态（同事务，失败不阻断）
            if (request.Approved)
                await TryMarkAssetChecksIncludedAsync((app.Id, app.ClassificationResult, app.ApplicantIdCard), ct);

            await tx.CommitAsync(ct);

            LogInfo($"执行操作");
            return Result.Success();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "审批申请失败");
            return Result.FromException(ex);
        }
    }

    public async Task<Result> UpdateEconomicInfoAsync(EconomicInfoUpdateRequest request, CancellationToken ct = default)
    {
        ValidateNotNull(request, nameof(request));

        LogInfo($"更新Application收入汇总: ApplicationId={request.ApplicationId}");

        // 【年值基准·财政会计口径】由表单 RecalculateIncome 统一计算并传入年值权威口径，
        // 此处仅持久化，不重复计算（消除双实现导致的明细与汇总不一致）。
        // 月值 = 年值÷12（分解显示口径，一次舍入），禁止从月值反推年值。
        var totalMonthly = Math.Round(request.TotalAnnualIncome / 12m, 2);
        var perCapitaMonthly = request.FamilySize > 0
            ? Math.Round(request.TotalAnnualIncome / request.FamilySize / 12m, 2)
            : 0;

        var sql = $@"UPDATE nc_biz_applications SET 
            work_income_total = $1, business_income_total = $2, property_income_total = $3,
            transfer_income_total = $4, other_income_total = $5, alimony_income = $6,
            rigid_expenditure = $7, total_family_income = $8, per_capita_income = $9,
            land_income_total = $10, subsidy_total = $11,
            total_annual_income = $12, per_capita_annual_income = $13,
            updated_by = $14, updated_at = NOW()
            WHERE id = $15";

        var updateResult = await _db.ExecuteNonQueryAsync(sql, ct,
            request.WageIncome, request.BusinessIncome, request.PropertyIncome,
            request.TransferIncome, request.OtherIncome, request.SupportIncome,
            request.RigidExpenditure, totalMonthly, perCapitaMonthly,
            request.LandIncome, request.SubsidyIncome,
            request.TotalAnnualIncome, request.PerCapitaAnnualIncome,
            request.UpdatedBy, request.ApplicationId);

        if (updateResult.IsFailure)
            return Result.Failure(updateResult.ErrorCode!, updateResult.Message!);

        LogInfo($"Application收入汇总更新成功: TotalAnnual={request.TotalAnnualIncome}, PerCapitaAnnual={request.PerCapitaAnnualIncome}");
        return Result.Success();
    }

    public async Task<Result<string>> GetNextApplicationNoAsync(CancellationToken ct = default)
    {
        return await GetNextApplicationNoAsync(DateTime.Now, ct);
    }

    public async Task<Result<string>> GetNextApplicationNoAsync(DateTime date, CancellationToken ct = default)
    {
        var prefix = ApplicationNoHelper.BuildPrefix(date);

        var sql = @"SELECT COALESCE(MAX(CAST(SUBSTRING(application_no FROM 11) AS INTEGER)), 0) + 1
                    FROM nc_biz_applications 
                    WHERE application_no LIKE $1";

        var result = await _db.ExecuteScalarAsync(sql, ct, $"{prefix}%");
        if (result.IsFailure)
            return Result.Failure<string>(result.ErrorCode!, result.Message!);

        var seq = result.Value;
        var appNo = ApplicationNoHelper.BuildNo(date, (int)seq);
        return Result.Success(appNo);
    }

    public async Task<Result<ChangeApplicationDateResult>> ChangeApplicationDateAsync(long applicationId, DateTime newDate, string operatorName, CancellationToken ct = default)
    {
        LogInfo($"修改申请日期: Id={applicationId}, NewDate={newDate:yyyy-MM-dd}");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);
        try
        {
            // 读档（GetByIdAsync 已过滤 deleted_at IS NULL）
            var appResult = await GetByIdAsync(applicationId, ct);
            if (appResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeApplicationDateResult>(appResult.ErrorCode!, appResult.Message!);
            }

            var app = appResult.Value;
            if (app == null)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeApplicationDateResult>(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");
            }

            // 状态闸：仅草稿 Tab（status=Draft 且 step<5，与工作流草稿 Tab 同口径；
            // 已建档 step=5 需先退回草稿，已完结不开放——防止已出文书与库内批次打架）
            var isDraftTab = string.Equals(app.Status, ApplicationStatusCodes.DRAFT, StringComparison.Ordinal)
                             && app.CurrentStep < WorkflowSteps.PENDING_ARCHIVE;
            if (!isDraftTab)
            {
                await tx.RollbackAsync(ct);
                var message = string.Equals(app.Status, ApplicationStatusCodes.DRAFT, StringComparison.Ordinal)
                    ? "已建档档案不可修改申请日期，请先退回草稿"
                    : "当前状态不允许修改申请日期（仅草稿可改）";
                return Result.Failure<ChangeApplicationDateResult>(ErrorCodes.INVALID_TRANSITION, message);
            }

            // 范围闸：≤ 今天 且 不早于当年 1 月 1 日（跨月允许——跨月正是改期的目的；未来日/往年禁止）
            var today = DateTime.Today;
            var newDay = newDate.Date;
            if (newDay > today)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeApplicationDateResult>(ErrorCodes.VALIDATION_FAILED,
                    $"申请日期 {newDay:yyyy-MM-dd} 不能晚于今天 {today:yyyy-MM-dd}");
            }
            if (newDay < new DateTime(today.Year, 1, 1))
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeApplicationDateResult>(ErrorCodes.VALIDATION_FAILED,
                    $"申请日期不能早于 {today.Year}-01-01");
            }

            var oldNo = app.ApplicationNo;
            var oldDay = ApplicationNoHelper.TryParseDate(oldNo) ?? app.CreatedAt.Date;
            if (newDay == oldDay)
            {
                await tx.CommitAsync(ct);
                return Result.Success(new ChangeApplicationDateResult(oldNo, null));
            }

            // 目标日取号：前缀内 MAX+1（同 GetNextApplicationNoAsync 算法），排除自身防自撞；
            // application_no 有全表唯一索引，并发同日取号仍可能撞 → 下方给出明确提示供重试
            var prefix = ApplicationNoHelper.BuildPrefix(newDay);
            var seqSql = @"SELECT COALESCE(MAX(CAST(SUBSTRING(application_no FROM 11) AS INTEGER)), 0) + 1
                           FROM nc_biz_applications
                           WHERE application_no LIKE $1 AND application_no <> $2";
            var seqResult = await _db.ExecuteScalarAsync(seqSql, ct, $"{prefix}%", oldNo);
            if (seqResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeApplicationDateResult>(seqResult.ErrorCode!, seqResult.Message!);
            }

            var newNo = ApplicationNoHelper.BuildNo(newDay, (int)seqResult.Value);

            // 同改两列：编号日期段（锚点第一优先级）+ created_at（列表"申请添加时间"/排序）；
            // created_at 仅换日期、保留原时分秒；application_no 作并发守卫（防同档并发改期互相覆盖）
            var newCreatedAt = newDay + app.CreatedAt.TimeOfDay;
            var updateSql = @"UPDATE nc_biz_applications
                              SET application_no = $1, created_at = $2, updated_at = NOW(), updated_by = $3
                              WHERE id = $4 AND application_no = $5 AND deleted_at IS NULL";
            var updateResult = await _db.ExecuteNonQueryAsync(updateSql, ct,
                newNo, newCreatedAt, operatorName, applicationId, oldNo);

            if (updateResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                if (IsUniqueViolation(updateResult))
                    return Result.Failure<ChangeApplicationDateResult>(ErrorCodes.DB_UNIQUE_VIOLATION,
                        "申请编号撞号（同日序号被并发占用），请重试");
                return Result.Failure<ChangeApplicationDateResult>(updateResult.ErrorCode!, updateResult.Message!);
            }

            if (updateResult.Value == 0)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeApplicationDateResult>(ErrorCodes.CONCURRENCY_CONFLICT,
                    "该档案正在被其他操作修改，请刷新后重试");
            }

            // ── 入户调查日期联动（2026-11-11）────────────────────────────────
            // 批次锚点 = 入户日期优先：只改申请日期、入户日期不动 → 批次/顺延提示不变（实测反馈）。
            // 同事务把入户日期钳进窗口 [新申请日, min(公示前一工作日(=会议日), 今天)]——
            // 即 Step4 三重校验（≥申请日 / ≤今天 / ≤会议日）的合法区间，钳完必然可保存。
            // 例外：导入库建档档（source_type=ImportedArchive）调查日=纳入时间铁律（Step4 锁定且校验豁免），不动；
            //       该档无入户调查记录时无可钳对象，跳过。
            DateTime? clampedSurveyDate = null;
            if (!string.Equals(app.SourceType, ChainTypeConstants.IMPORTED_ARCHIVE, StringComparison.OrdinalIgnoreCase))
            {
                var surveyRowSql = @"SELECT survey_date FROM nc_biz_household_surveys
                                     WHERE application_id = $1 AND deleted_at IS NULL
                                     ORDER BY id DESC LIMIT 1";
                var surveyRows = await _db.QueryAsync<SurveyDateRow>(surveyRowSql, ct, applicationId);
                if (surveyRows.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<ChangeApplicationDateResult>(surveyRows.ErrorCode!, surveyRows.Message!);
                }

                var currentSurvey = (surveyRows.Value ?? new List<SurveyDateRow>()).FirstOrDefault()?.SurveyDate;
                if (currentSurvey.HasValue)
                {
                    var bLine = _timelineService.GetTimelineForApplicationDate(newDay, TimelineType.BusinessProcess);
                    var upper = _timelineService.GetPrePublicityWorkday(bLine);
                    if (upper > today) upper = today;
                    if (upper < newDay) upper = newDay;   // 防御：窗口倒挂钳回申请日（理论不发生）

                    var targetSurvey = BatchDeferralHelper.ClampSurveyDate(currentSurvey.Value.Date, newDay, upper);
                    if (targetSurvey != currentSurvey.Value.Date)
                    {
                        var surveyUpdateSql = @"UPDATE nc_biz_household_surveys
                                                SET survey_date = $1, updated_at = NOW()
                                                WHERE application_id = $2 AND deleted_at IS NULL";
                        var surveyUpdate = await _db.ExecuteNonQueryAsync(surveyUpdateSql, ct,
                            targetSurvey, applicationId);
                        if (surveyUpdate.IsFailure)
                        {
                            await tx.RollbackAsync(ct);
                            return Result.Failure<ChangeApplicationDateResult>(surveyUpdate.ErrorCode!, surveyUpdate.Message!);
                        }
                        LogInfo($"改期联动调整入户调查日期: Id={applicationId}, {currentSurvey.Value.Date:yyyy-MM-dd} → {targetSurvey:yyyy-MM-dd}");
                        clampedSurveyDate = targetSurvey;   // 仅在实际改动时返回，供 UI 回写与提示
                    }
                }
            }

            // 审计留痕（列口径同 ApplicationStatusService.WriteAuditAsync；失败不阻断，同其语义）
            var auditSql = @"INSERT INTO nc_biz_application_logs
                                  (application_id, action, action_detail, old_value, new_value, operator_name, operated_at)
                             VALUES ($1, $2, $3, $4, $5, $6, NOW())";
            var auditDetail = $"申请日期 {oldDay:yyyy-MM-dd} → {newDay:yyyy-MM-dd}（编号日期段与创建时间同改，公示/卷号/批次随锚点重算）"
                + (clampedSurveyDate.HasValue
                    ? $"；入户调查日期同步为 {clampedSurveyDate:yyyy-MM-dd}（窗口 [新申请日, min(会议日, 今天)]）"
                    : string.Empty);
            var auditResult = await _db.ExecuteNonQueryAsync(auditSql, ct,
                applicationId, "修改申请日期", auditDetail,
                $"{oldNo} / {oldDay:yyyy-MM-dd}", $"{newNo} / {newDay:yyyy-MM-dd}",
                string.IsNullOrEmpty(operatorName) ? "System" : operatorName);
            if (auditResult.IsFailure)
                LogWarn($"改期审计写入失败: ApplicationId={applicationId}, {auditResult.Message}");

            await tx.CommitAsync(ct);
            LogInfo($"申请日期修改成功: Id={applicationId}, {oldNo} → {newNo}");
            return Result.Success(new ChangeApplicationDateResult(newNo, clampedSurveyDate));
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "修改申请日期失败");
            return Result.FromException<ChangeApplicationDateResult>(ex);
        }
    }

    /// <summary>入户调查日期批查投影（仅取 survey_date 一列，供改期联动钳制）</summary>
    private sealed class SurveyDateRow
    {
        public DateTime? SurveyDate { get; set; }
    }

    /// <summary>
    /// 判断写入失败是否由 UNIQUE 约束冲突引起（口径同 ArchiveService.IsUniqueViolation：
    /// 错误码映射不总可靠，兼看消息文本）
    /// </summary>
    private static bool IsUniqueViolation(Result result)
    {
        if (result.ErrorCode == ErrorCodes.DB_UNIQUE_VIOLATION) return true;
        var msg = result.Message ?? string.Empty;
        return msg.Contains("23505") || msg.Contains("duplicate key", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<Result<bool>> CheckIdCardExistsAsync(string idCard, long? excludeId = null, CancellationToken ct = default)
    {
        ValidateNotNullOrEmpty(idCard, nameof(idCard));

        string sql;
        object[] parameters;

        if (excludeId.HasValue)
        {
            sql = "SELECT COUNT(*) FROM nc_biz_applications WHERE applicant_id_card = $1 AND id != $2 AND deleted_at IS null";
            parameters = new object[] { idCard, excludeId.Value };
        }
        else
        {
            sql = "SELECT COUNT(*) FROM nc_biz_applications WHERE applicant_id_card = $1 AND deleted_at IS null";
            parameters = new object[] { idCard };
        }

        var result = await _db.ExecuteScalarAsync(sql, ct, parameters);
        if (result.IsFailure)
            return Result.Failure<bool>(result.ErrorCode!, result.Message!);

        return Result.Success(result.Value > 0);
    }

    public async Task<Result<PagedResult<ApplicationEntity>>> GetArchiveBuiltNotSubmittedPagedAsync(string keyword, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        LogInfo("获取已完成档案建设但未提交的申请（分页）: keywordLength=" + keyword.Length + ", 第" + pageIndex + "页");

        // 2026-11 收敛：三份手抄 COUNT+LIMIT 分页（草稿/已建档/已完结）统一走 PagedQueryHelper.RunAsync
        //（规范推荐模式，此前零调用方=死规范）；行为与原手抄版逐语句等价。
        var conditions = new SqlConditionBuilder()
            .Add("status = {0}", ApplicationStatusCodes.DRAFT)
            .Add("current_step = {0}", WorkflowSteps.PENDING_ARCHIVE)
            .Add("deleted_at IS NULL")
            // 18 位身份证走精确匹配（命中 idx_applications_applicant_id_card 索引）；
            // 其余关键词保持 ILIKE 姓名模糊（前后通配本就无索引可用）
            .AddIf(!string.IsNullOrWhiteSpace(keyword) && keyword.Length == 18,
                "(applicant_name ILIKE {0} OR applicant_id_card = {1})", "%" + keyword + "%", keyword.Trim())
            .AddIf(!string.IsNullOrWhiteSpace(keyword) && keyword.Length != 18,
                "(applicant_name ILIKE {0} OR applicant_id_card ILIKE {0})", "%" + keyword + "%");

        return await PagedQueryHelper.RunAsync<ApplicationEntity>(
            _db,
            "SELECT COUNT(*) FROM nc_biz_applications{WHERE}",
            "SELECT " + ApplicationColumns.List + " FROM nc_biz_applications{WHERE} ORDER BY updated_at DESC{PAGE}",
            conditions, pageIndex, pageSize, ct);
    }

    public async Task<Result<int>> GetArchiveBuiltNotSubmittedCountAsync(CancellationToken ct = default)
    {
        var sql = "SELECT COUNT(*) FROM nc_biz_applications WHERE status = $1 AND current_step = $2 AND deleted_at IS NULL";
        var result = await _db.ExecuteScalarAsync(sql, ct, ApplicationStatusCodes.DRAFT, WorkflowSteps.PENDING_ARCHIVE);
        if (result.IsFailure)
            return Result.Failure<int>(result.ErrorCode!, result.Message!);
        return Result.Success(Convert.ToInt32(result.Value));
    }

    public async Task<Result<PagedResult<ApplicationEntity>>> GetArchivedPagedAsync(string keyword, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        LogInfo("获取已完结档案（分页）: keywordLength=" + keyword.Length + ", 第" + pageIndex + "页");

        // 「已完结档案」= 已归档（current_step=6）且已出草稿态：
        // 加 status 过滤防止 Draft/Refused 的异常行（如补全模式遗留的 Draft+step6）被当作"已完结"显示
        // 2026-11 收敛：分页走 PagedQueryHelper.RunAsync（与草稿/已建档两方法同模式）
        var conditions = new SqlConditionBuilder()
            .Add("current_step = {0}", WorkflowSteps.ARCHIVED)
            .Add("status = ANY({0})", ArchivedStatusFilter)
            .Add("deleted_at IS NULL")
            .AddIf(!string.IsNullOrWhiteSpace(keyword) && keyword.Length == 18,
                "(applicant_name ILIKE {0} OR applicant_id_card = {1})", "%" + keyword + "%", keyword.Trim())
            .AddIf(!string.IsNullOrWhiteSpace(keyword) && keyword.Length != 18,
                "(applicant_name ILIKE {0} OR applicant_id_card ILIKE {0})", "%" + keyword + "%");

        return await PagedQueryHelper.RunAsync<ApplicationEntity>(
            _db,
            "SELECT COUNT(*) FROM nc_biz_applications{WHERE}",
            "SELECT " + ApplicationColumns.List + " FROM nc_biz_applications{WHERE} ORDER BY updated_at DESC{PAGE}",
            conditions, pageIndex, pageSize, ct);
    }

    public async Task<Result<int>> GetArchivedCountAsync(CancellationToken ct = default)
    {
        // 与 GetArchivedPagedAsync 同口径：已归档（step=6）且已出草稿态
        var sql = @"SELECT COUNT(*) FROM nc_biz_applications
            WHERE current_step = $1 AND status = ANY($2) AND deleted_at IS NULL";
        var result = await _db.ExecuteScalarAsync(sql, ct, WorkflowSteps.ARCHIVED, ArchivedStatusFilter);
        if (result.IsFailure)
            return Result.Failure<int>(result.ErrorCode!, result.Message!);
        return Result.Success(Convert.ToInt32(result.Value));
    }

    /// <inheritdoc />
    public async Task<Result> CompleteArchiveAsync(long applicationId, string operatorName, CancellationToken ct = default)
    {
        // 归档 = 审批通过 + 已归档完结：current_step 推进到 6 且 status 置 Approved。
        // 状态写入统一走 ApplicationStatusService（§9 状态机单一权威 + §8 审计留痕），
        // 状态机校验、first_approved_at 只写一次、已 Approved/Completed 仅推进步骤均在该服务内处理。
        // operatorName 透传给状态服务写 nc_biz_application_logs.operator_name——
        // 原为硬编码 "System"，致「完成归档」审计日志操作人全部不可追溯。
        var result = await _statusService.CompleteArchiveAsync(applicationId, operatorName, ct);
        if (result.IsFailure)
            return result;

        // 归档即审批通过 → 联动升级资产核查状态（失败不阻断）
        // 窄列重读（原 GetByIdAsync 取 95 列全行，此处仅消费 3 字段——宽表 IO 减 ~90%）
        var markLookup = await _db.QuerySingleAsync<(long Id, string? ClassificationResult, string? ApplicantIdCard)>(
            "SELECT id, classification_result, applicant_id_card FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL",
            ct, applicationId);
        if (markLookup.IsSuccess)
            await TryMarkAssetChecksIncludedAsync(markLookup.Value, ct);

        // 归档联动：为年满80周岁的户主/共同生活成员自动创建普惠高龄草稿（失败不阻断归档主流程）
        try
        {
            var elderlyResult = await _elderlyApplicationService.CreateDraftsForArchivedApplicationAsync(applicationId, ct);
            if (elderlyResult.IsSuccess && elderlyResult.Value > 0)
                LogInfo($"归档联动创建普惠高龄草稿完成: ApplicationId={applicationId}, DraftCount={elderlyResult.Value}");
            else if (elderlyResult.IsFailure)
                LogWarn($"普惠高龄草稿联动失败（不阻断）: ApplicationId={applicationId}, {elderlyResult.Message}");
        }
        catch (Exception ex)
        {
            LogException(ex, "普惠高龄草稿归档联动异常");
        }

        // 归档联动：对已建高龄档案/在册发放成员写入"待复核"队列（身份变化重评类别，失败不阻断）
        try
        {
            var reviewResult = await _elderlyApplicationService.TriggerReviewsForHouseholdAsync(
                applicationId, NewCosmos.Constants.ElderlyBenefitConstants.ReviewTriggerArchive, ct);
            if (reviewResult.IsSuccess && reviewResult.Value > 0)
                LogInfo($"归档联动写入高龄待复核完成: ApplicationId={applicationId}, Count={reviewResult.Value}");
            else if (reviewResult.IsFailure)
                LogWarn($"高龄待复核联动失败（不阻断）: ApplicationId={applicationId}, {reviewResult.Message}");
        }
        catch (Exception ex)
        {
            LogException(ex, "高龄待复核归档联动异常");
        }

        LogInfo($"完成归档（置已审批）: ApplicationId={applicationId}");
        return Result.Success();
    }

    public async Task<Result<PagedResult<ApplicationEntity>>> GetDraftPagedAsync(string keyword, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        LogInfo("获取草稿申请（分页）: keywordLength=" + keyword.Length + ", 第" + pageIndex + "页");

        // 2026-11 收敛：分页走 PagedQueryHelper.RunAsync（与已建档/已完结两方法同模式）
        var conditions = new SqlConditionBuilder()
            .Add("status = {0}", ApplicationStatusCodes.DRAFT)
            .Add("current_step < {0}", WorkflowSteps.PENDING_ARCHIVE)
            .Add("deleted_at IS NULL")
            .AddIf(!string.IsNullOrWhiteSpace(keyword) && keyword.Length == 18,
                "(applicant_name ILIKE {0} OR applicant_id_card = {1})", "%" + keyword + "%", keyword.Trim())
            .AddIf(!string.IsNullOrWhiteSpace(keyword) && keyword.Length != 18,
                "(applicant_name ILIKE {0} OR applicant_id_card ILIKE {0})", "%" + keyword + "%");

        return await PagedQueryHelper.RunAsync<ApplicationEntity>(
            _db,
            "SELECT COUNT(*) FROM nc_biz_applications{WHERE}",
            "SELECT " + ApplicationColumns.List + " FROM nc_biz_applications{WHERE} ORDER BY updated_at DESC{PAGE}",
            conditions, pageIndex, pageSize, ct);
    }

    public async Task<Result<int>> GetDraftCountAsync(CancellationToken ct = default)
    {
        var sql = "SELECT COUNT(*) FROM nc_biz_applications WHERE status = $1 AND current_step < $2 AND deleted_at IS NULL";
        var result = await _db.ExecuteScalarAsync(sql, ct, ApplicationStatusCodes.DRAFT, WorkflowSteps.PENDING_ARCHIVE);
        if (result.IsFailure)
            return Result.Failure<int>(result.ErrorCode!, result.Message!);
        return Result.Success(Convert.ToInt32(result.Value));
    }

    public async Task<Result<long>> CreateSingleRescueDraftAsync(long sourceApplicationId, FamilyMember member, CancellationToken ct = default)
    {
        LogInfo($"创建单人保草稿: SourceApplicationId={sourceApplicationId}, Member={DataMasker.MaskName(member.Name)}");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            // 获取源申请
            var sourceResult = await GetByIdAsync(sourceApplicationId, ct);
            if (sourceResult.IsFailure || sourceResult.Value == null)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<long>(ErrorCodes.APPLICATION_NOT_FOUND, "源申请不存在");
            }

            var source = sourceResult.Value;

            // 获取下一个申请编号
            var appNoResult = await GetNextApplicationNoAsync(ct);
            var appNo = appNoResult.IsSuccess ? appNoResult.Value : $"SR-{DateTime.Now:yyyyMMddHHmmss}";

            // ── 计算单人保保障金额 ──
            var isRural = ClassificationConstants.HukouType.IsHukouRural(source.HukouType);
            var classificationCode = isRural
                ? ClassificationConstants.RuralLowIncomeSingle
                : ClassificationConstants.UrbanLowIncomeSingle;

            // 户月保障金额（单人保固定标准）
            var guaranteeResult = await _classificationService.CalculateGuaranteeAmountAsync(
                classificationCode, familySize: 1, isRural,
                totalFamilyIncome: source.TotalFamilyIncome, ct);
            var householdMonthly = guaranteeResult.IsSuccess ? guaranteeResult.Value : 0m;

            // 分类施保（重病/重残/高龄/未成年）
            var subsidyResult = await _classificationService.CalculateClassifiedSubsidyAsync(
                isRural, source, new List<FamilyMember> { member }, ct);

            // 保障金总额
            var totalGuarantee = _guaranteeAmountService.CalculateTotalGuaranteeAmount(
                householdMonthly, subsidyResult.TotalAmount, caregiverSubsidyAmount: 0m);

            // 创建新申请（单人保）- 设置 is_single_rescue = true
            var insertSql = @"INSERT INTO nc_biz_applications 
                (application_no, applicant_name, applicant_id_card, applicant_phone,
                 gender, ethnicity, marital_status, hukou_type, education_level, political_status,
                 hukou_address, disability_card_no,
                 province, city, district, town, community, address,
                 health_status, disease_name, disability_type, disability_level,
                 family_size, is_eligible, is_single_rescue, status, current_step, original_application_id, chain_type,
                 work_income_total, business_income_total, property_income_total, transfer_income_total,
                 other_income_total, total_family_income, per_capita_income, rigid_expenditure, alimony_income,
                 classification_result, classified_subsidy_type, classified_subsidy_amount,
                 household_monthly_guarantee_amount, total_guarantee_amount,
                 family_land_area, self_farmed_land_area, subleased_land_area, contracted_land_area,
                 land_income_total, subsidy_total,
                 created_by, created_at, updated_at)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,$20,$21,$22,$23,$24,$25,$26,$27,$28,'SingleRescue',$29,$30,$31,$32,$33,$34,$35,$36,$37,$38,$39,$40,$41,$42,$43,$44,$45,$46,$47,$48,$49, NOW(), NOW())
                RETURNING id";

            var insertResult = await _db.ExecuteScalarAsync(insertSql, ct,
                appNo, member.Name, member.IdCard, member.Phone ?? "",
                member.Gender ?? "", member.Ethnicity ?? "", member.MaritalStatus ?? "",
                source.HukouType, member.EducationLevel ?? "", member.PoliticalStatus ?? "",
                source.HukouAddress, "",
                source.Province, source.City, source.District, source.Town, source.Community, source.Address,
                member.HealthStatus ?? "", member.DiseaseName ?? "", member.DisabilityType ?? "", member.DisabilityLevel ?? "",
                1, true, true, "Draft", 1, sourceApplicationId,
                source.WorkIncomeTotal, source.BusinessIncomeTotal, source.PropertyIncomeTotal, source.TransferIncomeTotal,
                source.OtherIncomeTotal, source.TotalFamilyIncome, source.PerCapitaIncome, source.RigidExpenditure, source.AlimonyIncome,
                classificationCode, subsidyResult.Types, subsidyResult.TotalAmount,
                householdMonthly, totalGuarantee,
                source.FamilyLandArea, source.SelfFarmedLandArea, source.SubleasedLandArea, source.ContractedLandArea,
                source.LandIncomeTotal, source.SubsidyTotal,
                "System");

            if (insertResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<long>(insertResult.ErrorCode!, insertResult.Message!);
            }

            var newAppId = insertResult.Value;

            // 创建户主成员（即该单人保成员）
            var memberInsertSql = @"INSERT INTO nc_biz_family_members 
                (application_id, name, id_card, is_applicant, gender, birth_date, age, ethnicity, phone,
                 hukou_type, hukou_address,
                 marital_status, education_level, political_status, health_status, work_capacity,
                 relationship_to_head, member_category,
                 is_disabled, disability_type, disability_level, is_severe_disability, disability_certificate_no,
                 disease_category, disease_name, secondary_disease, disease_code, is_severe_disease, is_labor_exempt,
                 home_province, home_city, home_district, home_town, home_village, home_address,
                 hukou_province, hukou_city, hukou_district, hukou_town,
                 employment_status, work_unit, main_income_source, annual_income,
                 annual_support_fee, is_support_ability, monthly_support_fee, support_months, family_size,
                 person_type, monthly_income_capacity, medical_self_paid_12m,
                 created_at, updated_at)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,$20,$21,$22,$23,$24,$25,$26,$27,$28,$29,$30,$31,$32,$33,$34,$35,$36,$37,$38,$39,$40,$41,$42,$43,$44,$45,$46,$47,$48,$49,$50,$51,NOW(),NOW())";

            // 户主成员插入失败必须让整个单人保创建失败回滚（此前结果被丢弃，产生无成员的孤儿申请）
            await ExecOrThrowAsync(_db, memberInsertSql, ct,
                newAppId, member.Name, member.IdCard, true,
                member.Gender ?? "", member.BirthDate, member.Age, member.Ethnicity ?? "", member.Phone ?? "",
                source.HukouType ?? "", source.HukouAddress ?? "",
                member.MaritalStatus ?? "", member.EducationLevel ?? "", member.PoliticalStatus ?? "", member.HealthStatus ?? "", member.WorkCapacity ?? "",
                "本人/户主", "SharedLiving",
                member.IsDisabled, member.DisabilityType ?? "", member.DisabilityLevel ?? "", member.IsSevereDisability, member.DisabilityCertificateNo ?? "",
                member.DiseaseCategory ?? "", member.DiseaseName ?? "", member.SecondaryDisease ?? "", member.DiseaseCode ?? "", member.IsSevereDisease, member.IsLaborExempt,
                member.HomeProvince ?? "", member.HomeCity ?? "", member.HomeDistrict ?? "", member.HomeTown ?? "", member.HomeVillage ?? "", member.HomeAddress ?? "",
                member.HukouProvince ?? "", member.HukouCity ?? "", member.HukouDistrict ?? "", member.HukouTown ?? "",
                member.EmploymentStatus ?? "", member.WorkUnit ?? "", member.MainIncomeSource ?? "", member.AnnualIncome,
                member.AnnualSupportFee, member.IsSupportAbility, member.MonthlySupportFee, member.SupportMonths, member.FamilySize,
                member.PersonType ?? "", member.MonthlyIncomeCapacity,
                (object?)member.MedicalSelfPaid12m);

            // 复制经济信息（务工收入、经营收入等）
            await CopyEconomicDataAsync(sourceApplicationId, newAppId, ct);

            await tx.CommitAsync(ct);

            LogInfo($"单人保草稿创建成功: NewApplicationId={newAppId}");
            return Result.Success(newAppId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "创建单人保草稿失败");
            return Result.FromException<long>(ex);
        }
    }

    /// <summary>
    /// 复制经济数据到新申请。
    /// 仅在 CreateSingleRescueDraftAsync 的事务内调用：任一 INSERT 失败即抛异常，
    /// 由调用方回滚整个事务（此前 11 个 INSERT 的结果全部被丢弃，失败会产生数据不完整的草稿）。
    /// </summary>
    private async Task CopyEconomicDataAsync(long sourceAppId, long targetAppId, CancellationToken ct)
    {
        // 复制务工收入
        var laborSql = @"INSERT INTO nc_biz_labor_incomes 
            (application_id, member_name, member_id_card, member_age, income_sub_type, work_unit, monthly_income, months_worked, annual_income, created_at)
            SELECT $1, member_name, member_id_card, member_age, income_sub_type, work_unit, monthly_income, months_worked, annual_income, NOW()
            FROM nc_biz_labor_incomes WHERE application_id = $2 AND deleted_at IS NULL";
        await ExecOrThrowAsync(_db, laborSql, ct, targetAppId, sourceAppId);

        // 复制经营收入
        var businessSql = @"INSERT INTO nc_biz_business_incomes 
            (application_id, member_name, member_id_card, member_age, company_name, vendor_type, monthly_income, created_at)
            SELECT $1, member_name, member_id_card, member_age, company_name, vendor_type, monthly_income, NOW()
            FROM nc_biz_business_incomes WHERE application_id = $2 AND deleted_at IS NULL";
        await ExecOrThrowAsync(_db, businessSql, ct, targetAppId, sourceAppId);

        // 复制财产收入
        var propertyIncomeSql = @"INSERT INTO nc_biz_property_incomes 
            (application_id, member_name, member_id_card, member_age, income_type, property_description, amount, created_at)
            SELECT $1, member_name, member_id_card, member_age, income_type, property_description, amount, NOW()
            FROM nc_biz_property_incomes WHERE application_id = $2 AND deleted_at IS NULL";
        await ExecOrThrowAsync(_db, propertyIncomeSql, ct, targetAppId, sourceAppId);

        // 复制转移收入（养老金、残疾人补贴等）
        var transferSql = @"INSERT INTO nc_biz_transfer_incomes 
            (application_id, member_name, member_id_card, member_age, income_type, monthly_amount, months_or_times, total_amount, created_at)
            SELECT $1, member_name, member_id_card, member_age, income_type, monthly_amount, months_or_times, total_amount, NOW()
            FROM nc_biz_transfer_incomes WHERE application_id = $2 AND deleted_at IS NULL";
        await ExecOrThrowAsync(_db, transferSql, ct, targetAppId, sourceAppId);

        // 复制其他收入
        var otherSql = @"INSERT INTO nc_biz_other_incomes 
            (application_id, member_name, member_id_card, member_age, income_type, amount, created_at)
            SELECT $1, member_name, member_id_card, member_age, income_type, amount, NOW()
            FROM nc_biz_other_incomes WHERE application_id = $2 AND deleted_at IS NULL";
        await ExecOrThrowAsync(_db, otherSql, ct, targetAppId, sourceAppId);

        // 复制刚性支出
        var rigidSql = @"INSERT INTO nc_biz_rigid_expenditures 
            (application_id, person_description, expenditure_type, amount, created_at)
            SELECT $1, person_description, expenditure_type, amount, NOW()
            FROM nc_biz_rigid_expenditures WHERE application_id = $2 AND deleted_at IS NULL";
        await ExecOrThrowAsync(_db, rigidSql, ct, targetAppId, sourceAppId);

        // 复制补贴
        var subsidySql = @"INSERT INTO nc_biz_subsidies 
            (application_id, subsidy_type, area, unit_price, count, ratio_factor, original_amount, amount, member_name, member_id_card, created_at)
            SELECT $1, subsidy_type, area, unit_price, count, ratio_factor, original_amount, amount, member_name, member_id_card, NOW()
            FROM nc_biz_subsidies WHERE application_id = $2 AND deleted_at IS NULL";
        await ExecOrThrowAsync(_db, subsidySql, ct, targetAppId, sourceAppId);

        // 复制房产
        var propertySql = @"INSERT INTO nc_biz_properties 
            (application_id, property_type, address, housing_structure, housing_nature, area, room_count, build_year, estimated_value, created_at)
            SELECT $1, property_type, address, housing_structure, housing_nature, area, room_count, build_year, estimated_value, NOW()
            FROM nc_biz_properties WHERE application_id = $2 AND deleted_at IS NULL";
        await ExecOrThrowAsync(_db, propertySql, ct, targetAppId, sourceAppId);

        // 复制车辆
        var vehicleSql = @"INSERT INTO nc_biz_vehicles 
            (application_id, vehicle_type, brand, model, license_plate, purchase_year, purchase_price, estimated_value, created_at)
            SELECT $1, vehicle_type, brand, model, license_plate, purchase_year, purchase_price, estimated_value, NOW()
            FROM nc_biz_vehicles WHERE application_id = $2 AND deleted_at IS NULL";
        await ExecOrThrowAsync(_db, vehicleSql, ct, targetAppId, sourceAppId);

        // 复制金融资产
        var financialSql = @"INSERT INTO nc_biz_financial_assets 
            (application_id, has_cash, cash_amount, has_bank_deposit, bank_deposit_amount, 
             has_securities, securities_amount, has_commercial_insurance, commercial_insurance_type, 
             commercial_insurance_amount, created_at)
            SELECT $1, has_cash, cash_amount, has_bank_deposit, bank_deposit_amount, 
             has_securities, securities_amount, has_commercial_insurance, commercial_insurance_type, 
             commercial_insurance_amount, NOW()
            FROM nc_biz_financial_assets WHERE application_id = $2 AND deleted_at IS NULL";
        await ExecOrThrowAsync(_db, financialSql, ct, targetAppId, sourceAppId);

        // 复制赡养人（从 nc_biz_family_members 复制 member_category='Support' 的记录）
        // 列集与档案制作模板 SUPPORTER_* 字段对齐（性别/年龄/工作单位/收入来源等），避免补打文书索引字段为空
        var supporterSql = @"INSERT INTO nc_biz_family_members
            (application_id, name, id_card, gender, age, ethnicity, phone, marital_status,
             relationship_to_head, health_status, employment_status, work_unit, main_income_source,
             monthly_income_capacity, family_size, annual_support_fee, is_support_ability, person_type,
             member_category, created_at)
            SELECT $1, name, id_card, gender, age, ethnicity, phone, marital_status,
             relationship_to_head, health_status, employment_status, work_unit, main_income_source,
             monthly_income_capacity, family_size, annual_support_fee, is_support_ability, person_type,
             'Support', NOW()
            FROM nc_biz_family_members
            WHERE application_id = $2 AND member_category = 'Support' AND deleted_at IS NULL";
        await ExecOrThrowAsync(_db, supporterSql, ct, targetAppId, sourceAppId);
    }
}
