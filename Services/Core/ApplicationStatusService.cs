using NewCosmos.Constants;
using NewCosmos.Models.Enums;
using NewCosmos.Models.Results;
using NewCosmos.Services.Database;
using NewCosmos.Services.StateMachine;

namespace NewCosmos.Services.Core;

/// <inheritdoc cref="IApplicationStatusService" />
public class ApplicationStatusService : BaseService, IApplicationStatusService
{
    private readonly IDatabaseService _db;

    protected override string ServiceName => "ApplicationStatusService";

    public ApplicationStatusService(IDatabaseService db, ILoggerService logger)
        : base(logger)
    {
        _db = db;
    }

    /// <inheritdoc />
    public Task<Result> SubmitAsync(long applicationId, string submittedBy, CancellationToken ct = default)
        => TransitionAsync(applicationId, ApplicationStatus.Submitted, submittedBy, null, null,
            action: "提交申请", detail: null, allowSubmittedOverride: false, ct: ct);

    /// <inheritdoc />
    public Task<Result> ApproveAsync(long applicationId, string approvedBy, CancellationToken ct = default)
        => TransitionAsync(applicationId, ApplicationStatus.Approved, approvedBy, null, null,
            action: "审批通过", detail: null, allowSubmittedOverride: false, ct: ct);

    /// <inheritdoc />
    public Task<Result> RefuseAsync(long applicationId, string refusedBy, CancellationToken ct = default)
        => TransitionAsync(applicationId, ApplicationStatus.Refused, refusedBy, null, null,
            action: "不予受理", detail: null, allowSubmittedOverride: false, ct: ct);

