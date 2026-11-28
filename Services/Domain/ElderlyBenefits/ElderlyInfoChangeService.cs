using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.ElderlyBenefits;

/// <summary>
/// 高龄年度认证·信息变更单服务。
/// 只读写变更单（records/details）与申请主表（nc_biz_elderly_applications，仅白名单列），
/// 不改认证任务快照；审批通过后由报表按申请主表读取“当前信息”。
/// </summary>
public class ElderlyInfoChangeService : BaseService, IElderlyInfoChangeService
{
    private const string RecordColumns = @"
        id, change_no, campaign_id, task_id, application_id, id_card, name, change_type,
        reason, status, source_type, source_file, source_row, effective_month,
        applied_at, applied_by, reviewed_at, reviewed_by, review_opinion,
        created_at, created_by, updated_at, deleted_at";

    private const string DetailColumns = @"
        id, change_id, field_key, field_label, old_value, new_value, created_at";

    private const string TaskContextColumns = @"
        id, campaign_id, id_card, name, application_id, cert_status, stop_decision";

    private const string ApplicationColumns = @"
        id, id_card, name, status, phone, family_address, detail_address,
        bank_name, bank_account, agent_name, agent_relation,
        agent_receive_name, agent_receive_relation, agent_receive_bank_name, agent_receive_bank_account";

    private static readonly string[] TaskStoppedStatuses =
    {
        ElderlyCertificationConstants.CertStatusStopped,
        ElderlyCertificationConstants.CertStatusDeceased
    };

    private static readonly string[] AllowedSourceTypes =
    {
        ElderlyInfoChangeConstants.SourceManual,
        ElderlyInfoChangeConstants.SourceCertificationImport,
        ElderlyInfoChangeConstants.SourceCertification
    };

    private readonly IDatabaseService _db;

    protected override string ServiceName => "ElderlyInfoChangeService";

