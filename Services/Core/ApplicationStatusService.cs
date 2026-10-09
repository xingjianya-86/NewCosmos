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
                "SELECT id, status, current_step, is_eligible, classification_result FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL",
                ct, applicationId);
            if (appResult.IsFailure)
                return Result.Failure(appResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    appResult.Message ?? "读取申请状态失败");
            if (appResult.Value == null)
                return Result.Failure(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");

            var current = appResult.Value;
            var currentStatus = ApplicationStatusExtensions.FromCode(current.Status ?? string.Empty);

            // 步骤门槛：五个录入步骤未完成的残档不得一键归档成「已完结」。
            // 放行 Draft 直落目标状态后，若不拦，step=1 的半成品也会跳到 step=6。
            // 已 Approved/Completed/Stopped 的存量行 current_step 均为 6，不受此门槛影响。
            if (current.CurrentStep < WorkflowSteps.PENDING_ARCHIVE)
                return Result.Failure(ErrorCodes.VALIDATION_FAILED,
                    $"档案录入未完成（当前第 {current.CurrentStep} 步，需完成第 {WorkflowSteps.PENDING_ARCHIVE} 步），不能完成归档");

            // 归档目标状态：复核判定不合格 → Stopped；否则 Approved（系统无独立审批工作流，归档即视为通过）。
            // 库内先例：「已完结」Tab 中 is_eligible=false 的档案一律为 Stopped，Approved 全部为 true。
            // is_eligible 列 defaultValue=false（未判定的新档同为 false），必须结合 classification_result
            // 非空才能区分「判了不合格」与「还没判」，否则未判定的新档会被误置为 Stopped。
            var isIneligible = !string.IsNullOrEmpty(current.ClassificationResult)
                               && current.IsEligible == false;
            var targetStatus = isIneligible ? ApplicationStatus.Stopped : ApplicationStatus.Approved;

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

            // 已 Stopped：终态不改状态，仅推进步骤使其进入「已完结」（Stopped 在已完结过滤集内）。
            // 历史遗留的 step<6 停保行（如停旧建新中途停保）五个 Tab 全不可见，推步后归位。
            if (currentStatus == ApplicationStatus.Stopped)
            {
                var stopStepOnly = await _db.ExecuteNonQueryAsync(
                    @"UPDATE nc_biz_applications SET current_step = $2, updated_at = NOW()
                      WHERE id = $1 AND deleted_at IS NULL", ct, applicationId, WorkflowSteps.ARCHIVED);
                if (stopStepOnly.IsFailure)
                    return Result.Failure(stopStepOnly.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        stopStepOnly.Message ?? "归档推进步骤失败");
                await WriteAuditAsync(applicationId, "完成归档", "已 Stopped（终态），仅推进 current_step=6",
                    current.Status, current.Status, operatorName, ct);
                LogInfo($"归档完成（已停保，仅推步）: ApplicationId={applicationId}");
                return Result.Success();
            }

            var transition = ApplicationStateMachine.ValidateTransition(currentStatus, targetStatus);

            // 业务放行：系统无独立审批工作流，归档即视为审批通过/终结。
            // 停旧建新产物（CategoryRebuild，Draft+1）与数据录入完成的档案停留在 Draft，
            // 归档必须能直接落到 Approved/Stopped —— 否则完成归档对这批档案恒失败。
            // 与 TransitionAsync 的 allowSubmittedOverride 同一模式：放行并记警告，
            // 不放宽状态机本身（ApproveAsync / 一事一议等入口仍受严格约束）。
            if (transition.IsFailure
                && currentStatus is ApplicationStatus.Draft or ApplicationStatus.Submitted)
            {
                LogWarn($"归档状态放行 {currentStatus.GetDescription()} → {targetStatus.GetDescription()}（归档即视为通过）: ApplicationId={applicationId}");
                transition = Result.Success();
            }

            if (transition.IsFailure)
                return Result.Failure(ErrorCodes.INVALID_TRANSITION,
                    transition.Message ?? $"不允许从 {currentStatus.GetDescription()} 转换到 {targetStatus.GetDescription()}");

            // 归档完结：current_step=6，status=目标状态。两条分支各自的配套列与状态机写入口径一致
            // （见 TransitionAsync）：Approved 写 first_approved_at 只写一次（导入建档的纳入时间已在
            // 补全 MarkDataCompletedAsync 写入，COALESCE 保留；普通申请即审批时刻 NOW()）；
            // Stopped 写 stop_reason / stop_date（归档终结的停保原因）。
            var sets = new List<string> { "status = $1", "current_step = $2", "updated_at = NOW()" };
            var args = new List<object> { targetStatus.GetCode(), WorkflowSteps.ARCHIVED };
            void Set(string col, object? v) { args.Add(v!); sets.Add($"{col} = ${args.Count}"); }

            if (targetStatus == ApplicationStatus.Approved)
                sets.Add("first_approved_at = COALESCE(first_approved_at, NOW())");
            else if (targetStatus == ApplicationStatus.Stopped)
            {
                Set("stop_reason", BuildIneligibleStopReason(current.ClassificationResult));
                Set("stop_date", DateTime.Today);
            }
            Set("updated_by", operatorName);

            args.Add(applicationId);
            var sql = $@"UPDATE nc_biz_applications SET {string.Join(", ", sets)}
                         WHERE id = ${args.Count} AND deleted_at IS NULL";
            var result = await _db.ExecuteNonQueryAsync(sql, ct, args.ToArray());
            if (result.IsFailure)
                return Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    result.Message ?? "归档完成失败");
            if (result.Value == 0)
                return Result.Failure(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");

            await WriteAuditAsync(applicationId, "完成归档",
                targetStatus == ApplicationStatus.Stopped
                    ? "归档终结：判定不合格，status=Stopped、current_step=6、stop_reason/stop_date 落库"
                    : "归档完结：status=Approved、current_step=6、first_approved_at 落库",
                current.Status, targetStatus.GetCode(), operatorName, ct);

            Logger.LogBusiness("完成归档", ("ApplicationId", applicationId),
                ("Status", targetStatus.GetCode()), ("Operator", operatorName ?? string.Empty));
            LogInfo($"归档完成: ApplicationId={applicationId}, {currentStatus.GetDescription()} → {targetStatus.GetDescription()}");
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
        public bool? IsEligible { get; set; }
        public string? ClassificationResult { get; set; }
    }

    /// <summary>归档终结的停保原因（复核判定不合格），如「复核判定不合格 · 收入超标」。</summary>
    private static string BuildIneligibleStopReason(string? classificationResult)
        => $"复核判定不合格 · {ClassificationConstants.ConvertToMajorCategoryName(classificationResult ?? string.Empty)}";
}
