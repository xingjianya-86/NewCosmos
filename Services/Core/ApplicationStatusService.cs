using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Enums;
using NewCosmos.Models.Results;
using NewCosmos.Services.Database;
using NewCosmos.Services.StateMachine;
using NewCosmos.Services.Utilities;

namespace NewCosmos.Services.Core;

/// <inheritdoc cref="IApplicationStatusService" />
public class ApplicationStatusService : BaseService, IApplicationStatusService
{
    private readonly IDatabaseService _db;
    private readonly IBusinessTimelineService _timelineService;

    protected override string ServiceName => "ApplicationStatusService";

    public ApplicationStatusService(IDatabaseService db, ILoggerService logger, IBusinessTimelineService timelineService)
        : base(logger)
    {
        _db = db;
        _timelineService = timelineService;
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

            // 已 Approved/Completed：仅推进步骤（幂等），不改状态避免降级。
            // 顺带补空 first_approved_at（COALESCE，已有值不覆盖）：走「归档判停→恢复在保」路径的档案
            // 从未经过写首审时间的分支，会永久为 NULL —— 补打中心按月查询强制
            // first_approved_at IS NOT NULL，该档案任何年月都查不到；月报"本月新增"同样漏计。
            // 此处是存量自愈入口：下次完成归档即补齐。
            if (currentStatus is ApplicationStatus.Approved or ApplicationStatus.Completed)
            {
                var stepOnly = await _db.ExecuteNonQueryAsync(
                    @"UPDATE nc_biz_applications SET current_step = $2,
                        first_approved_at = COALESCE(first_approved_at, NOW()), updated_at = NOW()
                      WHERE id = $1 AND deleted_at IS NULL", ct, applicationId, WorkflowSteps.ARCHIVED);
                if (stepOnly.IsFailure)
                    return Result.Failure(stepOnly.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        stepOnly.Message ?? "归档推进步骤失败");
                await WriteAuditAsync(applicationId, "完成归档",
                    "已 Approved/Completed，仅推进 current_step=6（first_approved_at 为空则补空，不覆盖原值）",
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

            // 时序闸口（告警不拦）：转 Approved 时检查建档/审批与受理截止、公示期的先后
            if (targetStatus == ApplicationStatus.Approved)
                await LogTimelineOrderWarningsAsync(applicationId, operatorName, ct);

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

    /// <inheritdoc />
    public async Task<Result> ResumeAsync(long applicationId, string operatorName, CancellationToken ct = default)
    {
        const string action = "恢复保障";
        try
        {
            var appResult = await _db.QuerySingleAsync<StatusRow>(
                "SELECT id, status, current_step, stop_reason FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL",
                ct, applicationId);
            if (appResult.IsFailure)
                return Result.Failure(appResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    appResult.Message ?? "读取申请状态失败");
            if (appResult.Value == null)
                return Result.Failure(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");

            var current = appResult.Value;
            var currentStatus = ApplicationStatusExtensions.FromCode(current.Status ?? string.Empty);

            // 幂等：已在保无需恢复
            if (currentStatus is ApplicationStatus.Approved or ApplicationStatus.Completed)
                return Result.Success();

            // 只有已停保档案可恢复（Draft/Refused 从未进入保障，不存在"恢复"一说）
            if (currentStatus != ApplicationStatus.Stopped)
                return Result.Failure(ErrorCodes.INVALID_TRANSITION,
                    $"仅已停保档案可恢复保障，当前状态：{currentStatus.GetDescription()}");

            var transition = ApplicationStateMachine.ValidateTransition(currentStatus, ApplicationStatus.Approved);
            if (transition.IsFailure)
                return Result.Failure(ErrorCodes.INVALID_TRANSITION,
                    transition.Message ?? $"不允许从 {currentStatus.GetDescription()} 转换到 {ApplicationStatus.Approved.GetDescription()}");

            // 守卫：停旧建新链上已有未删除的下游新档 → 拒绝恢复，
            // 否则旧档与新档同时在保，同一家庭会出现两份在保档案（统计、月报口径同时失真）。
            var chainResult = await _db.ExecuteScalarAsync(
                "SELECT COUNT(*) FROM nc_biz_applications WHERE original_application_id = $1 AND deleted_at IS NULL",
                ct, applicationId);
            if (chainResult.IsFailure)
                return Result.Failure(chainResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    chainResult.Message ?? "恢复保障前置检查失败");
            if (Convert.ToInt32(chainResult.Value) > 0)
                return Result.Failure(ErrorCodes.INVALID_TRANSITION,
                    "该停保档案已停旧建新生成新档案，不可恢复；请在新档案上办理业务");

            // 状态与配套列单点写入：恢复为在保并清空停保标记（current_step 不动）。
            // first_approved_at 只补空不覆盖（COALESCE，与 ApproveAsync/完成归档/补全同一口径）：
            // 「归档判停 → 恢复在保」是 Approved 的旁路出口，若不补空，先判停后恢复的档案
            // 首次审批时间永久为 NULL（补打中心按月查询、月报"本月新增"均按该列过滤，会永远漏掉它）。
            var sets = new List<string>
            {
                "status = $1", "stop_reason = NULL", "stop_date = NULL",
                "first_approved_at = COALESCE(first_approved_at, NOW())", "updated_at = NOW()"
            };
            var args = new List<object?> { ApplicationStatus.Approved.GetCode() };
            if (!string.IsNullOrEmpty(operatorName))
            {
                args.Add(operatorName);
                sets.Add($"updated_by = ${args.Count}");
            }
            args.Add(applicationId);
            var updateSql = $@"UPDATE nc_biz_applications SET {string.Join(", ", sets)}
                               WHERE id = ${args.Count} AND deleted_at IS NULL";
            var updateResult = await _db.ExecuteNonQueryAsync(updateSql, ct, args.ToArray());
            if (updateResult.IsFailure)
                return Result.Failure(updateResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    updateResult.Message ?? "状态更新失败");
            if (updateResult.Value == 0)
                return Result.Failure(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");

            await WriteAuditAsync(applicationId, action,
                $"恢复在保：status=Approved、stop_reason/stop_date 清空（原停保原因：{current.StopReason ?? "无"}）",
                currentStatus.GetDescription(), ApplicationStatus.Approved.GetDescription(), operatorName, ct);

            // 时序闸口（告警不拦）：恢复在保等同转 Approved，检查建档/审批与受理截止、公示期的先后
            await LogTimelineOrderWarningsAsync(applicationId, operatorName, ct);

            Logger.LogBusiness(action, ("ApplicationId", applicationId),
                ("Operator", operatorName ?? string.Empty));
            LogInfo($"{action}: ApplicationId={applicationId}, {currentStatus.GetDescription()} → {ApplicationStatus.Approved.GetDescription()}");
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogException(ex, action);
            return Result.FromException(ex);
        }
    }

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

            // 时序闸口（告警不拦）：审批通过时检查建档/审批与受理截止、公示期的先后
            if (target == ApplicationStatus.Approved)
                await LogTimelineOrderWarningsAsync(applicationId, operatorName, ct);

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

    /// <summary>
    /// 归档/审批时序闸口（告警不拦，AGENTS §10：低频操作日志 + 审计留痕）：
    /// ① 建档日期 &gt; 该档案 B 线受理截止（AcceptanceDeadline）→ 逾期受理告警；
    /// ② 审批时刻 &lt; 该档案 B 线公示结束日（PublicityEndDate）→ 审批先于公示完成告警。
    /// 窗口按申请日期锚定（GetTimelineForApplicationDate，与文书产出/Step4 校验同源）。
    /// 只在转 Approved（归档通过 / 审批通过 / 恢复在保）时触发；检查失败只留日志，绝不影响归档主流程。
    /// </summary>
    private async Task LogTimelineOrderWarningsAsync(long applicationId, string? operatorName, CancellationToken ct)
    {
        try
        {
            var rowResult = await _db.QuerySingleAsync<TimelineCheckRow>(
                @"SELECT application_no, created_at, first_approved_at, data_completed_at, updated_at
                  FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL", ct, applicationId);
            var row = rowResult.IsSuccess ? rowResult.Value : null;
            if (row == null) return;

            // 窗口锚点 = 申请日期（申请编号内嵌日期优先，与文书产出同源）
            var applicationDate = ArchiveAnchorHelper.ResolveApplicationDate(
                row.ApplicationNo, row.CreatedAt, row.FirstApprovedAt, row.DataCompletedAt, row.UpdatedAt);
            var tl = _timelineService.GetTimelineForApplicationDate(applicationDate, TimelineType.BusinessProcess);

            var warnings = new List<string>();
            if (row.CreatedAt != default && row.CreatedAt.Date > tl.AcceptanceDeadline.Date)
                warnings.Add($"建档 {row.CreatedAt:yyyy-MM-dd} 晚于受理截止 {tl.AcceptanceDeadline:yyyy-MM-dd}（逾期受理）");

            if (DateTime.Today < tl.PublicityEndDate.Date)
                warnings.Add($"审批早于公示结束 {tl.PublicityEndDate:yyyy-MM-dd}"
                    + $"（本档案公示期 {tl.PublicityStartDate:yyyy-MM-dd}~{tl.PublicityEndDate:yyyy-MM-dd}，先归档后公示）");

            if (warnings.Count == 0) return;

            var detail = string.Join("；", warnings);
            LogWarn($"时序告警: ApplicationId={applicationId}, {detail}");
            await WriteAuditAsync(applicationId, "时序告警", detail, null, null, operatorName, ct);
        }
        catch (Exception ex)
        {
            LogWarn($"时序告警检查失败（不影响归档）: ApplicationId={applicationId}, {ex.Message}");
        }
    }

    /// <summary>时序告警检查读取行（申请日期锚定所需列）</summary>
    private sealed class TimelineCheckRow
    {
        public string? ApplicationNo { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? FirstApprovedAt { get; set; }
        public DateTime? DataCompletedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>状态流转读取行</summary>
    private sealed class StatusRow
    {
        public long Id { get; set; }
        public string? Status { get; set; }
        public int CurrentStep { get; set; }
        public bool? IsEligible { get; set; }
        public string? ClassificationResult { get; set; }
        public string? StopReason { get; set; }
    }

    /// <summary>归档终结的停保原因（复核判定不合格），如「复核判定不合格 · 收入超标」。</summary>
    private static string BuildIneligibleStopReason(string? classificationResult)
        => $"复核判定不合格 · {ClassificationConstants.ConvertToMajorCategoryName(classificationResult ?? string.Empty)}";
}
