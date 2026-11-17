using System.Text.Json;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Enums;
using NewCosmos.Models.Results;
using NewCosmos.Services.StateMachine;

namespace NewCosmos.Services.Domain.ChangeManagement;

using ApplicationEntity = NewCosmos.Models.Entities.Application;

public partial class ChangeService
{
    /// <summary>
    /// 核验停止（保障对象动态管理「拒绝配合核验工作」按钮）：核验失败、拒不配合 → 直接停止保障。
    /// 与单人户户主死亡整户停止同型：只停旧档、不建新档，分类与保障金列不动（§9.7 保障金属审批结果），
    /// 停保后可经「恢复保障」无损逆向（ResumeAsync 无下游新档即放行）。
    /// 事务内：状态守卫 → 停止旧档（ApplicationStatusService 单点写入）→ 结清渐退期 → 写变更记录（VerifyStop）。
    /// </summary>
    public async Task<Result<ChangeResult>> ExecuteVerifyStopAsync(VerifyStopContext context, CancellationToken ct = default)
    {
        LogInfo($"执行核验停止: ApplicationId={context.ApplicationId}");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            // 1. 读旧档案（已软删的申请不允许再变更）
            var appResult = await _db.QuerySingleAsync<ApplicationEntity>(
                $"SELECT {ApplicationColumns.Full} FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL",
                ct, context.ApplicationId);
            if (!appResult.IsSuccess || appResult.Value == null)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");
            }
            var application = appResult.Value;
            var oldClassification = application.ClassificationResult ?? string.Empty;
            var oldGuaranteeAmount = application.TotalGuaranteeAmount;
            var oldPerCapitaIncome = application.PerCapitaIncome;

