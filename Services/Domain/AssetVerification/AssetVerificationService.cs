using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.AssetVerification;

public class AssetVerificationService : BaseService, IAssetVerificationService
{
    protected override string ServiceName => "AssetVerificationService";
    private readonly IDatabaseService _db;

    public AssetVerificationService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
    }

    public async Task<Result<AssetVerification>> GetByIdAsync(long id, CancellationToken ct = default)
    {
        LogInfo("获取资产核查: " + id);
        var sql = "SELECT * FROM nc_biz_asset_verifications WHERE id = $1";
        return await _db.QuerySingleAsync<AssetVerification>(sql, ct, id);
    }

    public async Task<Result<List<AssetVerification>>> GetByArchiveIdAsync(long archiveId, CancellationToken ct = default)
    {
        LogInfo("通过档案ID获取资产核查: " + archiveId);
        var sql = "SELECT * FROM nc_biz_asset_verifications WHERE archive_id = $1 ORDER BY verification_year DESC, verification_month DESC";
        return await _db.QueryAsync<AssetVerification>(sql, ct, archiveId);
    }

    public async Task<Result<long>> CreateAsync(AssetVerificationCreateRequest request, CancellationToken ct = default)
    {
        ValidateNotNull(request, nameof(request));
        LogInfo("创建资产核查: ArchiveId=" + request.ArchiveId);

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            var existsSql = @"SELECT COUNT(*) FROM nc_biz_asset_verifications 
                              WHERE archive_id = $1 AND verification_year = $2 AND verification_month = $3";
            var existsResult = await _db.ExecuteScalarAsync(existsSql, ct, request.ArchiveId, request.VerificationYear, request.VerificationMonth);
            if (existsResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<long>(existsResult.ErrorCode!, existsResult.Message!);
            }

            if (existsResult.Value > 0)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<long>(ErrorCodes.APPLICATION_ALREADY_EXISTS, "该档案本月已有核查记录");
            }

            var sql = @"INSERT INTO nc_biz_asset_verifications 
                (archive_id, verification_year, verification_month, verification_type, status, created_by, created_at, updated_at)
                VALUES ($1, $2, $3, $4, $6, $5, NOW(), NOW())
                RETURNING id";

            var insertResult = await _db.ExecuteScalarAsync(sql, ct,
                request.ArchiveId, request.VerificationYear, request.VerificationMonth, request.VerificationType, request.CreatedBy,
                ApplicationStatusCodes.PENDING);

            if (insertResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<long>(insertResult.ErrorCode!, insertResult.Message!);
            }

            await tx.CommitAsync(ct);

            LogInfo("资产核查创建成功: Id=" + insertResult.Value);
            Logger.LogBusiness("创建资产核查", ("VerificationId", insertResult.Value), ("ArchiveId", request.ArchiveId));
            return Result.Success(insertResult.Value);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "创建资产核查失败");
            return Result.FromException<long>(ex);
        }
    }

    public async Task<Result> UpdateResultAsync(long id, string verificationResult, decimal assetValue, string remarks, string updatedBy, CancellationToken ct = default)
    {
        LogInfo("更新核查结果: Id=" + id);

        var verificationResultObj = await GetByIdAsync(id, ct);
        if (verificationResultObj.IsFailure)
            return Result.Failure(verificationResultObj.ErrorCode!, verificationResultObj.Message!);

        var verification = verificationResultObj.Value;
        if (verification == null)
            return Result.Failure(ErrorCodes.NOT_FOUND, "核查记录不存在");

        var sql = @"UPDATE nc_biz_asset_verifications SET 
            verification_result = $1, total_asset_value = $2, remarks = $3, updated_by = $4, updated_at = NOW()
            WHERE id = $5";

        var updateResult = await _db.ExecuteNonQueryAsync(sql, ct, verificationResult, assetValue, remarks, updatedBy, id);

        if (updateResult.IsFailure)
            return Result.Failure(updateResult.ErrorCode!, updateResult.Message!);

        LogInfo("核查结果更新成功: Id=" + id);
        return Result.Success();
    }

    public async Task<Result> CompleteAsync(long id, string completedBy, CancellationToken ct = default)
    {
        LogInfo("完成资产核查: Id=" + id);

        var verificationResultObj = await GetByIdAsync(id, ct);
        if (verificationResultObj.IsFailure)
            return Result.Failure(verificationResultObj.ErrorCode!, verificationResultObj.Message!);

        var verification = verificationResultObj.Value;
        if (verification == null)
            return Result.Failure(ErrorCodes.NOT_FOUND, "核查记录不存在");

        if (verification.Status == ApplicationStatusCodes.COMPLETED)
            return Result.Failure(ErrorCodes.INVALID_TRANSITION, "核查已完成");

        var sql = @"UPDATE nc_biz_asset_verifications SET 
            status = $3, completed_at = NOW(), completed_by = $1, updated_at = NOW()
            WHERE id = $2";

        var completeResult = await _db.ExecuteNonQueryAsync(sql, ct, completedBy, id, ApplicationStatusCodes.COMPLETED);

        if (completeResult.IsFailure)
            return Result.Failure(completeResult.ErrorCode!, completeResult.Message!);

        LogInfo("资产核查完成: Id=" + id);
        Logger.LogBusiness("完成资产核查", ("VerificationId", id), ("CompletedBy", completedBy));
        return Result.Success();
    }

    public async Task<Result<PagedResult<AssetVerification>>> GetPendingAsync(int pageIndex, int pageSize, CancellationToken ct = default)
    {
        LogInfo("获取待核查列表: 第" + pageIndex + "页");

        var countSql = "SELECT COUNT(*) FROM nc_biz_asset_verifications WHERE status = $1";
        var countResult = await _db.ExecuteScalarAsync(countSql, ct, ApplicationStatusCodes.PENDING);
        if (countResult.IsFailure)
            return Result.Failure<PagedResult<AssetVerification>>(countResult.ErrorCode!, countResult.Message!);

        var totalCount = countResult.Value;
        var offset = (pageIndex - 1) * pageSize;
        var querySql = @"SELECT * FROM nc_biz_asset_verifications 
                         WHERE status = $1 
                         ORDER BY created_at DESC 
                         LIMIT $2 OFFSET $3";

        var listResult = await _db.QueryAsync<AssetVerification>(querySql, ct, ApplicationStatusCodes.PENDING, pageSize, offset);
        if (listResult.IsFailure)
            return Result.Failure<PagedResult<AssetVerification>>(listResult.ErrorCode!, listResult.Message!);

        return Result.Success(PagedResult<AssetVerification>.FromList(listResult.Value, pageIndex, pageSize, Convert.ToInt32(totalCount)));
    }

    public async Task<Result<List<AssetVerification>>> GetMonthlyReviewAsync(int year, int month, CancellationToken ct = default)
    {
        LogInfo("获取月度审核列表: " + year + "年" + month + "月");
        var sql = @"SELECT * FROM nc_biz_asset_verifications 
                    WHERE verification_year = $1 AND verification_month = $2
                    ORDER BY created_at DESC";
        return await _db.QueryAsync<AssetVerification>(sql, ct, year, month);
    }

    public async Task<Result<PagedResult<AssetVerificationTask>>> SearchPagedAsync(string keyword, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        LogInfo("搜索核查任务: keyword=" + keyword + ", 第" + pageIndex + "页");
        return await GetTasksPagedAsync(pageIndex, pageSize, null, keyword, ct: ct);
    }

    public async Task<Result<PagedResult<AssetVerificationTask>>> SearchPagedAsync(int year, int month, string keyword, string status, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        LogInfo("搜索核查任务: year=" + year + ", month=" + month + ", keyword=" + keyword + ", status=" + status + ", 第" + pageIndex + "页");
        return await GetTasksPagedAsync(pageIndex, pageSize, status, keyword, ct: ct);
    }

    public async Task<Result<PagedResult<AssetVerificationTask>>> GetTasksPagedAsync(int pageIndex, int pageSize, string status = null, string keyword = null, DateTime? dateFrom = null, DateTime? dateTo = null, CancellationToken ct = default)
    {
        LogInfo("获取核查任务列表: 第" + pageIndex + "页");

        var countSql = "SELECT COUNT(*) FROM nc_biz_asset_checks WHERE deleted_at IS NULL";
        var querySql = @"SELECT id, batch_id, applicant_name AS archive_name, applicant_id_type, 
            applicant_id_card AS archive_id_card, relationship, is_head, head_id_card, 
            family_address, application_reason, application_date, contact_phone, 
            status, created_at, updated_at, community, deleted_at
            FROM nc_biz_asset_checks WHERE deleted_at IS NULL";
        var conditions = new List<string>();
        var parameters = new List<object>();
        var paramIndex = 1;

        if (!string.IsNullOrEmpty(status))
        {
            conditions.Add(" status = $" + paramIndex + " ");
            parameters.Add(status);
            paramIndex++;
        }

        if (!string.IsNullOrEmpty(keyword))
        {
            conditions.Add(" (applicant_name ILIKE $" + paramIndex + " OR applicant_id_card ILIKE $" + paramIndex + ") ");
            parameters.Add("%" + keyword + "%");
            paramIndex++;
        }

        if (dateFrom.HasValue)
        {
            conditions.Add(" created_at >= $" + paramIndex + " ");
            parameters.Add(dateFrom.Value.Date);
            paramIndex++;
        }

        if (dateTo.HasValue)
        {
            conditions.Add(" created_at < $" + paramIndex + " ");
            parameters.Add(dateTo.Value.Date.AddDays(1));
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
            return Result.Failure<PagedResult<AssetVerificationTask>>(countResult.ErrorCode!, countResult.Message!);

        var totalCount = countResult.Value;
        var offset = (pageIndex - 1) * pageSize;
        querySql += " ORDER BY created_at DESC LIMIT $" + paramIndex + " OFFSET $" + (paramIndex + 1);
        parameters.Add(pageSize);
        parameters.Add(offset);

        var listResult = await _db.QueryAsync<AssetVerificationTask>(querySql, ct, parameters.ToArray());
        if (listResult.IsFailure)
            return Result.Failure<PagedResult<AssetVerificationTask>>(listResult.ErrorCode!, listResult.Message!);

        return Result.Success(PagedResult<AssetVerificationTask>.FromList(listResult.Value, pageIndex, pageSize, Convert.ToInt32(totalCount)));
    }

    public async Task<Result<AssetVerificationTask?>> GetTaskByIdAsync(long id, CancellationToken ct = default)
    {
        var sql = @"SELECT id, batch_id, applicant_name AS archive_name, applicant_id_type,
            applicant_id_card AS archive_id_card, relationship, is_head, head_id_card,
            family_address, application_reason, application_date, contact_phone,
            status, created_at, updated_at, community, deleted_at
            FROM nc_biz_asset_checks
            WHERE id = $1 AND deleted_at IS NULL
            LIMIT 1";

        var result = await _db.QuerySingleAsync<AssetVerificationTask>(sql, ct, id);
        if (result.IsFailure)
            return Result.Failure<AssetVerificationTask?>(result.ErrorCode!, result.Message ?? "核查任务查询失败");

        return Result.Success<AssetVerificationTask?>(result.Value);
    }

    public async Task<Result<List<AssetVerificationTask>>> GetFamilyMembersAsync(string headIdCard, CancellationToken ct = default)
    {
        var sql = @"SELECT id, batch_id, applicant_name AS archive_name, applicant_id_type,
            applicant_id_card AS archive_id_card, relationship, is_head, head_id_card,
            family_address, application_reason, application_date, contact_phone,
            status, created_at, community
            FROM nc_biz_asset_checks
            WHERE head_id_card = $1 AND deleted_at IS NULL
            ORDER BY is_head DESC, id ASC";

        var result = await _db.QueryAsync<AssetVerificationTask>(sql, ct, headIdCard);
        if (result.IsFailure)
            return Result.Failure<List<AssetVerificationTask>>(result.ErrorCode!, result.Message!);

        return Result.Success(result.Value ?? new List<AssetVerificationTask>());
    }

    public async Task<Result<List<AssetVerificationTask>>> GetFamilyMembersByBatchIdAsync(string batchId, CancellationToken ct = default)
    {
        var sql = @"SELECT id, batch_id, applicant_name AS archive_name, applicant_id_type,
            applicant_id_card AS archive_id_card, relationship, is_head, head_id_card,
            family_address, application_reason, application_date, contact_phone,
            status, created_at, community
            FROM nc_biz_asset_checks
            WHERE batch_id = $1 AND deleted_at IS NULL
            ORDER BY is_head DESC, id ASC";

        var result = await _db.QueryAsync<AssetVerificationTask>(sql, ct, batchId);
        if (result.IsFailure)
            return Result.Failure<List<AssetVerificationTask>>(result.ErrorCode!, result.Message!);

        return Result.Success(result.Value ?? new List<AssetVerificationTask>());
    }

    public async Task<Dictionary<string, string>?> GetApplicationFieldsByIdCardAsync(string idCard, CancellationToken ct = default)
    {
        var sql = @"SELECT 
            applicant_name, applicant_id_card, gender, family_size, income_source,
            work_income_total, business_income_total, property_income_total,
            transfer_income_total, other_income_total, alimony_income,
            total_family_income, per_capita_income, rigid_expenditure,
            application_reason, application_reason_detail, address, community, town,
            classification_result, health_status, physical_condition,
            disease_name, disability_type, disability_level,
            family_land_area, self_farmed_land_area, subleased_land_area, contracted_land_area,
            land_income_total, subsidy_total
            FROM nc_biz_applications
            WHERE applicant_id_card = $1 AND deleted_at IS NULL
            ORDER BY created_at DESC LIMIT 1";

        var result = await _db.QuerySingleAsync<ApplicationRecord>(sql, ct, idCard);
        if (result.IsFailure || result.Value == null)
            return null;

        var app = result.Value;
        var fields = new Dictionary<string, string>();

        void Add(string key, string? val) { if (!string.IsNullOrWhiteSpace(val)) fields[key] = val; }
        void AddNum(string key, decimal val) { if (val != 0) fields[key] = val.ToString("N2"); }

        Add("APPLICANT_NAME", app.ApplicantName);
        Add("APPLICANT_ID_CARD", app.ApplicantIdCard);
        Add("GENDER", app.Gender);
        Add("INCOME_SOURCE", app.IncomeSource);
        Add("APPLICATION_REASON", app.ApplicationReason);
        Add("APPLICATION_REASON_DETAIL", app.ApplicationReasonDetail);
        Add("ADDRESS", app.Address);
        Add("COMMUNITY", app.Community);
        Add("TOWN", app.Town);
        Add("CLASSIFICATION_RESULT", app.ClassificationResult);
        Add("HEALTH_STATUS", app.HealthStatus);
        Add("PHYSICAL_CONDITION", app.PhysicalCondition);
        Add("DISEASE_NAME", app.DiseaseName);
        Add("DISABILITY_TYPE", app.DisabilityType);
        Add("DISABILITY_LEVEL", app.DisabilityLevel);

        if (app.FamilySize > 0) fields["HEAD_FAMILY_SIZE"] = app.FamilySize.ToString();
        AddNum("INCOME_LABOR", app.WorkIncomeTotal);
        AddNum("INCOME_BUSINESS", app.BusinessIncomeTotal);
        AddNum("INCOME_PROPERTY", app.PropertyIncomeTotal);
        AddNum("INCOME_TRANSFER", app.TransferIncomeTotal);
        AddNum("INCOME_OTHER", app.OtherIncomeTotal);
        AddNum("INCOME_ALIMONY", app.AlimonyIncome);
        AddNum("INCOME_TOTAL", app.TotalFamilyIncome);
        AddNum("INCOME_RIGID_EXPENDITURE", app.RigidExpenditure);
        AddNum("FAMILY_LAND_AREA", app.FamilyLandArea);
        AddNum("SELF_FARMED_LAND_AREA", app.SelfFarmedLandArea);
        AddNum("SUBLEASED_LAND_AREA", app.SubleasedLandArea);
        AddNum("CONTRACTED_LAND_AREA", app.ContractedLandArea);
        AddNum("LAND_INCOME_TOTAL", app.LandIncomeTotal);
        AddNum("INCOME_SUBSIDY", app.SubsidyTotal);

        return fields;
    }

    private class ApplicationRecord
    {
        public string? ApplicantName { get; set; }
        public string? ApplicantIdCard { get; set; }
        public string? Gender { get; set; }
        public int FamilySize { get; set; }
        public string? IncomeSource { get; set; }
        public decimal WorkIncomeTotal { get; set; }
        public decimal BusinessIncomeTotal { get; set; }
        public decimal PropertyIncomeTotal { get; set; }
        public decimal TransferIncomeTotal { get; set; }
        public decimal OtherIncomeTotal { get; set; }
        public decimal AlimonyIncome { get; set; }
        public decimal TotalFamilyIncome { get; set; }
        public decimal PerCapitaIncome { get; set; }
        public decimal RigidExpenditure { get; set; }
        public string? ApplicationReason { get; set; }
        public string? ApplicationReasonDetail { get; set; }
        public string? Address { get; set; }
        public string? Community { get; set; }
        public string? Town { get; set; }
        public string? ClassificationResult { get; set; }
        public string? HealthStatus { get; set; }
        public string? PhysicalCondition { get; set; }
        public string? DiseaseName { get; set; }
        public string? DisabilityType { get; set; }
        public string? DisabilityLevel { get; set; }
        public decimal FamilyLandArea { get; set; }
        public decimal SelfFarmedLandArea { get; set; }
        public decimal SubleasedLandArea { get; set; }
        public decimal ContractedLandArea { get; set; }
        public decimal LandIncomeTotal { get; set; }
        public decimal SubsidyTotal { get; set; }
    }

    public async Task<Result<bool>> CheckDuplicateAsync(string headIdCard, int year, int month, CancellationToken ct = default)
    {
        LogInfo("检查重复核查: HeadIdCard=" + DataMasker.MaskIdCard(headIdCard));

        var sql = @"SELECT COUNT(*) FROM nc_biz_asset_checks 
                    WHERE head_id_card = $1 
                      AND application_date >= $2 AND application_date < $3
                      AND deleted_at IS NULL";

        var monthStart = new DateTime(year, month, 1);
        var monthEnd = monthStart.AddMonths(1);
        var countResult = await _db.ExecuteScalarAsync(sql, ct, headIdCard, monthStart, monthEnd);
        if (countResult.IsFailure)
            return Result.Failure<bool>(countResult.ErrorCode!, countResult.Message!);

        var hasDuplicate = countResult.Value > 0;
        LogInfo("重复检查结果: " + (hasDuplicate ? "存在重复" : "无重复"));

        return Result.Success(hasDuplicate);
    }

    public async Task<Result> DeleteByHeadIdCardAsync(string headIdCard, int year, int month, CancellationToken ct = default)
    {
        LogInfo("删除当月核查记录: HeadIdCard=" + DataMasker.MaskIdCard(headIdCard));

        await using var tx = await _db.BeginTransactionScopeAsync(ct);
        try
        {
            var batchSql = @"SELECT DISTINCT batch_id FROM nc_biz_asset_checks 
                             WHERE head_id_card = $1 
                               AND application_date >= $2 AND application_date < $3
                               AND deleted_at IS NULL";

            var monthStart = new DateTime(year, month, 1);
            var monthEnd = monthStart.AddMonths(1);
            var batchResult = await _db.QuerySingleAsync<string>(batchSql, ct, headIdCard, monthStart, monthEnd);
            if (batchResult.IsSuccess && batchResult.Value != null)
            {
                var batchId = batchResult.Value;
                await ExecOrThrowAsync(_db, "DELETE FROM nc_biz_asset_check_agents WHERE batch_id = $1", ct, batchId);
                await ExecOrThrowAsync(_db, "DELETE FROM nc_biz_asset_check_operators WHERE batch_id = $1", ct, batchId);
            }

            var deleteSql = @"UPDATE nc_biz_asset_checks SET deleted_at = NOW()
                              WHERE head_id_card = $1 
                                AND application_date >= $2 AND application_date < $3
                                AND deleted_at IS NULL";

            await ExecOrThrowAsync(_db, deleteSql, ct, headIdCard, monthStart, monthEnd);
            await tx.CommitAsync(ct);

            LogInfo("已删除当月核查记录");
            Logger.LogBusiness("删除当月核查记录（事务提交）",
                ("HeadIdCard", DataMasker.MaskIdCard(headIdCard)), ("Year", year), ("Month", month));

            return Result.Success();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "删除核查记录失败");
            return Result.Failure(ErrorCodes.UNKNOWN_ERROR, "删除核查记录失败: " + ex.Message);
        }
    }

    public async Task<Result<long>> SubmitQuickVerificationAsync(QuickAssetCheckSubmitRequest request, CancellationToken ct = default)
    {
        ValidateNotNull(request, nameof(request));
        LogInfo("提交快速核查: BatchId=" + request.BatchId);

        await using var tx = await _db.BeginTransactionScopeAsync(ct);
        try
        {
            if (request.Applicants.Count > 0)
            {
                var headIdCard = request.Applicants.FirstOrDefault(a => a.IsHead)?.ApplicantIdCard
                    ?? request.Applicants.First().ApplicantIdCard;

                var (valuesClause, insertArgs) = MultiRowValuesBuilder.Build(request.Applicants.Count, 12, i =>
                {
                    var applicant = request.Applicants[i];
                    return new object?[]
                    {
                        request.BatchId,
                        applicant.ApplicantName,
                        applicant.ApplicantIdType,
                        applicant.ApplicantIdCard,
                        applicant.Relationship,
                        applicant.IsHead,
                        headIdCard,
                        request.FamilyAddress,
                        request.Community,
                        request.ApplicationReason,
                        request.ApplicationDate,
                        request.ContactPhone
                    };
                }, o => $"(${o}, ${o + 1}, ${o + 2}, ${o + 3}, ${o + 4}, ${o + 5}, ${o + 6}, ${o + 7}, ${o + 8}, ${o + 9}, ${o + 10}, ${o + 11}, '0', NOW(), NOW())");

                var insertResult = await _db.ExecuteNonQueryAsync(@"INSERT INTO nc_biz_asset_checks 
                    (batch_id, applicant_name, applicant_id_type, applicant_id_card, relationship, is_head,
                     head_id_card, family_address, community, application_reason, application_date,
                     contact_phone, status, created_at, updated_at)
                    VALUES " + valuesClause, ct, insertArgs);

                if (insertResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    LogError("快速核查提交失败");
                    return Result.Failure<long>(insertResult.ErrorCode!, insertResult.Message!);
                }
            }

            if (request.IsAgent)
            {
                var agentSql = @"INSERT INTO nc_biz_asset_check_agents 
                    (batch_id, is_agent, agent_name, agent_id_type, agent_id_card, agent_relationship, created_at)
                    VALUES ($1, true, $2, $3, $4, $5, NOW())";

                var agentResult = await _db.ExecuteNonQueryAsync(agentSql, ct,
                    request.BatchId,
                    request.AgentName,
                    request.AgentIdType,
                    request.AgentIdCard,
                    request.AgentRelationship);

                if (agentResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    LogError("代理人信息写入失败");
                    return Result.Failure<long>(agentResult.ErrorCode!, agentResult.Message!);
                }
            }

            var operatorSql = @"INSERT INTO nc_biz_asset_check_operators 
                (batch_id, operator_user_id, operator_name, operator_account, operator_unit_name, created_at)
                VALUES ($1, $2, $3, $4, $5, NOW())";

            var operatorResult = await _db.ExecuteNonQueryAsync(operatorSql, ct,
                request.BatchId,
                request.OperatorUserId,
                request.OperatorName,
                request.OperatorAccount,
                request.OperatorUnitName);

            if (operatorResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                LogError("操作员信息写入失败");
                return Result.Failure<long>(operatorResult.ErrorCode!, operatorResult.Message!);
            }

            await tx.CommitAsync(ct);

            Logger.LogBusiness("快速核查提交成功",
                ("BatchId", request.BatchId),
                ("HeadName", request.Applicants.FirstOrDefault()?.ApplicantName ?? ""),
                ("MemberCount", request.Applicants.Count));

            LogInfo("快速核查提交成功: " + request.Applicants.Count + "人");
            return Result.Success((long)request.Applicants.Count);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "快速核查提交失败");
            return Result.Failure<long>(ErrorCodes.UNKNOWN_ERROR, "快速核查提交失败: " + ex.Message);
        }
    }

    public async Task<Result<MonthlyVerificationStats>> GetStatsByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken ct = default)
    {
        LogInfo("获取日期范围统计");

        var sql = $@"SELECT
                        COALESCE(COUNT(*) FILTER (WHERE status = $3), 0) as submitted_count,
                        COALESCE(COUNT(*) FILTER (WHERE status = $4), 0) as has_report_count,
                        COALESCE(COUNT(*) FILTER (WHERE status = $5), 0) as archived_count,
                        COALESCE(COUNT(*) FILTER (WHERE status = $6), 0) as rejected_count,
                        COALESCE(COUNT(*), 0) as total_count,
                        0 as total_asset_value
                    FROM nc_biz_asset_checks
                    WHERE application_date >= $1 AND application_date < $2;
";

        try
        {
            var result = await _db.QueryAsync<MonthlyStatsDto>(sql, ct, startDate, endDate,
                AssetCheckStatusConstants.SUBMITTED, AssetCheckStatusConstants.VERIFIED,
                AssetCheckStatusConstants.INCLUDED, AssetCheckStatusConstants.REFUSED);
            if (result.IsFailure)
                return Result.Failure<MonthlyVerificationStats>(result.ErrorCode!, result.Message!);

            if (result.Value == null || result.Value.Count == 0)
            {
                return Result.Success(new MonthlyVerificationStats
                {
                    SubmittedCount = 0,
                    HasReportCount = 0,
                    ArchivedCount = 0,
                    RejectedCount = 0,
                    TotalCount = 0,
                    TotalAssetValue = 0,
                    GeneratedAt = DateTime.Now
                });
            }

            var dto = result.Value.First();
            var stats = new MonthlyVerificationStats
            {
                SubmittedCount = dto.SubmittedCount,
                HasReportCount = dto.HasReportCount,
                ArchivedCount = dto.ArchivedCount,
                RejectedCount = dto.RejectedCount,
                TotalCount = dto.TotalCount,
                TotalAssetValue = dto.TotalAssetValue,
                GeneratedAt = DateTime.Now
            };

            LogInfo($"日期范围统计获取成功: Submitted={stats.SubmittedCount}, HasReport={stats.HasReportCount}, Archived={stats.ArchivedCount}");
            return Result.Success(stats);
        }
        catch (Exception ex)
        {
            LogException(ex, "获取日期范围统计失败");
            return Result.Failure<MonthlyVerificationStats>(ErrorCodes.UNKNOWN_ERROR, "获取统计失败: " + ex.Message);
        }
    }

    public async Task<Result<PagedResult<AssetVerificationTask>>> SearchByDateRangePagedAsync(
        DateTime startDate, DateTime endDate,
        string? keyword, string? status,
        int pageIndex, int pageSize,
        CancellationToken ct = default,
        bool onlyHead = false)
    {
        LogInfo("按日期范围搜索: " + startDate.ToString("yyyy-MM-dd") + " ~ " + endDate.ToString("yyyy-MM-dd"));

        var conditions = new SqlConditionBuilder()
            .Add("application_date >= {0} AND application_date < {1}", startDate, endDate)
            .Add("deleted_at IS NULL")
            .AddIf(!string.IsNullOrEmpty(status), "status = {0}", status)
            .AddIf(!string.IsNullOrEmpty(keyword), "(applicant_name ILIKE {0} OR applicant_id_card ILIKE {0})", "%" + keyword + "%");

        // 只查询户主（业务申请工作流 Tab 列表）
        // 使用 applicant_id_card = head_id_card 判断户主，兼容历史数据
        if (onlyHead)
            conditions.Add("applicant_id_card = head_id_card");

        var whereClause = conditions.ToWhereClause();

        var countSql = "SELECT COUNT(*) FROM nc_biz_asset_checks" + whereClause;
        var countResult = await _db.ExecuteScalarAsync<long>(countSql, ct, conditions.GetParameters());
        if (countResult.IsFailure)
            return Result.Failure<PagedResult<AssetVerificationTask>>(countResult.ErrorCode!, countResult.Message!);

        var offset = (pageIndex - 1) * pageSize;

        var querySql = $@"SELECT id, 0 AS archive_id, applicant_name AS archive_name, applicant_id_card AS archive_id_card,
                                   batch_id, relationship,
                                   head_id_card, family_address, community, applicant_id_type,
                                   EXTRACT(YEAR FROM application_date)::int AS verification_year,
                                   EXTRACT(MONTH FROM application_date)::int AS verification_month,
                                   'Quick' AS verification_type,
                                   CASE WHEN status = ${conditions.ParamCount + 1} THEN '已提交' WHEN status = ${conditions.ParamCount + 2} THEN '已核查' WHEN status = ${conditions.ParamCount + 3} THEN '已纳入低收入人群' ELSE '不予认定' END AS status,
                                   NULL::decimal AS total_asset_value, NULL::text AS verification_result,
                                   created_at, NULL::timestamp AS completed_at
                            FROM nc_biz_asset_checks " + whereClause +
                   $" ORDER BY created_at DESC LIMIT ${conditions.ParamCount + 4} OFFSET ${conditions.ParamCount + 5}";
        var pageParams = new List<object?>(conditions.GetParameters())
        {
            AssetCheckStatusConstants.SUBMITTED, AssetCheckStatusConstants.VERIFIED, AssetCheckStatusConstants.INCLUDED,
            pageSize, offset
        };

        var listResult = await _db.QueryAsync<AssetVerificationTask>(querySql, ct, pageParams.ToArray());
        if (listResult.IsFailure)
            return Result.Failure<PagedResult<AssetVerificationTask>>(listResult.ErrorCode!, listResult.Message!);

        LogInfo("日期范围搜索成功: 共" + Convert.ToInt32(countResult.Value) + "条");
        return Result.Success(PagedResult<AssetVerificationTask>.FromList(listResult.Value, pageIndex, pageSize, Convert.ToInt32(countResult.Value)));
    }

    /// <inheritdoc />
    public async Task<Result<List<AssetVerificationTask>>> GetByHeadIdsAsync(
        IReadOnlyCollection<string> headIdCards, CancellationToken ct = default)
    {
        if (headIdCards == null || headIdCards.Count == 0)
            return Result.Success(new List<AssetVerificationTask>());

        try
        {
            // SELECT 列与 SearchByDateRangePagedAsync 保持一致（同一实体映射）
            var sql = $@"
                SELECT id, 0 AS archive_id, applicant_name AS archive_name, applicant_id_card AS archive_id_card,
                       batch_id, relationship,
                       head_id_card, family_address, community, applicant_id_type,
                       EXTRACT(YEAR FROM application_date)::int AS verification_year,
                       EXTRACT(MONTH FROM application_date)::int AS verification_month,
                       'Quick' AS verification_type,
                       CASE WHEN status = $2 THEN '已提交' WHEN status = $3 THEN '已核查' WHEN status = $4 THEN '已纳入低收入人群' ELSE '不予认定' END AS status,
                       NULL::decimal AS total_asset_value, NULL::text AS verification_result,
                       created_at, NULL::timestamp AS completed_at
                FROM nc_biz_asset_checks
                WHERE head_id_card = ANY($1::text[]) AND deleted_at IS NULL
                ORDER BY created_at DESC;
";

            var result = await _db.QueryAsync<AssetVerificationTask>(sql, ct, new object[]
            {
                headIdCards.ToArray(),
                AssetCheckStatusConstants.SUBMITTED, AssetCheckStatusConstants.VERIFIED, AssetCheckStatusConstants.INCLUDED
            });
            if (result.IsFailure)
                return Result.Failure<List<AssetVerificationTask>>(result.ErrorCode!, result.Message!);

            LogInfo($"按户主身份证批查同户记录: 户数={headIdCards.Count}, 行数={result.Value?.Count ?? 0}");
            return Result.Success(result.Value ?? new List<AssetVerificationTask>());
        }
        catch (Exception ex)
        {
            LogException(ex, "GetByHeadIdsAsync");
            return Result.FromException<List<AssetVerificationTask>>(ex);
        }
    }

    public async Task<Result<long?>> GetLatestIdByIdCardAsync(string idCard, CancellationToken ct = default)
    {
        ValidateNotNullOrEmpty(idCard, nameof(idCard));
        LogInfo("按身份证查询最新核查记录: IdCard=" + DataMasker.MaskIdCard(idCard));
        var sql = @"SELECT ac.id FROM nc_biz_asset_checks ac
                  WHERE ac.applicant_id_card = $1 AND ac.deleted_at IS NULL
                  ORDER BY ac.created_at DESC LIMIT 1";
        var result = await _db.ExecuteScalarAsync<long?>(sql, ct, idCard);
        return result.IsSuccess
            ? Result.Success<long?>(result.Value)
            : Result.Failure<long?>(result.ErrorCode!, result.Message!);
    }

    public async Task<AssetCheckHistoryResult?> GetHistoryByIdCardAsync(string idCard, CancellationToken ct = default)
    {
        LogInfo("查询历史记录: IdCard=" + DataMasker.MaskIdCard(idCard));

        var sql1 = @"SELECT applicant_name, applicant_id_type, applicant_id_card, relationship,
                            batch_id, family_address, community AS village, application_reason, contact_phone
                     FROM nc_biz_asset_checks
                     WHERE applicant_id_card = $1 AND deleted_at IS NULL
                     ORDER BY created_at DESC LIMIT 1";
        var result1 = await _db.QuerySingleAsync<AssetCheckHistoryResult>(sql1, ct, idCard);
        if (result1.IsSuccess && result1.Value != null)
        {
            var history = result1.Value;

            var batchSql = @"SELECT applicant_name AS name, applicant_id_type AS id_type,
                                    applicant_id_card AS id_card, relationship, is_head
                             FROM nc_biz_asset_checks
                             WHERE batch_id = $1 AND deleted_at IS NULL
                             ORDER BY is_head DESC, id ASC";
            var batchResult = await _db.QueryAsync<FamilyMemberResult>(batchSql, ct, history.BatchId);
            if (batchResult.IsSuccess && batchResult.Value != null)
            {
                history.FamilyMembers = batchResult.Value;
            }

            var agentSql = @"SELECT agent_name, agent_id_type, agent_id_card, agent_relationship
                             FROM nc_biz_asset_check_agents
                             WHERE batch_id = $1 LIMIT 1";
            var agentResult = await _db.QuerySingleAsync<AssetCheckHistoryResult>(agentSql, ct, history.BatchId);
            if (agentResult.IsSuccess && agentResult.Value != null)
            {
                history.HasAgent = true;
                history.AgentName = agentResult.Value.AgentName;
                history.AgentIdCard = agentResult.Value.AgentIdCard;
                history.AgentRelationship = agentResult.Value.AgentRelationship;
                history.AgentIdType = agentResult.Value.AgentIdType;
            }

            LogInfo("历史记录命中[资产核查+代理]: IdCard=" + DataMasker.MaskIdCard(idCard));
            return history;
        }

        var appIdSql = @"SELECT a.id FROM nc_biz_applications a
                          WHERE a.applicant_id_card = $1 AND a.deleted_at IS NULL
                          ORDER BY a.created_at DESC LIMIT 1";
        var appIdResult = await _db.ExecuteScalarAsync(appIdSql, ct, idCard);
        if (appIdResult.IsSuccess && appIdResult.Value > 0)
        {
            var applicationId = Convert.ToInt64(appIdResult.Value);

            var sql2 = @"SELECT a.applicant_name AS applicant_name, a.applicant_phone AS contact_phone,
                                a.address AS family_address, a.community AS village,
                                a.application_reason
                         FROM nc_biz_applications a
                         WHERE a.id = $1";
            var result2 = await _db.QuerySingleAsync<AssetCheckHistoryResult>(sql2, ct, applicationId);
            if (result2.IsSuccess && result2.Value != null)
            {
                var history = result2.Value;
                history.ApplicantIdCard = idCard;
                history.ApplicantIdType = "居民身份证";

                var memberSql = @"SELECT m.name, m.id_card AS id_card, 
                                        m.relationship_to_head AS relationship,
                                        COALESCE(m.is_applicant, false) AS is_head
                                  FROM nc_biz_family_members m
                                  WHERE m.application_id = $1 AND m.deleted_at IS NULL
                                  ORDER BY m.is_applicant DESC, m.id ASC";
                var memberResult = await _db.QueryAsync<FamilyMemberResult>(memberSql, ct, applicationId);
                if (memberResult.IsSuccess && memberResult.Value != null)
                {
                    history.FamilyMembers = memberResult.Value;
                }

                LogInfo("历史记录命中[申请记录]: IdCard=" + DataMasker.MaskIdCard(idCard));
                return history;
            }
        }

        var sql3 = @"
            SELECT p.name AS applicant_name, p.relationship,
                   CASE WHEN p.family_address IS NOT NULL AND p.family_address != '' THEN p.family_address
                        ELSE COALESCE(f.address, '') END AS family_address,
                   COALESCE(f.phone, '') AS contact_phone,
                   COALESCE(f.community, '') AS village,
                   '' AS application_reason
            FROM nc_biz_rural_subsistence_persons p
            LEFT JOIN nc_biz_rural_subsistence_families f ON p.family_id = f.id
            WHERE p.id_card = $1
            UNION ALL
            SELECT p.name AS applicant_name, p.relationship,
                   CASE WHEN p.family_address IS NOT NULL AND p.family_address != '' THEN p.family_address
                        ELSE COALESCE(f.address, '') END AS family_address,
                   COALESCE(f.phone, '') AS contact_phone,
                   COALESCE(f.community, '') AS village,
                   '' AS application_reason
            FROM nc_biz_urban_subsistence_persons p
            LEFT JOIN nc_biz_urban_subsistence_families f ON p.family_id = f.id
            WHERE p.id_card = $1
            UNION ALL
            SELECT p.name AS applicant_name, p.relationship,
                   COALESCE(p.hukou_address, '') AS family_address,
                   COALESCE(f.phone, '') AS contact_phone,
                   COALESCE(f.community, '') AS village,
                   '' AS application_reason
            FROM nc_biz_destitute_persons p
            LEFT JOIN nc_biz_destitute_families f ON p.family_id = f.id
            WHERE p.id_card = $1
            UNION ALL
            SELECT p.name AS applicant_name, p.relationship,
                   COALESCE(p.address, '') AS family_address,
                   COALESCE(f.phone, '') AS contact_phone,
                   COALESCE(f.community, '') AS village,
                   '' AS application_reason
            FROM nc_biz_low_income_edge_persons p
            LEFT JOIN nc_biz_low_income_edge_families f ON p.family_id = f.id
            WHERE p.id_card = $1
            UNION ALL
            SELECT p.name AS applicant_name, p.relationship,
                   COALESCE(p.address, '') AS family_address,
                   COALESCE(f.phone, '') AS contact_phone,
                   COALESCE(f.community, '') AS village,
                   '' AS application_reason
            FROM nc_biz_rigid_expenditure_persons p
            LEFT JOIN nc_biz_rigid_expenditure_families f ON p.family_id = f.id
            WHERE p.id_card = $1
            LIMIT 1";
        var result3 = await _db.QuerySingleAsync<AssetCheckHistoryResult>(sql3, ct, idCard);
        if (result3.IsSuccess && result3.Value != null)
        {
            var history = result3.Value;
            history.ApplicantIdCard = idCard;
            history.ApplicantIdType = "居民身份证";
            LogInfo("历史记录命中[业务库]: IdCard=" + DataMasker.MaskIdCard(idCard));
            return history;
        }

        LogInfo("未查询到历史记录: IdCard=" + DataMasker.MaskIdCard(idCard));
        return null;
    }

    public async Task<Result<List<AssetVerificationTask>>> GetMonthlyDetailAsync(int year, int month, CancellationToken ct = default)
    {
        LogInfo("获取月度明细: " + year + "年" + month + "月");

        try
        {
            var sql = $@"SELECT id, 0 AS archive_id, applicant_name AS archive_name, applicant_id_card AS archive_id_card,
                               EXTRACT(YEAR FROM application_date)::int AS verification_year,
                               EXTRACT(MONTH FROM application_date)::int AS verification_month,
                               'Quick' AS verification_type,
                               CASE WHEN status = $3 THEN '已提交' WHEN status = $4 THEN '已核查' WHEN status = $5 THEN '已纳入低收入人群' ELSE '不予认定' END AS status,
                               NULL::decimal AS total_asset_value, NULL::text AS verification_result,
                               created_at, NULL::timestamp AS completed_at
                        FROM nc_biz_asset_checks
                        WHERE application_date >= $1 AND application_date < $2
                          AND deleted_at IS NULL
                         ORDER BY created_at DESC;
";
            var monthStart = new DateTime(year, month, 1);
            var monthEnd = monthStart.AddMonths(1);
            var result = await _db.QueryAsync<AssetVerificationTask>(sql, ct, monthStart, monthEnd,
                AssetCheckStatusConstants.SUBMITTED, AssetCheckStatusConstants.VERIFIED, AssetCheckStatusConstants.INCLUDED);
            if (result.IsFailure)
                return Result.Failure<List<AssetVerificationTask>>(result.ErrorCode!, result.Message!);

            LogInfo("月度明细获取成功: " + year + "年" + month + "月 共" + result.Value.Count + "条");
            return Result.Success(result.Value);
        }
        catch (Exception ex)
        {
            LogException(ex, "获取月度明细失败");
            return Result.Failure<List<AssetVerificationTask>>(ErrorCodes.UNKNOWN_ERROR, "获取月度明细失败: " + ex.Message);
        }
    }

    public async Task<Result<List<AssetVerificationTask>>> SearchByNameOrIdCardAsync(string keyword, CancellationToken ct = default)
    {
        LogInfo("按姓名/身份证搜索: keyword=" + DataMasker.MaskIdCard(keyword));

        if (string.IsNullOrWhiteSpace(keyword))
            return Result.Success<List<AssetVerificationTask>>(new List<AssetVerificationTask>());

        try
        {
            var trimmedKeyword = keyword.Trim();

            // 第一步：精确匹配（TRIM 去除前后空格）
            var exactSql = @"SELECT id, 0 AS archive_id, applicant_name AS archive_name, applicant_id_card AS archive_id_card,
                                   batch_id, relationship,
                                   EXTRACT(YEAR FROM application_date)::int AS verification_year,
                                   EXTRACT(MONTH FROM application_date)::int AS verification_month,
                                   'Quick' AS verification_type, status,
                                   NULL::decimal AS total_asset_value, NULL::text AS verification_result,
                                   created_at, NULL::timestamp AS completed_at
                            FROM nc_biz_asset_checks
                            WHERE (TRIM(applicant_name) = TRIM($1) OR applicant_id_card = TRIM($1))
                              AND deleted_at IS NULL
                            ORDER BY created_at DESC
                            LIMIT 100";

            var exactResult = await _db.QueryAsync<AssetVerificationTask>(exactSql, ct, trimmedKeyword);
            if (exactResult.IsFailure)
                return Result.Failure<List<AssetVerificationTask>>(exactResult.ErrorCode!, exactResult.Message!);

            if (exactResult.Value.Count > 0)
            {
                LogInfo("精确匹配完成: 共" + exactResult.Value.Count + "条");
                return Result.Success(exactResult.Value);
            }

            // 第二步：精确匹配无结果，降级为模糊匹配（限制20条，由调用方决定如何处理）
            LogInfo("精确匹配无结果，降级模糊匹配");

            var fuzzySql = @"SELECT id, 0 AS archive_id, applicant_name AS archive_name, applicant_id_card AS archive_id_card,
                                   batch_id, relationship,
                                   EXTRACT(YEAR FROM application_date)::int AS verification_year,
                                   EXTRACT(MONTH FROM application_date)::int AS verification_month,
                                   'Quick' AS verification_type, status,
                                   NULL::decimal AS total_asset_value, NULL::text AS verification_result,
                                   created_at, NULL::timestamp AS completed_at
                            FROM nc_biz_asset_checks
                            WHERE (applicant_name ILIKE $1 OR applicant_id_card ILIKE $1)
                              AND deleted_at IS NULL
                            ORDER BY created_at DESC
                            LIMIT 20";

            var fuzzyResult = await _db.QueryAsync<AssetVerificationTask>(fuzzySql, ct, "%" + trimmedKeyword + "%");
            if (fuzzyResult.IsFailure)
                return Result.Failure<List<AssetVerificationTask>>(fuzzyResult.ErrorCode!, fuzzyResult.Message!);

            LogInfo("模糊匹配完成: 共" + fuzzyResult.Value.Count + "条");
            return Result.Success(fuzzyResult.Value);
        }
        catch (Exception ex)
        {
            LogException(ex, "搜索失败");
            return Result.Failure<List<AssetVerificationTask>>(ErrorCodes.UNKNOWN_ERROR, "搜索失败: " + ex.Message);
        }
    }

    public async Task<Result<List<string>>> GetAllApplicantNamesAsync(CancellationToken ct = default)
    {
        try
        {
            var sql = "SELECT DISTINCT applicant_name FROM nc_biz_asset_checks WHERE applicant_name IS NOT NULL AND applicant_name != '' AND deleted_at IS NULL";
            // 单列查询直接映射 string；原 QueryAsync<dynamic> 会得到空 object，
            // r.applicant_name 必抛 RuntimeBinderException（此功能一直失败并被 catch 吞掉）
            var result = await _db.QueryAsync<string>(sql, ct);
            if (result.IsFailure)
                return Result.Failure<List<string>>(result.ErrorCode!, result.Message!);

            return Result.Success(result.Value);
        }
        catch (Exception ex)
        {
            LogException(ex, "获取所有申请人姓名失败");
            return Result.Failure<List<string>>(ErrorCodes.UNKNOWN_ERROR, "获取所有申请人姓名失败: " + ex.Message);
        }
    }

    public async Task<Result<AssetVerificationDetail>> GetDetailByIdAsync(long id, CancellationToken ct = default)
    {
        LogInfo("获取核查详情: Id=" + id);

        try
        {
            var sql = @"SELECT id, batch_id, applicant_name, applicant_id_type, applicant_id_card,
                               relationship, is_head, head_id_card, family_address, community,
                               application_reason, application_date, contact_phone, status,
                               created_at, updated_at
                        FROM nc_biz_asset_checks
                        WHERE id = $1 AND deleted_at IS NULL";

            var result = await _db.QuerySingleAsync<AssetVerificationDetail>(sql, ct, id);
            if (result.IsFailure)
                return Result.Failure<AssetVerificationDetail>(result.ErrorCode!, result.Message!);

            if (result.Value == null)
                return Result.Success<AssetVerificationDetail>(null);

            var detail = result.Value;

            var memberSql = $@"SELECT applicant_name AS name, applicant_id_card AS id_card,
                                     applicant_id_type AS id_type, relationship, is_head
                              FROM nc_biz_asset_checks
                              WHERE head_id_card = $1 AND status = $2
                                AND applicant_id_card != $1 AND deleted_at IS NULL
                              GROUP BY applicant_id_card, applicant_name, applicant_id_type, relationship, is_head
                              ORDER BY applicant_name;
";
            var memberResult = await _db.QueryAsync<AssetVerificationFamilyMember>(memberSql, ct, detail.HeadIdCard, AssetCheckStatusConstants.VERIFIED);
            if (memberResult.IsSuccess && memberResult.Value != null)
            {
                detail.FamilyMembers = memberResult.Value;
            }

            var agentSql = @"SELECT agent_name, agent_id_card, agent_relationship, agent_id_type
                             FROM nc_biz_asset_check_agents
                             WHERE batch_id = $1 LIMIT 1";
            var agentResult = await _db.QuerySingleAsync<AssetCheckHistoryResult>(agentSql, ct, detail.BatchId);
            if (agentResult.IsSuccess && agentResult.Value != null)
            {
                detail.HasAgent = true;
                detail.AgentName = agentResult.Value.AgentName;
                detail.AgentIdCard = agentResult.Value.AgentIdCard;
                detail.AgentRelationship = agentResult.Value.AgentRelationship;
                detail.AgentIdType = agentResult.Value.AgentIdType;
            }

            var operatorSql = @"SELECT operator_name, operator_unit_name
                                FROM nc_biz_asset_check_operators
                                WHERE batch_id = $1 LIMIT 1";
            var operatorResult = await _db.QuerySingleAsync<OperatorDto>(operatorSql, ct, detail.BatchId);
            if (operatorResult.IsSuccess && operatorResult.Value != null)
            {
                detail.OperatorName = operatorResult.Value.OperatorName;
                detail.OperatorUnitName = operatorResult.Value.OperatorUnitName;
            }

            LogInfo("核查详情获取成功: Id=" + id);
            return Result.Success<AssetVerificationDetail>(detail);
        }
        catch (Exception ex)
        {
            LogException(ex, "获取核查详情失败");
            return Result.Failure<AssetVerificationDetail>(ErrorCodes.UNKNOWN_ERROR, "获取详情失败: " + ex.Message);
        }
    }

    public async Task<Result<byte[]>> ExportPendingListToExcelAsync(int year, int? month, CancellationToken ct = default)
    {
        if (!month.HasValue)
            return Result.Failure<byte[]>(ErrorCodes.VALIDATION_FAILED, "月份不能为空");
        return await ExportMonthlyListToExcelAsync(year, month.Value, "0", ct);
    }

    public async Task<Result<byte[]>> ExportMonthlyListToExcelAsync(int year, int month, string statusFilter, CancellationToken ct = default)
    {
        LogInfo("导出月度资产核查列表: Year=" + year + ", Month=" + month + ", Status=" + (statusFilter ?? "全部"));

        try
        {
            var (startDate, endDate) = ALinePeriodHelper.GetPeriod(year, month);

            var conditions = new List<string> { "application_date >= $1 AND application_date < $2", "deleted_at IS NULL" };
            var parameters = new List<object> { startDate, endDate };
            var paramIndex = 3;

            if (!string.IsNullOrEmpty(statusFilter))
            {
                conditions.Add(" status = $" + paramIndex + " ");
                parameters.Add(statusFilter);
                paramIndex++;
            }

            var whereClause = " WHERE " + string.Join(" AND ", conditions);
            var sql = @"SELECT id, applicant_name, applicant_id_card, applicant_id_type,
                               relationship, is_head, family_address, community,
                               application_reason, application_date, contact_phone, status
                        FROM nc_biz_asset_checks"
                      + whereClause +
                      " ORDER BY application_date DESC, id ASC";

            var result = await _db.QueryAsync<PendingExportRow>(sql, ct, parameters.ToArray());
            if (result.IsFailure)
                return Result.Failure<byte[]>(result.ErrorCode!, result.Message!);

            var rows = result.Value;
            if (rows.Count == 0)
                return Result.Success(Array.Empty<byte>());

            var (title, sheetName) = statusFilter switch
            {
                "0" => (year + "年" + month + "月资产核查尚未完成人员列表", "尚未完成_" + year + "年" + month + "月"),
                "1" => (year + "年" + month + "月资产核查已完成人员列表", "已完成_" + year + "年" + month + "月"),
                _ => (year + "年" + month + "月资产核查全部人员列表", "全部_" + year + "年" + month + "月")
            };

            var excelBytes = GenerateMonthlyExcel(rows, year, month, title, sheetName);
            LogInfo("导出月度资产核查列表成功: 共" + rows.Count + "条 Status=" + (statusFilter ?? "全部"));

            Logger.LogBusiness("导出月度资产核查列表",
                ("Year", year),
                ("Month", month),
                ("Status", statusFilter ?? "全部"),
                ("Count", rows.Count));

            return Result.Success(excelBytes);
        }
        catch (Exception ex)
        {
            LogException(ex, "导出月度资产核查列表失败");
            return Result.Failure<byte[]>(ErrorCodes.UNKNOWN_ERROR, "导出失败: " + ex.Message);
        }
    }

    public async Task<Result<List<MonthlyVerificationStats>>> GetHistoryStatsAsync(int? year, CancellationToken ct = default)
    {
        LogInfo("获取历史统计");

        try
        {
            var conditions = new List<string>();
            var parameters = new List<object>();
            var paramIndex = 1;

            if (year.HasValue)
            {
                conditions.Add(" application_date >= $" + paramIndex + " AND application_date < $" + (paramIndex + 1) + " ");
                parameters.Add(new DateTime(year.Value, 1, 1));
                parameters.Add(new DateTime(year.Value + 1, 1, 1));
                paramIndex += 2;
            }

            var whereClause = conditions.Count > 0 ? " WHERE " + string.Join(" AND ", conditions) : "";

            // [索引豁免] GROUP BY EXTRACT(...) 为月度聚合计算列；WHERE 已按年份范围收敛，
            // 表规模有限，不为此加冗余列。
            var sql = $@"SELECT EXTRACT(YEAR FROM application_date)::int AS year,
                               EXTRACT(MONTH FROM application_date)::int AS month,
                               COALESCE(COUNT(*) FILTER (WHERE status = ${paramIndex}), 0) AS pending_count,
                               COALESCE(COUNT(*) FILTER (WHERE status = ${paramIndex + 1}), 0) AS completed_count,
                               0 AS error_count,
                               COUNT(*)::int AS total_count,
                               0 AS total_asset_value
                        FROM nc_biz_asset_checks
                      " + whereClause +
                      " GROUP BY EXTRACT(YEAR FROM application_date), EXTRACT(MONTH FROM application_date)"
                      + " ORDER BY year DESC, month DESC";

            parameters.Add(AssetCheckStatusConstants.SUBMITTED);
            parameters.Add(AssetCheckStatusConstants.VERIFIED);

            var result = await _db.QueryAsync<MonthlyVerificationStats>(sql, ct, parameters.ToArray());
            if (result.IsFailure)
                return Result.Failure<List<MonthlyVerificationStats>>(result.ErrorCode!, result.Message!);

            foreach (var stat in result.Value)
            {
                stat.GeneratedAt = DateTime.Now;
            }

            LogInfo("历史统计获取成功: 共" + result.Value.Count + "个月");
            return Result.Success(result.Value);
        }
        catch (Exception ex)
        {
            LogException(ex, "获取历史统计失败");
            return Result.Failure<List<MonthlyVerificationStats>>(ErrorCodes.UNKNOWN_ERROR, "获取统计失败: " + ex.Message);
        }
    }

    public async Task<Result<List<AssetVerificationTask>>> GetPendingTasksAsync(int year, int? month, CancellationToken ct = default)
    {
        LogInfo("获取待处理任务: Year=" + year);

        try
        {
            var conditions = new List<string> { "status = $1" };
            var parameters = new List<object> { AssetCheckStatusConstants.SUBMITTED };
            var paramIndex = 2;

            var rangeStart = month.HasValue ? new DateTime(year, month.Value, 1) : new DateTime(year, 1, 1);
            var rangeEnd = month.HasValue ? rangeStart.AddMonths(1) : rangeStart.AddYears(1);
            conditions.Add(" application_date >= $" + paramIndex + " AND application_date < $" + (paramIndex + 1) + " ");
            parameters.Add(rangeStart);
            parameters.Add(rangeEnd);
            paramIndex += 2;

            var whereClause = " WHERE " + string.Join(" AND ", conditions);

            var sql = $@"SELECT id, 0 AS archive_id, applicant_name AS archive_name, applicant_id_card AS archive_id_card,
                               batch_id, relationship,
                               EXTRACT(YEAR FROM application_date)::int AS verification_year,
                               EXTRACT(MONTH FROM application_date)::int AS verification_month,
                               'Quick' AS verification_type,
                               '{ApplicationStatusCodes.PENDING}' AS status,
                               NULL::decimal AS total_asset_value, NULL::text AS verification_result,
                               created_at, NULL::timestamp AS completed_at
                        FROM nc_biz_asset_checks"
                      + whereClause +
                      " ORDER BY created_at DESC";

            var result = await _db.QueryAsync<AssetVerificationTask>(sql, ct, parameters.ToArray());
            if (result.IsFailure)
                return Result.Failure<List<AssetVerificationTask>>(result.ErrorCode!, result.Message!);

            LogInfo("待处理任务获取成功: 共" + result.Value.Count + "条");
            return Result.Success(result.Value);
        }
        catch (Exception ex)
        {
            LogException(ex, "获取待处理任务失败");
            return Result.Failure<List<AssetVerificationTask>>(ErrorCodes.UNKNOWN_ERROR, "获取待处理任务失败: " + ex.Message);
        }
    }

    public async Task<Result<List<AssetVerificationTask>>> GetPendingTasksByDateRangeAsync(DateTime startDate, DateTime endDate, CancellationToken ct = default)
    {
        LogInfo("按日期范围获取待处理任务");

        try
        {
            var sql = $@"SELECT id, 0 AS archive_id, applicant_name AS archive_name, applicant_id_card AS archive_id_card,
                               batch_id, relationship,
                               EXTRACT(YEAR FROM application_date)::int AS verification_year,
                               EXTRACT(MONTH FROM application_date)::int AS verification_month,
                               'Quick' AS verification_type,
                               '{ApplicationStatusCodes.PENDING}' AS status,
                               NULL::decimal AS total_asset_value, NULL::text AS verification_result,
                               created_at, NULL::timestamp AS completed_at
                        FROM nc_biz_asset_checks
                        WHERE status = $1 AND application_date >= $2 AND application_date < $3
                        ORDER BY created_at DESC;
";

            var result = await _db.QueryAsync<AssetVerificationTask>(sql, ct, AssetCheckStatusConstants.SUBMITTED, startDate, endDate);
            if (result.IsFailure)
                return Result.Failure<List<AssetVerificationTask>>(result.ErrorCode!, result.Message!);

            LogInfo("按日期范围获取待处理任务成功: 共" + result.Value.Count + "条");
            return Result.Success(result.Value);
        }
        catch (Exception ex)
        {
            LogException(ex, "按日期范围获取待处理任务失败");
            return Result.Failure<List<AssetVerificationTask>>(ErrorCodes.UNKNOWN_ERROR, "获取待处理任务失败: " + ex.Message);
        }
    }

    private static byte[] GenerateMonthlyExcel(List<PendingExportRow> rows, int year, int month, string title, string sheetName)
    {
        using var package = new OfficeOpenXml.ExcelPackage();
        var ws = package.Workbook.Worksheets.Add(sheetName);

        ws.Cells[1, 1].Value = title;
        ws.Cells[1, 1, 1, 8].Merge = true;
        ws.Cells[1, 1].Style.Font.Size = 16;
        ws.Cells[1, 1].Style.Font.Bold = true;
        ws.Cells[1, 1].Style.HorizontalAlignment = OfficeOpenXml.Style.ExcelHorizontalAlignment.Center;

        var headers = new[] { "序号", "姓名", "身份证号", "证件类型", "与户主关系", "家庭住址", "申请日期", "联系电话" };
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cells[2, i + 1].Value = headers[i];
            ws.Cells[2, i + 1].Style.Font.Bold = true;
            ws.Cells[2, i + 1].Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
            ws.Cells[2, i + 1].Style.Fill.BackgroundColor.SetColor(global::System.Drawing.Color.LightGray);
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var r = i + 3;
            ws.Cells[r, 1].Value = i + 1;
            ws.Cells[r, 2].Value = row.ApplicantName;
            ws.Cells[r, 3].Value = row.ApplicantIdCard;
            ws.Cells[r, 4].Value = row.ApplicantIdType;
            ws.Cells[r, 5].Value = row.Relationship;
            ws.Cells[r, 6].Value = row.FamilyAddress;
            ws.Cells[r, 7].Value = row.ApplicationDate.ToString("yyyy-MM-dd");
            ws.Cells[r, 8].Value = row.ContactPhone;
        }

        ws.Cells[ws.Dimension.Address].AutoFitColumns();
        return package.GetAsByteArray();
    }

    public async Task<Result<PagedResult<AssetVerificationTask>>> GetCompletedPagedAsync(string keyword, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        LogInfo("获取已完成资产核查记录（分页）: keyword=" + keyword + ", 第" + pageIndex + "页");

        var countSql = "SELECT COUNT(*) FROM nc_biz_asset_checks WHERE status = $1 AND deleted_at IS NULL";
        var querySql = @"SELECT id, batch_id, 
                applicant_name AS archive_name, 
                applicant_id_card AS archive_id_card,
                relationship, head_id_card, family_address, community, 
                applicant_id_type, status, created_at, updated_at
            FROM nc_biz_asset_checks WHERE status = $1 AND deleted_at IS NULL";
        var conditions = new List<string>();
        var parameters = new List<object> { AssetCheckStatusConstants.VERIFIED };
        var paramIndex = 2;

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
            return Result.Failure<PagedResult<AssetVerificationTask>>(countResult.ErrorCode!, countResult.Message!);

        var totalCount = countResult.Value;
        var offset = (pageIndex - 1) * pageSize;
        querySql += " ORDER BY updated_at DESC LIMIT $" + paramIndex + " OFFSET $" + (paramIndex + 1);
        parameters.Add(pageSize);
        parameters.Add(offset);

        var listResult = await _db.QueryAsync<AssetVerificationTask>(querySql, ct, parameters.ToArray());
        if (listResult.IsFailure)
            return Result.Failure<PagedResult<AssetVerificationTask>>(listResult.ErrorCode!, listResult.Message!);

        return Result.Success(PagedResult<AssetVerificationTask>.FromList(listResult.Value, pageIndex, pageSize, Convert.ToInt32(totalCount)));
    }

    public async Task<Result<PagedResult<AssetVerificationTask>>> GetPendingVerificationPagedAsync(string keyword, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        LogInfo("获取待处理资产核查记录（分页）: keyword=" + keyword + ", 第" + pageIndex + "页");

        var countSql = "SELECT COUNT(*) FROM nc_biz_asset_checks WHERE status = $1 AND deleted_at IS NULL";
        var querySql = @"SELECT id, batch_id, 
                applicant_name AS archive_name, 
                applicant_id_card AS archive_id_card,
                relationship, head_id_card, family_address, community, 
                applicant_id_type, status, created_at, updated_at
            FROM nc_biz_asset_checks WHERE status = $1 AND deleted_at IS NULL";
        var conditions = new List<string>();
        var parameters = new List<object> { AssetCheckStatusConstants.SUBMITTED };
        var paramIndex = 2;

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
            return Result.Failure<PagedResult<AssetVerificationTask>>(countResult.ErrorCode!, countResult.Message!);

        var totalCount = countResult.Value;
        var offset = (pageIndex - 1) * pageSize;
        querySql += " ORDER BY created_at DESC LIMIT $" + paramIndex + " OFFSET $" + (paramIndex + 1);
        parameters.Add(pageSize);
        parameters.Add(offset);

        var listResult = await _db.QueryAsync<AssetVerificationTask>(querySql, ct, parameters.ToArray());
        if (listResult.IsFailure)
            return Result.Failure<PagedResult<AssetVerificationTask>>(listResult.ErrorCode!, listResult.Message!);

        return Result.Success(PagedResult<AssetVerificationTask>.FromList(listResult.Value, pageIndex, pageSize, Convert.ToInt32(totalCount)));
    }

    public async Task<Result<int>> GetCompletedCountAsync(CancellationToken ct = default)
    {
        var sql = "SELECT COUNT(*) FROM nc_biz_asset_checks WHERE status = $1 AND deleted_at IS NULL";
        var result = await _db.ExecuteScalarAsync(sql, ct, AssetCheckStatusConstants.VERIFIED);
        if (result.IsFailure)
            return Result.Failure<int>(result.ErrorCode!, result.Message!);
        return Result.Success(Convert.ToInt32(result.Value));
    }

    public async Task<Result<int>> GetPendingCountAsync(CancellationToken ct = default)
    {
        var sql = "SELECT COUNT(*) FROM nc_biz_asset_checks WHERE status = $1 AND deleted_at IS NULL";
        var result = await _db.ExecuteScalarAsync(sql, ct, AssetCheckStatusConstants.SUBMITTED);
        if (result.IsFailure)
            return Result.Failure<int>(result.ErrorCode!, result.Message!);
        return Result.Success(Convert.ToInt32(result.Value));
    }

    public async Task<Result> UpdateCheckStatusAsync(long checkId, string status, CancellationToken ct = default)
    {
        LogInfo($"更新核查状态: CheckId={checkId}, Status={status}");

        // 按户更新：同 head_id_card 的整户记录一并更新（写法参照 PdfVerificationService.UpdateCheckStatusAsync）
        var sql = @"UPDATE nc_biz_asset_checks SET status = $1, updated_at = NOW()
                    WHERE head_id_card = (SELECT head_id_card FROM nc_biz_asset_checks WHERE id = $2)
                      AND deleted_at IS NULL";

        try
        {
            var result = await _db.ExecuteNonQueryAsync(sql, ct, status, checkId);
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);

            Logger.LogBusiness("核查状态更新",
                ("CheckId", checkId), ("Status", status), ("AffectedRows", result.Value));
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "更新核查状态失败");
            return Result.Failure(ErrorCodes.UNKNOWN_ERROR, "更新核查状态失败: " + ex.Message);
        }
    }

    private class OperatorDto
    {
        public string OperatorName { get; set; } = string.Empty;
        public string OperatorUnitName { get; set; } = string.Empty;
    }

    private class PendingExportRow
    {
        public long Id { get; set; }
        public string ApplicantName { get; set; } = string.Empty;
        public string ApplicantIdCard { get; set; } = string.Empty;
        public string ApplicantIdType { get; set; } = string.Empty;
        public string Relationship { get; set; } = string.Empty;
        public bool IsHead { get; set; }
        public string FamilyAddress { get; set; } = string.Empty;
        public string Community { get; set; } = string.Empty;
        public string ApplicationReason { get; set; } = string.Empty;
        public DateTime ApplicationDate { get; set; }
        public string ContactPhone { get; set; } = string.Empty;
        public string Status { get; set; } = "0";
    }

    private class MonthlyStatsDto
    {
        public int SubmittedCount { get; set; }      // 状态0：已提交申请
        public int HasReportCount { get; set; }      // 状态1：有报告未建档
        public int ArchivedCount { get; set; }       // 状态2：已完成建档
        public int RejectedCount { get; set; }       // 状态3：申请被拒
        public int TotalCount { get; set; }
        public decimal TotalAssetValue { get; set; }

        // 兼容旧属性
        public int PendingCount => SubmittedCount;
        public int CompletedCount => HasReportCount;
        public int ErrorCount => RejectedCount;
    }
}
