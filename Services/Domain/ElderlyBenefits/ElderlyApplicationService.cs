using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.System;

namespace NewCosmos.Services.Domain.ElderlyBenefits;

/// <summary>
/// 普惠高龄补贴申请服务实现
/// </summary>
public class ElderlyApplicationService : BaseService, IElderlyApplicationService
{
    protected override string ServiceName => "ElderlyApplicationService";

    private readonly IDatabaseService _db;
    private readonly IStandardConfigService _standardConfigService;
    private readonly ElderlyCategoryService _categoryService;

    public ElderlyApplicationService(
        IDatabaseService db,
        IStandardConfigService standardConfigService,
        ElderlyCategoryService categoryService,
        ILoggerService logger) : base(logger)
    {
        _db = db;
        _standardConfigService = standardConfigService;
        _categoryService = categoryService;
    }

    public async Task<Result<decimal>> GetMonthlyAmountAsync(string category, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(category))
            return Result.Failure<decimal>(ErrorCodes.VALIDATION_FAILED, "类别不能为空");

        var result = await _standardConfigService.GetConfigStandardByTypeAsync(
            ElderlyBenefitConstants.StandardType, category, null, ct);
        if (result.IsFailure || result.Value is null)
            return Result.Failure<decimal>(ErrorCodes.CONFIG_NOT_FOUND, $"未配置类别[{category}]的高龄补贴标准");

