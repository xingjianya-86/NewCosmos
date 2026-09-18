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

    public ApplicationService(IDatabaseService db, ILoggerService logger, IPdfVerificationService pdfVerificationService,
        IElderlyApplicationService elderlyApplicationService) : base(logger)
    {
        _db = db;
        _pdfVerificationService = pdfVerificationService;
        _elderlyApplicationService = elderlyApplicationService;
    }

    /// <summary>
    /// 建档/审批成为保障对象后，联动升级资产核查状态为"已纳入低收入人口"。
    /// 分类口径与 PdfVerificationService.HasLowIncomeIdentityAsync 一致
    /// （低保/单人/边缘/特困，不含刚性支出）；失败仅记日志不阻断主流程。
    /// 处于环境事务中时自动随事务提交/回滚。
    /// </summary>
    private async Task TryMarkAssetChecksIncludedAsync(ApplicationEntity app, CancellationToken ct)
    {
        try
        {
            var code = app.ClassificationResult;
            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(app.ApplicantIdCard))
                return;

            var isProtectedCategory = ClassificationConstants.IsCodeSubsistence(code)
                || ClassificationConstants.IsCodeLowIncome(code)
                || ClassificationConstants.IsCodeDestitute(code);
            if (!isProtectedCategory)
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
        var sql = "SELECT * FROM nc_biz_applications WHERE id = $1 AND deleted_at IS null";
        return await _db.QuerySingleAsync<ApplicationEntity>(sql, ct, id);
    }

    public async Task<Result<List<ApplicationEntity>>> GetByIdCardAsync(string idCard, CancellationToken ct = default)
    {
        ValidateNotNullOrEmpty(idCard, nameof(idCard));
        LogInfo($"根据身份证查询: {DataMasker.MaskIdCard(idCard)}");
        var sql = "SELECT * FROM nc_biz_applications WHERE applicant_id_card = $1 AND deleted_at IS NULL ORDER BY created_at DESC";
        return await _db.QueryAsync<ApplicationEntity>(sql, ct, idCard);
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
        var querySql = $"SELECT * FROM nc_biz_applications{where}";

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
        var querySql = $"SELECT * FROM nc_biz_applications{where}";

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

    public async Task<Result<PagedResult<ApplicationEntity>>> SearchPagedAsync(string keyword, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        LogInfo($"搜索申请: keyword={keyword}, 第{pageIndex}页");
        return await GetPagedAsync(pageIndex, pageSize, null, keyword, ct);
    }

    public async Task<Result<PagedResult<ApplicationEntity>>> SearchPagedAsync(string? keyword, string? status, string? role, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        LogInfo($"搜索申请: keyword={keyword}, status={status}, role={role}, 第{pageIndex}页");
        return await GetPagedAsync(pageIndex, pageSize, status!, keyword!, ct);
    }

    public async Task<Result<long>> CreateAsync(ApplicationCreateRequest request, CancellationToken ct = default)
    {
        ValidateNotNull(request, nameof(request));
        ValidateNotNullOrEmpty(request.ApplicantName, nameof(request.ApplicantName));
        ValidateNotNullOrEmpty(request.ApplicantIdCard, nameof(request.ApplicantIdCard));

        LogInfo($"创建申请: {DataMasker.MaskName(request.ApplicantName)}");

        var shouldManageTransaction = !_db.HasTransaction;
        if (shouldManageTransaction)
            await _db.BeginTransactionAsync();

        try
        {
            var existsResult = await CheckIdCardExistsAsync(request.ApplicantIdCard, null, ct);
            if (existsResult.IsSuccess && existsResult.Value)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<long>(ErrorCodes.DUPLICATE_ID_CARD, "该身份证号已存在");
            }

            var appNoResult = await GetNextApplicationNoAsync(ct);
            if (appNoResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<long>(appNoResult.ErrorCode!, appNoResult.Message!);
            }

            var sql = $@"INSERT INTO nc_biz_applications 
            (application_no, applicant_name, applicant_id_card, applicant_phone, 
             gender, ethnicity, marital_status, hukou_type, education_level, political_status, hukou_address, disability_card_no,
             province, city, district, town, community, address,
              physical_condition, disease_name, secondary_disease_name, disease_code, is_severe_disease, disability_type, disability_level, health_status,
             employment_status, work_unit, income_source,
             family_size, total_family_income, application_reason, status, created_by, created_at, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, $18, $19, $20, $21, $22, $23, $24, $25, $26, $27, $28, $29, $30, $31, $32, 'Draft', $33, NOW(), NOW())
            RETURNING id";

            var hukouType = request.HukouType ?? "Rural";
            var insertResult = await _db.ExecuteScalarAsync(sql, ct,
                appNoResult.Value, request.ApplicantName, request.ApplicantIdCard, request.ApplicantPhone,
                request.Gender ?? "", request.Ethnicity, request.MaritalStatus, hukouType, request.EducationLevel, request.PoliticalStatus, request.HukouAddress, request.DisabilityCardNo,
                request.Province, request.City, request.District, request.Town, request.Community, request.Address,
                request.PhysicalCondition, request.DiseaseName, request.SecondaryDiseaseName, request.DiseaseCode, request.IsSevereDisease, request.DisabilityType, request.DisabilityLevel, request.HealthStatus,
                request.EmploymentStatus, request.WorkUnit, request.IncomeSource,
                request.FamilySize, request.AnnualIncome, request.ApplicationReason, request.CreatedBy);

            if (insertResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<long>(insertResult.ErrorCode!, insertResult.Message!);
            }

            if (shouldManageTransaction)
                await _db.CommitTransactionAsync();

            LogInfo($"执行操作");
            Logger.LogBusiness("创建申请", ("ApplicationId", insertResult.Value), ("ApplicantName", DataMasker.MaskName(request.ApplicantName)));
            return Result.Success(insertResult.Value);
        }
        catch (Exception ex)
        {
            if (shouldManageTransaction) await _db.RollbackTransactionAsync();
            LogException(ex, "创建申请失败");
            return Result.FromException<long>(ex);
        }
    }

    public async Task<Result<long>> CreateAsync(ApplicationEntity application, CancellationToken ct = default)
    {
        ValidateNotNull(application, nameof(application));

        LogInfo($"创建申请(完整): {DataMasker.MaskName(application.ApplicantName)}");

        var shouldManageTransaction = !_db.HasTransaction;
        if (shouldManageTransaction)
            await _db.BeginTransactionAsync();

        try
        {
            var existsResult = await CheckIdCardExistsAsync(application.ApplicantIdCard, null, ct);
            if (existsResult.IsSuccess && existsResult.Value)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<long>(ErrorCodes.DUPLICATE_ID_CARD, "该身份证号已存在");
            }

            var appNoResult = await GetNextApplicationNoAsync(ct);
            if (appNoResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
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
              status, current_step, created_by, created_at, updated_at, bank_name, bank_account)
             VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,
                    $11,$12,$13,$14,$15,$16,$17,$18,
                    $19,$20,$21,$22,$23,$24,$25,$26,
                    $27,$28,$29,$30,$31,$32,$33,$34,$35,
                    $36,$37,$38,$39,$40,$41,$42,$43,
                    $44,$45,$46,$47,$48,$49,$50,$51,$52,$53,
                    $54,$55,$56,$57,$58,$59,$60,$61,$62,$63,
                    $64,$65,$66,$67,$68,$69,$70,$71,$72,$73,
                    $74,$75,$76,$77,$78,$79,$80)
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
                application.CreatedAt == default ? DateTime.UtcNow : application.CreatedAt,
                DateTime.UtcNow,
                application.BankName ?? string.Empty, application.BankAccount ?? string.Empty);

            if (insertResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<long>(insertResult.ErrorCode!, insertResult.Message!);
            }

            if (shouldManageTransaction)
                await _db.CommitTransactionAsync();

            var newId = insertResult.Value;
            LogInfo($"创建申请成功: Id={newId}");
            return Result.Success(newId);
        }
        catch (Exception ex)
        {
            if (shouldManageTransaction) await _db.RollbackTransactionAsync();
            LogError($"创建申请失败: {ex.Message}");
            return Result.Failure<long>(ErrorCodes.DB_CONNECTION_FAILED, ex.Message);
        }
    }

    public async Task<Result> UpdateAsync(ApplicationUpdateRequest request, CancellationToken ct = default, bool allowNonEditable = false)
    {
        ValidateNotNull(request, nameof(request));

        LogInfo($"执行操作");

        var shouldManageTransaction = !_db.HasTransaction;
        if (shouldManageTransaction)
            await _db.BeginTransactionAsync();

        try
        {
            var appResult = await GetByIdAsync(request.ApplicationId, ct);
            if (appResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure(appResult.ErrorCode!, appResult.Message!);
            }

            var app = appResult.Value;
            if (app == null)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");
            }

            // 补全模式（allowNonEditable=true）允许更新已建档的 Approved 档案；普通编辑仍只允许草稿
            if (!allowNonEditable && !ApplicationStateMachine.IsEditable(ApplicationStatusExtensions.FromCode(app.Status)))
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure(ErrorCodes.INVALID_TRANSITION, "当前状态不允许编辑");
            }

            // 动态 SET 构建：字段名→值 顺序一一对应，参数编号自动生成（WHERE 参数放最后）
            var sets = new List<string>();
            var args = new List<object?>();
            void Set(string col, object? v) { args.Add(v); sets.Add($"{col} = ${args.Count}"); }

            Set("applicant_name", request.ApplicantName);
            Set("applicant_id_card", request.ApplicantIdCard);
            Set("applicant_phone", request.ApplicantPhone);
            Set("address", request.Address);
            Set("town", request.Town);
            Set("community", request.Community);
            Set("family_size", request.FamilySize);
            Set("total_family_income", request.AnnualIncome);
            Set("updated_by", request.UpdatedBy);
            Set("gender", request.Gender);
            Set("ethnicity", request.Ethnicity);
            Set("marital_status", request.MaritalStatus);
            Set("hukou_type", request.HukouType);
            Set("education_level", request.EducationLevel);
            Set("political_status", request.PoliticalStatus);
            Set("hukou_address", request.HukouAddress);
            Set("disability_card_no", request.DisabilityCardNo);
            Set("province", request.Province);
            Set("city", request.City);
            Set("district", request.District);
            // 补齐历史缺失字段（与 CreateAsync INSERT 列对齐；注：nc_biz_applications 无 village 列，
            // 表单的村/社区数据走 community 列）
            Set("city_id", request.CityId);
            Set("county_id", request.CountyId);
            Set("town_id", request.TownId);
            Set("village_id", request.VillageId);
            Set("hukou_city_id", request.HukouCityId);
            Set("hukou_county_id", request.HukouCountyId);
            Set("hukou_town_id", request.HukouTownId);
            Set("hukou_village_id", request.HukouVillageId);
            Set("physical_condition", request.PhysicalCondition);
            Set("disease_name", request.DiseaseName);
            Set("secondary_disease_name", request.SecondaryDiseaseName);
            Set("disease_code", request.DiseaseCode);
            Set("is_severe_disease", request.IsSevereDisease);
            Set("disability_type", request.DisabilityType);
            Set("disability_level", request.DisabilityLevel);
            Set("health_status", request.HealthStatus);
            Set("employment_status", request.EmploymentStatus);
            Set("work_unit", request.WorkUnit);
            Set("income_source", request.IncomeSource);
            Set("bank_name", request.BankName);
            Set("bank_account", request.BankAccount);
            Set("application_reason", request.ApplicationReason);
            Set("application_reason_detail", request.ApplicationReasonDetail);
            Set("work_income_total", request.WorkIncomeTotal);
            Set("business_income_total", request.BusinessIncomeTotal);
            Set("property_income_total", request.PropertyIncomeTotal);
            Set("transfer_income_total", request.TransferIncomeTotal);
            Set("other_income_total", request.OtherIncomeTotal);
            Set("alimony_income", request.AlimonyIncome);
            Set("per_capita_income", request.PerCapitaIncome);
            Set("total_annual_income", request.TotalAnnualIncome);
            Set("per_capita_annual_income", request.PerCapitaAnnualIncome);
            Set("rigid_expenditure", request.RigidExpenditure);
            Set("family_land_area", request.FamilyLandArea);
            Set("self_farmed_land_area", request.SelfFarmedLandArea);
            Set("subleased_land_area", request.SubleasedLandArea);
            Set("contracted_land_area", request.ContractedLandArea);
            Set("land_income_total", request.LandIncomeTotal);
            Set("subsidy_total", request.SubsidyTotal);
            Set("is_eligible", request.IsEligible);
            Set("classification_result", request.ClassificationResult);
            Set("classified_subsidy_type", request.ClassifiedSubsidyType);
            Set("classified_subsidy_amount", request.ClassifiedSubsidyAmount);
            Set("household_monthly_guarantee_amount", request.HouseholdMonthlyGuaranteeAmount);
            Set("person_category_protection_total_amount", request.PersonCategoryProtectionTotalAmount);
            Set("caregiver_subsidy_amount", request.CaregiverSubsidyAmount);
            Set("total_guarantee_amount", request.TotalGuaranteeAmount);
            Set("is_special_approval", request.IsSpecialApproval);
            Set("special_approval_id", request.SpecialApprovalId);
            Set("caregiver_type", request.CaregiverType);
            Set("destitute_support_type", request.DestituteSupportType);
            Set("confirmed_family_size", request.ConfirmedFamilySize);
            Set("is_single_rescue", request.IsSingleRescue);
            Set("support_mode", request.SupportMode);
            Set("support_institution_id", request.SupportInstitutionId);
            Set("current_step", request.CurrentStep);
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
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure(updateResult.ErrorCode!, updateResult.Message!);
            }

            if (updateResult.Value == 0)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                // 携带令牌时 0 行 = 并发冲突（前面 GetByIdAsync 已确认记录存在）；
                // 未携带令牌时 0 行 = 记录已被并发删除
                return request.LoadedUpdatedAt.HasValue
                    ? Result.Failure(ErrorCodes.CONCURRENCY_CONFLICT, "该申请已被他人修改，请刷新后重试")
                    : Result.Failure(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在或已被删除");
            }

            if (shouldManageTransaction)
                await _db.CommitTransactionAsync();

            LogInfo($"执行操作");
            return Result.Success();
        }
        catch (Exception ex)
        {
            if (shouldManageTransaction) await _db.RollbackTransactionAsync();
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
            var sql = $@"UPDATE nc_biz_applications SET
                data_completed_at = NOW(), updated_at = NOW()
                WHERE id = $1 AND deleted_at IS NULL";
            var result = await _db.ExecuteNonQueryAsync(sql, ct, id);
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

        var shouldManageTransaction = !_db.HasTransaction;
        if (shouldManageTransaction)
            await _db.BeginTransactionAsync();

        try
        {
            var appResult = await GetByIdAsync(id, ct);
            if (appResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure(appResult.ErrorCode!, appResult.Message!);
            }

            var app = appResult.Value;
            if (app == null)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");
            }

            if (!ApplicationStateMachine.IsEditable(ApplicationStatusExtensions.FromCode(app.Status)))
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure(ErrorCodes.INVALID_TRANSITION, "当前状态不允许删除");
            }

            var sql = "UPDATE nc_biz_applications SET deleted_at = NOW(), updated_by = $1 WHERE id = $2";
            var deleteResult = await _db.ExecuteNonQueryAsync(sql, ct, deletedBy, id);

            if (deleteResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure(deleteResult.ErrorCode!, deleteResult.Message!);
            }

            if (shouldManageTransaction)
                await _db.CommitTransactionAsync();

            LogInfo($"执行操作");
            return Result.Success();
        }
        catch (Exception ex)
        {
            if (shouldManageTransaction) await _db.RollbackTransactionAsync();
            LogException(ex, "删除申请失败");
            return Result.FromException(ex);
        }
    }

    public async Task<Result> SubmitAsync(ApplicationSubmitRequest request, CancellationToken ct = default)
    {
        ValidateNotNull(request, nameof(request));

        LogInfo($"执行操作");

        var shouldManageTransaction = !_db.HasTransaction;
        if (shouldManageTransaction)
            await _db.BeginTransactionAsync();

        try
        {
            var appResult = await GetByIdAsync(request.ApplicationId, ct);
            if (appResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure(appResult.ErrorCode!, appResult.Message!);
            }

            var app = appResult.Value;
            if (app == null)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");
            }

            var currentStatus = ApplicationStatusExtensions.FromCode(app.Status);
            var validateResult = ApplicationStateMachine.ValidateTransition(currentStatus, ApplicationStatus.Submitted);
            if (validateResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure(ErrorCodes.INVALID_TRANSITION, "当前状态不允许提交");
            }

            var sql = $@"UPDATE nc_biz_applications SET 
            status = $1, submit_at = NOW(), submit_by = $2, updated_at = NOW()
            WHERE id = $3";

            var submitResult = await _db.ExecuteNonQueryAsync(sql, ct, ApplicationStatus.Submitted.GetCode(), request.SubmittedBy, request.ApplicationId);

            if (submitResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure(submitResult.ErrorCode!, submitResult.Message!);
            }

            if (shouldManageTransaction)
                await _db.CommitTransactionAsync();

            LogInfo($"执行操作");
            Logger.LogBusiness("提交申请", ("ApplicationId", request.ApplicationId), ("SubmittedBy", request.SubmittedBy));
            return Result.Success();
        }
        catch (Exception ex)
        {
            if (shouldManageTransaction) await _db.RollbackTransactionAsync();
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

        var shouldManageTransaction = !_db.HasTransaction;
        if (shouldManageTransaction)
            await _db.BeginTransactionAsync();

        try
        {
            var appResult = await GetByIdAsync(request.ApplicationId, ct);
            if (appResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure(appResult.ErrorCode!, appResult.Message!);
            }

            var app = appResult.Value;
            if (app == null)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");
            }

            var currentStatus = ApplicationStatusExtensions.FromCode(app.Status);
            var targetStatus = request.Approved ? ApplicationStatus.Approved : ApplicationStatus.Refused;
            var validateResult = ApplicationStateMachine.ValidateTransition(currentStatus, targetStatus);
            if (validateResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure(ErrorCodes.INVALID_TRANSITION, "当前状态不允许审批");
            }

            var newStatus = targetStatus.GetCode();
            var sql = $@"UPDATE nc_biz_applications SET 
            status = $1, updated_by = $2,
            first_approved_at = CASE WHEN $1 = '{ApplicationStatusCodes.APPROVED}' THEN COALESCE(first_approved_at, NOW()) ELSE first_approved_at END,
            updated_at = NOW()
            WHERE id = $3";

            var approveResult = await _db.ExecuteNonQueryAsync(sql, ct, newStatus, request.ApprovedBy, request.ApplicationId);

            if (approveResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure(approveResult.ErrorCode!, approveResult.Message!);
            }

            // 审批通过成为保障对象 → 联动升级资产核查状态（同事务，失败不阻断）
            if (request.Approved)
                await TryMarkAssetChecksIncludedAsync(app, ct);

            if (shouldManageTransaction)
                await _db.CommitTransactionAsync();

            LogInfo($"执行操作");
            Logger.LogBusiness("审批申请", ("ApplicationId", request.ApplicationId), ("Approved", request.Approved), ("ApprovedBy", request.ApprovedBy));
            return Result.Success();
        }
        catch (Exception ex)
        {
            if (shouldManageTransaction) await _db.RollbackTransactionAsync();
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
        var prefix = $"SA{date:yyyyMMdd}";

        var sql = @"SELECT COALESCE(MAX(CAST(SUBSTRING(application_no FROM 11) AS INTEGER)), 0) + 1
                    FROM nc_biz_applications 
                    WHERE application_no LIKE $1";

        var result = await _db.ExecuteScalarAsync(sql, ct, $"{prefix}%");
        if (result.IsFailure)
            return Result.Failure<string>(result.ErrorCode!, result.Message!);

        var seq = result.Value;
        var appNo = $"{prefix}{seq:D4}";
        return Result.Success(appNo);
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
        LogInfo("获取已完成档案建设但未提交的申请（分页）: keyword=" + keyword + ", 第" + pageIndex + "页");

        var countSql = $"SELECT COUNT(*) FROM nc_biz_applications WHERE status = '{ApplicationStatusCodes.DRAFT}' AND current_step = {WorkflowSteps.PENDING_ARCHIVE} AND deleted_at IS NULL";
        var querySql = $"SELECT * FROM nc_biz_applications WHERE status = '{ApplicationStatusCodes.DRAFT}' AND current_step = {WorkflowSteps.PENDING_ARCHIVE} AND deleted_at IS NULL";
        var conditions = new List<string>();
        var parameters = new List<object>();
        var paramIndex = 1;

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            conditions.Add(" (applicant_name ILIKE $" + paramIndex + " OR applicant_id_card ILIKE $" + paramIndex + ") ");
            parameters.Add("%" + keyword + "%");
            paramIndex++;
        }

        if (conditions.Count > 0)
        {
            var whereClause = " AND " + string.Join(" AND ", conditions);
            countSql += whereClause;
            querySql += whereClause;
        }

        var countResult = await _db.ExecuteScalarAsync(countSql, ct, parameters.ToArray());
        if (countResult.IsFailure)
            return Result.Failure<PagedResult<ApplicationEntity>>(countResult.ErrorCode!, countResult.Message!);

        var totalCount = countResult.Value;
        var offset = (pageIndex - 1) * pageSize;
        querySql += " ORDER BY updated_at DESC LIMIT $" + paramIndex + " OFFSET $" + (paramIndex + 1);
        parameters.Add(pageSize);
        parameters.Add(offset);

        var listResult = await _db.QueryAsync<ApplicationEntity>(querySql, ct, parameters.ToArray());
        if (listResult.IsFailure)
            return Result.Failure<PagedResult<ApplicationEntity>>(listResult.ErrorCode!, listResult.Message!);

        return Result.Success(PagedResult<ApplicationEntity>.FromList(listResult.Value, pageIndex, pageSize, Convert.ToInt32(totalCount)));
    }

    public async Task<Result<int>> GetArchiveBuiltNotSubmittedCountAsync(CancellationToken ct = default)
    {
        var sql = $"SELECT COUNT(*) FROM nc_biz_applications WHERE status = '{ApplicationStatusCodes.DRAFT}' AND current_step = {WorkflowSteps.PENDING_ARCHIVE} AND deleted_at IS NULL";
        var result = await _db.ExecuteScalarAsync(sql, ct);
        if (result.IsFailure)
            return Result.Failure<int>(result.ErrorCode!, result.Message!);
        return Result.Success(Convert.ToInt32(result.Value));
    }

    public async Task<Result<PagedResult<ApplicationEntity>>> GetArchivedPagedAsync(string keyword, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        LogInfo("获取已完结档案（分页）: keyword=" + keyword + ", 第" + pageIndex + "页");

        var countSql = $"SELECT COUNT(*) FROM nc_biz_applications WHERE current_step = {WorkflowSteps.ARCHIVED} AND deleted_at IS NULL";
        var querySql = $"SELECT * FROM nc_biz_applications WHERE current_step = {WorkflowSteps.ARCHIVED} AND deleted_at IS NULL";
        var conditions = new List<string>();
        var parameters = new List<object>();
        var paramIndex = 1;

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            conditions.Add(" (applicant_name ILIKE $" + paramIndex + " OR applicant_id_card ILIKE $" + paramIndex + ") ");
            parameters.Add("%" + keyword + "%");
            paramIndex++;
        }

        if (conditions.Count > 0)
        {
            var whereClause = " AND " + string.Join(" AND ", conditions);
            countSql += whereClause;
            querySql += whereClause;
        }

        var countResult = await _db.ExecuteScalarAsync(countSql, ct, parameters.ToArray());
        if (countResult.IsFailure)
            return Result.Failure<PagedResult<ApplicationEntity>>(countResult.ErrorCode!, countResult.Message!);

        var totalCount = countResult.Value;
        var offset = (pageIndex - 1) * pageSize;
        querySql += " ORDER BY updated_at DESC LIMIT $" + paramIndex + " OFFSET $" + (paramIndex + 1);
        parameters.Add(pageSize);
        parameters.Add(offset);

        var listResult = await _db.QueryAsync<ApplicationEntity>(querySql, ct, parameters.ToArray());
        if (listResult.IsFailure)
            return Result.Failure<PagedResult<ApplicationEntity>>(listResult.ErrorCode!, listResult.Message!);

        return Result.Success(PagedResult<ApplicationEntity>.FromList(listResult.Value, pageIndex, pageSize, Convert.ToInt32(totalCount)));
    }

    public async Task<Result<int>> GetArchivedCountAsync(CancellationToken ct = default)
    {
        var sql = $"SELECT COUNT(*) FROM nc_biz_applications WHERE current_step = {WorkflowSteps.ARCHIVED} AND deleted_at IS NULL";
        var result = await _db.ExecuteScalarAsync(sql, ct);
        if (result.IsFailure)
            return Result.Failure<int>(result.ErrorCode!, result.Message!);
        return Result.Success(Convert.ToInt32(result.Value));
    }

    public async Task<Result<PagedResult<ApplicationEntity>>> GetStoppedPagedAsync(string keyword, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        LogInfo("获取已停保档案（分页）: keyword=" + keyword + ", 第" + pageIndex + "页");

        var countSql = "SELECT COUNT(*) FROM nc_biz_applications WHERE status = $1 AND deleted_at IS NULL";
        var querySql = "SELECT * FROM nc_biz_applications WHERE status = $1 AND deleted_at IS NULL";
        var conditions = new List<string>();
        var parameters = new List<object> { ApplicationStatusCodes.STOPPED };
        var paramIndex = 2;

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            conditions.Add($" (applicant_name ILIKE ${paramIndex} OR applicant_id_card ILIKE ${paramIndex}) ");
            parameters.Add("%" + keyword + "%");
            paramIndex++;
        }

        if (conditions.Count > 0)
        {
            var whereClause = " AND " + string.Join(" AND ", conditions);
            countSql += whereClause;
            querySql += whereClause;
        }

        var countResult = await _db.ExecuteScalarAsync(countSql, ct, parameters.ToArray());
        if (countResult.IsFailure)
            return Result.Failure<PagedResult<ApplicationEntity>>(countResult.ErrorCode!, countResult.Message!);

        var totalCount = countResult.Value;
        var offset = (pageIndex - 1) * pageSize;
        querySql += $" ORDER BY stop_date DESC, updated_at DESC LIMIT ${paramIndex} OFFSET ${paramIndex + 1}";
        parameters.Add(pageSize);
        parameters.Add(offset);

        var listResult = await _db.QueryAsync<ApplicationEntity>(querySql, ct, parameters.ToArray());
        if (listResult.IsFailure)
            return Result.Failure<PagedResult<ApplicationEntity>>(listResult.ErrorCode!, listResult.Message!);

        return Result.Success(PagedResult<ApplicationEntity>.FromList(listResult.Value, pageIndex, pageSize, Convert.ToInt32(totalCount)));
    }

    public async Task<Result<int>> GetStoppedCountAsync(CancellationToken ct = default)
    {
        var sql = "SELECT COUNT(*) FROM nc_biz_applications WHERE status = $1 AND deleted_at IS NULL";
        var result = await _db.ExecuteScalarAsync(sql, ct, ApplicationStatusCodes.STOPPED);
        if (result.IsFailure)
            return Result.Failure<int>(result.ErrorCode!, result.Message!);
        return Result.Success(Convert.ToInt32(result.Value));
    }

    public async Task<Result> UpdateCurrentStepAsync(long applicationId, int step, CancellationToken ct = default)
    {
        var sql = "UPDATE nc_biz_applications SET current_step = $1, updated_at = NOW() WHERE id = $2 AND deleted_at IS NULL";
        var result = await _db.ExecuteNonQueryAsync(sql, ct, step, applicationId);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode!, result.Message!);
        if (result.Value == 0)
            return Result.Failure(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");
        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> CompleteArchiveAsync(long applicationId, CancellationToken ct = default)
    {
        // 归档 = 审批通过（系统无独立审批工作流）：current_step 推进到 6 且 status 置 Approved。
        // 跳过状态机 Draft→Approved 校验（用户确认：无审批流程，点归档即已审批）。
        // 已 Approved/Completed 的档案仅推进步骤，不改状态（避免降级）。
        var sql = $@"UPDATE nc_biz_applications SET
            current_step = {WorkflowSteps.ARCHIVED},
            status = CASE WHEN status IN ('Approved','Completed') THEN status ELSE 'Approved' END,
            first_approved_at = COALESCE(first_approved_at, NOW()),
            updated_at = NOW()
            WHERE id = $1 AND deleted_at IS NULL";
        var result = await _db.ExecuteNonQueryAsync(sql, ct, applicationId);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode!, result.Message!);
        if (result.Value == 0)
            return Result.Failure(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");

        // 归档即审批通过 → 联动升级资产核查状态（失败不阻断）
        var appResult = await GetByIdAsync(applicationId, ct);
        if (appResult.IsSuccess && appResult.Value != null)
            await TryMarkAssetChecksIncludedAsync(appResult.Value, ct);

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
        LogInfo("获取草稿申请（分页）: keyword=" + keyword + ", 第" + pageIndex + "页");

        var countSql = $"SELECT COUNT(*) FROM nc_biz_applications WHERE status = '{ApplicationStatusCodes.DRAFT}' AND current_step < {WorkflowSteps.PENDING_ARCHIVE} AND deleted_at IS NULL";
        var querySql = $"SELECT * FROM nc_biz_applications WHERE status = '{ApplicationStatusCodes.DRAFT}' AND current_step < {WorkflowSteps.PENDING_ARCHIVE} AND deleted_at IS NULL";
        var conditions = new List<string>();
        var parameters = new List<object>();
        var paramIndex = 1;

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            conditions.Add(" (applicant_name ILIKE $" + paramIndex + " OR applicant_id_card ILIKE $" + paramIndex + ") ");
            parameters.Add("%" + keyword + "%");
            paramIndex++;
        }

        if (conditions.Count > 0)
        {
            var whereClause = " AND " + string.Join(" AND ", conditions);
            countSql += whereClause;
            querySql += whereClause;
        }

        var countResult = await _db.ExecuteScalarAsync(countSql, ct, parameters.ToArray());
        if (countResult.IsFailure)
            return Result.Failure<PagedResult<ApplicationEntity>>(countResult.ErrorCode!, countResult.Message!);

        var totalCount = countResult.Value;
        var offset = (pageIndex - 1) * pageSize;
        querySql += " ORDER BY updated_at DESC LIMIT $" + paramIndex + " OFFSET $" + (paramIndex + 1);
        parameters.Add(pageSize);
        parameters.Add(offset);

        var listResult = await _db.QueryAsync<ApplicationEntity>(querySql, ct, parameters.ToArray());
        if (listResult.IsFailure)
            return Result.Failure<PagedResult<ApplicationEntity>>(listResult.ErrorCode!, listResult.Message!);

        return Result.Success(PagedResult<ApplicationEntity>.FromList(listResult.Value, pageIndex, pageSize, Convert.ToInt32(totalCount)));
    }

    public async Task<Result<int>> GetDraftCountAsync(CancellationToken ct = default)
    {
        var sql = $"SELECT COUNT(*) FROM nc_biz_applications WHERE status = '{ApplicationStatusCodes.DRAFT}' AND current_step < {WorkflowSteps.PENDING_ARCHIVE} AND deleted_at IS NULL";
        var result = await _db.ExecuteScalarAsync(sql, ct);
        if (result.IsFailure)
            return Result.Failure<int>(result.ErrorCode!, result.Message!);
        return Result.Success(Convert.ToInt32(result.Value));
    }

    public async Task<Result<long>> CreateSingleRescueDraftAsync(long sourceApplicationId, FamilyMember member, CancellationToken ct = default)
    {
        LogInfo($"创建单人保草稿: SourceApplicationId={sourceApplicationId}, Member={DataMasker.MaskName(member.Name)}");

        var shouldManageTransaction = !_db.HasTransaction;
        if (shouldManageTransaction)
            await _db.BeginTransactionAsync();

        try
        {
            // 获取源申请
            var sourceResult = await GetByIdAsync(sourceApplicationId, ct);
            if (sourceResult.IsFailure || sourceResult.Value == null)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<long>(ErrorCodes.APPLICATION_NOT_FOUND, "源申请不存在");
            }

            var source = sourceResult.Value;

            // 获取下一个申请编号
            var appNoResult = await GetNextApplicationNoAsync(ct);
            var appNo = appNoResult.IsSuccess ? appNoResult.Value : $"SR-{DateTime.Now:yyyyMMddHHmmss}";

            // 创建新申请（单人保）- 设置 is_single_rescue = true
            var insertSql = @"INSERT INTO nc_biz_applications 
                (application_no, applicant_name, applicant_id_card, applicant_phone,
                 gender, ethnicity, marital_status, hukou_type, education_level, political_status,
                 hukou_address, disability_card_no,
                 province, city, district, town, community, address,
                 health_status, disease_name, disability_type, disability_level,
                 family_size, is_single_rescue, status, current_step, original_application_id, chain_type,
                 work_income_total, business_income_total, property_income_total, transfer_income_total,
                 other_income_total, total_family_income, per_capita_income, rigid_expenditure,
                 classification_result, household_monthly_guarantee_amount,
                 created_by, created_at, updated_at)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,$20,$21,$22,$23,$24,$25,$26,$27,'SingleRescue',$28,$29,$30,$31,$32,$33,$34,$35,$36,$37,$38, NOW(), NOW())
                RETURNING id";

            var insertResult = await _db.ExecuteScalarAsync(insertSql, ct,
                appNo, member.Name, member.IdCard, member.Phone ?? "",
                member.Gender ?? "", member.Ethnicity ?? "", member.MaritalStatus ?? "",
                source.HukouType, member.EducationLevel ?? "", member.PoliticalStatus ?? "",
                source.HukouAddress, "",
                source.Province, source.City, source.District, source.Town, source.Community, source.Address,
                member.HealthStatus ?? "", member.DiseaseName ?? "", member.DisabilityType ?? "", member.DisabilityLevel ?? "",
                1, true, "Draft", 1, sourceApplicationId,
                source.WorkIncomeTotal, source.BusinessIncomeTotal, source.PropertyIncomeTotal, source.TransferIncomeTotal,
                source.OtherIncomeTotal, source.TotalFamilyIncome, source.PerCapitaIncome, source.RigidExpenditure,
                // 单人保使用农村/城市低收入（单）分类，以便匹配低保档案模板
                ClassificationConstants.HukouType.IsHukouRural(source.HukouType)
                    ? ClassificationConstants.RuralLowIncomeSingle
                    : ClassificationConstants.UrbanLowIncomeSingle,
                source.HouseholdMonthlyGuaranteeAmount,
                "System");

            if (insertResult.IsFailure)
            {
                if (shouldManageTransaction) await _db.RollbackTransactionAsync();
                return Result.Failure<long>(insertResult.ErrorCode!, insertResult.Message!);
            }

            var newAppId = insertResult.Value;

            // 创建户主成员（即该单人保成员）
            var memberInsertSql = @"INSERT INTO nc_biz_family_members 
                (application_id, name, id_card, is_applicant, gender, age, ethnicity, phone,
                 marital_status, education_level, political_status, health_status,
                 relationship_to_head, member_category, is_disabled, disability_type, disability_level,
                 disease_category, disease_name, disease_code, is_severe_disease,
                 created_at, updated_at)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,$20,NOW(),NOW())";

            // 户主成员插入失败必须让整个单人保创建失败回滚（此前结果被丢弃，产生无成员的孤儿申请）
            await ExecOrThrowAsync(_db, memberInsertSql, ct,
                newAppId, member.Name, member.IdCard, true,
                member.Gender ?? "", member.Age, member.Ethnicity ?? "", member.Phone ?? "",
                member.MaritalStatus ?? "", member.EducationLevel ?? "", member.PoliticalStatus ?? "", member.HealthStatus ?? "",
                "本人/户主", "SharedLiving", member.IsDisabled, member.DisabilityType ?? "", member.DisabilityLevel ?? "",
                member.DiseaseCategory ?? "", member.DiseaseName ?? "", member.DiseaseCode ?? "", member.IsSevereDisease);

            // 复制经济信息（务工收入、经营收入等）
            await CopyEconomicDataAsync(sourceApplicationId, newAppId, ct);

            if (shouldManageTransaction)
                await _db.CommitTransactionAsync();

            LogInfo($"单人保草稿创建成功: NewApplicationId={newAppId}");
            return Result.Success(newAppId);
        }
        catch (Exception ex)
        {
            if (shouldManageTransaction) await _db.RollbackTransactionAsync();
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
        var supporterSql = @"INSERT INTO nc_biz_family_members
            (application_id, name, id_card, person_type, relationship_to_head,
             annual_support_fee, is_support_ability, member_category, created_at)
            SELECT $1, name, id_card, person_type, relationship_to_head,
             annual_support_fee, is_support_ability, 'Support', NOW()
            FROM nc_biz_family_members
            WHERE application_id = $2 AND member_category = 'Support' AND deleted_at IS NULL";
        await ExecOrThrowAsync(_db, supporterSql, ct, targetAppId, sourceAppId);
    }
}
