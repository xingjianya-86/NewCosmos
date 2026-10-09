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
public partial class ElderlyApplicationService : BaseService, IElderlyApplicationService
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

    public async Task<Result<PagedResult<ElderlyApplication>>> GetPagedAsync(string keyword, string status, int pageIndex, int pageSize, CancellationToken ct = default, bool distinctIdCard = false)
    {
        LogInfo($"分页查询: keywordLength={keyword.Length}, status={status}, 第{pageIndex}页, 每页{pageSize}条, 去重={distinctIdCard}");

        var conditions = new SqlConditionBuilder()
            .Add("deleted_at IS NULL")
            .AddIf(!string.IsNullOrEmpty(status), "status = {0}", status)
            .AddIf(!string.IsNullOrWhiteSpace(keyword), "(name ILIKE {0} OR id_card ILIKE {0})", $"%{keyword}%");

        var where = conditions.ToWhereClause();
        // 去重模式：同身份证存在多条在享档案时只取最新一条，计数同步按 distinct id_card，
        // 避免复核页"在享人员检索"把同一人显示成两行（历史重复档案的显示口径）。
        var fromClause = distinctIdCard
            ? $"(SELECT DISTINCT ON (id_card) * FROM nc_biz_elderly_applications{where} ORDER BY id_card, created_at DESC) t"
            : $"nc_biz_elderly_applications{where}";
        var countSql = distinctIdCard
            ? $"SELECT COUNT(DISTINCT id_card) FROM nc_biz_elderly_applications{where}"
            : $"SELECT COUNT(*) FROM nc_biz_elderly_applications{where}";
        var querySql = $"SELECT * FROM {fromClause}";

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

        // 新增明细：受理日期落在该月；停止明细：实际停发时间落在该月
        var conditions = new SqlConditionBuilder().Add("deleted_at IS NULL");
        string dateColumn;
        string orderBy;
        if (isStop)
        {
            conditions.Add("status = {0}", ApplicationStatusCodes.STOPPED);
            conditions.Add("(stop_reason IS NULL OR stop_reason <> {0})", ElderlyBenefitConstants.StopReasonReview);
            dateColumn = "actual_stop_date";
            orderBy = "actual_stop_date, id";
        }
        else
        {
            conditions.Add("status IN ({0}, {1})", ElderlyBenefitConstants.StatusConfirmed, ElderlyBenefitConstants.StatusStopped);
            dateColumn = "apply_date";
            orderBy = "apply_date, id";
        }
        conditions.Add(dateColumn + " >= {0} AND " + dateColumn + " < {1}", monthStart, monthEnd);
        conditions.AddIf(!string.IsNullOrWhiteSpace(categoryCode), "category = {0}", categoryCode?.Trim());
        conditions.AddIf(excludeImported, "source_type IS NULL");

        var sql = $"SELECT * FROM nc_biz_elderly_applications{conditions.ToWhereClause()} ORDER BY {orderBy}";
        var result = await _db.QueryAsync<ElderlyApplication>(sql, ct, conditions.GetParameters());
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

        // 同证在享预检（uq_elderly_confirmed_idcard 唯一索引的业务侧护栏）：
        // 已有在享档案时应走复核/停发流程，提前给出可操作提示而非数据库冲突原文。
        if (await HasOtherConfirmedAsync(id, ct))
            return Result.Failure(ErrorCodes.DB_UNIQUE_VIOLATION,
                "该身份证已存在在享档案，不能重复确认（请先办理复核或停发）");

        var sql = @"UPDATE nc_biz_elderly_applications SET
                    status = $1, confirmed_at = NOW(), confirmed_by = $2, updated_at = NOW()
                    WHERE id = $3 AND deleted_at IS NULL";
        var result = await _db.ExecuteNonQueryAsync(sql, ct, ElderlyBenefitConstants.StatusConfirmed, operatorName ?? "System", id);
        if (result.IsFailure)
            return result.ErrorCode == ErrorCodes.DB_UNIQUE_VIOLATION
                ? Result.Failure(ErrorCodes.DB_UNIQUE_VIOLATION,
                    "该身份证已存在在享档案，不能重复确认（请先办理复核或停发）")
                : Result.Failure(result.ErrorCode!, result.Message!);
        if (result.Value == 0)
            return Result.Failure(ErrorCodes.RECORD_NOT_FOUND, "登记记录不存在或已删除");

        Logger.LogBusiness("确认普惠高龄登记", ("ApplicationId", id));
        return Result.Success();
    }

    /// <summary>同身份证是否已存在其它在享（Confirmed）档案；预检失败按"无冲突"处理，由唯一索引兜底。</summary>
    private async Task<bool> HasOtherConfirmedAsync(long applicationId, CancellationToken ct)
    {
        var result = await _db.ExecuteScalarAsync<long>(@"
            SELECT COUNT(*) FROM nc_biz_elderly_applications
            WHERE deleted_at IS NULL AND status = $1 AND id <> $2
              AND id_card = (SELECT id_card FROM nc_biz_elderly_applications
                             WHERE id = $2 AND deleted_at IS NULL)",
            ct, ElderlyBenefitConstants.StatusConfirmed, applicationId);
        return result.IsSuccess && result.Value > 0;
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
                    "UPDATE nc_biz_elderly_subsidy_history SET status = $2, stopped_at = NOW(), updated_at = NOW() WHERE id = $1",
                    ct, src.SourceId.Value, ElderlyBenefitConstants.StatusStopped);
                if (byId.IsSuccess) marked += byId.Value;
            }

            if (!string.IsNullOrWhiteSpace(src.IdCard))
            {
                var byCard = await _db.ExecuteNonQueryAsync(
                    "UPDATE nc_biz_elderly_subsidy_history SET status = $2, stopped_at = NOW(), updated_at = NOW() WHERE id_card = $1 AND status = $3",
                    ct, src.IdCard, ElderlyBenefitConstants.StatusStopped, ElderlyBenefitConstants.StatusActive);
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
        if (segments == null || segments.Count == 0)
            return Result.Success();

        var (valuesClause, args) = MultiRowValuesBuilder.Build(segments.Count, 7, i =>
        {
            var segment = segments[i];
            return new object?[]
            {
                applicationId, segment.CategoryCode, segment.MonthlyAmount,
                segment.SegmentStartMonth, segment.SegmentEndMonth,
                segment.Months, segment.SegmentAmount
            };
        }, o => $"(${o}, ${o + 1}, ${o + 2}, ${o + 3}, ${o + 4}, ${o + 5}, ${o + 6}, NOW())");

        var result = await _db.ExecuteNonQueryAsync(@"
            INSERT INTO nc_biz_elderly_payback_segments
            (application_id, category_code, monthly_amount, segment_start_month, segment_end_month, months, segment_amount, created_at)
            VALUES " + valuesClause, ct, args);
        if (result.IsFailure)
            return result;

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
                WHERE (name ILIKE $1 OR id_card ILIKE $1) AND status = $2
                LIMIT 20";

            var result = await _db.QueryAsync<ElderlyImportedSearchItem>(sql, ct,
                $"%{keyword.Trim()}%", ElderlyBenefitConstants.StatusActive);
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
        const string draftSql = "SELECT id FROM nc_biz_elderly_applications WHERE id_card = $1 AND status = $2 AND deleted_at IS NULL ORDER BY id DESC LIMIT 1";
        var draftResult = await _db.QuerySingleAsync<long?>(draftSql, ct, idCard, ElderlyBenefitConstants.StatusDraft);
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
    public async Task<Result<int>> MarkHistoryMigratedAsync(string idCard, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idCard))
            return Result.Failure<int>(ErrorCodes.VALIDATION_FAILED, "身份证不能为空");

        try
        {
            // 只标记不删除：停止明细表"实际发放"列依赖名册行读历史金额（删行即回退计发金额）。
            // Active→Stopped 后，各"待建档/在册"口径（status=Active 过滤）不再把它当名册待办。
            const string sql = @"UPDATE nc_biz_elderly_subsidy_history
                                 SET status = $2, stopped_at = NOW(), updated_at = NOW()
                                 WHERE id_card = $1 AND status = $3";
            var result = await _db.ExecuteNonQueryAsync(sql, ct, idCard.Trim(),
                ElderlyBenefitConstants.StatusStopped, ElderlyBenefitConstants.StatusActive);
            if (result.IsFailure)
                return Result.Failure<int>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    result.Message ?? "标记导入库记录失败");

            if (result.Value > 0)
                Logger.LogBusiness("导入台账标记已并入当前库",
                    ("IdCard", DataMasker.MaskIdCard(idCard)), ("MarkedCount", result.Value));
            return Result.Success(result.Value);
        }
        catch (Exception ex)
        {
            LogException(ex, "MarkHistoryMigratedAsync");
            return Result.FromException<int>(ex);
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
                WHERE id = $1 AND deleted_at IS NULL AND current_step = $2";
            var appResult = await _db.QuerySingleAsync<ArchivedApplicationRow>(appSql, ct, applicationId, WorkflowSteps.ARCHIVED);
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
            //   待新增   = 无 Pending 复核 且正式表无 Confirmed 且名册无 Active
            //   需停旧增新 = 存在 Pending 复核（待办为复核队列的「已完结档案」子集，办结即消账）
            const string sql = @"
                WITH target_apps AS (
                    SELECT a.id, a.applicant_id_card
                    FROM nc_biz_applications a
                    WHERE a.current_step = $3 AND a.deleted_at IS NULL
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
                        NOT EXISTS (SELECT 1 FROM nc_biz_elderly_reviews r
                                    WHERE r.id_card = q.id_card AND r.status = $4 AND r.deleted_at IS NULL)
                        AND NOT EXISTS (SELECT 1 FROM nc_biz_elderly_applications e
                                        WHERE e.id_card = q.id_card AND e.deleted_at IS NULL AND e.status = $5)
                        AND NOT EXISTS (SELECT 1 FROM nc_biz_elderly_subsidy_history h
                                        WHERE h.id_card = q.id_card AND h.status = $6)) AS pending_new,
                    COUNT(*) FILTER (WHERE
                        EXISTS (SELECT 1 FROM nc_biz_elderly_reviews r
                                WHERE r.id_card = q.id_card AND r.status = $4 AND r.deleted_at IS NULL)) AS pending_transfer
                FROM qualified q";

            var excludeCodes = new[]
            {
                ClassificationConstants.RuralLowIncomeSingle,
                ClassificationConstants.UrbanLowIncomeSingle
            };
            // 满80周岁门槛（月数）：与 C# 侧 Threshold80 常量同源，禁止在 SQL 内写死 960
            var thresholdMonths = ElderlyBenefitConstants.Threshold80 * 12;
            var result = await _db.QuerySingleAsync<ElderlyPendingCounts>(sql, ct, excludeCodes, thresholdMonths, WorkflowSteps.ARCHIVED, ElderlyBenefitConstants.ReviewStatusPending, ElderlyBenefitConstants.StatusConfirmed, ElderlyBenefitConstants.StatusActive);
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
            //   待新增   = 无 Pending 复核 且正式表无 Confirmed 且名册无 Active
            //   需停旧增新 = 存在 Pending 复核（办结后队列置 Completed，该人自动退出列表）
            // 附带正式表在享ID/历史名册ID/复核记录ID，供跳转办理。
            const string sql = @"
                WITH target_apps AS (
                    SELECT a.id, a.application_no, a.applicant_id_card, a.applicant_name
                    FROM nc_biz_applications a
                    WHERE a.current_step = $3 AND a.deleted_at IS NULL
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
                           h.id AS history_id, rv.id AS review_id
                    FROM qualified q
                    LEFT JOIN nc_biz_elderly_applications e
                           ON e.id_card = q.id_card AND e.deleted_at IS NULL AND e.status = $4
                    LEFT JOIN nc_biz_elderly_subsidy_history h ON h.id_card = q.id_card AND h.status = $5
                    LEFT JOIN LATERAL (
                        SELECT id FROM nc_biz_elderly_reviews r
                        WHERE r.id_card = q.id_card AND r.status = $8 AND r.deleted_at IS NULL
                        ORDER BY id LIMIT 1) rv ON TRUE
                )
                SELECT application_id, application_no, id_card, name, member_role,
                       elderly_application_id, elderly_application_no, history_id, review_id,
                       CASE WHEN review_id IS NOT NULL THEN $7
                            WHEN elderly_application_id IS NULL AND history_id IS NULL THEN $6
                            ELSE NULL END AS pending_type
                FROM with_status
                WHERE review_id IS NOT NULL OR (elderly_application_id IS NULL AND history_id IS NULL)
                ORDER BY pending_type, name, id_card";

            var excludeCodes = new[]
            {
                ClassificationConstants.RuralLowIncomeSingle,
                ClassificationConstants.UrbanLowIncomeSingle
            };
            var thresholdMonths = ElderlyBenefitConstants.Threshold80 * 12;
            var result = await _db.QueryAsync<ElderlyPendingRow>(sql, ct, excludeCodes, thresholdMonths, WorkflowSteps.ARCHIVED,
            ElderlyBenefitConstants.StatusConfirmed, ElderlyBenefitConstants.StatusActive,
            ElderlyBenefitConstants.PendingTypeNew, ElderlyBenefitConstants.PendingTypeTransfer,
            ElderlyBenefitConstants.ReviewStatusPending);
            if (result.IsFailure)
                return Result.Failure<List<ElderlyPendingItem>>(result.ErrorCode!, result.Message!);

            // 调试日志：SQL 原始返回行数（按分流聚合计数，避免逐行 dump 淹没日志）
            var rawRows = result.Value ?? new List<ElderlyPendingRow>();
            var rawNew = rawRows.Count(r => r.PendingType == ElderlyBenefitConstants.PendingTypeNew);
            LogInfo($"下月待办-SQL原始返回: {rawRows.Count} 行（待新增={rawNew}，停旧增新={rawRows.Count - rawNew}）");

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
                    ReviewId = r.ReviewId,
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
        public long ReviewId { get; set; }
        public string PendingType { get; set; } = string.Empty;
    }
}