        return Result.Success(result.Value.StandardValue);
    }

    public async Task<Result<ElderlyEvaluateResult>> EvaluateAsync(string idCard, DateTime applyDate, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idCard))
            return Result.Failure<ElderlyEvaluateResult>(ErrorCodes.VALIDATION_FAILED, "请先填写身份证号码");

        if (!IdCardValidator.IsValid(idCard))
            return Result.Failure<ElderlyEvaluateResult>(ErrorCodes.VALIDATION_FAILED, "身份证号码无效");

        var birthDate = IdCardValidator.ExtractBirthDate(idCard);
        if (birthDate == null)
            return Result.Failure<ElderlyEvaluateResult>(ErrorCodes.VALIDATION_FAILED, "身份证号码无效，无法提取出生日期");

        var gender = IdCardValidator.ExtractGender(idCard) ?? string.Empty;
        var today = DateTime.Today;
        var age = today.Year - birthDate.Value.Year;
        if (birthDate.Value.Date > today.AddYears(-age)) age--;

        if (age < ElderlyBenefitConstants.Threshold80)
            return Result.Failure<ElderlyEvaluateResult>(ErrorCodes.VALIDATION_FAILED, $"当前年龄 {age} 周岁，未达到80周岁享受门槛");

        // 身份比对（仅 80-89 档需要判断高低标准；90+ 不区分身份）
        var identityResult = await _categoryService.MatchIdentityAsync(idCard, ct);
        if (identityResult.IsFailure)
            return Result.Failure<ElderlyEvaluateResult>(identityResult.ErrorCode!, identityResult.Message!);

        var identityFlag = identityResult.Value.IdentityFlag;
        var identitySource = identityResult.Value.SourceTable;

        var category = age >= ElderlyBenefitConstants.Threshold100 ? ElderlyBenefitConstants.Cat100Plus
            : age >= ElderlyBenefitConstants.Threshold90 ? ElderlyBenefitConstants.Cat90To99
            : ElderlyBenefitConstants.IsHighSubsidyIdentity(identityFlag) ? ElderlyBenefitConstants.CatLowSubsidy
            : ElderlyBenefitConstants.CatOtherElderly;

        var amountResult = await GetMonthlyAmountAsync(category, ct);
        if (amountResult.IsFailure)
            return Result.Failure<ElderlyEvaluateResult>(amountResult.ErrorCode!, amountResult.Message!);

        var endMonth = new DateTime(applyDate.Year, applyDate.Month, 1).ToString("yyyy-MM");

        // 预取各年龄档月标准（循环外一次性 await 获取，委托内纯内存查表）。
        // 禁止在同步分段循环内 sync-over-async：UI 上下文下 GetResult() 会死锁主线程（曾致页面冻结）。
        var standardMap = new Dictionary<string, decimal>();
        foreach (var cat in new[]
                 {
                     ElderlyBenefitConstants.CatOtherElderly,
                     ElderlyBenefitConstants.CatLowSubsidy,
                     ElderlyBenefitConstants.Cat90To99,
                     ElderlyBenefitConstants.Cat100Plus
                 })
        {
            var stdResult = await _standardConfigService.GetStandardValueAsync(
                ElderlyBenefitConstants.StandardType, cat, null, ct);
            standardMap[cat] = stdResult.IsSuccess ? stdResult.Value : 0m;
        }

        var payback = ElderlyPaybackCalculator.Calculate(birthDate.Value, identityFlag, endMonth,
            cat => standardMap.GetValueOrDefault(cat));

        // 校验各段标准均有配置（避免标准缺失时静默算出0元）
        if (payback.TotalMonths > 0 && payback.Segments.Any(s => s.MonthlyAmount <= 0))
            return Result.Failure<ElderlyEvaluateResult>(ErrorCodes.CONFIG_NOT_FOUND, "部分年龄档位的补贴标准未配置，请先在标准配置管理中维护");

        // 计发年月 = 受理次月（补发已算到受理当月，次月起正常发放）
        var issueStartMonth = new DateTime(applyDate.Year, applyDate.Month, 1).AddMonths(1).ToString("yyyy-MM");

        return Result.Success(new ElderlyEvaluateResult
        {
            Gender = gender,
            BirthDate = birthDate,
            Age = age,
            Category = category,
            MonthlyAmount = amountResult.Value,
            IdentityFlag = identityFlag,
            IdentitySource = identitySource,
            IssueStartMonth = issueStartMonth,
            IssueAmount = amountResult.Value,
            Payback = payback
        });
    }

    public async Task<Result<ElderlyApplication>> GetByIdAsync(long id, CancellationToken ct = default)
    {
        LogInfo($"按ID查询: {id}");
        var sql = "SELECT * FROM nc_biz_elderly_applications WHERE id = $1 AND deleted_at IS NULL";
        return await _db.QuerySingleAsync<ElderlyApplication>(sql, ct, id);
    }

    /// <summary>
    /// 查询登记补发分段明细（按登记ID，按 id 排序保持分段顺序）
    /// </summary>
    public async Task<Result<List<ElderlyPaybackSegment>>> GetSegmentsAsync(long applicationId, CancellationToken ct = default)
    {
        LogInfo($"查询补发分段: applicationId={applicationId}");
        var sql = @"SELECT id, application_id, category_code, monthly_amount,
                           segment_start_month, segment_end_month, months, segment_amount
                    FROM nc_biz_elderly_payback_segments
                    WHERE application_id = $1
                    ORDER BY id";
        return await _db.QueryAsync<ElderlyPaybackSegment>(sql, ct, applicationId);
    }

    public async Task<Result<PagedResult<ElderlyApplication>>> GetPagedAsync(string keyword, string status, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        LogInfo($"分页查询: keyword={keyword}, status={status}, 第{pageIndex}页, 每页{pageSize}条");

        var conditions = new SqlConditionBuilder()
            .Add("deleted_at IS NULL")
            .AddIf(!string.IsNullOrEmpty(status), "status = {0}", status)
            .AddIf(!string.IsNullOrWhiteSpace(keyword), "(name ILIKE {0} OR id_card ILIKE {0})", $"%{keyword}%");

        var where = conditions.ToWhereClause();
        var countSql = $"SELECT COUNT(*) FROM nc_biz_elderly_applications{where}";
        var querySql = $"SELECT * FROM nc_biz_elderly_applications{where}";

        var countResult = await _db.ExecuteScalarAsync<long>(countSql, ct, conditions.GetParameters());
        if (countResult.IsFailure)
            return Result.Failure<PagedResult<ElderlyApplication>>(countResult.ErrorCode!, countResult.Message!);

        var offset = (pageIndex - 1) * pageSize;
        querySql += $" ORDER BY created_at DESC LIMIT ${conditions.ParamCount + 1} OFFSET ${conditions.ParamCount + 2}";
        var pageParams = new List<object?>(conditions.GetParameters()) { pageSize, offset };

        var listResult = await _db.QueryAsync<ElderlyApplication>(querySql, ct, pageParams.ToArray());
        if (listResult.IsFailure)
            return Result.Failure<PagedResult<ElderlyApplication>>(listResult.ErrorCode!, listResult.Message!);

        return Result.Success(PagedResult<ElderlyApplication>.FromList(listResult.Value, pageIndex, pageSize, Convert.ToInt32(countResult.Value)));
    }

    public async Task<Result<List<ElderlyApplication>>> GetByMonthAsync(int year, int month, bool isStop, string? categoryCode = null, bool excludeImported = false, CancellationToken ct = default)
    {
        LogInfo($"按月份查询: {year}-{month}, isStop={isStop}, categoryCode={categoryCode}, excludeImported={excludeImported}");

        var monthStart = new DateTime(year, month, 1);
        var monthEnd = monthStart.AddMonths(1);

        var excludeImportClause = excludeImported ? "  AND source_type IS NULL" : "";
        var categoryClause = string.IsNullOrWhiteSpace(categoryCode)
            ? ""
            : $"  AND category = '{categoryCode.Trim().Replace("'", "''")}'";

        // 新增明细：受理日期落在该月；停止明细：实际停发时间落在该月
        var sql = isStop
            ? $@"SELECT * FROM nc_biz_elderly_applications
                WHERE deleted_at IS NULL
                  AND status = '{ApplicationStatusCodes.STOPPED}'
                  AND actual_stop_date >= $1 AND actual_stop_date < $2
                  AND (stop_reason IS NULL OR stop_reason <> '{ElderlyBenefitConstants.StopReasonReview}')
                  {categoryClause}
                  {excludeImportClause}
                ORDER BY actual_stop_date, id"
            : $@"SELECT * FROM nc_biz_elderly_applications
                WHERE deleted_at IS NULL
                  AND status IN ('Confirmed','Stopped')
                  AND apply_date >= $1 AND apply_date < $2
                  {categoryClause}
                  {excludeImportClause}
                ORDER BY apply_date, id";

        var result = await _db.QueryAsync<ElderlyApplication>(sql, ct, monthStart, monthEnd);
        if (result.IsFailure)
            return Result.Failure<List<ElderlyApplication>>(result.ErrorCode!, result.Message!);

        return Result.Success(result.Value);
    }

    public async Task<Result<List<ElderlyApplication>>> GetByIdsAsync(List<long> ids, CancellationToken ct = default)
    {
        if (ids == null || ids.Count == 0)
            return Result.Success(new List<ElderlyApplication>());

        var sql = @"SELECT * FROM nc_biz_elderly_applications
                    WHERE deleted_at IS NULL AND id = ANY($1)
                    ORDER BY id";
        var result = await _db.QueryAsync<ElderlyApplication>(sql, ct, ids.ToArray());
        if (result.IsFailure)
            return Result.Failure<List<ElderlyApplication>>(result.ErrorCode!, result.Message!);

        return Result.Success(result.Value);
    }

    public async Task<Result<long>> CreateAsync(ElderlyApplication application, List<ElderlyPaybackSegment> segments, CancellationToken ct = default)
    {
        ValidateNotNull(application, nameof(application));
        ValidateNotNullOrEmpty(application.Name, nameof(application.Name));
        ValidateNotNullOrEmpty(application.IdCard, nameof(application.IdCard));

        LogInfo($"创建登记: {DataMasker.MaskName(application.Name)}, 身份证={DataMasker.MaskIdCard(application.IdCard)}");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);
        try
        {
            var existsResult = await CheckIdCardExistsAsync(application.IdCard, null, ct);
            if (existsResult.IsSuccess && existsResult.Value)
                return Result.Failure<long>(ErrorCodes.DUPLICATE_ID_CARD, "该身份证号已登记");

            var appNoResult = await GetNextApplicationNoAsync(ct);
            if (appNoResult.IsFailure)
                return Result.Failure<long>(appNoResult.ErrorCode!, appNoResult.Message!);

            const string sql = @"
                INSERT INTO nc_biz_elderly_applications
                (application_no, name, id_card, gender, birth_date, phone,
                 hukou_address, hukou_village,
                 hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id, hukou_detail_address,
                 family_address, family_city_id, family_county_id, family_town_id, family_village_id, detail_address,
                 bank_name, bank_account, agent_name, agent_relation,
                 agent_receive_name, agent_receive_relation, agent_receive_bank_name, agent_receive_bank_account, agent_receive_reason,
                 issue_start_month, issue_amount,
                 category, identity_flag, identity_source, is_category_manual,
                 payback_start_month, payback_end_month, auto_start_month, auto_end_month,
                 payback_months, payback_amount, is_special_case, special_reason, payback_reason,
                 status, apply_date, created_by, created_at, updated_at)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,
                        $20,$21,$22,$23,$24,$25,$26,$27,$28,$29,$30,$31,$32,$33,$34,$35,$36,
                        $37,$38,$39,$40,$41,$42,$43,$44,$45,$46,
                        NOW(), NOW())
                RETURNING id";

            var insertResult = await _db.ExecuteScalarAsync(sql, ct,
                appNoResult.Value,
                application.Name, application.IdCard, application.Gender ?? "",
                application.BirthDate, application.Phone ?? "",
                application.HukouAddress ?? "", application.HukouVillage ?? "",
                (object?)application.HukouCityId, (object?)application.HukouCountyId,
                (object?)application.HukouTownId, (object?)application.HukouVillageId,
                application.HukouDetailAddress ?? "",
                application.FamilyAddress ?? "",
                (object?)application.FamilyCityId, (object?)application.FamilyCountyId,
                (object?)application.FamilyTownId, (object?)application.FamilyVillageId,
                application.DetailAddress ?? "",
                application.BankName ?? "", application.BankAccount ?? "",
                application.AgentName ?? "", application.AgentRelation ?? "",
                application.AgentReceiveName ?? "", application.AgentReceiveRelation ?? "",
                application.AgentReceiveBankName ?? "", application.AgentReceiveBankAccount ?? "",
                application.AgentReceiveReason ?? "",
                application.IssueStartMonth ?? "", application.IssueAmount,
                application.Category ?? "", application.IdentityFlag ?? "", application.IdentitySource ?? "", application.IsCategoryManual,
                application.PaybackStartMonth ?? "", application.PaybackEndMonth ?? "",
                application.AutoStartMonth ?? "", application.AutoEndMonth ?? "",
                application.PaybackMonths, application.PaybackAmount,
                application.IsSpecialCase, application.SpecialReason ?? "",
                application.PaybackReason ?? "",
                application.Status ?? ElderlyBenefitConstants.StatusDraft,
                application.ApplyDate ?? DateTime.Today,
                application.CreatedBy ?? "System");

            if (insertResult.IsFailure)
                return Result.Failure<long>(insertResult.ErrorCode!, insertResult.Message!);

            var newId = insertResult.Value;

            if (segments != null && segments.Count > 0)
            {
                var segmentResult = await InsertSegmentsAsync(newId, segments, ct);
                if (segmentResult.IsFailure)
                    return Result.Failure<long>(segmentResult.ErrorCode!, segmentResult.Message!);
            }

            await tx.CommitAsync(ct);

            Logger.LogBusiness("创建普惠高龄登记",
                ("ApplicationId", newId),
                ("ApplicationNo", appNoResult.Value),
                ("Name", DataMasker.MaskName(application.Name)));
            return Result.Success(newId);
        }
        catch (Exception ex)
        {
            LogException(ex, "创建普惠高龄登记失败");
            return Result.FromException<long>(ex);
        }
    }

    public async Task<Result> UpdateAsync(ElderlyApplication application, List<ElderlyPaybackSegment> segments, CancellationToken ct = default)
    {
        ValidateNotNull(application, nameof(application));

        LogInfo($"更新登记: id={application.Id}, 姓名={DataMasker.MaskName(application.Name)}");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);
        try
        {
            var existsResult = await CheckIdCardExistsAsync(application.IdCard, application.Id, ct);
            if (existsResult.IsSuccess && existsResult.Value)
                return Result.Failure(ErrorCodes.DUPLICATE_ID_CARD, "该身份证号已登记");

            const string sql = @"
                UPDATE nc_biz_elderly_applications SET
                 name = $1, id_card = $2, gender = $3, birth_date = $4, phone = $5,
                 hukou_address = $6, hukou_village = $7,
                 hukou_city_id = $8, hukou_county_id = $9, hukou_town_id = $10, hukou_village_id = $11,
                 hukou_detail_address = $12,
                 family_address = $13, family_city_id = $14, family_county_id = $15,
                 family_town_id = $16, family_village_id = $17, detail_address = $18,
                 bank_name = $19, bank_account = $20, agent_name = $21, agent_relation = $22,
                 agent_receive_name = $23, agent_receive_relation = $24,
                 agent_receive_bank_name = $25, agent_receive_bank_account = $26, agent_receive_reason = $27,
                 issue_start_month = $28, issue_amount = $29,
                 category = $30, identity_flag = $31, identity_source = $32, is_category_manual = $33,
                 payback_start_month = $34, payback_end_month = $35,
                 auto_start_month = $36, auto_end_month = $37,
                 payback_months = $38, payback_amount = $39,
                 is_special_case = $40, special_reason = $41, payback_reason = $42,
                 updated_by = $43, updated_at = NOW()
                WHERE id = $44 AND deleted_at IS NULL";

            var updateResult = await _db.ExecuteNonQueryAsync(sql, ct,
                application.Name, application.IdCard, application.Gender ?? "",
                application.BirthDate, application.Phone ?? "",
                application.HukouAddress ?? "", application.HukouVillage ?? "",
                (object?)application.HukouCityId, (object?)application.HukouCountyId,
                (object?)application.HukouTownId, (object?)application.HukouVillageId,
                application.HukouDetailAddress ?? "",
                application.FamilyAddress ?? "",
                (object?)application.FamilyCityId, (object?)application.FamilyCountyId,
                (object?)application.FamilyTownId, (object?)application.FamilyVillageId,
                application.DetailAddress ?? "",
                application.BankName ?? "", application.BankAccount ?? "",
                application.AgentName ?? "", application.AgentRelation ?? "",
                application.AgentReceiveName ?? "", application.AgentReceiveRelation ?? "",
                application.AgentReceiveBankName ?? "", application.AgentReceiveBankAccount ?? "",
                application.AgentReceiveReason ?? "",
                application.IssueStartMonth ?? "", application.IssueAmount,
                application.Category ?? "", application.IdentityFlag ?? "", application.IdentitySource ?? "", application.IsCategoryManual,
                application.PaybackStartMonth ?? "", application.PaybackEndMonth ?? "",
                application.AutoStartMonth ?? "", application.AutoEndMonth ?? "",
                application.PaybackMonths, application.PaybackAmount,
                application.IsSpecialCase, application.SpecialReason ?? "",
                application.PaybackReason ?? "",
                application.UpdatedBy ?? "System",
                application.Id);

            if (updateResult.IsFailure)
                return Result.Failure(updateResult.ErrorCode!, updateResult.Message!);
            if (updateResult.Value == 0)
                return Result.Failure(ErrorCodes.RECORD_NOT_FOUND, "登记记录不存在或已删除");

            // 重建分段明细
            var deleteSegments = await _db.ExecuteNonQueryAsync(
                "DELETE FROM nc_biz_elderly_payback_segments WHERE application_id = $1", ct, application.Id);
            if (deleteSegments.IsFailure)
                return Result.Failure(deleteSegments.ErrorCode!, deleteSegments.Message!);

            if (segments != null && segments.Count > 0)
            {
                var segmentResult = await InsertSegmentsAsync(application.Id, segments, ct);
                if (segmentResult.IsFailure)
                    return Result.Failure(segmentResult.ErrorCode!, segmentResult.Message!);
            }

            await tx.CommitAsync(ct);
            Logger.LogBusiness("更新普惠高龄登记", ("ApplicationId", application.Id));
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "更新普惠高龄登记失败");
            return Result.FromException(ex);
        }
    }

    public async Task<Result> ConfirmAsync(long id, string operatorName, CancellationToken ct = default)
    {
        LogInfo($"确认登记: id={id}, operator={operatorName}");

        var sql = @"UPDATE nc_biz_elderly_applications SET
                    status = $1, confirmed_at = NOW(), confirmed_by = $2, updated_at = NOW()
                    WHERE id = $3 AND deleted_at IS NULL";
        var result = await _db.ExecuteNonQueryAsync(sql, ct, ElderlyBenefitConstants.StatusConfirmed, operatorName ?? "System", id);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode!, result.Message!);
        if (result.Value == 0)
            return Result.Failure(ErrorCodes.RECORD_NOT_FOUND, "登记记录不存在或已删除");

        Logger.LogBusiness("确认普惠高龄登记", ("ApplicationId", id));
        return Result.Success();
    }

    public async Task<Result> StopAsync(long id, string reason, string operatorName, DateTime? dueStopDate,
        DateTime? deathDate, bool isRecover,
        string recoverStartMonth, string recoverEndMonth, decimal recoverAmount, string remark,
        CancellationToken ct = default)
    {
        LogInfo($"停发登记: id={id}, operator={operatorName}");

        var sql = @"UPDATE nc_biz_elderly_applications SET
                    status = $1, stopped_at = NOW(), actual_stop_date = NOW(),
                    stopped_by = $2, stop_reason = $3,
                    due_stop_date = $4, death_date = $5,
                    is_recover = $6,
                    recover_start_month = $7, recover_end_month = $8,
                    recover_amount = $9, remark = $10, updated_at = NOW()
                    WHERE id = $11 AND deleted_at IS NULL";
        var result = await _db.ExecuteNonQueryAsync(sql, ct,
            ElderlyBenefitConstants.StatusStopped,
            operatorName ?? "System",
            reason ?? string.Empty,
            dueStopDate,
            deathDate,
            isRecover,
            recoverStartMonth ?? string.Empty,
            recoverEndMonth ?? string.Empty,
            recoverAmount,
            remark ?? string.Empty,
            id);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode!, result.Message!);
        if (result.Value == 0)
            return Result.Failure(ErrorCodes.RECORD_NOT_FOUND, "登记记录不存在或已删除");

        // 停发成功后联动标记导入台账（nc_biz_elderly_subsidy_history）：来源记录按 id 标记，另按身份证兜底标记全部 Active 记录
        await MarkHistoryStoppedAsync(id, ct);

        Logger.LogBusiness("停发普惠高龄登记",
            ("ApplicationId", id),
            ("Reason", reason ?? string.Empty),
            ("DeathDate", deathDate?.ToString("yyyy-MM-dd") ?? ""),
            ("IsRecover", isRecover),
            ("RecoverAmount", recoverAmount));
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(long id, CancellationToken ct = default)
    {
        LogInfo($"删除登记: id={id}");

        var sql = "UPDATE nc_biz_elderly_applications SET deleted_at = NOW(), updated_at = NOW() WHERE id = $1 AND deleted_at IS NULL";
        var result = await _db.ExecuteNonQueryAsync(sql, ct, id);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode!, result.Message!);
        if (result.Value == 0)
            return Result.Failure(ErrorCodes.RECORD_NOT_FOUND, "登记记录不存在或已删除");

        Logger.LogBusiness("删除普惠高龄登记", ("ApplicationId", id));
        return Result.Success();
    }

    /// <summary>
    /// 停发联动：将导入台账（nc_biz_elderly_subsidy_history）对应人员标记为已停发。
    /// 1) 来自导入库建档（source_type=ElderlySubsidyHistory）按 source_id 精确标记；
    /// 2) 无论来源，按身份证兜底将该人员全部 Active 记录标记 Stopped（用户口径：全部标记）。
    /// </summary>
    private async Task MarkHistoryStoppedAsync(long applicationId, CancellationToken ct)
    {
        try
        {
            const string srcSql = @"SELECT COALESCE(source_type,'') AS source_type, source_id, id_card
                                    FROM nc_biz_elderly_applications WHERE id = $1 AND deleted_at IS NULL";
            var srcResult = await _db.QuerySingleAsync<HistoryMarkRow>(srcSql, ct, applicationId);
            if (srcResult.IsFailure || srcResult.Value == null) return;
            var src = srcResult.Value;

            var marked = 0;
            if (string.Equals(src.SourceType, "ElderlySubsidyHistory", StringComparison.OrdinalIgnoreCase)
                && src.SourceId.HasValue)
            {
                var byId = await _db.ExecuteNonQueryAsync(
                    "UPDATE nc_biz_elderly_subsidy_history SET status = 'Stopped', stopped_at = NOW(), updated_at = NOW() WHERE id = $1",
                    ct, src.SourceId.Value);
                if (byId.IsSuccess) marked += byId.Value;
            }

            if (!string.IsNullOrWhiteSpace(src.IdCard))
            {
                var byCard = await _db.ExecuteNonQueryAsync(
                    "UPDATE nc_biz_elderly_subsidy_history SET status = 'Stopped', stopped_at = NOW(), updated_at = NOW() WHERE id_card = $1 AND status = 'Active'",
                    ct, src.IdCard);
                if (byCard.IsSuccess) marked += byCard.Value;
            }

            if (marked > 0)
                Logger.LogBusiness("停发联动标记导入台账", ("ApplicationId", applicationId), ("MarkedCount", marked));
        }
        catch (Exception ex)
        {
            LogWarn($"停发联动标记导入台账失败: {ex.Message}");
        }
    }

    private sealed class HistoryMarkRow
    {
        public string SourceType { get; set; } = string.Empty;
        public long? SourceId { get; set; }
        public string IdCard { get; set; } = string.Empty;
    }

    public async Task<Result<bool>> CheckIdCardExistsAsync(string idCard, long? excludeId = null, CancellationToken ct = default)
    {
        ValidateNotNullOrEmpty(idCard, nameof(idCard));

        string sql;
        object[] parameters;

        if (excludeId.HasValue)
        {
            sql = "SELECT COUNT(*) FROM nc_biz_elderly_applications WHERE id_card = $1 AND id != $2 AND deleted_at IS NULL";
            parameters = new object[] { idCard, excludeId.Value };
        }
        else
        {
            sql = "SELECT COUNT(*) FROM nc_biz_elderly_applications WHERE id_card = $1 AND deleted_at IS NULL";
            parameters = new object[] { idCard };
        }

        var result = await _db.ExecuteScalarAsync(sql, ct, parameters);
        if (result.IsFailure)
            return Result.Failure<bool>(result.ErrorCode!, result.Message!);

        return Result.Success(result.Value > 0);
    }

    /// <summary>
    /// 生成申请编号：GA{yyyyMMdd}{seq:D4}
    /// </summary>
    private async Task<Result<string>> GetNextApplicationNoAsync(CancellationToken ct = default)
    {
        var prefix = $"{ElderlyBenefitConstants.ApplicationNoPrefix}{DateTime.Now:yyyyMMdd}";

        var sql = @"SELECT COALESCE(MAX(CAST(SUBSTRING(application_no FROM 11) AS INTEGER)), 0) + 1
                    FROM nc_biz_elderly_applications
                    WHERE application_no LIKE $1";
        var result = await _db.ExecuteScalarAsync(sql, ct, $"{prefix}%");
        if (result.IsFailure)
            return Result.Failure<string>(result.ErrorCode!, result.Message!);

        var seq = result.Value;
        var appNo = $"{prefix}{seq:D4}";
        return Result.Success(appNo);
    }

    /// <summary>
    /// 批量插入补发分段明细
    /// </summary>
    private async Task<Result> InsertSegmentsAsync(long applicationId, List<ElderlyPaybackSegment> segments, CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO nc_biz_elderly_payback_segments
            (application_id, category_code, monthly_amount, segment_start_month, segment_end_month, months, segment_amount, created_at)
            VALUES ($1,$2,$3,$4,$5,$6,$7,NOW())";

        foreach (var segment in segments)
        {
            var result = await _db.ExecuteNonQueryAsync(sql, ct,
                applicationId, segment.CategoryCode, segment.MonthlyAmount,
                segment.SegmentStartMonth, segment.SegmentEndMonth,
                segment.Months, segment.SegmentAmount);
            if (result.IsFailure)
                return result;
        }

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<List<ElderlyImportedSearchItem>>> SearchImportedLibraryAsync(string keyword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return Result.Success(new List<ElderlyImportedSearchItem>());

        try
        {
            var sql = @"
                SELECT id, name, id_card, gender, birth_date, age, phone, address,
                       subsidy_amount, bank_account, person_type, original_type, data_year
                FROM nc_biz_elderly_subsidy_history
                WHERE name ILIKE $1 OR id_card ILIKE $1
                LIMIT 20";

            var result = await _db.QueryAsync<ElderlyImportedSearchItem>(sql, ct, $"%{keyword.Trim()}%");
            if (result.IsFailure)
                return Result.Failure<List<ElderlyImportedSearchItem>>(result.ErrorCode!, result.Message!);

            return Result.Success(result.Value ?? new List<ElderlyImportedSearchItem>());
        }
        catch (Exception ex)
        {
            LogException(ex, "SearchImportedLibraryAsync");
            return Result.FromException<List<ElderlyImportedSearchItem>>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result<ElderlyImportedSearchItem?>> GetImportedByIdCardAsync(string idCard, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idCard))
            return Result.Success<ElderlyImportedSearchItem?>(null);

        try
        {
            const string sql = @"
                SELECT id, name, id_card, gender, birth_date, age, phone, address,
                       subsidy_amount, bank_account, person_type, original_type, data_year
                FROM nc_biz_elderly_subsidy_history
                WHERE id_card = $1
                ORDER BY data_year DESC, id DESC
                LIMIT 1";
            var result = await _db.QuerySingleAsync<ElderlyImportedSearchItem>(sql, ct, idCard.Trim());
            if (result.IsFailure)
                return Result.Failure<ElderlyImportedSearchItem?>(result.ErrorCode!, result.Message!);

            return Result.Success<ElderlyImportedSearchItem?>(result.Value);
        }
        catch (Exception ex)
        {
            LogException(ex, "GetImportedByIdCardAsync");
            return Result.FromException<ElderlyImportedSearchItem?>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result<long>> CreateAutoDraftAsync(string idCard, string name, string? createdBy, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idCard))
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "身份证不能为空");

        // 已有草稿 → 复用，避免重复（已在享/已停发记录不阻塞新建，属"停旧增新"）
        const string draftSql = "SELECT id FROM nc_biz_elderly_applications WHERE id_card = $1 AND status = 'Draft' AND deleted_at IS NULL ORDER BY id DESC LIMIT 1";
        var draftResult = await _db.QuerySingleAsync<long?>(draftSql, ct, idCard);
        if (draftResult.IsFailure)
            return Result.Failure<long>(draftResult.ErrorCode!, draftResult.Message!);
        if (draftResult.Value.HasValue)
            return Result.Success(draftResult.Value.Value);

        var applyDate = DateTime.Today;
        var evaluateResult = await EvaluateAsync(idCard, applyDate, ct);
        if (evaluateResult.IsFailure)
            return Result.Failure<long>(evaluateResult.ErrorCode!, evaluateResult.Message!);
        var eval = evaluateResult.Value;

        await using var tx = await _db.BeginTransactionScopeAsync(ct);
        try
        {
            var appNoResult = await GetNextApplicationNoAsync(ct);
            if (appNoResult.IsFailure)
                return Result.Failure<long>(appNoResult.ErrorCode!, appNoResult.Message!);

            const string sql = @"
                INSERT INTO nc_biz_elderly_applications
                (application_no, name, id_card, gender, birth_date, phone,
                 hukou_address, hukou_village,
                 hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id, hukou_detail_address,
                 family_address, family_city_id, family_county_id, family_town_id, family_village_id, detail_address,
                 bank_name, bank_account, agent_name, agent_relation,
                 agent_receive_name, agent_receive_relation, agent_receive_bank_name, agent_receive_bank_account, agent_receive_reason,
                 issue_start_month, issue_amount,
                 category, identity_flag, identity_source, is_category_manual,
                 payback_start_month, payback_end_month, auto_start_month, auto_end_month,
                 payback_months, payback_amount, is_special_case, special_reason, payback_reason,
                 status, apply_date, created_by, created_at, updated_at)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,
                        $20,$21,$22,$23,$24,$25,$26,$27,$28,$29,$30,$31,$32,$33,$34,$35,$36,
                        $37,$38,$39,$40,$41,$42,$43,$44,$45,$46,
                        NOW(), NOW())
                RETURNING id";

            var insertResult = await _db.ExecuteScalarAsync(sql, ct,
                appNoResult.Value,
                name ?? string.Empty, idCard, eval.Gender ?? "", eval.BirthDate, string.Empty,
                string.Empty, string.Empty,
                null, null, null, null, string.Empty,
                string.Empty, null, null, null, null, string.Empty,
                string.Empty, string.Empty, string.Empty, string.Empty,
                string.Empty, string.Empty, string.Empty, string.Empty, string.Empty,
                eval.IssueStartMonth ?? string.Empty, eval.IssueAmount,
                eval.Category ?? string.Empty, eval.IdentityFlag ?? string.Empty, eval.IdentitySource ?? string.Empty, false,
                eval.Payback.StartMonth ?? string.Empty, eval.Payback.EndMonth ?? string.Empty,
                eval.Payback.StartMonth ?? string.Empty, eval.Payback.EndMonth ?? string.Empty,
                eval.Payback.TotalMonths, eval.Payback.TotalAmount, false, string.Empty, string.Empty,
                ElderlyBenefitConstants.StatusDraft, applyDate, createdBy ?? "System");

            if (insertResult.IsFailure)
                return Result.Failure<long>(insertResult.ErrorCode!, insertResult.Message!);

            var newId = insertResult.Value;
            if (eval.Payback.Segments != null && eval.Payback.Segments.Count > 0)
            {
                var segmentResult = await InsertSegmentsAsync(newId, eval.Payback.Segments, ct);
                if (segmentResult.IsFailure)
                    return Result.Failure<long>(segmentResult.ErrorCode!, segmentResult.Message!);
            }

            await tx.CommitAsync(ct);

            Logger.LogBusiness("需停旧增新-自动生成草稿",
                ("ApplicationId", newId),
                ("ApplicationNo", appNoResult.Value),
                ("Name", DataMasker.MaskName(name ?? string.Empty)),
                ("IdCard", DataMasker.MaskIdCard(idCard)));
            return Result.Success(newId);
        }
        catch (Exception ex)
        {
            LogException(ex, "需停旧增新-自动生成草稿失败");
            return Result.FromException<long>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result<long>> MigrateFromHistoryAsync(long historyId, string createdBy, CancellationToken ct = default)
    {
        try
        {
            // 1. 查询导入库记录
            var historySql = @"
                SELECT id, name, id_card, gender, birth_date, age, phone, address,
                       subsidy_amount, bank_account, person_type, original_type, data_year, imported_at
                FROM nc_biz_elderly_subsidy_history
                WHERE id = $1";

            var historyResult = await _db.QuerySingleAsync<ElderlyImportedSearchItem>(historySql, ct, historyId);
            if (historyResult.IsFailure || historyResult.Value == null)
                return Result.Failure<long>(ErrorCodes.RECORD_NOT_FOUND, "导入库记录不存在");

            var history = historyResult.Value;

            // 2. 检查是否已建档（幂等）
            var existingSql = "SELECT id FROM nc_biz_elderly_applications WHERE id_card = $1 AND deleted_at IS NULL LIMIT 1";
            var existingResult = await _db.QuerySingleAsync<long?>(existingSql, ct, history.IdCard);
            if (existingResult.IsSuccess && existingResult.Value.HasValue)
            {
                LogInfo($"导入库记录已建档: historyId={historyId}, existingAppId={existingResult.Value.Value}");
                return Result.Success(existingResult.Value.Value);
            }

            // 3. 生成申请编号
            var appNoResult = await GetNextApplicationNoAsync(ct);
            if (appNoResult.IsFailure)
                return Result.Failure<long>(appNoResult.ErrorCode!, appNoResult.Message!);

            // 4. 使用当天日期作为受理日期和创建时间
            var applyDate = DateTime.Today;

            // 5. 创建新档案（status=Confirmed，可直接停发）
            var insertSql = @"
                INSERT INTO nc_biz_elderly_applications
                (application_no, name, id_card, gender, birth_date, phone, hukou_address,
                 bank_account, status, apply_date, created_at, created_by,
                 source_type, source_id)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14)
                RETURNING id";

            var insertResult = await _db.ExecuteScalarAsync<long>(insertSql, ct,
                appNoResult.Value, history.Name, history.IdCard, history.Gender, history.BirthDate,
                history.Phone, history.Address, history.BankAccount,
                ElderlyBenefitConstants.StatusConfirmed, applyDate, applyDate, createdBy,
                "ElderlySubsidyHistory", historyId);

            if (insertResult.IsFailure)
                return Result.Failure<long>(insertResult.ErrorCode!, insertResult.Message!);

            var newAppId = insertResult.Value;

            // 6. 调用评估逻辑补全 category/identity/issue 字段
            var evaluateResult = await EvaluateAsync(history.IdCard, applyDate, ct);
            if (evaluateResult.IsSuccess && evaluateResult.Value != null)
            {
                var eval = evaluateResult.Value;
                var updateSql = @"
                    UPDATE nc_biz_elderly_applications SET
                        category = $1, identity_flag = $2, identity_source = $3,
                        issue_start_month = $4, issue_amount = $5,
                        updated_at = NOW()
                    WHERE id = $6 AND deleted_at IS NULL";
                await _db.ExecuteNonQueryAsync(updateSql, ct,
                    eval.Category, eval.IdentityFlag, eval.IdentitySource,
                    eval.IssueStartMonth, eval.IssueAmount,
                    newAppId);
            }

            Logger.LogBusiness("从导入库建档",
                ("HistoryId", historyId),
                ("NewApplicationId", newAppId),
                ("Name", history.Name),
                ("ApplyDate", applyDate));

            return Result.Success(newAppId);
        }
        catch (Exception ex)
        {
            LogException(ex, "MigrateFromHistoryAsync");
            return Result.FromException<long>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result> DeleteHistoryAsync(long historyId, CancellationToken ct = default)
    {
        try
        {
            var sql = "DELETE FROM nc_biz_elderly_subsidy_history WHERE id = $1";
            var result = await _db.ExecuteNonQueryAsync(sql, ct, historyId);
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    result.Message ?? "删除导入库记录失败");

            LogInfo($"删除高龄导入库记录: historyId={historyId}");
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "DeleteHistoryAsync");
            return Result.FromException(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result<int>> CreateDraftsForArchivedApplicationAsync(long applicationId, CancellationToken ct = default)
    {
        try
        {
            // 触发源档案：已完结 + 排除单人保（单人保档案不触发高龄联动，人员随家庭档案走）
            const string appSql = @"
                SELECT applicant_name, applicant_id_card, classification_result, address
                FROM nc_biz_applications
                WHERE id = $1 AND deleted_at IS NULL AND current_step = 6";
            var appResult = await _db.QuerySingleAsync<ArchivedApplicationRow>(appSql, ct, applicationId);
            if (appResult.IsFailure || appResult.Value == null)
                return Result.Success(0);

            var app = appResult.Value;
            if (!string.IsNullOrEmpty(app.ClassificationResult)
                && ClassificationConstants.IsCodeSingleRescue(app.ClassificationResult))
            {
                LogInfo($"单人保档案不触发普惠高龄草稿联动: ApplicationId={applicationId}");
                return Result.Success(0);
            }

            // 候选人员 = 户主/申请人 + 共同生活成员（赡养抚养人不住本户不参与）；排除死亡登记成员
            const string memberSql = @"
                SELECT DISTINCT m.name, m.id_card
                FROM nc_biz_family_members m
                WHERE m.application_id = $1 AND m.deleted_at IS NULL
                  AND m.id_card IS NOT NULL AND length(m.id_card) = 18
                  AND (m.is_applicant OR m.member_category = 'SharedLiving')
                  AND NOT EXISTS (
                      SELECT 1 FROM nc_biz_death_records d
                      WHERE d.application_id = m.application_id AND d.member_id_card = m.id_card)";
            var membersResult = await _db.QueryAsync<ArchivedMemberRow>(memberSql, ct, applicationId);
            if (membersResult.IsFailure)
                return Result.Failure<int>(membersResult.ErrorCode!, membersResult.Message!);

            // 身份证去重合并候选名单
            var candidates = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var m in membersResult.Value ?? new List<ArchivedMemberRow>())
            {
                if (!string.IsNullOrWhiteSpace(m.IdCard) && !candidates.ContainsKey(m.IdCard))
                    candidates[m.IdCard] = m.Name ?? string.Empty;
            }

            var created = 0;
            foreach (var (idCard, name) in candidates)
            {
                // 满80周岁精确到月（口径与导入迁移 TryCreateElderlyBenefitsAsync 一致）
                var birthDate = IdCardValidator.ExtractBirthDate(idCard);
                if (birthDate == null) continue;
                var ageMonths = (DateTime.Today.Year - birthDate.Value.Year) * 12
                              + (DateTime.Today.Month - birthDate.Value.Month);
                if (ageMonths < ElderlyBenefitConstants.Threshold80 * 12) continue;

                // 查重：任意未删除登记记录（含停发历史）均视为已登记，不重复自动建
                var existsResult = await CheckIdCardExistsAsync(idCard, null, ct);
                if (existsResult.IsSuccess && existsResult.Value)
                {
                    LogInfo($"归档联动跳过（已有普惠高龄登记）: {DataMasker.MaskName(name)}");
                    continue;
                }

                var eval = await EvaluateAsync(idCard, DateTime.Now, ct);
                if (eval.IsFailure || eval.Value == null)
                {
                    LogWarn($"归档联动评估失败（跳过）: {DataMasker.MaskName(name)}, {eval.Message}");
                    continue;
                }

                var segments = eval.Value.Payback?.Segments?
                    .Select(s => new ElderlyPaybackSegment
                    {
                        SegmentStartMonth = s.SegmentStartMonth,
                        SegmentEndMonth = s.SegmentEndMonth,
                        MonthlyAmount = s.MonthlyAmount,
                        Months = s.Months,
                        SegmentAmount = s.SegmentAmount
                    }).ToList() ?? new List<ElderlyPaybackSegment>();

                var elderlyApp = new ElderlyApplication
                {
                    Name = name,
                    IdCard = idCard,
                    Gender = eval.Value.Gender,
                    BirthDate = eval.Value.BirthDate,
                    HukouAddress = app.Address ?? string.Empty,
                    FamilyAddress = app.Address ?? string.Empty,
                    Category = eval.Value.Category,
                    IdentityFlag = eval.Value.IdentityFlag,
                    IdentitySource = eval.Value.IdentitySource,
                    IssueStartMonth = eval.Value.IssueStartMonth,
                    IssueAmount = eval.Value.IssueAmount,
                    Status = ElderlyBenefitConstants.StatusDraft,
                    ApplyDate = DateTime.Now
                };

                var createResult = await CreateAsync(elderlyApp, segments, ct);
                if (createResult.IsSuccess)
                {
                    created++;
                    Logger.LogBusiness("归档自动创建普惠高龄草稿",
                        ("ApplicationId", applicationId),
                        ("ElderlyId", createResult.Value),
                        ("Name", DataMasker.MaskName(name)),
                        ("Age", (ageMonths / 12).ToString()),
                        ("Category", eval.Value.Category));
                }
                else
                {
                    LogWarn($"归档联动创建草稿失败（继续处理其余成员）: {DataMasker.MaskName(name)}, {createResult.Message}");
                }
            }

            return Result.Success(created);
        }
        catch (Exception ex)
        {
            LogException(ex, "CreateDraftsForArchivedApplicationAsync");
            return Result.FromException<int>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result<ElderlyPendingCounts>> GetPendingElderlyCountsAsync(CancellationToken ct = default)
    {
        try
        {
            // 人员集：已完结（排除单人保）档案的户主/申请人与共同生活成员（排除死亡登记），身份证去重；
            // 满80判定按身份证出生段精确到月；分流：
            //   待新增   = 正式表无 Confirmed 且历史名册无记录
            //   待停旧增新 = 正式表 Confirmed 在享 或 历史名册有记录
            const string sql = @"
                WITH target_apps AS (
                    SELECT a.id, a.applicant_id_card
                    FROM nc_biz_applications a
                    WHERE a.current_step = 6 AND a.deleted_at IS NULL
                      AND a.classification_result IS NOT NULL
                      AND a.classification_result <> ALL($1)
                ),
                persons AS (
                    SELECT DISTINCT ta.applicant_id_card AS id_card
                    FROM target_apps ta
                    WHERE ta.applicant_id_card IS NOT NULL AND length(ta.applicant_id_card) = 18
                    UNION
                    SELECT DISTINCT m.id_card
                    FROM nc_biz_family_members m
                    JOIN target_apps ta ON ta.id = m.application_id
                    WHERE m.deleted_at IS NULL AND m.id_card IS NOT NULL AND length(m.id_card) = 18
                      AND (m.is_applicant OR m.member_category = 'SharedLiving')
                      AND NOT EXISTS (
                          SELECT 1 FROM nc_biz_death_records d
                          WHERE d.application_id = m.application_id AND d.member_id_card = m.id_card)
                ),
                qualified AS (
                    SELECT DISTINCT p.id_card
                    FROM persons p
                    WHERE (SUBSTRING(p.id_card, 7, 4)::int * 12 + SUBSTRING(p.id_card, 11, 2)::int)
                        <= (EXTRACT(YEAR FROM CURRENT_DATE)::int * 12 + EXTRACT(MONTH FROM CURRENT_DATE)::int - $2)
                )
                SELECT
                    COUNT(*) FILTER (WHERE
                        NOT EXISTS (SELECT 1 FROM nc_biz_elderly_applications e
                                    WHERE e.id_card = q.id_card AND e.deleted_at IS NULL AND e.status = 'Confirmed')
                        AND NOT EXISTS (SELECT 1 FROM nc_biz_elderly_subsidy_history h WHERE h.id_card = q.id_card)) AS pending_new,
                    COUNT(*) FILTER (WHERE
                        EXISTS (SELECT 1 FROM nc_biz_elderly_applications e
                                WHERE e.id_card = q.id_card AND e.deleted_at IS NULL AND e.status = 'Confirmed')
                        OR EXISTS (SELECT 1 FROM nc_biz_elderly_subsidy_history h WHERE h.id_card = q.id_card)) AS pending_transfer
                FROM qualified q";

            var excludeCodes = new[]
            {
                ClassificationConstants.RuralLowIncomeSingle,
                ClassificationConstants.UrbanLowIncomeSingle
            };
            // 满80周岁门槛（月数）：与 C# 侧 Threshold80 常量同源，禁止在 SQL 内写死 960
            var thresholdMonths = ElderlyBenefitConstants.Threshold80 * 12;
            var result = await _db.QuerySingleAsync<ElderlyPendingCounts>(sql, ct, excludeCodes, thresholdMonths);
            if (result.IsFailure)
                return Result.Failure<ElderlyPendingCounts>(result.ErrorCode!, result.Message!);

            return Result.Success(result.Value ?? new ElderlyPendingCounts());
        }
        catch (Exception ex)
        {
            LogException(ex, "GetPendingElderlyCountsAsync");
            return Result.FromException<ElderlyPendingCounts>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result<List<ElderlyPendingItem>>> GetPendingElderlyListAsync(CancellationToken ct = default)
    {
        try
        {
            // 人员集与 GetPendingElderlyCountsAsync 完全同口径：
            // 已完结（排除单人保）档案的户主/申请人与共同生活成员（排除死亡登记），身份证去重优先户主；
            // 满80判定按身份证出生段精确到月；分流：
            //   待新增   = 正式表无 Confirmed 且历史名册无记录
            //   待停旧增新 = 正式表 Confirmed 在享 或 历史名册有记录
            // 附带正式表在享ID/历史名册ID，供跳转办理。
            const string sql = @"
                WITH target_apps AS (
                    SELECT a.id, a.application_no, a.applicant_id_card, a.applicant_name
                    FROM nc_biz_applications a
                    WHERE a.current_step = 6 AND a.deleted_at IS NULL
                      AND a.classification_result IS NOT NULL
                      AND a.classification_result <> ALL($1)
                ),
                persons AS (
                    SELECT DISTINCT ta.id AS application_id, ta.application_no,
                           ta.applicant_id_card AS id_card, ta.applicant_name AS name, '户主' AS member_role
                    FROM target_apps ta
                    WHERE ta.applicant_id_card IS NOT NULL AND length(ta.applicant_id_card) = 18
                    UNION
                    SELECT DISTINCT ta.id, ta.application_no,
                           m.id_card, m.name, '共同生活成员' AS member_role
                    FROM nc_biz_family_members m
                    JOIN target_apps ta ON ta.id = m.application_id
                    WHERE m.deleted_at IS NULL AND m.id_card IS NOT NULL AND length(m.id_card) = 18
                      AND (m.is_applicant OR m.member_category = 'SharedLiving')
                      AND NOT EXISTS (
                          SELECT 1 FROM nc_biz_death_records d
                          WHERE d.application_id = m.application_id AND d.member_id_card = m.id_card)
                ),
                dedup AS (
                    SELECT DISTINCT ON (p.id_card)
                           p.application_id, p.application_no, p.id_card, p.name, p.member_role
                    FROM persons p
                    ORDER BY p.id_card, (p.member_role = '户主') DESC, p.application_id
                ),
                qualified AS (
                    SELECT d.application_id, d.application_no, d.id_card, d.name, d.member_role
                    FROM dedup d
                    WHERE (SUBSTRING(d.id_card, 7, 4)::int * 12 + SUBSTRING(d.id_card, 11, 2)::int)
                        <= (EXTRACT(YEAR FROM CURRENT_DATE)::int * 12 + EXTRACT(MONTH FROM CURRENT_DATE)::int - $2)
                ),
                with_status AS (
                    SELECT q.application_id, q.application_no, q.id_card, q.name, q.member_role,
                           e.id AS elderly_application_id, e.application_no AS elderly_application_no,
                           h.id AS history_id
                    FROM qualified q
                    LEFT JOIN nc_biz_elderly_applications e
                           ON e.id_card = q.id_card AND e.deleted_at IS NULL AND e.status = 'Confirmed'
                    LEFT JOIN nc_biz_elderly_subsidy_history h ON h.id_card = q.id_card AND h.status = 'Active'
                )
                SELECT application_id, application_no, id_card, name, member_role,
                       elderly_application_id, elderly_application_no, history_id,
                       CASE WHEN elderly_application_id IS NULL AND history_id IS NULL
                            THEN 'New' ELSE 'Transfer' END AS pending_type
                FROM with_status
                ORDER BY pending_type, name, id_card";

            var excludeCodes = new[]
            {
                ClassificationConstants.RuralLowIncomeSingle,
                ClassificationConstants.UrbanLowIncomeSingle
            };
            var thresholdMonths = ElderlyBenefitConstants.Threshold80 * 12;
            var result = await _db.QueryAsync<ElderlyPendingRow>(sql, ct, excludeCodes, thresholdMonths);
            if (result.IsFailure)
                return Result.Failure<List<ElderlyPendingItem>>(result.ErrorCode!, result.Message!);

            // 调试日志：SQL 原始返回行数
            var rawRows = result.Value ?? new List<ElderlyPendingRow>();
            LogInfo($"下月待办-SQL原始返回: {rawRows.Count} 行");
            foreach (var r in rawRows.Take(50))
            {
                LogInfo($"  SQL行: {r.Name} {r.IdCard?.Substring(Math.Max(0, (r.IdCard?.Length ?? 0) - 4))} " +
                    $"Type={r.PendingType} Role={r.MemberRole} AppId={r.ApplicationId} " +
                    $"ElderlyAppId={r.ElderlyApplicationId} HistoryId={r.HistoryId}");
            }

            var items = new List<ElderlyPendingItem>();
            foreach (var r in result.Value ?? new List<ElderlyPendingRow>())
            {
                var item = new ElderlyPendingItem
                {
                    PendingType = string.IsNullOrEmpty(r.PendingType) ? ElderlyBenefitConstants.PendingTypeNew : r.PendingType,
                    Name = r.Name ?? string.Empty,
                    IdCard = r.IdCard ?? string.Empty,
                    MemberRole = r.MemberRole ?? string.Empty,
                    SourceApplicationId = r.ApplicationId,
                    SourceApplicationNo = r.ApplicationNo ?? string.Empty,
                    ElderlyApplicationId = r.ElderlyApplicationId,
                    ElderlyApplicationNo = r.ElderlyApplicationNo ?? string.Empty,
                    HistoryId = r.HistoryId,
                    Gender = IdCardValidator.ExtractGender(r.IdCard ?? string.Empty) ?? string.Empty
                };

                var birthDate = IdCardValidator.ExtractBirthDate(r.IdCard ?? string.Empty);
                item.BirthDate = birthDate;
                if (birthDate.HasValue)
                {
                    var today = DateTime.Today;
                    var age = today.Year - birthDate.Value.Year;
                    if (birthDate.Value.Date > today.AddYears(-age)) age--;
                    item.Age = age;
                }

                items.Add(item);
            }

            // 调试日志：逐条记录分流结果，便于排查"下月待办缺人"问题
            foreach (var item in items)
            {
                LogInfo($"下月待办-分流: {item.Name} {DataMasker.MaskIdCard(item.IdCard)} " +
                    $"Type={item.PendingType} SrcAppId={item.SourceApplicationId} " +
                    $"ElderlyAppId={item.ElderlyApplicationId} HistoryId={item.HistoryId}");
            }

            LogInfo($"下月待办明细加载完成: 共 {items.Count} 人（待新增={items.Count(i => !i.IsTransfer)}，停旧增新={items.Count(i => i.IsTransfer)}）");
            return Result.Success(items);
        }
        catch (Exception ex)
        {
            LogException(ex, "GetPendingElderlyListAsync");
            return Result.FromException<List<ElderlyPendingItem>>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result<Dictionary<string, decimal>>> GetHistorySubsidyAmountsAsync(IEnumerable<string> idCards, CancellationToken ct = default)
    {
        var idCardList = idCards?
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? new List<string>();
        if (idCardList.Count == 0)
            return Result.Success(new Dictionary<string, decimal>());

        try
        {
            var sql = @"SELECT id_card, subsidy_amount
                        FROM nc_biz_elderly_subsidy_history
                        WHERE id_card = ANY($1)
                        ORDER BY data_year DESC NULLS LAST, id";
            // idCardList 是 List<string>：直接传递（展开为单个 text[] 参数）。
        // 勿改为 .ToArray()——单个 string[] 实参会因数组协变被当作 params 形参数组本身，
        // 导致 $1 退化为首个标量，ANY($1) 报 42809（同 NewPermissionService 重建 DELETE 的教训）。
        var result = await _db.QueryAsync<HistorySubsidyAmountRow>(sql, ct, idCardList);
            if (result.IsFailure)
                return Result.Failure<Dictionary<string, decimal>>(result.ErrorCode!, result.Message!);

            var map = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var row in result.Value ?? new List<HistorySubsidyAmountRow>())
            {
                // 同身份证多条时取最新 data_year（排序已保证首条最新）
                if (!map.ContainsKey(row.IdCard) && row.SubsidyAmount.HasValue)
                    map[row.IdCard] = row.SubsidyAmount.Value;
            }
            LogInfo($"历史档案发放金额读取: 请求 {idCardList.Count} 人, 命中 {map.Count} 人");
            return Result.Success(map);
        }
        catch (Exception ex)
        {
            LogException(ex, "GetHistorySubsidyAmountsAsync");
            return Result.FromException<Dictionary<string, decimal>>(ex);
        }
    }

    /// <summary>历史名册发放金额行（snake_case 列自动映射）</summary>
    private sealed class HistorySubsidyAmountRow
    {
        public string IdCard { get; set; } = string.Empty;
        public decimal? SubsidyAmount { get; set; }
    }

    /// <summary>归档联动用：触发源档案行（snake_case 列自动映射）</summary>
    private sealed class ArchivedApplicationRow
    {
        public string ApplicantName { get; set; } = string.Empty;
        public string ApplicantIdCard { get; set; } = string.Empty;
        public string? ClassificationResult { get; set; }
        public string? Address { get; set; }
    }

    /// <summary>归档联动用：家庭成员行</summary>
    private sealed class ArchivedMemberRow
    {
        public string Name { get; set; } = string.Empty;
        public string IdCard { get; set; } = string.Empty;
    }

    /// <summary>下月待办明细行（snake_case 列自动映射 PascalCase 属性）</summary>
    private sealed class ElderlyPendingRow
    {
        public long ApplicationId { get; set; }
        public string ApplicationNo { get; set; } = string.Empty;
        public string IdCard { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string MemberRole { get; set; } = string.Empty;
        public long ElderlyApplicationId { get; set; }
        public string ElderlyApplicationNo { get; set; } = string.Empty;
        public long HistoryId { get; set; }
        public string PendingType { get; set; } = string.Empty;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  类别复核（身份 + 年龄重评，停旧增新）
    // ═══════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task<Result<int>> TriggerReviewsForHouseholdAsync(long applicationId, string triggerSource, CancellationToken ct = default)
    {
        try
        {
            // 申请人 + 共同生活成员（赡养抚养人不参与）；排除死亡登记成员
            const string appSql = "SELECT applicant_id_card FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL";
            var appResult = await _db.QuerySingleAsync<string?>(appSql, ct, applicationId);
            if (appResult.IsFailure)
                return Result.Failure<int>(appResult.ErrorCode!, appResult.Message!);

            const string memberSql = @"
                SELECT DISTINCT m.id_card
                FROM nc_biz_family_members m
                WHERE m.application_id = $1 AND m.deleted_at IS NULL
                  AND m.id_card IS NOT NULL AND length(m.id_card) = 18
                  AND (m.is_applicant OR m.member_category = 'SharedLiving')
                  AND NOT EXISTS (
                      SELECT 1 FROM nc_biz_death_records d
                      WHERE d.application_id = m.application_id AND d.member_id_card = m.id_card)";
            var membersResult = await _db.QueryAsync<string>(memberSql, ct, applicationId);
            if (membersResult.IsFailure)
                return Result.Failure<int>(membersResult.ErrorCode!, membersResult.Message!);

            var cards = new List<string>();
            if (!string.IsNullOrWhiteSpace(appResult.Value)) cards.Add(appResult.Value!);
            cards.AddRange(membersResult.Value ?? new List<string>());

            return await TriggerReviewsForIdCardsAsync(cards, triggerSource, applicationId,
                "低收入家庭信息变更", ct);
        }
        catch (Exception ex)
        {
            LogException(ex, "TriggerReviewsForHouseholdAsync");
            return Result.FromException<int>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result<int>> TriggerReviewsForIdCardsAsync(IEnumerable<string> idCards, string triggerSource,
        long? triggerRef, string? triggerReason, CancellationToken ct = default)
    {
        var cards = idCards?
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList() ?? new List<string>();
        if (cards.Count == 0) return Result.Success(0);

        var created = 0;
        foreach (var card in cards)
        {
            try
            {
                var evalResult = await EvaluateReviewAsync(null, card, null, ct);
                if (evalResult.IsFailure || evalResult.Value == null)
                {
                    LogDebug($"复核联动跳过（无法评估）: {DataMasker.MaskIdCard(card)} {evalResult.Message}");
                    continue;
                }
                var eval = evalResult.Value;
                if (!eval.HasChange) continue;

                // 在享档案必须为 Confirmed（已停发档案不入队）
                if (!eval.IsHistoryOnly && eval.ApplicationId is > 0)
                {
                    var status = await _db.ExecuteScalarAsync<string>(
                        "SELECT status FROM nc_biz_elderly_applications WHERE id = $1 AND deleted_at IS NULL",
                        ct, eval.ApplicationId.Value);
                    if (status.IsFailure || !string.Equals(status.Value, ElderlyBenefitConstants.StatusConfirmed, StringComparison.Ordinal))
                        continue;
                }

                // 待复核去重：同一身份证仅保留一条 Pending
                var existsResult = await _db.ExecuteScalarAsync<long?>(
                    "SELECT id FROM nc_biz_elderly_reviews WHERE id_card = $1 AND status = $2 AND deleted_at IS NULL LIMIT 1",
                    ct, card, ElderlyBenefitConstants.ReviewStatusPending);
                if (existsResult.IsSuccess && existsResult.Value is > 0) continue;

                await InsertPendingReviewAsync(eval, triggerSource, triggerRef, triggerReason, ct);
                created++;
            }
            catch (Exception ex)
            {
                LogWarn($"复核联动单人生成失败（继续）: {DataMasker.MaskIdCard(card)}, {ex.Message}");
            }
        }

        if (created > 0)
        {
            Logger.LogBusiness("复核联动写入待复核队列",
                ("TriggerSource", triggerSource),
                ("TriggerRef", triggerRef?.ToString() ?? ""),
                ("Created", created));
        }
        return Result.Success(created);
    }

    /// <inheritdoc />
    public async Task<Result<ElderlyReviewEvaluation>> EvaluateReviewAsync(long? applicationId, string? idCard, long? historyId, CancellationToken ct = default)
    {
        try
        {
            if (applicationId is > 0)
                return await BuildEvaluationFromApplicationAsync(applicationId.Value, ct);

            if (historyId is > 0)
            {
                var row = await LoadHistoryReviewRowAsync(historyId.Value, null, ct);
                if (row == null)
                    return Result.Failure<ElderlyReviewEvaluation>(ErrorCodes.RECORD_NOT_FOUND, "名册记录不存在");
                return await BuildEvaluationFromHistoryAsync(row, ct);
            }

            if (string.IsNullOrWhiteSpace(idCard))
                return Result.Failure<ElderlyReviewEvaluation>(ErrorCodes.VALIDATION_FAILED, "请提供身份证号或在享档案");

            // 先查当前库（优先在享），命中则按当前档案评估；否则回落名册
            const string appSql = @"
                SELECT id FROM nc_biz_elderly_applications
                WHERE id_card = $1 AND deleted_at IS NULL
                ORDER BY CASE WHEN status = 'Confirmed' THEN 0 ELSE 1 END, id DESC
                LIMIT 1";
            var appIdResult = await _db.QuerySingleAsync<long?>(appSql, ct, idCard.Trim());
            if (appIdResult.IsFailure)
                return Result.Failure<ElderlyReviewEvaluation>(appIdResult.ErrorCode!, appIdResult.Message!);
            if (appIdResult.Value is > 0)
                return await BuildEvaluationFromApplicationAsync(appIdResult.Value.Value, ct);

            var historyRow = await LoadHistoryReviewRowAsync(null, idCard.Trim(), ct);
            if (historyRow == null)
                return Result.Failure<ElderlyReviewEvaluation>(ErrorCodes.RECORD_NOT_FOUND, "未找到该人员的高龄档案或名册记录");
            return await BuildEvaluationFromHistoryAsync(historyRow, ct);
        }
        catch (Exception ex)
        {
            LogException(ex, "EvaluateReviewAsync");
            return Result.FromException<ElderlyReviewEvaluation>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result<List<ElderlyReview>>> GetPendingReviewsAsync(CancellationToken ct = default)
    {
        var sql = $@"SELECT * FROM nc_biz_elderly_reviews
                     WHERE status = '{ElderlyBenefitConstants.ReviewStatusPending}' AND deleted_at IS NULL
                     ORDER BY created_at, id";
        var result = await _db.QueryAsync<ElderlyReview>(sql, ct);
        if (result.IsFailure)
            return Result.Failure<List<ElderlyReview>>(result.ErrorCode!, result.Message!);
        return Result.Success(result.Value ?? new List<ElderlyReview>());
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<ElderlyReview>>> GetReviewsPagedAsync(string keyword, string status, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        var conditions = new SqlConditionBuilder()
            .Add("deleted_at IS NULL")
            .AddIf(!string.IsNullOrEmpty(status), "status = {0}", status)
            .AddIf(!string.IsNullOrWhiteSpace(keyword), "(name ILIKE {0} OR id_card ILIKE {0})", $"%{keyword}%");

        var where = conditions.ToWhereClause();
        var countSql = $"SELECT COUNT(*) FROM nc_biz_elderly_reviews{where}";
        var querySql = $"SELECT * FROM nc_biz_elderly_reviews{where}";

        var countResult = await _db.ExecuteScalarAsync<long>(countSql, ct, conditions.GetParameters());
        if (countResult.IsFailure)
            return Result.Failure<PagedResult<ElderlyReview>>(countResult.ErrorCode!, countResult.Message!);

        var offset = (pageIndex - 1) * pageSize;
        querySql += $" ORDER BY COALESCE(reviewed_at, created_at) DESC, id DESC LIMIT ${conditions.ParamCount + 1} OFFSET ${conditions.ParamCount + 2}";
        var pageParams = new List<object?>(conditions.GetParameters()) { pageSize, offset };

        var listResult = await _db.QueryAsync<ElderlyReview>(querySql, ct, pageParams.ToArray());
        if (listResult.IsFailure)
            return Result.Failure<PagedResult<ElderlyReview>>(listResult.ErrorCode!, listResult.Message!);

        return Result.Success(PagedResult<ElderlyReview>.FromList(listResult.Value, pageIndex, pageSize, Convert.ToInt32(countResult.Value)));
    }

    /// <inheritdoc />
    public async Task<Result<List<ElderlyReview>>> GetReviewsByPersonAsync(string idCard, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idCard)) return Result.Success(new List<ElderlyReview>());
        const string sql = @"SELECT * FROM nc_biz_elderly_reviews
                             WHERE id_card = $1 AND deleted_at IS NULL
                             ORDER BY COALESCE(reviewed_at, created_at) DESC, id DESC";
        var result = await _db.QueryAsync<ElderlyReview>(sql, ct, idCard.Trim());
        if (result.IsFailure)
            return Result.Failure<List<ElderlyReview>>(result.ErrorCode!, result.Message!);
        return Result.Success(result.Value ?? new List<ElderlyReview>());
    }

    /// <inheritdoc />
    public async Task<Result<long>> ConfirmReviewAsync(long? reviewId, long? applicationId, string? idCard, long? historyId,
        string reviewOpinion, string operatorName, CancellationToken ct = default)
    {
        var evalResult = reviewId is > 0
            ? await EvaluateReviewByPendingAsync(reviewId.Value, ct)
            : await EvaluateReviewAsync(applicationId, idCard, historyId, ct);
        if (evalResult.IsFailure || evalResult.Value == null)
            return Result.Failure<long>(evalResult.ErrorCode ?? ErrorCodes.REVIEW_EVALUATE_FAILED,
                evalResult.Message ?? "复核评估失败");
        var eval = evalResult.Value;

        // 在享档案必须为 Confirmed（名册人员除外）
        if (!eval.IsHistoryOnly && eval.ApplicationId is > 0)
        {
            var statusResult = await _db.QuerySingleAsync<string>(
                "SELECT status FROM nc_biz_elderly_applications WHERE id = $1 AND deleted_at IS NULL",
                ct, eval.ApplicationId.Value);
            if (statusResult.IsFailure || !string.Equals(statusResult.Value, ElderlyBenefitConstants.StatusConfirmed, StringComparison.Ordinal))
                return Result.Failure<long>(ErrorCodes.ELDERLY_REVIEW_INVALID_STATUS, "仅支持对在享（已确认）的高龄档案办理复核");
        }

        await using var tx = await _db.BeginTransactionScopeAsync(ct);
        try
        {
            if (!eval.HasChange)
            {
                await UpsertCompletedReviewAsync(reviewId, eval, ElderlyBenefitConstants.ReviewResultNoChange,
                    0, 0, operatorName, reviewOpinion, ct);
                await tx.CommitAsync(ct);
                Logger.LogBusiness("高龄类别复核-无需调整",
                    ("IdCard", DataMasker.MaskIdCard(eval.IdCard)),
                    ("Category", eval.OldCategory),
                    ("Operator", operatorName));
                return Result.Success(eval.ApplicationId ?? 0);
            }

            // 名册未建档：先补建旧档案（承载旧类别），再停旧增新
            long oldAppId;
            if (eval.IsHistoryOnly)
            {
                var backfill = await BackfillHistoryArchiveAsync(eval, operatorName, ct);
                if (backfill.IsFailure)
                    return Result.Failure<long>(backfill.ErrorCode!, backfill.Message!);
                oldAppId = backfill.Value;
            }
            else
            {
                oldAppId = eval.ApplicationId ?? 0;
                if (oldAppId <= 0)
                    return Result.Failure<long>(ErrorCodes.RECORD_NOT_FOUND, "未找到待复核的在享档案");
            }

            // 停旧档（stop_reason=REVIEW，报表/统计排除）
            var stopResult = await StopOldApplicationForReviewAsync(oldAppId, operatorName, ct);
            if (stopResult.IsFailure)
                return Result.Failure<long>(stopResult.ErrorCode!, stopResult.Message!);

            // 建新档（Confirmed，次月起按新档计发，不补差）
            var newAppResult = await CreateReviewedApplicationAsync(oldAppId, eval, operatorName, ct);
            if (newAppResult.IsFailure)
                return Result.Failure<long>(newAppResult.ErrorCode!, newAppResult.Message!);
            var newAppId = newAppResult.Value;

            // 名册联动标记（按身份证兜底将 Active 标记 Stopped）
            await MarkHistoryStoppedAsync(oldAppId, ct);

            await UpsertCompletedReviewAsync(reviewId, eval, ElderlyBenefitConstants.ReviewResultChanged,
                oldAppId, newAppId, operatorName, reviewOpinion, ct);

            await tx.CommitAsync(ct);
            Logger.LogBusiness("高龄类别复核-已变更（停旧增新）",
                ("IdCard", DataMasker.MaskIdCard(eval.IdCard)),
                ("OldApplicationId", oldAppId),
                ("NewApplicationId", newAppId),
                ("OldCategory", eval.OldCategory),
                ("NewCategory", eval.NewCategory),
                ("Backfilled", eval.IsHistoryOnly),
                ("Operator", operatorName));
            return Result.Success(newAppId);
        }
        catch (Exception ex)
        {
            LogException(ex, "ConfirmReviewAsync");
            return Result.FromException<long>(ex);
        }
    }

    // ── 复核：评估构建 ──

    private async Task<Result<ElderlyReviewEvaluation>> BuildEvaluationFromApplicationAsync(long applicationId, CancellationToken ct)
    {
        var appResult = await GetByIdAsync(applicationId, ct);
        if (appResult.IsFailure || appResult.Value == null)
            return Result.Failure<ElderlyReviewEvaluation>(ErrorCodes.RECORD_NOT_FOUND, "高龄档案不存在");

        var app = appResult.Value;
        var birthDate = app.BirthDate ?? IdCardValidator.ExtractBirthDate(app.IdCard);
        if (birthDate == null)
            return Result.Failure<ElderlyReviewEvaluation>(ErrorCodes.VALIDATION_FAILED, "档案缺少出生日期，无法复核");

        var age = ComputeAge(birthDate.Value, DateTime.Today);
        var identityResult = await _categoryService.MatchIdentityAsync(app.IdCard, ct);
        if (identityResult.IsFailure)
            return Result.Failure<ElderlyReviewEvaluation>(identityResult.ErrorCode!, identityResult.Message!);
        var identityFlag = identityResult.Value.IdentityFlag;
        var newCategory = ElderlyPaybackCalculator.GetCategoryForAge(age, identityFlag);
        var newAmountResult = await GetMonthlyAmountAsync(newCategory, ct);
        if (newAmountResult.IsFailure)
            return Result.Failure<ElderlyReviewEvaluation>(newAmountResult.ErrorCode!, newAmountResult.Message!);

        var oldAmount = app.IssueAmount > 0 ? app.IssueAmount : await GetAmountOrZeroAsync(app.Category, ct);
        var oldIdentity = string.IsNullOrWhiteSpace(app.IdentityFlag) ? ElderlyBenefitConstants.IdentityNone : app.IdentityFlag;

        return Result.Success(BuildEvaluationCore(
            isHistoryOnly: false,
            applicationId: app.Id, applicationNo: app.ApplicationNo ?? string.Empty, historyId: null,
            name: app.Name, idCard: app.IdCard, gender: app.Gender, birthDate: birthDate.Value, age: age,
            oldIdentity: oldIdentity, identityFlag: identityFlag, identitySource: identityResult.Value.SourceTable,
            oldCategory: app.Category, oldAmount: oldAmount,
            newCategory: newCategory, newAmount: newAmountResult.Value));
    }

    private async Task<Result<ElderlyReviewEvaluation>> BuildEvaluationFromHistoryAsync(ElderlyHistoryReviewRow row, CancellationToken ct)
    {
        var birthDate = row.BirthDate;
        if (birthDate == null)
            return Result.Failure<ElderlyReviewEvaluation>(ErrorCodes.VALIDATION_FAILED, "名册缺少出生日期，无法复核");

        var age = ComputeAge(birthDate.Value, DateTime.Today);
        var oldIdentity = ElderlyBenefitConstants.GetIdentityFlagFromHistoryPersonType(row.PersonType);
        var oldCategory = ElderlyPaybackCalculator.GetCategoryForAge(age, oldIdentity);

        var identityResult = await _categoryService.MatchIdentityAsync(row.IdCard, ct);
        if (identityResult.IsFailure)
            return Result.Failure<ElderlyReviewEvaluation>(identityResult.ErrorCode!, identityResult.Message!);
        var identityFlag = identityResult.Value.IdentityFlag;
        var newCategory = ElderlyPaybackCalculator.GetCategoryForAge(age, identityFlag);
        var newAmountResult = await GetMonthlyAmountAsync(newCategory, ct);
        if (newAmountResult.IsFailure)
            return Result.Failure<ElderlyReviewEvaluation>(newAmountResult.ErrorCode!, newAmountResult.Message!);

        var oldAmount = row.SubsidyAmount ?? await GetAmountOrZeroAsync(oldCategory, ct);

        return Result.Success(BuildEvaluationCore(
            isHistoryOnly: true,
            applicationId: null, applicationNo: string.Empty, historyId: row.Id,
            name: row.Name, idCard: row.IdCard, gender: row.Gender, birthDate: birthDate.Value, age: age,
            oldIdentity: oldIdentity, identityFlag: identityFlag, identitySource: identityResult.Value.SourceTable,
            oldCategory: oldCategory, oldAmount: oldAmount,
            newCategory: newCategory, newAmount: newAmountResult.Value));
    }

    /// <summary>评估核心：判定调整原因/方向，计算应生效月与次月起计月</summary>
    private static ElderlyReviewEvaluation BuildEvaluationCore(
        bool isHistoryOnly, long? applicationId, string applicationNo, long? historyId,
        string name, string idCard, string? gender, DateTime birthDate, int age,
        string oldIdentity, string identityFlag, string identitySource,
        string oldCategory, decimal oldAmount, string newCategory, decimal newAmount)
    {
        var identityChanged = !string.Equals(
            string.IsNullOrWhiteSpace(oldIdentity) ? ElderlyBenefitConstants.IdentityNone : oldIdentity,
            string.IsNullOrWhiteSpace(identityFlag) ? ElderlyBenefitConstants.IdentityNone : identityFlag,
            StringComparison.Ordinal);

        var oldHigh = ElderlyBenefitConstants.IsHighSubsidyIdentity(oldIdentity);
        var newHigh = ElderlyBenefitConstants.IsHighSubsidyIdentity(identityFlag);

        var reason = identityChanged
            ? ElderlyBenefitConstants.AdjustReasonIdentity
            : (!string.Equals(oldCategory, newCategory, StringComparison.Ordinal)
                ? ElderlyBenefitConstants.AdjustReasonAge
                : ElderlyBenefitConstants.AdjustReasonOther);

        var effectiveMonth = string.Empty;
        if (reason == ElderlyBenefitConstants.AdjustReasonAge)
        {
            var threshold = newCategory == ElderlyBenefitConstants.Cat100Plus ? ElderlyBenefitConstants.Threshold100
                : newCategory == ElderlyBenefitConstants.Cat90To99 ? ElderlyBenefitConstants.Threshold90
                : 0;
            if (threshold > 0)
                effectiveMonth = birthDate.AddYears(threshold).ToString("yyyy-MM");
        }

        var issueStartMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).AddMonths(1).ToString("yyyy-MM");

        return new ElderlyReviewEvaluation
        {
            IsHistoryOnly = isHistoryOnly,
            ApplicationId = applicationId,
            ApplicationNo = applicationNo,
            HistoryId = historyId,
            Name = name ?? string.Empty,
            IdCard = idCard ?? string.Empty,
            Gender = gender ?? string.Empty,
            BirthDate = birthDate,
            Age = age,
            OldIdentityFlag = string.IsNullOrWhiteSpace(oldIdentity) ? ElderlyBenefitConstants.IdentityNone : oldIdentity,
            IdentityFlag = identityFlag ?? ElderlyBenefitConstants.IdentityNone,
            IdentitySource = identitySource ?? string.Empty,
            OldCategory = oldCategory ?? string.Empty,
            OldMonthlyAmount = oldAmount,
            NewCategory = newCategory ?? string.Empty,
            NewMonthlyAmount = newAmount,
            AdjustReasonCode = reason,
            IdentityAdd = newHigh && !oldHigh,
            EffectiveMonth = effectiveMonth,
            IssueStartMonth = issueStartMonth
        };
    }

    private async Task<decimal> GetAmountOrZeroAsync(string? category, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(category)) return 0m;
        var result = await GetMonthlyAmountAsync(category, ct);
        return result.IsSuccess ? result.Value : 0m;
    }

    private static int ComputeAge(DateTime birthDate, DateTime onDate)
    {
        var age = onDate.Year - birthDate.Year;
        if (birthDate.Date > onDate.AddYears(-age)) age--;
        return age;
    }

    private async Task<ElderlyHistoryReviewRow?> LoadHistoryReviewRowAsync(long? historyId, string? idCard, CancellationToken ct)
    {
        const string byIdSql = @"
            SELECT id, name, id_card, gender, birth_date, phone, address, bank_account,
                   person_type, subsidy_amount, data_year
            FROM nc_biz_elderly_subsidy_history WHERE id = $1";
        const string byCardSql = @"
            SELECT id, name, id_card, gender, birth_date, phone, address, bank_account,
                   person_type, subsidy_amount, data_year
            FROM nc_biz_elderly_subsidy_history WHERE id_card = $1 AND status = 'Active'
            ORDER BY data_year DESC NULLS LAST, id DESC LIMIT 1";
        // 名册检索仅命中在册发放（Active）；byId 不限制（队列记录可能随后被标记，仍需可评估）
        var result = historyId is > 0
            ? await _db.QuerySingleAsync<ElderlyHistoryReviewRow>(byIdSql, ct, historyId.Value)
            : await _db.QuerySingleAsync<ElderlyHistoryReviewRow>(byCardSql, ct, idCard);
        if (result.IsFailure || result.Value == null) return null;
        return result.Value;
    }

    // ── 复核：落库 ──

    private async Task InsertPendingReviewAsync(ElderlyReviewEvaluation eval, string triggerSource,
        long? triggerRef, string? triggerReason, CancellationToken ct)
    {
        var reviewNo = await GetNextReviewNoAsync(ct);
        const string sql = @"
            INSERT INTO nc_biz_elderly_reviews
            (review_no, id_card, name, status, trigger_source, trigger_ref, trigger_reason, source_history_id,
             old_application_id, old_application_no, old_category, old_monthly_amount,
             new_category, new_monthly_amount, identity_flag, age_at_review, effective_month, issue_start_month,
             adjust_reason_code, created_at, updated_at)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,NOW(),NOW())";
        var result = await _db.ExecuteNonQueryAsync(sql, ct,
            reviewNo, eval.IdCard, eval.Name, ElderlyBenefitConstants.ReviewStatusPending,
            triggerSource, triggerRef, triggerReason ?? string.Empty, eval.HistoryId,
            eval.ApplicationId, eval.ApplicationNo, eval.OldCategory, eval.OldMonthlyAmount,
            eval.NewCategory, eval.NewMonthlyAmount, eval.IdentityFlag, eval.Age,
            eval.EffectiveMonth, eval.IssueStartMonth, eval.AdjustReasonCode);
        if (result.IsFailure)
        {
            LogWarn($"写入待复核失败: {DataMasker.MaskIdCard(eval.IdCard)}, {result.Message}");
            return;
        }
        Logger.LogBusiness("写入待复核",
            ("IdCard", DataMasker.MaskIdCard(eval.IdCard)),
            ("Old", eval.OldCategory), ("New", eval.NewCategory),
            ("Trigger", triggerSource));
    }

    private async Task UpsertCompletedReviewAsync(long? reviewId, ElderlyReviewEvaluation eval, string reviewResult,
        long oldAppId, long newAppId, string operatorName, string reviewOpinion, CancellationToken ct)
    {
        var hasChange = reviewResult == ElderlyBenefitConstants.ReviewResultChanged;
        var oldAppNo = string.Empty;
        var newAppNo = string.Empty;
        if (oldAppId > 0)
        {
            var r = await _db.QuerySingleAsync<string?>(
                "SELECT application_no FROM nc_biz_elderly_applications WHERE id = $1", ct, oldAppId);
            if (r.IsSuccess) oldAppNo = r.Value ?? string.Empty;
        }
        if (newAppId > 0)
        {
            var r = await _db.QuerySingleAsync<string?>(
                "SELECT application_no FROM nc_biz_elderly_applications WHERE id = $1", ct, newAppId);
            if (r.IsSuccess) newAppNo = r.Value ?? string.Empty;
        }

        const string updateSql = @"
            UPDATE nc_biz_elderly_reviews SET
                status = $1, source_history_id = $2,
                old_application_id = $3, old_application_no = $4, old_category = $5, old_monthly_amount = $6,
                new_application_id = $7, new_application_no = $8, new_category = $9, new_monthly_amount = $10,
                identity_flag = $11, age_at_review = $12, effective_month = $13, issue_start_month = $14,
                review_result = $15, adjust_reason_code = $16, review_opinion = $17,
                reviewed_by = $18, reviewed_at = NOW(), updated_at = NOW()
            WHERE id = $19 AND deleted_at IS NULL";

        var oldCat = eval.OldCategory;
        var newCat = hasChange ? eval.NewCategory : string.Empty;
        var newAmount = hasChange ? eval.NewMonthlyAmount : (decimal?)null;

        if (reviewId is > 0)
        {
            var updateResult = await _db.ExecuteNonQueryAsync(updateSql, ct,
                ElderlyBenefitConstants.ReviewStatusCompleted, eval.HistoryId,
                oldAppId > 0 ? oldAppId : (object?)eval.ApplicationId, oldAppNo, oldCat, eval.OldMonthlyAmount,
                newAppId > 0 ? newAppId : null, newAppNo, newCat, newAmount,
                eval.IdentityFlag, eval.Age, eval.EffectiveMonth, eval.IssueStartMonth,
                reviewResult, eval.AdjustReasonCode, reviewOpinion ?? string.Empty,
                operatorName ?? "System", reviewId.Value);
            if (updateResult.IsFailure)
                LogWarn($"更新复核记录失败: {updateResult.Message}");
            return;
        }

        // 人工搜索无队列记录 → 直接补一条已完成记录
        var reviewNo = await GetNextReviewNoAsync(ct);
        const string insertSql = @"
            INSERT INTO nc_biz_elderly_reviews
            (review_no, id_card, name, status, trigger_source, trigger_ref, trigger_reason, source_history_id,
             old_application_id, old_application_no, old_category, old_monthly_amount,
             new_application_id, new_application_no, new_category, new_monthly_amount,
             identity_flag, age_at_review, effective_month, issue_start_month,
             review_result, adjust_reason_code, review_opinion, reviewed_by, reviewed_at, created_at, updated_at)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,$15,$16,$17,$18,$19,$20,$21,$22,$23,$24,NOW(),NOW(),NOW())";
        var insertResult = await _db.ExecuteNonQueryAsync(insertSql, ct,
            reviewNo, eval.IdCard, eval.Name, ElderlyBenefitConstants.ReviewStatusCompleted,
            ElderlyBenefitConstants.ReviewTriggerManual, null, string.Empty, eval.HistoryId,
            oldAppId > 0 ? oldAppId : (object?)eval.ApplicationId, oldAppNo, oldCat, eval.OldMonthlyAmount,
            newAppId > 0 ? newAppId : null, newAppNo, newCat, newAmount,
            eval.IdentityFlag, eval.Age, eval.EffectiveMonth, eval.IssueStartMonth,
            reviewResult, eval.AdjustReasonCode, reviewOpinion ?? string.Empty, operatorName ?? "System");
        if (insertResult.IsFailure)
            LogWarn($"写入复核记录失败: {insertResult.Message}");
    }

    /// <summary>名册未建档人员补建旧类别档案（source_type=ElderlyReview，不产生"本月新增"）</summary>
    private async Task<Result<long>> BackfillHistoryArchiveAsync(ElderlyReviewEvaluation eval, string operatorName, CancellationToken ct)
    {
        var row = eval.HistoryId is > 0 ? await LoadHistoryReviewRowAsync(eval.HistoryId, null, ct) : null;
        var appNoResult = await GetNextApplicationNoAsync(ct);
        if (appNoResult.IsFailure)
            return Result.Failure<long>(appNoResult.ErrorCode!, appNoResult.Message!);

        var currentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).ToString("yyyy-MM");
        const string sql = @"
            INSERT INTO nc_biz_elderly_applications
            (application_no, name, id_card, gender, birth_date, phone, bank_account,
             hukou_address, family_address,
             issue_start_month, issue_amount, category, identity_flag, identity_source,
             status, apply_date, confirmed_at, confirmed_by, remark, source_type, source_id,
             created_by, created_at, updated_at)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,
                    'Confirmed',$15,NOW(),$16,$17,$18,$19,$20,NOW(),NOW())
            RETURNING id";
        var result = await _db.ExecuteScalarAsync<long>(sql, ct,
            appNoResult.Value, eval.Name, eval.IdCard, eval.Gender, eval.BirthDate,
            row?.Phone ?? string.Empty, row?.BankAccount ?? string.Empty,
            row?.Address ?? string.Empty, row?.Address ?? string.Empty,
            currentMonth, eval.OldMonthlyAmount, eval.OldCategory, eval.OldIdentityFlag, "nc_biz_elderly_subsidy_history",
            DateTime.Today, operatorName ?? "System",
            $"复核补建（原类别 {ElderlyBenefitConstants.GetCategoryName(eval.OldCategory)}）",
            ElderlyBenefitConstants.SourceTypeElderlyReview, eval.HistoryId, operatorName ?? "System");
        if (result.IsFailure)
            return Result.Failure<long>(result.ErrorCode!, result.Message!);
        return Result.Success(result.Value);
    }

    private async Task<Result> StopOldApplicationForReviewAsync(long oldAppId, string operatorName, CancellationToken ct)
    {
        const string sql = @"
            UPDATE nc_biz_elderly_applications SET
                status = $1, stopped_at = NOW(), actual_stop_date = NOW(), due_stop_date = NOW(),
                stopped_by = $2, stop_reason = $3, remark = $4, updated_at = NOW()
            WHERE id = $5 AND deleted_at IS NULL";
        var result = await _db.ExecuteNonQueryAsync(sql, ct,
            ElderlyBenefitConstants.StatusStopped, operatorName ?? "System",
            ElderlyBenefitConstants.StopReasonReview, "类别复核停旧增新", oldAppId);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode!, result.Message!);
        if (result.Value == 0)
            return Result.Failure(ErrorCodes.RECORD_NOT_FOUND, "旧龄档案不存在或已删除");
        return Result.Success();
    }

    /// <summary>复制旧档人/址/银行字段，按新类别新建 Confirmed 档案（次月起计发，不补差）</summary>
    private async Task<Result<long>> CreateReviewedApplicationAsync(long oldAppId, ElderlyReviewEvaluation eval, string operatorName, CancellationToken ct)
    {
        var appNoResult = await GetNextApplicationNoAsync(ct);
        if (appNoResult.IsFailure)
            return Result.Failure<long>(appNoResult.ErrorCode!, appNoResult.Message!);

        var remark = $"类别复核调整，由{ElderlyBenefitConstants.GetCategoryName(eval.OldCategory)}" +
                     $"调整为{ElderlyBenefitConstants.GetCategoryName(eval.NewCategory)}";
        const string sql = @"
            INSERT INTO nc_biz_elderly_applications
            (application_no, name, id_card, gender, birth_date, phone,
             hukou_address, hukou_village, hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id, hukou_detail_address,
             family_address, family_city_id, family_county_id, family_town_id, family_village_id, detail_address,
             bank_name, bank_account, agent_name, agent_relation,
             agent_receive_name, agent_receive_relation, agent_receive_bank_name, agent_receive_bank_account, agent_receive_reason,
             issue_start_month, issue_amount, category, identity_flag, identity_source, is_category_manual,
             status, apply_date, confirmed_at, confirmed_by, remark, source_type, source_id,
             created_by, created_at, updated_at)
            SELECT $1, name, id_card, gender, birth_date, phone,
             hukou_address, hukou_village, hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id, hukou_detail_address,
             family_address, family_city_id, family_county_id, family_town_id, family_village_id, detail_address,
             bank_name, bank_account, agent_name, agent_relation,
             agent_receive_name, agent_receive_relation, agent_receive_bank_name, agent_receive_bank_account, agent_receive_reason,
             $2, $3, $4, $5, $6, false,
             'Confirmed', $7::date, NOW(), $8, $9, $10, $11, $8, NOW(), NOW()
            FROM nc_biz_elderly_applications WHERE id = $12 AND deleted_at IS NULL
            RETURNING id";
        var result = await _db.ExecuteScalarAsync<long>(sql, ct,
            appNoResult.Value, eval.IssueStartMonth, eval.NewMonthlyAmount,
            eval.NewCategory, eval.IdentityFlag, eval.IdentitySource ?? string.Empty,
            DateTime.Today, operatorName ?? "System", remark,
            ElderlyBenefitConstants.SourceTypeElderlyReview, oldAppId, oldAppId);
        if (result.IsFailure)
            return Result.Failure<long>(result.ErrorCode!, result.Message!);
        return Result.Success(result.Value);
    }

    private async Task<string> GetNextReviewNoAsync(CancellationToken ct)
    {
        var prefix = $"{ElderlyBenefitConstants.ReviewNoPrefix}{DateTime.Now:yyyyMMdd}";
        var sql = @"SELECT COALESCE(MAX(CAST(SUBSTRING(review_no FROM 11) AS INTEGER)), 0) + 1
                    FROM nc_biz_elderly_reviews WHERE review_no LIKE $1";
        var result = await _db.ExecuteScalarAsync<long>(sql, ct, $"{prefix}%");
        var seq = result.IsSuccess ? result.Value : 1;
        return $"{prefix}{seq:D4}";
    }

    #region 月报：满90周岁调整备案表

    /// <inheritdoc />
    public async Task<Result<List<ElderlyAge90Row>>> GetAge90AdjustRowsAsync(int year, int month, CancellationToken ct = default)
    {
        try
        {
            var monthStart = new DateTime(year, month, 1);
            var monthEnd = monthStart.AddMonths(1);
            var rows = new List<ElderlyAge90Row>();
            var newAmount = await GetAmountOrZeroAsync(ElderlyBenefitConstants.Cat90To99, ct);

            // 在享：本月满90周岁且尚未按90档计发（CAT1/CAT2）
            const string appSql = @"
                SELECT id, name, id_card, gender, birth_date, category, identity_flag,
                       issue_amount, hukou_address, family_address, detail_address
                FROM nc_biz_elderly_applications
                WHERE deleted_at IS NULL AND status = 'Confirmed'
                  AND category IN ('CAT1','CAT2')
                  AND birth_date IS NOT NULL
                  AND (birth_date + INTERVAL '90 years') >= $1
                  AND (birth_date + INTERVAL '90 years') < $2
                ORDER BY birth_date, id";
            var appResult = await _db.QueryAsync<Age90AppRow>(appSql, ct, monthStart, monthEnd);
            if (appResult.IsFailure)
                return Result.Failure<List<ElderlyAge90Row>>(appResult.ErrorCode!, appResult.Message!);
            foreach (var a in appResult.Value ?? new List<Age90AppRow>())
            {
                var oldAmount = a.IssueAmount > 0 ? a.IssueAmount : await GetAmountOrZeroAsync(a.Category, ct);
                rows.Add(new ElderlyAge90Row
                {
                    ApplicationId = a.Id,
                    Name = a.Name,
                    IdCard = a.IdCard,
                    Gender = a.Gender,
                    BirthDate = a.BirthDate,
                    IdentityFlag = string.IsNullOrWhiteSpace(a.IdentityFlag) ? ElderlyBenefitConstants.IdentityNone : a.IdentityFlag,
                    HukouAddress = a.HukouAddress ?? string.Empty,
                    FamilyAddress = CombineAddress(a.FamilyAddress, a.DetailAddress),
                    OldCategory = a.Category,
                    OldMonthlyAmount = oldAmount,
                    NewCategory = ElderlyBenefitConstants.Cat90To99,
                    NewMonthlyAmount = newAmount,
                    EffectiveMonth = a.BirthDate!.Value.AddYears(90).ToString("yyyy-MM")
                });
            }

            // 名册未建档：本月满90周岁（旧类别按 person_type 的 89 岁档）
            const string histSql = @"
                SELECT h.id, h.name, h.id_card, h.gender, h.birth_date, h.person_type, h.address, h.subsidy_amount
                FROM nc_biz_elderly_subsidy_history h
                WHERE h.status = 'Active'
                  AND h.birth_date IS NOT NULL
                  AND (h.birth_date + INTERVAL '90 years') >= $1
                  AND (h.birth_date + INTERVAL '90 years') < $2
                  AND NOT EXISTS (SELECT 1 FROM nc_biz_elderly_applications e
                                  WHERE e.id_card = h.id_card AND e.deleted_at IS NULL)
                ORDER BY h.birth_date, h.id";
            var histResult = await _db.QueryAsync<Age90HistRow>(histSql, ct, monthStart, monthEnd);
            if (histResult.IsFailure)
                return Result.Failure<List<ElderlyAge90Row>>(histResult.ErrorCode!, histResult.Message!);
            foreach (var h in histResult.Value ?? new List<Age90HistRow>())
            {
                var identity = ElderlyBenefitConstants.GetIdentityFlagFromHistoryPersonType(h.PersonType);
                var oldCategory = ElderlyPaybackCalculator.GetCategoryForAge(ElderlyBenefitConstants.Threshold90 - 1, identity);
                var oldAmount = await GetAmountOrZeroAsync(oldCategory, ct);
                rows.Add(new ElderlyAge90Row
                {
                    HistoryId = h.Id,
                    Name = h.Name,
                    IdCard = h.IdCard,
                    Gender = h.Gender,
                    BirthDate = h.BirthDate,
                    IdentityFlag = identity,
                    HukouAddress = h.Address ?? string.Empty,
                    FamilyAddress = h.Address ?? string.Empty,
                    OldCategory = oldCategory,
                    OldMonthlyAmount = oldAmount,
                    NewCategory = ElderlyBenefitConstants.Cat90To99,
                    NewMonthlyAmount = newAmount,
                    EffectiveMonth = h.BirthDate!.Value.AddYears(90).ToString("yyyy-MM")
                });
            }

            LogInfo($"满90周岁名单: {year}-{month} 共 {rows.Count} 人（在享={rows.Count(r => !r.IsHistoryOnly)}，名册={rows.Count(r => r.IsHistoryOnly)}）");
            return Result.Success(rows);
        }
        catch (Exception ex)
        {
            LogException(ex, "GetAge90AdjustRowsAsync");
            return Result.FromException<List<ElderlyAge90Row>>(ex);
        }
    }

    /// <inheritdoc />
    public async Task<Result<int>> EnqueueAge90ReviewsAsync(int year, int month, CancellationToken ct = default)
    {
        var rowsResult = await GetAge90AdjustRowsAsync(year, month, ct);
        if (rowsResult.IsFailure)
            return Result.Failure<int>(rowsResult.ErrorCode!, rowsResult.Message!);
        var rows = rowsResult.Value ?? new List<ElderlyAge90Row>();
        if (rows.Count == 0) return Result.Success(0);

        var nextMonth = new DateTime(year, month, 1).AddMonths(1).ToString("yyyy-MM");
        var created = 0;
        foreach (var row in rows)
        {
            try
            {
                var exists = await _db.ExecuteScalarAsync<long?>(
                    "SELECT id FROM nc_biz_elderly_reviews WHERE id_card = $1 AND status = $2 AND deleted_at IS NULL LIMIT 1",
                    ct, row.IdCard, ElderlyBenefitConstants.ReviewStatusPending);
                if (exists.IsSuccess && exists.Value is > 0) continue;

                var eval = new ElderlyReviewEvaluation
                {
                    IsHistoryOnly = row.IsHistoryOnly,
                    ApplicationId = row.ApplicationId,
                    HistoryId = row.HistoryId,
                    Name = row.Name,
                    IdCard = row.IdCard,
                    Gender = row.Gender,
                    BirthDate = row.BirthDate,
                    Age = row.Age,
                    OldIdentityFlag = row.IdentityFlag,
                    IdentityFlag = row.IdentityFlag,
                    IdentitySource = string.Empty,
                    OldCategory = row.OldCategory,
                    OldMonthlyAmount = row.OldMonthlyAmount,
                    NewCategory = row.NewCategory,
                    NewMonthlyAmount = row.NewMonthlyAmount,
                    AdjustReasonCode = ElderlyBenefitConstants.AdjustReasonAge,
                    IdentityAdd = false,
                    EffectiveMonth = row.EffectiveMonth,
                    IssueStartMonth = nextMonth
                };
                await InsertPendingReviewAsync(eval, ElderlyBenefitConstants.ReviewTriggerAge90, null,
                    $"{year}年{month}月满90周岁自动入队", ct);
                created++;
            }
            catch (Exception ex)
            {
                LogWarn($"满90周岁入队失败（继续）: {DataMasker.MaskIdCard(row.IdCard)}, {ex.Message}");
            }
        }

        if (created > 0)
            Logger.LogBusiness("满90周岁月报自动入队", ("Year", year), ("Month", month), ("Created", created));
        return Result.Success(created);
    }

    /// <inheritdoc />
    public async Task<Result<ElderlyReviewEvaluation>> EvaluateReviewByPendingAsync(long reviewId, CancellationToken ct = default)
    {
        var rowResult = await _db.QuerySingleAsync<ElderlyReview>(
            "SELECT * FROM nc_biz_elderly_reviews WHERE id = $1 AND deleted_at IS NULL", ct, reviewId);
        if (rowResult.IsFailure || rowResult.Value == null)
            return Result.Failure<ElderlyReviewEvaluation>(ErrorCodes.RECORD_NOT_FOUND, "复核记录不存在");

        var row = rowResult.Value;
        // 月报自动入队：采用队列已算好的旧→新（名册旧类别不能用当前年龄重推）
        if (string.Equals(row.TriggerSource, ElderlyBenefitConstants.ReviewTriggerAge90, StringComparison.Ordinal))
        {
            return Result.Success(new ElderlyReviewEvaluation
            {
                IsHistoryOnly = row.SourceHistoryId is > 0 && row.OldApplicationId is null or 0,
                ApplicationId = row.OldApplicationId,
                HistoryId = row.SourceHistoryId,
                Name = row.Name,
                IdCard = row.IdCard,
                Gender = IdCardValidator.ExtractGender(row.IdCard) ?? string.Empty,
                BirthDate = IdCardValidator.ExtractBirthDate(row.IdCard),
                Age = row.AgeAtReview ?? ElderlyBenefitConstants.Threshold90,
                OldIdentityFlag = row.IdentityFlag,
                IdentityFlag = row.IdentityFlag,
                OldCategory = row.OldCategory,
                OldMonthlyAmount = row.OldMonthlyAmount ?? 0m,
                NewCategory = string.IsNullOrEmpty(row.NewCategory) ? ElderlyBenefitConstants.Cat90To99 : row.NewCategory,
                NewMonthlyAmount = row.NewMonthlyAmount ?? 0m,
                AdjustReasonCode = string.IsNullOrEmpty(row.AdjustReasonCode) ? ElderlyBenefitConstants.AdjustReasonAge : row.AdjustReasonCode,
                EffectiveMonth = row.EffectiveMonth,
                IssueStartMonth = row.IssueStartMonth
            });
        }

        return await EvaluateReviewAsync(row.OldApplicationId, row.IdCard, row.SourceHistoryId, ct);
    }

    /// <summary>家庭住址拼接（区划地址 + 门牌号）</summary>
    private static string CombineAddress(string? familyAddress, string? detailAddress)
    {
        if (string.IsNullOrWhiteSpace(detailAddress)) return familyAddress ?? string.Empty;
        if (string.IsNullOrWhiteSpace(familyAddress)) return detailAddress;
        return familyAddress + detailAddress;
    }

    /// <summary>满90周岁-在享档案行（snake_case 自动映射）</summary>
    private sealed class Age90AppRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string IdCard { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public DateTime? BirthDate { get; set; }
        public string Category { get; set; } = string.Empty;
        public string? IdentityFlag { get; set; }
        public decimal IssueAmount { get; set; }
        public string? HukouAddress { get; set; }
        public string? FamilyAddress { get; set; }
        public string? DetailAddress { get; set; }
    }

    /// <summary>满90周岁-名册行（snake_case 自动映射）</summary>
    private sealed class Age90HistRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string IdCard { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public DateTime? BirthDate { get; set; }
        public string PersonType { get; set; } = string.Empty;
        public string? Address { get; set; }
        public decimal? SubsidyAmount { get; set; }
    }

    #endregion

    /// <summary>名册复核源行（snake_case 列自动映射）</summary>
    private sealed class ElderlyHistoryReviewRow
    {
        public long Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string IdCard { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public DateTime? BirthDate { get; set; }
        public string Phone { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string BankAccount { get; set; } = string.Empty;
        public string PersonType { get; set; } = string.Empty;
        public decimal? SubsidyAmount { get; set; }
        public int? DataYear { get; set; }
    }
}