    public ElderlyInfoChangeService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
    }

    public async Task<Result<PagedResult<ElderlyInfoChangeRecord>>> GetPagedAsync(
        string? keyword,
        string? status,
        int pageIndex,
        int pageSize,
        CancellationToken ct = default)
    {
        if (pageIndex < 1) pageIndex = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 200) pageSize = 200;

        var conditions = new List<string> { "deleted_at IS NULL" };
        var parameters = new List<object?>();
        var paramIndex = 1;

        if (string.IsNullOrWhiteSpace(status))
        {
            conditions.Add($"status = ANY(${paramIndex}::text[])");
            parameters.Add(ElderlyInfoChangeConstants.ReviewListStatuses);
            paramIndex++;
        }
        else
        {
            if (!ElderlyInfoChangeConstants.StatusOptions.Any(o => o.Key == status))
                return Result.Failure<PagedResult<ElderlyInfoChangeRecord>>(
                    ErrorCodes.VALIDATION_FAILED, "变更单状态不合法");

            conditions.Add($"status = ${paramIndex}");
            parameters.Add(status);
            paramIndex++;
        }

        if (!string.IsNullOrWhiteSpace(keyword))
        {
            conditions.Add($"(name ILIKE ${paramIndex} OR id_card ILIKE ${paramIndex} OR change_no ILIKE ${paramIndex})");
            parameters.Add($"%{keyword.Trim()}%");
            paramIndex++;
        }

        var where = string.Join(" AND ", conditions);
        var countSql = $"SELECT COUNT(*) FROM nc_biz_elderly_info_change_records WHERE {where}";
        var querySql = $@"
            SELECT {RecordColumns}
            FROM nc_biz_elderly_info_change_records
            WHERE {where}
            ORDER BY created_at DESC NULLS LAST, id DESC
            LIMIT ${paramIndex} OFFSET ${paramIndex + 1}";
        parameters.Add(pageSize);
        parameters.Add((pageIndex - 1) * pageSize);

        try
        {
            var countResult = await _db.ExecuteScalarAsync(countSql, ct, parameters.Take(paramIndex - 1).ToArray());
            if (countResult.IsFailure)
                return Result.Failure<PagedResult<ElderlyInfoChangeRecord>>(countResult.ErrorCode!, countResult.Message!);

            var listResult = await _db.QueryAsync<ElderlyInfoChangeRecord>(querySql, ct, parameters.ToArray());
            if (listResult.IsFailure)
                return Result.Failure<PagedResult<ElderlyInfoChangeRecord>>(listResult.ErrorCode!, listResult.Message!);

            var records = listResult.Value ?? new List<ElderlyInfoChangeRecord>();
            await LoadDetailsAsync(records, ct);

            return Result.Success(PagedResult<ElderlyInfoChangeRecord>.FromList(
                records, pageIndex, pageSize, Convert.ToInt32(countResult.Value)));
        }
        catch (Exception ex)
        {
            LogException(ex, "GetPagedAsync");
            return Result.FromException<PagedResult<ElderlyInfoChangeRecord>>(ex);
        }
    }

    public async Task<Result<List<ElderlyInfoChangeRecord>>> GetByTaskAsync(long taskId, CancellationToken ct = default)
    {
        if (taskId <= 0)
            return Result.Failure<List<ElderlyInfoChangeRecord>>(ErrorCodes.VALIDATION_FAILED, "认证任务ID不合法");

        try
        {
            var task = await LoadTaskAsync(taskId, ct);
            if (task == null)
                return Result.Failure<List<ElderlyInfoChangeRecord>>(ErrorCodes.NOT_FOUND, "认证任务不存在或已删除");

            const string sql = @"
                SELECT id, change_no, campaign_id, task_id, application_id, id_card, name, change_type,
                       reason, status, source_type, source_file, source_row, effective_month,
                       applied_at, applied_by, reviewed_at, reviewed_by, review_opinion,
                       created_at, created_by, updated_at, deleted_at
                FROM nc_biz_elderly_info_change_records
                WHERE (task_id = $1 OR id_card = $2) AND deleted_at IS NULL
                ORDER BY created_at DESC NULLS LAST, id DESC
                LIMIT 200";

            var result = await _db.QueryAsync<ElderlyInfoChangeRecord>(sql, ct, taskId, task.IdCard);
            if (result.IsFailure)
                return Result.Failure<List<ElderlyInfoChangeRecord>>(result.ErrorCode!, result.Message!);

            var records = result.Value ?? new List<ElderlyInfoChangeRecord>();
            await LoadDetailsAsync(records, ct);
            return Result.Success(records);
        }
        catch (Exception ex)
        {
            LogException(ex, "GetByTaskAsync");
            return Result.FromException<List<ElderlyInfoChangeRecord>>(ex);
        }
    }

    public async Task<Result<long>> CreateAsync(
        long taskId,
        string? reason,
        string? sourceType,
        string? sourceFile,
        int? sourceRow,
        string? effectiveMonth,
        IReadOnlyList<ElderlyInfoChangeDetailInput> details,
        string operatorName,
        CancellationToken ct = default)
    {
        if (taskId <= 0)
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "认证任务ID不合法");
        if (details == null || details.Count == 0)
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "请至少填写一项变更内容");

        var trimmedReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (trimmedReason?.Length > 500)
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "变更原因不能超过 500 字");

        var trimmedSourceType = string.IsNullOrWhiteSpace(sourceType)
            ? ElderlyInfoChangeConstants.SourceManual
            : sourceType.Trim();
        if (!AllowedSourceTypes.Contains(trimmedSourceType, StringComparer.Ordinal))
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "变更来源类型不合法");

        var trimmedSourceFile = string.IsNullOrWhiteSpace(sourceFile) ? null : sourceFile.Trim();
        if (trimmedSourceFile?.Length > 200)
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "来源文件名不能超过 200 字");
        if (sourceRow is < 1)
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "来源行号不合法");

        var trimmedEffectiveMonth = string.IsNullOrWhiteSpace(effectiveMonth) ? null : effectiveMonth.Trim();
        if (trimmedEffectiveMonth != null && !ElderlyInfoChangeConstants.IsValidEffectiveMonth(trimmedEffectiveMonth))
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "生效月格式应为 YYYY-MM");

        try
        {
            await using var tx = await _db.BeginTransactionScopeAsync(ct);

            var task = await LoadTaskAsync(taskId, ct);
            if (task == null)
                return Result.Failure<long>(ErrorCodes.NOT_FOUND, "认证任务不存在或已删除");
            if (TaskStoppedStatuses.Contains(task.CertStatus, StringComparer.Ordinal)
                || string.Equals(task.StopDecision, ElderlyCertificationConstants.StopDecisionApproved, StringComparison.Ordinal))
                return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "该人员已停发或已去世，不能再提交信息变更；请先处理停发");

            var application = await LoadApplicationAsync(task, ct);
            if (application == null)
                return Result.Failure<long>(ErrorCodes.NOT_FOUND, "该人员尚未建立或没有在享档案，请先补建档");
            if (!string.Equals(application.Status, ElderlyBenefitConstants.StatusConfirmed, StringComparison.Ordinal))
                return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "关联档案不是“已确认/在享”状态，不能提交信息变更");
            if (!string.Equals(application.IdCard, task.IdCard, StringComparison.OrdinalIgnoreCase))
                return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "关联档案与认证任务的身份证不一致，请人工核实");

            var normalized = new List<(string Key, string Label, string Risk, string OldValue, string NewValue)>();
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var input in details)
            {
                var key = ElderlyInfoChangeConstants.NormalizeFieldKey(input.FieldKey);
                if (key == null)
                {
                    var message = ElderlyInfoChangeConstants.IsIdentityField(input.FieldKey)
                        ? $"字段“{input.FieldKey}”属于身份/户籍/类别信息，不能通过信息变更修改"
                        : $"字段“{input.FieldKey}”不在允许变更范围";
                    return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, message);
                }
                if (!seenKeys.Add(key))
                    return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, $"字段“{ElderlyInfoChangeConstants.GetFieldLabel(key)}”重复提交");

                var newValue = input.NewValue?.Trim() ?? string.Empty;
                if (newValue.Length == 0)
                    return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, $"字段“{ElderlyInfoChangeConstants.GetFieldLabel(key)}”的新值不能为空");

                var maxLength = ElderlyInfoChangeConstants.GetMaxLength(key);
                if (maxLength > 0 && newValue.Length > maxLength)
                    return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED,
                        $"字段“{ElderlyInfoChangeConstants.GetFieldLabel(key)}”不能超过 {maxLength} 个字符");

                var oldValue = string.IsNullOrWhiteSpace(input.OldValue)
                    ? application.GetValue(key).Trim()
                    : input.OldValue.Trim();

                normalized.Add((key, ElderlyInfoChangeConstants.GetFieldLabel(key),
                    ElderlyInfoChangeConstants.GetRisk(key), oldValue, newValue));
            }

            if (trimmedEffectiveMonth == null
                && ElderlyInfoChangeConstants.RequiresEffectiveMonth(normalized.Select(n => n.Key)))
            {
                trimmedEffectiveMonth = ElderlyInfoChangeConstants.GetDefaultEffectiveMonth(DateTime.Today);
            }

            var changeType = normalized.Any(n => n.Risk == ElderlyInfoChangeConstants.RiskHigh)
                ? ElderlyInfoChangeConstants.RiskHigh
                : ElderlyInfoChangeConstants.RiskLow;

            // 同人同字段已有 Pending：旧单置 Cancelled（新单保留完整 old/new）。
            var canceled = await CancelPendingAsync(task.IdCard, normalized.Select(n => n.Key).ToArray(), ct);
            if (canceled.IsFailure)
                return Result.Failure<long>(canceled.ErrorCode!, canceled.Message!);

            var changeId = await InsertRecordAsync(
                task, application.Id, trimmedReason, changeType, trimmedSourceType,
                trimmedSourceFile, sourceRow, trimmedEffectiveMonth, operatorName, ct);
            if (changeId.IsFailure)
                return Result.Failure<long>(changeId.ErrorCode!, changeId.Message!);

            var detailResult = await InsertDetailsAsync(changeId.Value, normalized, ct);
            if (detailResult.IsFailure)
                return Result.Failure<long>(detailResult.ErrorCode!, detailResult.Message!);

            await tx.CommitAsync(ct);

            Logger.LogBusiness("创建高龄信息变更单",
                ("ChangeId", changeId.Value),
                ("TaskId", taskId),
                ("IdCard", DataMasker.MaskIdCard(task.IdCard)),
                ("Fields", normalized.Count),
                ("ChangeType", changeType),
                ("EffectiveMonth", trimmedEffectiveMonth ?? "-"),
                ("Operator", operatorName));

            return Result.Success(changeId.Value);
        }
        catch (Exception ex)
        {
            LogException(ex, "CreateAsync");
            return Result.FromException<long>(ex);
        }
    }

    public async Task<Result> ApproveAsync(
        long changeId,
        string? reviewOpinion,
        string? effectiveMonth,
        string operatorName,
        CancellationToken ct = default)
    {
        if (changeId <= 0)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "变更单ID不合法");

        var trimmedOpinion = string.IsNullOrWhiteSpace(reviewOpinion) ? null : reviewOpinion.Trim();
        if (trimmedOpinion?.Length > 500)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "审批意见不能超过 500 字");

        var trimmedEffectiveMonth = string.IsNullOrWhiteSpace(effectiveMonth) ? null : effectiveMonth.Trim();
        if (trimmedEffectiveMonth != null && !ElderlyInfoChangeConstants.IsValidEffectiveMonth(trimmedEffectiveMonth))
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "生效月格式应为 YYYY-MM");

        try
        {
            await using var tx = await _db.BeginTransactionScopeAsync(ct);

            const string recordSql = @"
                SELECT id, change_no, campaign_id, task_id, application_id, id_card, name, change_type,
                       reason, status, source_type, source_file, source_row, effective_month,
                       applied_at, applied_by, reviewed_at, reviewed_by, review_opinion,
                       created_at, created_by, updated_at, deleted_at
                FROM nc_biz_elderly_info_change_records
                WHERE id = $1 AND deleted_at IS NULL
                FOR UPDATE";

            var recordResult = await _db.QueryAsync<ElderlyInfoChangeRecord>(recordSql, ct, changeId);
            if (recordResult.IsFailure)
                return Result.Failure(recordResult.ErrorCode!, recordResult.Message!);

            var record = recordResult.Value?.FirstOrDefault();
            if (record == null)
                return Result.Failure(ErrorCodes.NOT_FOUND, "变更单不存在或已删除");
            if (!record.IsPending)
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, "只有待审批的变更单可以审批");
            if (record.ApplicationId == null)
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, "变更单未关联在享档案，无法写回申请主表");

            var detailResult = await _db.QueryAsync<ElderlyInfoChangeDetail>(
                $@"SELECT {DetailColumns}
                   FROM nc_biz_elderly_info_change_details
                   WHERE change_id = $1
                   ORDER BY id", ct, changeId);
            if (detailResult.IsFailure)
                return Result.Failure(detailResult.ErrorCode!, detailResult.Message!);

            var details = detailResult.Value ?? new List<ElderlyInfoChangeDetail>();
            if (details.Count == 0)
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, "变更单没有字段明细，不能审批");

            var application = await LockApplicationAsync(record.ApplicationId.Value, ct);
            if (application == null)
                return Result.Failure(ErrorCodes.NOT_FOUND, "关联档案不存在或已删除");
            if (!string.Equals(application.Status, ElderlyBenefitConstants.StatusConfirmed, StringComparison.Ordinal))
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, "关联档案已不是“已确认/在享”状态，不能写回信息");

            var updateSets = new List<string>();
            var updateParameters = new List<object?> { application.Id };
            foreach (var detail in details)
            {
                var key = ElderlyInfoChangeConstants.NormalizeFieldKey(detail.FieldKey);
                if (key == null)
                    return Result.Failure(ErrorCodes.VALIDATION_FAILED,
                        $"变更单包含不允许的字段“{detail.FieldKey}”，请驳回后重新提交");

                var currentValue = application.GetValue(key).Trim();
                var oldValue = detail.OldValue?.Trim() ?? string.Empty;
                if (!string.Equals(currentValue, oldValue, StringComparison.Ordinal))
                    return Result.Failure(ErrorCodes.VALIDATION_FAILED,
                        $"字段“{ElderlyInfoChangeConstants.GetFieldLabel(key)}”的当前值已变化，请驳回后重新提交");

                var newValue = detail.NewValue?.Trim() ?? string.Empty;
                if (newValue.Length == 0)
                    return Result.Failure(ErrorCodes.VALIDATION_FAILED,
                        $"字段“{ElderlyInfoChangeConstants.GetFieldLabel(key)}”的新值为空，请驳回后重新提交");

                updateSets.Add($"{key} = ${updateParameters.Count + 1}");
                updateParameters.Add(newValue);
            }

            updateSets.Add("updated_at = NOW()");
            updateSets.Add($"updated_by = ${updateParameters.Count + 1}");
            updateParameters.Add(operatorName);

            var updateSetsSql = string.Join(", ", updateSets);
            var updateSql = $"UPDATE nc_biz_elderly_applications SET {updateSetsSql} WHERE id = $1 AND deleted_at IS NULL";
            var updateResult = await _db.ExecuteNonQueryAsync(updateSql, ct, updateParameters.ToArray());
            if (updateResult.IsFailure)
                return Result.Failure(updateResult.ErrorCode!, updateResult.Message!);
            if (updateResult.Value != 1)
                return Result.Failure(ErrorCodes.NOT_FOUND, "关联档案不存在或已删除，未能写入申请主表");

            const string recordUpdateSql = @"
                UPDATE nc_biz_elderly_info_change_records
                SET status = $2,
                    applied_at = NOW(),
                    applied_by = $3,
                    reviewed_at = NOW(),
                    reviewed_by = $3,
                    review_opinion = $4,
                    effective_month = COALESCE($5, effective_month),
                    updated_at = NOW()
                WHERE id = $1 AND status = $6 AND deleted_at IS NULL";
            var recordUpdate = await _db.ExecuteNonQueryAsync(recordUpdateSql, ct,
                changeId,
                ElderlyInfoChangeConstants.StatusApproved,
                operatorName,
                trimmedOpinion,
                trimmedEffectiveMonth,
                ElderlyInfoChangeConstants.StatusPending);
            if (recordUpdate.IsFailure)
                return Result.Failure(recordUpdate.ErrorCode!, recordUpdate.Message!);
            if (recordUpdate.Value != 1)
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, "变更单已被处理，请刷新后重试");

            await tx.CommitAsync(ct);

            WriteSensitiveAudit(record, details, operatorName);

            Logger.LogBusiness("高龄信息变更审批通过",
                ("ChangeId", changeId),
                ("ChangeNo", record.ChangeNo),
                ("IdCard", DataMasker.MaskIdCard(record.IdCard)),
                ("Fields", details.Count),
                ("EffectiveMonth", trimmedEffectiveMonth ?? record.EffectiveMonth ?? "-"),
                ("Operator", operatorName));

            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "ApproveAsync");
            return Result.FromException(ex);
        }
    }

    public async Task<Result> RejectAsync(
        long changeId,
        string? reviewOpinion,
        string operatorName,
        CancellationToken ct = default)
    {
        if (changeId <= 0)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "变更单ID不合法");

        var trimmedOpinion = string.IsNullOrWhiteSpace(reviewOpinion) ? null : reviewOpinion.Trim();
        if (trimmedOpinion == null)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "驳回必须填写审批意见");
        if (trimmedOpinion.Length > 500)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "审批意见不能超过 500 字");

        const string sql = @"
            UPDATE nc_biz_elderly_info_change_records
            SET status = $2,
                reviewed_at = NOW(),
                reviewed_by = $3,
                review_opinion = $4,
                updated_at = NOW()
            WHERE id = $1 AND status = $5 AND deleted_at IS NULL";

        try
        {
            var result = await _db.ExecuteNonQueryAsync(sql, ct,
                changeId,
                ElderlyInfoChangeConstants.StatusRejected,
                operatorName,
                trimmedOpinion,
                ElderlyInfoChangeConstants.StatusPending);
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode!, result.Message!);
            if (result.Value != 1)
                return Result.Failure(ErrorCodes.VALIDATION_FAILED, "变更单不存在或已被处理，请刷新后重试");

            Logger.LogBusiness("高龄信息变更驳回",
                ("ChangeId", changeId),
                ("Operator", operatorName));

            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "RejectAsync");
            return Result.FromException(ex);
        }
    }

    public async Task<Result<ElderlyInfoChangeApplicationValues?>> GetApplicationValuesAsync(
        long taskId,
        CancellationToken ct = default)
    {
        if (taskId <= 0)
            return Result.Failure<ElderlyInfoChangeApplicationValues?>(ErrorCodes.VALIDATION_FAILED, "认证任务ID不合法");

        try
        {
            var task = await LoadTaskAsync(taskId, ct);
            if (task == null)
                return Result.Failure<ElderlyInfoChangeApplicationValues?>(ErrorCodes.NOT_FOUND, "认证任务不存在或已删除");

            var application = await LoadApplicationAsync(task, ct);
            return Result.Success<ElderlyInfoChangeApplicationValues?>(application);
        }
        catch (Exception ex)
        {
            LogException(ex, "GetApplicationValuesAsync");
            return Result.FromException<ElderlyInfoChangeApplicationValues?>(ex);
        }
    }

    #region 查询辅助

    private async Task<ElderlyCertTask?> LoadTaskAsync(long taskId, CancellationToken ct)
    {
        var sql = $@"
            SELECT {TaskContextColumns}
            FROM nc_biz_elderly_cert_tasks
            WHERE id = $1 AND deleted_at IS NULL
            LIMIT 1";
        var result = await _db.QueryAsync<ElderlyCertTask>(sql, ct, taskId);
        result.ThrowIfFailure("加载认证任务");
        return result.Value?.FirstOrDefault();
    }

    private async Task<ElderlyInfoChangeApplicationValues?> LoadApplicationAsync(ElderlyCertTask task, CancellationToken ct)
    {
        if (task.ApplicationId is > 0)
        {
            var byId = await QueryApplicationByIdAsync(task.ApplicationId.Value, ct);
            if (byId != null) return byId;
        }

        var byIdCard = $@"
            SELECT {ApplicationColumns}
            FROM nc_biz_elderly_applications
            WHERE id_card = $1 AND status = $2 AND deleted_at IS NULL
            ORDER BY id DESC
            LIMIT 1";
        var result = await _db.QueryAsync<ElderlyInfoChangeApplicationValues>(
            byIdCard, ct, task.IdCard, ElderlyBenefitConstants.StatusConfirmed);
        result.ThrowIfFailure("加载关联档案");
        return result.Value?.FirstOrDefault();
    }

    private async Task<ElderlyInfoChangeApplicationValues?> QueryApplicationByIdAsync(long applicationId, CancellationToken ct)
    {
        var sql = $@"
            SELECT {ApplicationColumns}
            FROM nc_biz_elderly_applications
            WHERE id = $1 AND deleted_at IS NULL
            LIMIT 1";
        var result = await _db.QueryAsync<ElderlyInfoChangeApplicationValues>(sql, ct, applicationId);
        result.ThrowIfFailure("加载关联档案");
        return result.Value?.FirstOrDefault();
    }

    private async Task<ElderlyInfoChangeApplicationValues?> LockApplicationAsync(long applicationId, CancellationToken ct)
    {
        var sql = $@"
            SELECT {ApplicationColumns}
            FROM nc_biz_elderly_applications
            WHERE id = $1 AND deleted_at IS NULL
            FOR UPDATE";
        var result = await _db.QueryAsync<ElderlyInfoChangeApplicationValues>(sql, ct, applicationId);
        result.ThrowIfFailure("锁定关联档案");
        return result.Value?.FirstOrDefault();
    }

    private async Task LoadDetailsAsync(List<ElderlyInfoChangeRecord> records, CancellationToken ct)
    {
        if (records.Count == 0) return;

        var ids = records.Select(r => r.Id).Distinct().ToArray();
        var result = await _db.QueryAsync<ElderlyInfoChangeDetail>(
            $@"SELECT {DetailColumns}
               FROM nc_biz_elderly_info_change_details
               WHERE change_id = ANY($1::bigint[])
               ORDER BY change_id, id", ct, ids);
        if (result.IsFailure)
        {
            LogWarn($"加载信息变更字段明细失败: {result.Message}");
            return;
        }

        var grouped = (result.Value ?? new List<ElderlyInfoChangeDetail>())
            .GroupBy(d => d.ChangeId)
            .ToDictionary(g => g.Key, g => g.ToList());
        foreach (var record in records)
        {
            record.Details = grouped.TryGetValue(record.Id, out var details)
                ? details
                : new List<ElderlyInfoChangeDetail>();
        }
    }

    #endregion

    #region 写入辅助

    private async Task<Result> CancelPendingAsync(string idCard, string[] fieldKeys, CancellationToken ct)
    {
        const string sql = @"
            UPDATE nc_biz_elderly_info_change_records r
            SET status = $3,
                review_opinion = COALESCE(r.review_opinion, $4),
                updated_at = NOW()
            WHERE r.id_card = $1
              AND r.status = $2
              AND r.deleted_at IS NULL
              AND EXISTS (
                  SELECT 1 FROM nc_biz_elderly_info_change_details d
                  WHERE d.change_id = r.id AND d.field_key = ANY($5::text[])
              )";

        var result = await _db.ExecuteNonQueryAsync(sql, ct,
            idCard,
            ElderlyInfoChangeConstants.StatusPending,
            ElderlyInfoChangeConstants.StatusCancelled,
            "被新提交覆盖",
            fieldKeys);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode!, result.Message!);
        return Result.Success();
    }

    private async Task<Result<long>> InsertRecordAsync(
        ElderlyCertTask task,
        long applicationId,
        string? reason,
        string changeType,
        string sourceType,
        string? sourceFile,
        int? sourceRow,
        string? effectiveMonth,
        string operatorName,
        CancellationToken ct)
    {
        const string sql = @"
            INSERT INTO nc_biz_elderly_info_change_records
                (change_no, campaign_id, task_id, application_id, id_card, name, change_type,
                 reason, status, source_type, source_file, source_row, effective_month,
                 created_at, created_by, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, NOW(), $14, NOW())
            ON CONFLICT (change_no) DO NOTHING
            RETURNING id";

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var changeNo = ElderlyInfoChangeConstants.BuildChangeNo(DateTime.Now);
            if (attempt > 0)
                changeNo = $"{changeNo}-{attempt + 1}";

            var result = await _db.ExecuteScalarAsync<long?>(sql, ct,
                changeNo,
                task.CampaignId,
                task.Id,
                applicationId,
                task.IdCard,
                task.Name,
                changeType,
                reason,
                ElderlyInfoChangeConstants.StatusPending,
                sourceType,
                sourceFile,
                sourceRow,
                effectiveMonth,
                operatorName);

            if (result.IsFailure)
                return Result.Failure<long>(result.ErrorCode!, result.Message!);
            if (result.Value is > 0)
                return Result.Success(result.Value.Value);
        }

        return Result.Failure<long>(ErrorCodes.DB_UNIQUE_VIOLATION, "生成变更单号失败，请稍后重试");
    }

    private async Task<Result> InsertDetailsAsync(
        long changeId,
        List<(string Key, string Label, string Risk, string OldValue, string NewValue)> details,
        CancellationToken ct)
    {
        var rows = new List<string>(details.Count);
        var parameters = new List<object?>(details.Count * 5);
        foreach (var detail in details)
        {
            var index = parameters.Count + 1;
            rows.Add($"(${index}, ${index + 1}, ${index + 2}, ${index + 3}, ${index + 4}, NOW())");
            parameters.Add(changeId);
            parameters.Add(detail.Key);
            parameters.Add(detail.Label);
            parameters.Add(detail.OldValue);
            parameters.Add(detail.NewValue);
        }

        var valuesClause = string.Join(", ", rows);
        var sql = $@"
            INSERT INTO nc_biz_elderly_info_change_details
                (change_id, field_key, field_label, old_value, new_value, created_at)
            VALUES {valuesClause}";

        var result = await _db.ExecuteNonQueryAsync(sql, ct, parameters.ToArray());
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode!, result.Message!);
        if (result.Value != details.Count)
            return Result.Failure(ErrorCodes.DB_QUERY_ERROR, "信息变更字段明细写入不完整，请重试");
        return Result.Success();
    }

    /// <summary>银行/代办人字段写安全日志（旧/新值脱敏）。</summary>
    private void WriteSensitiveAudit(
        ElderlyInfoChangeRecord record,
        List<ElderlyInfoChangeDetail> details,
        string operatorName)
    {
        foreach (var detail in details)
        {
            if (!ElderlyInfoChangeConstants.IsHighRisk(detail.FieldKey)) continue;

            Logger.LogSecurity("高龄信息变更审批-敏感字段",
                ("ChangeNo", record.ChangeNo),
                ("Field", ElderlyInfoChangeConstants.GetFieldLabel(detail.FieldKey)),
                ("OldValue", MaskSensitiveValue(detail.FieldKey, detail.OldValue)),
                ("NewValue", MaskSensitiveValue(detail.FieldKey, detail.NewValue)),
                ("Operator", operatorName));
        }
    }

    private static string MaskSensitiveValue(string fieldKey, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        if (fieldKey.Contains("account", StringComparison.OrdinalIgnoreCase))
            return DataMasker.MaskBankAccount(value);
        if (fieldKey.EndsWith("name", StringComparison.OrdinalIgnoreCase))
            return DataMasker.MaskName(value);
        if (fieldKey == ElderlyInfoChangeConstants.FieldPhone)
            return DataMasker.MaskPhone(value);
        return value;
    }

    #endregion
}