            // 2. 状态守卫（与单人户户主死亡"最严"同策略）：
            //    - 已停保：入口卡片对停保档案本就隐藏（IsApplicationStopped 取反），此处为防御路径，拒绝重复执行；
            //    - Draft / Refused（从未进入保障）拒绝——状态机禁止 Draft→Stopped 的脏状态；
            //    - Submitted 放行（allowSubmittedOverride 由 StopAsync 侧统一 LogWarn 处理）。
            var currentStatus = ApplicationStatusExtensions.FromCode(application.Status ?? string.Empty);
            if (currentStatus == ApplicationStatus.Stopped)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.INVALID_TRANSITION,
                    "该档案已停保，无需重复执行核验停止");
            }
            if (currentStatus is ApplicationStatus.Draft or ApplicationStatus.Refused)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.INVALID_TRANSITION,
                    $"档案处于{currentStatus.GetDescription()}状态（从未进入保障），不能办理核验停止");
            }
            var transition = ApplicationStateMachine.ValidateTransition(currentStatus, ApplicationStatus.Stopped);
            if (transition.IsFailure)
                LogWarn($"状态 {application.Status} → Stopped 不在严格状态机允许范围内，按业务规则（拒绝配合核验工作）放行");

            // 3. 停止旧档案（状态写入统一走 ApplicationStatusService：状态机校验 + stop_reason/stop_date 落库 +
            //    审计留痕 + 其他困难人员台账联动）。stop_reason 保持纯理由文本——月报停保汇总理由列、
            //    特困取消备案表句式归一（ResolveKeyFromText → "其他"）、渐退审批表退出分支 D 均按自由文本消费。
            var stopResult = await _statusService.StopAsync(
                context.ApplicationId, "拒绝配合核验工作", context.StopDate,
                context.OperatorName, allowSubmittedOverride: true, ct: ct);
            if (stopResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(stopResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    stopResult.Message ?? "停止档案失败");
            }

            // 4. 结清渐退期（同"只停不建新"流程：渐退期仅对在保户有意义）
            var graceClearResult = await _gracePeriodService.ClearAsync(context.ApplicationId, ct);
            if (graceClearResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(graceClearResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    graceClearResult.Message ?? "结清渐退期失败");
            }

            // 5. 写变更记录（change_type=VerifyStop：变更历史显示"核验停止"，告知书按本类型取数）
            var changeContext = new ChangeContext
            {
                ApplicationId = context.ApplicationId,
                ChangeType = DictionaryConstants.ChangeType.VERIFY_STOP,
                // change_category=NULL：非人员变动，不入"增减员调整表"
                //（GetMemberChangeRecordsAsync 按 change_category='MemberChange' OR 人员变动 type 兜底取数）
                ChangeCategory = null,
                ChangeReason = "拒绝配合核验工作",
                ChangeReasonType = ChangeReasonTypeConstants.VerifyStop,
                ChangeDate = context.StopDate,
                OperatorName = context.OperatorName
            };
            var beforeJson = JsonSerializer.Serialize(new
            {
                HeadName = application.ApplicantName,
                HeadIdCard = application.ApplicantIdCard,
                FamilySize = application.FamilySize,
                Classification = oldClassification,
                GuaranteeAmount = oldGuaranteeAmount,
                Status = application.Status
            });
            var afterJson = JsonSerializer.Serialize(new
            {
                Status = ApplicationStatus.Stopped.GetCode(),
                StopReason = "拒绝配合核验工作",
                StopDate = context.StopDate,
                StoppedOnly = true
            });
            var changeIdResult = await CreateChangeAsync(changeContext, beforeJson, afterJson, ct);
            if (changeIdResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(changeIdResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    changeIdResult.Message ?? "创建变更记录失败");
            }
            var changeId = changeIdResult.Value;

            // 6. 回填变更记录结构化字段（无新档：new_classification/new_guarantee/new_application_id 一律 NULL）。
            //    triggered_stop=true：本记录就是停保事件本身——渐退审批表「退出渐退期情况」按
            //    change_date DESC 取最近一条，置 true 才能让本记录胜出并落入分支 D（正确停止日期+本理由），
            //    否则会被链上旧经济复核记录抢走、渲染成旧日期配本次停止理由。
            //    其余消费点均按 change_type 收窄（CategoryStop/HouseholdDeath），不会误命中本记录。
            var updateChangeSql = @"UPDATE nc_biz_change_records SET
                change_reason_type = $1,
                old_classification = $2,
                old_per_capita_income = $3,
                old_guarantee_amount = $4,
                triggered_stop = $5,
                original_application_id = $6,
                changed_at = NOW()
                WHERE id = $7;";
            var updateChangeResult = await _db.ExecuteNonQueryAsync(updateChangeSql, ct,
                ChangeReasonTypeConstants.VerifyStop,
                oldClassification,
                oldPerCapitaIncome,
                oldGuaranteeAmount,
                true,
                context.ApplicationId,
                changeId);
            if (updateChangeResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(updateChangeResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    updateChangeResult.Message ?? "更新变更记录对比字段失败");
            }

            await tx.CommitAsync(ct);

            LogInfo($"核验停止完成: 档案={context.ApplicationId} 已停保（拒绝配合核验工作）");

            return Result.Success(new ChangeResult
            {
                ChangeId = changeId,
                IsSuccess = true,
                OldClassification = oldClassification,
                OldGuaranteeAmount = oldGuaranteeAmount,
                TriggeredStop = true,
                NewApplicationId = 0
            });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "核验停止");
            return Result.FromException<ChangeResult>(ex);
        }
    }

    /// <summary>
    /// 查询最近一条"核验停止"记录（无则 Value 为 null 非失败）。
    /// 档案输出据此追加《档案_变更告知书》并装配"停止/停止享受"字段（理由含核验日期）。
    /// </summary>
    public async Task<Result<VerifyStopRecord?>> GetLatestVerifyStopRecordAsync(long applicationId, CancellationToken ct = default)
    {
        var sql = @"SELECT change_date AS stop_date
                    FROM nc_biz_change_records
                    WHERE (application_id = $1 OR new_application_id = $1)
                      AND change_type = $2
                      AND deleted_at IS NULL
                    ORDER BY change_date DESC, id DESC
                    LIMIT 1";
        var result = await _db.QuerySingleAsync<VerifyStopRecord>(sql, ct,
            applicationId, DictionaryConstants.ChangeType.VERIFY_STOP);
        if (result.IsFailure)
            return Result.Failure<VerifyStopRecord?>(result.ErrorCode!, result.Message!);
        return Result.Success<VerifyStopRecord?>(result.Value);
    }
}