    /// <inheritdoc />
    public async Task<Result> CompleteArchiveAsync(long applicationId, string operatorName, CancellationToken ct = default)
    {
        try
        {
            var appResult = await _db.QuerySingleAsync<StatusRow>(
                "SELECT id, status, current_step FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL",
                ct, applicationId);
            if (appResult.IsFailure)
                return Result.Failure(appResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    appResult.Message ?? "读取申请状态失败");
            if (appResult.Value == null)
                return Result.Failure(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");

            var current = appResult.Value;
            var currentStatus = ApplicationStatusExtensions.FromCode(current.Status ?? string.Empty);
            var targetStatus = ApplicationStatus.Approved;

            // 已 Approved/Completed：仅推进步骤（幂等），不改状态避免降级
            if (currentStatus is ApplicationStatus.Approved or ApplicationStatus.Completed)
            {
                var stepOnly = await _db.ExecuteNonQueryAsync(
                    @"UPDATE nc_biz_applications SET current_step = $2, updated_at = NOW()
                      WHERE id = $1 AND deleted_at IS NULL", ct, applicationId, WorkflowSteps.ARCHIVED);
                if (stepOnly.IsFailure)
                    return Result.Failure(stepOnly.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        stepOnly.Message ?? "归档推进步骤失败");
                await WriteAuditAsync(applicationId, "完成归档", "已 Approved/Completed，仅推进 current_step=6",
                    current.Status, current.Status, operatorName, ct);
                LogInfo($"归档完成（幂等，状态未变）: ApplicationId={applicationId}, Status={current.Status}");
                return Result.Success();
            }

            var transition = ApplicationStateMachine.ValidateTransition(currentStatus, targetStatus);
            if (transition.IsFailure)
                return Result.Failure(ErrorCodes.INVALID_TRANSITION,
                    transition.Message ?? $"不允许从 {currentStatus.GetDescription()} 转换到 {targetStatus.GetDescription()}");

            // 归档 = 审批通过 + 已归档完结：status=Approved、current_step=6、first_approved_at 只写一次。
            // 导入建档档案的纳入时间已在补全（MarkDataCompletedAsync）写入，此处 COALESCE 保留；
            // 普通新建申请的纳入时间即审批时刻（NOW()）。
            var sql = @"UPDATE nc_biz_applications SET
                status = $1,
                current_step = $4,
                first_approved_at = COALESCE(first_approved_at, NOW()),
                updated_at = NOW(),
                updated_by = $2
                WHERE id = $3 AND deleted_at IS NULL";
            var result = await _db.ExecuteNonQueryAsync(sql, ct, targetStatus.GetCode(), operatorName, applicationId, WorkflowSteps.ARCHIVED);
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    result.Message ?? "归档完成失败");
            if (result.Value == 0)
                return Result.Failure(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");

            await WriteAuditAsync(applicationId, "完成归档",
                "归档完结：status=Approved、current_step=6、first_approved_at 落库",
                current.Status, targetStatus.GetCode(), operatorName, ct);

            Logger.LogBusiness("完成归档", ("ApplicationId", applicationId), ("Operator", operatorName ?? string.Empty));
            LogInfo($"归档完成: ApplicationId={applicationId}, {currentStatus.GetDescription()} → Approved");
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, "完成归档");
            return Result.FromException(ex);
        }
    }

    /// <inheritdoc />
    public Task<Result> StopAsync(
        long applicationId,
        string stopReason,
        DateTime stopDate,
        string operatorName,
        bool allowSubmittedOverride = false,
        CancellationToken ct = default)
        => TransitionAsync(applicationId, ApplicationStatus.Stopped, operatorName, stopReason, stopDate,
            action: "停保", detail: stopReason, allowSubmittedOverride: allowSubmittedOverride, ct: ct);

    /// <summary>
    /// 状态流转单点实现：校验 → 写状态与配套列 → 审计留痕。
    /// </summary>
    private async Task<Result> TransitionAsync(
        long applicationId,
        ApplicationStatus target,
        string? operatorName,
        string? stopReason,
        DateTime? stopDate,
        string action,
        string? detail,
        bool allowSubmittedOverride,
        CancellationToken ct)
    {
        try
        {
            var appResult = await _db.QuerySingleAsync<StatusRow>(
                "SELECT id, status, current_step FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL",
                ct, applicationId);
            if (appResult.IsFailure)
                return Result.Failure(appResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    appResult.Message ?? "读取申请状态失败");
            if (appResult.Value == null)
                return Result.Failure(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");

            var current = appResult.Value;
            var currentStatus = ApplicationStatusExtensions.FromCode(current.Status ?? string.Empty);
            if (currentStatus == target)
                return Result.Success(); // 幂等：已是目标状态

            var transition = ApplicationStateMachine.ValidateTransition(currentStatus, target);

            // 业务放行：Submitted → Stopped（在途申请的全员死亡需能关闭）。
            // Draft/Refused → Stopped 仍拒绝：从未进入保障，不存在"停保"一说。
            if (transition.IsFailure
                && target == ApplicationStatus.Stopped
                && allowSubmittedOverride
                && currentStatus == ApplicationStatus.Submitted)
            {
                LogWarn($"状态 {currentStatus.GetDescription()} → {target.GetDescription()} 不在严格状态机允许范围内，按业务规则放行并记警告: ApplicationId={applicationId}");
                transition = Result.Success();
            }

            if (transition.IsFailure)
                return Result.Failure(ErrorCodes.INVALID_TRANSITION,
                    transition.Message ?? $"不允许从 {currentStatus.GetDescription()} 转换到 {target.GetDescription()}");

            // 状态与配套列的单点写入
            var sets = new List<string> { "status = $1", "updated_at = NOW()" };
            var args = new List<object?> { target.GetCode() };
            void Set(string col, object? v) { args.Add(v); sets.Add($"{col} = ${args.Count}"); }

            if (target == ApplicationStatus.Submitted)
            {
                Set("submit_at", DateTime.Now);
                Set("submit_by", operatorName);
            }
            if (target == ApplicationStatus.Approved)
            {
                // 首次审批时间只写一次。普通新建申请的"纳入时间"即审批时刻（NOW()）；
                // 导入建档档案的纳入时间已在建库时写入（ImportedArchiveService / MarkDataCompletedAsync），
                // 此处 COALESCE 保留其值，不覆盖。
                sets.Add("first_approved_at = COALESCE(first_approved_at, NOW())");
            }
            if (target == ApplicationStatus.Stopped)
            {
                Set("stop_reason", stopReason ?? string.Empty);
                Set("stop_date", stopDate ?? DateTime.Today);
            }
            if (!string.IsNullOrEmpty(operatorName))
                Set("updated_by", operatorName);

            args.Add(applicationId);
            var sql = $@"UPDATE nc_biz_applications SET {string.Join(", ", sets)}
                         WHERE id = ${args.Count} AND deleted_at IS NULL";

            var result = await _db.ExecuteNonQueryAsync(sql, ct, args.ToArray());
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    result.Message ?? "状态更新失败");
            if (result.Value == 0)
                return Result.Failure(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");

            await WriteAuditAsync(applicationId, action, detail,
                currentStatus.GetDescription(), target.GetDescription(), operatorName, ct);

            Logger.LogBusiness(action, ("ApplicationId", applicationId),
                ("From", currentStatus.GetDescription()), ("To", target.GetDescription()),
                ("Operator", operatorName ?? string.Empty));
            LogInfo($"{action}: ApplicationId={applicationId}, {currentStatus.GetDescription()} → {target.GetDescription()}");
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, action);
            return Result.FromException(ex);
        }
    }

    /// <summary>审计留痕（AGENTS.md §8）：状态流转写 nc_biz_application_logs，失败仅告警不阻断主流程。</summary>
    private async Task WriteAuditAsync(long applicationId, string action, string? detail,
        string? oldValue, string? newValue, string? operatorName, CancellationToken ct)
    {
        try
        {
            var audit = await _db.ExecuteNonQueryAsync(
                @"INSERT INTO nc_biz_application_logs
                      (application_id, action, action_detail, old_value, new_value, operator_name, operated_at)
                  VALUES ($1, $2, $3, $4, $5, $6, NOW())",
                ct, applicationId, action, detail, oldValue, newValue, operatorName);
            if (audit.IsFailure)
                LogWarn($"审计留痕写入失败: ApplicationId={applicationId}, Action={action}, {audit.Message}");
        }
        catch (Exception ex)
        {
            LogWarn($"审计留痕写入异常: ApplicationId={applicationId}, Action={action}, {ex.Message}");
        }
    }

    /// <summary>状态流转读取行</summary>
    private sealed class StatusRow
    {
        public long Id { get; set; }
        public string? Status { get; set; }
        public int CurrentStep { get; set; }
    }
}
