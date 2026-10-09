using System.Text;
using System.Text.Json;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Enums;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.SocialAssistance;
using NewCosmos.Services.StateMachine;
using NewCosmos.Services.System;

namespace NewCosmos.Services.Domain.ChangeManagement;

using ApplicationEntity = NewCosmos.Models.Entities.Application;

public partial class ChangeService
{
    public async Task<Result<long>> CreateChangeAsync(ChangeContext context, string beforeJson, string afterJson, CancellationToken ct = default)
    {
        var changeNo = $"CHG{DateTime.Now:yyyyMMddHHmmssfff}";

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            var sql = @"INSERT INTO nc_biz_change_records 
                       (application_id, change_no, change_type, change_category, change_reason, change_reason_type, change_date, operator_name, changed_at)
                       VALUES ($1,$2,$3,$8,$4,$5,$6,$7, NOW()) RETURNING id;";

            var result = await _db.ExecuteScalarAsync(sql, ct,
                context.ApplicationId, changeNo, context.ChangeType,
                context.ChangeReason, context.ChangeReasonType,
                context.ChangeDate, context.OperatorName,
                (object?)context.ChangeCategory);

            // 变更主记录写入失败必须中断：静默返回 0 会让调用方带着无效 ChangeId 提交事务，
            // 审计链条就断了。Result 化后由调用方显式检查并回滚。
            if (result.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<long>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    result.Message ?? "创建变更记录失败");
            }

            var changeId = result.Value;

            // 快照是变更审计的核心证据：写入失败即整体失败（避免"变更成功但无快照"断档）
            var beforeSnap = string.IsNullOrEmpty(beforeJson)
                ? Result.Success()
                : await SaveSnapshotAsync(changeId, "Before", beforeJson, ct);
            if (beforeSnap.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<long>(beforeSnap.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    beforeSnap.Message ?? "保存变更快照失败");
            }

            var afterSnap = string.IsNullOrEmpty(afterJson)
                ? Result.Success()
                : await SaveSnapshotAsync(changeId, "After", afterJson, ct);
            if (afterSnap.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<long>(afterSnap.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    afterSnap.Message ?? "保存变更快照失败");
            }

            await tx.CommitAsync(ct);

            LogInfo($"创建变更记录: ChangeId={changeId}");
            return Result.Success(changeId);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "创建变更记录");
            return Result.FromException<long>(ex);
        }
    }

    public async Task<Result> SaveSnapshotAsync(long changeId, string snapshotType, string dataJson, CancellationToken ct = default)
    {
        var sql = @"INSERT INTO nc_biz_change_snapshots (change_id, snapshot_type, snapshot_data) VALUES ($1,$2,$3::jsonb);";
        var result = await _db.ExecuteNonQueryAsync(sql, ct, changeId, snapshotType, dataJson);
        // 快照是变更审计的核心证据，写入失败不能吞掉（否则出现"变更成功但无快照"的断档）
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? $"保存变更快照失败: ChangeId={changeId}, Type={snapshotType}");
        return Result.Success();
    }

    /// <summary>
    /// 渐退超限减发：补写 FundChange（old&gt;new、同分类）供月报保障金减发表。
    /// 幂等：同户同 change_reason_type=户主死亡 且 old/new 金额一致的 FundChange 已存在则跳过。
    /// </summary>
    public async Task<Result<long>> CreateGraceCapFundChangeAsync(
        long applicationId,
        string newClassification,
        string oldClassification,
        decimal oldGuaranteeAmount,
        decimal newGuaranteeAmount,
        CancellationToken ct = default)
    {
        // 只在"减发"（新额 < 旧额）时写入；持平/增发不属于超限减发
        if (newGuaranteeAmount >= oldGuaranteeAmount)
            return Result.Success(0L);

        try
        {
            var existSql = @"SELECT id FROM nc_biz_change_records
                WHERE application_id = $1 AND change_type = $2
                  AND change_reason_type = $3
                  AND old_guarantee_amount = $4 AND new_guarantee_amount = $5
                  AND deleted_at IS NULL
                LIMIT 1";
            var exist = await _db.QuerySingleAsync<long?>(existSql, ct,
                applicationId, DictionaryConstants.ChangeType.FUND_CHANGE,
                ChangeReasonTypeConstants.HeadDeceased,
                oldGuaranteeAmount, newGuaranteeAmount);
            if (exist.IsSuccess && exist.Value.HasValue && exist.Value.Value > 0)
                return Result.Success(exist.Value.Value);

            await using var tx = await _db.BeginTransactionScopeAsync(ct);

            var changeContext = new ChangeContext
            {
                ApplicationId = applicationId,
                ChangeType = DictionaryConstants.ChangeType.FUND_CHANGE,
                ChangeCategory = DictionaryConstants.ChangeCategory.FUND_CHANGE,
                ChangeReason = "户主死亡进入渐退期，原保障金超户口类型上限封顶减发",
                ChangeReasonType = ChangeReasonTypeConstants.HeadDeceased,
                ChangeDate = DateTime.Today,
                OperatorName = "System"
            };
            var beforeJson = global::System.Text.Json.JsonSerializer.Serialize(new
            {
                Classification = oldClassification,
                GuaranteeAmount = oldGuaranteeAmount
            });
            var afterJson = global::System.Text.Json.JsonSerializer.Serialize(new
            {
                Classification = newClassification,
                GuaranteeAmount = newGuaranteeAmount,
                GraceCapped = true
            });

            var createResult = await CreateChangeAsync(changeContext, beforeJson, afterJson, ct);
            if (createResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<long>(createResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    createResult.Message ?? "创建渐退减发变更记录失败");
            }
            var changeId = createResult.Value;

            var updateSql = @"UPDATE nc_biz_change_records SET
                old_classification = $1, new_classification = $2,
                old_guarantee_amount = $3, new_guarantee_amount = $4,
                triggered_grace_period = TRUE,
                changed_at = NOW()
                WHERE id = $5";
            var updateResult = await _db.ExecuteNonQueryAsync(updateSql, ct,
                oldClassification, newClassification,
                oldGuaranteeAmount, newGuaranteeAmount,
                changeId);
            if (updateResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<long>(updateResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    updateResult.Message ?? "回填渐退减发金额字段失败");
            }

            await tx.CommitAsync(ct);

            LogInfo($"渐退超限减发FundChange: ChangeId={changeId}, {oldGuaranteeAmount}→{newGuaranteeAmount}");
            return Result.Success(changeId);
        }
        catch (Exception ex)
        {
            // await using 作用域在 try 内：异常展开时 Dispose 已自动回滚，无需显式回滚
            LogException(ex, "创建渐退减发FundChange");
            return Result.FromException<long>(ex);
        }
    }

    /// <summary>接续链跨类判定读取行（EnsureChainCategoryAddAsync 用：链型/分类/保障金）</summary>
    private sealed class ChainClassificationRow
    {
        public long OriginalApplicationId { get; set; }
        public string? ClassificationResult { get; set; }
        public string? ChainType { get; set; }
        public decimal TotalGuaranteeAmount { get; set; }
    }

    /// <summary>已存在的 CategoryAdd 行（EnsureChainCategoryAddAsync 幂等判定用）</summary>
    private sealed class CategoryAddExistingRow
    {
        public long Id { get; set; }
        public string? NewClassification { get; set; }
        public decimal OldGuaranteeAmount { get; set; }
        public decimal NewGuaranteeAmount { get; set; }
    }

    /// <inheritdoc/>
    public async Task<Result<long?>> EnsureChainCategoryAddAsync(long applicationId, string operatorName, CancellationToken ct = default)
    {
        try
        {
            var appSql = @"SELECT original_application_id, classification_result, chain_type, total_guarantee_amount
                           FROM nc_biz_applications
                           WHERE id = $1 AND deleted_at IS NULL";
            var appResult = await _db.QuerySingleAsync<ChainClassificationRow>(appSql, ct, applicationId);
            if (appResult.IsFailure)
                return Result.Failure<long?>(appResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    appResult.Message ?? "读取接续链档案失败");

            var app = appResult.Value;
            // 非链档案（无上游）或分类未判定 → 不涉及跨类新增，显式跳过
            if (app == null || app.OriginalApplicationId <= 0 || string.IsNullOrEmpty(app.ClassificationResult))
                return Result.Success<long?>(null);

            var oldSql = @"SELECT classification_result, total_guarantee_amount
                           FROM nc_biz_applications
                           WHERE id = $1 AND deleted_at IS NULL";
            var oldResult = await _db.QuerySingleAsync<ChainClassificationRow>(oldSql, ct, app.OriginalApplicationId);
            if (oldResult.IsFailure)
                return Result.Failure<long?>(oldResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    oldResult.Message ?? "读取上游档案失败");
            var old = oldResult.Value;
            // 上游档案缺失/未分类 → 无法判断跨类，显式跳过（不猜测、不写入）
            if (old == null || string.IsNullOrEmpty(old.ClassificationResult))
                return Result.Success<long?>(null);

            var newClassification = app.ClassificationResult!;
            var oldClassification = old.ClassificationResult!;

            if (!IsCrossCategoryChange(oldClassification, newClassification))
            {
                // 判定回同大类（草稿期重新判定/上游调整）：软删可能误存的 CategoryAdd，避免月报误记转入
                var purge = await _db.ExecuteNonQueryAsync(
                    @"UPDATE nc_biz_change_records SET deleted_at = NOW()
                      WHERE application_id = $1 AND change_type = $2 AND deleted_at IS NULL",
                    ct, applicationId, DictionaryConstants.ChangeType.CATEGORY_ADD);
                if (purge.IsFailure)
                    return Result.Failure<long?>(purge.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        purge.Message ?? "清理跨类新增记录失败");
                if (purge.Value > 0)
                    LogInfo($"接续链判定回同大类，软删 CategoryAdd: ApplicationId={applicationId}, {oldClassification}→{newClassification}");
                return Result.Success<long?>(null);
            }

            var oldMajorName = ClassificationConstants.ConvertToMajorCategoryName(oldClassification);
            var newMajorName = ClassificationConstants.ConvertToMajorCategoryName(newClassification);
            var changeReason = app.ChainType switch
            {
                ChainTypeConstants.HOUSEHOLD_DEATH => $"户主死亡后 由{oldMajorName}转入{newMajorName}",
                ChainTypeConstants.MEMBER_CHANGE => $"成员变更后 由{oldMajorName}转入{newMajorName}",
                _ => $"由{oldMajorName}转入{newMajorName}"
            };
            var reasonType = app.ChainType switch
            {
                ChainTypeConstants.HOUSEHOLD_DEATH => ChangeReasonTypeConstants.HeadDeceased,
                ChainTypeConstants.MEMBER_CHANGE => ChangeReasonTypeConstants.MemberChange,
                _ => ChangeReasonTypeConstants.CrossCategoryTransfer
            };

            // 幂等：已存在则比对分类，一致跳过，不一致就地更新（月报跨类行按 change_date/id 取最新，同 ID 更新不会重复）
            var existSql = @"SELECT id, new_classification, old_guarantee_amount, new_guarantee_amount
                             FROM nc_biz_change_records
                             WHERE application_id = $1 AND change_type = $2 AND deleted_at IS NULL
                             ORDER BY id DESC LIMIT 1";
            var exist = await _db.QuerySingleAsync<CategoryAddExistingRow>(existSql, ct,
                applicationId, DictionaryConstants.ChangeType.CATEGORY_ADD);
            if (exist.IsFailure)
                return Result.Failure<long?>(exist.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    exist.Message ?? "查询跨类新增记录失败");

            if (exist.Value != null)
            {
                if (string.Equals(exist.Value.NewClassification, newClassification, StringComparison.Ordinal))
                {
                    // 分类一致但金额漂移（后续重判/数据修复改了保障金）→ 同步金额，
                    // 否则月报「新增救助明细」与变更记录列表长期携带旧金额（分类一致即跳过的盲区）
                    if (exist.Value.OldGuaranteeAmount == old.TotalGuaranteeAmount
                        && exist.Value.NewGuaranteeAmount == app.TotalGuaranteeAmount)
                        return Result.Success<long?>(exist.Value.Id);

                    var refresh = await _db.ExecuteNonQueryAsync(
                        @"UPDATE nc_biz_change_records SET
                            old_guarantee_amount = $1, new_guarantee_amount = $2, changed_at = NOW()
                          WHERE id = $3",
                        ct, old.TotalGuaranteeAmount, app.TotalGuaranteeAmount, exist.Value.Id);
                    if (refresh.IsFailure)
                        return Result.Failure<long?>(refresh.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                            refresh.Message ?? "刷新跨类新增金额失败");
                    LogInfo($"接续链 CategoryAdd 金额刷新: ApplicationId={applicationId}, "
                        + $"{old.TotalGuaranteeAmount}→{app.TotalGuaranteeAmount}, ChangeId={exist.Value.Id}");
                    return Result.Success<long?>(exist.Value.Id);
                }

                var updateSql = @"UPDATE nc_biz_change_records SET
                    new_classification = $1, new_guarantee_amount = $2,
                    old_classification = $3, old_guarantee_amount = $4,
                    change_reason = $5, change_reason_type = $6,
                    change_date = $7, operator_name = $8, changed_at = NOW()
                    WHERE id = $9";
                var update = await _db.ExecuteNonQueryAsync(updateSql, ct,
                    newClassification, app.TotalGuaranteeAmount,
                    oldClassification, old.TotalGuaranteeAmount,
                    changeReason, reasonType,
                    DateTime.Today, operatorName,
                    exist.Value.Id);
                if (update.IsFailure)
                    return Result.Failure<long?>(update.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        update.Message ?? "更新跨类新增记录失败");
                LogInfo($"接续链 CategoryAdd 更新: ApplicationId={applicationId}, {oldClassification}→{newClassification}, ChangeId={exist.Value.Id}");
                return Result.Success<long?>(exist.Value.Id);
            }

            await using var tx = await _db.BeginTransactionScopeAsync(ct);

            var insertSql = @"INSERT INTO nc_biz_change_records
                (application_id, change_no, change_type, change_reason, change_reason_type, change_date,
                 old_classification, new_classification, old_guarantee_amount, new_guarantee_amount,
                 triggered_grace_period, triggered_stop, operator_name, changed_at)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10, FALSE, FALSE, $11, NOW())
                RETURNING id;";
            var insert = await _db.ExecuteScalarAsync<long>(insertSql, ct,
                applicationId,
                $"CHG{DateTime.Now:yyyyMMddHHmmssfff}",
                DictionaryConstants.ChangeType.CATEGORY_ADD,
                changeReason, reasonType, DateTime.Today,
                oldClassification, newClassification,
                old.TotalGuaranteeAmount, app.TotalGuaranteeAmount,
                operatorName);
            if (insert.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<long?>(insert.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    insert.Message ?? "写入跨类新增记录失败");
            }

            await tx.CommitAsync(ct);

            LogInfo($"接续链 CategoryAdd 写入: ApplicationId={applicationId}, {oldClassification}→{newClassification}, ChangeId={insert.Value}");
            return Result.Success<long?>(insert.Value);
        }
        catch (Exception ex)
        {
            // await using 作用域在 try 内：异常展开时 Dispose 已自动回滚，无需显式回滚
            LogException(ex, "同步接续链跨类新增CategoryAdd");
            return Result.FromException<long?>(ex);
        }
    }

    /// <summary>接续链/旧档读取行（EnsureChainClassifiedSubsidyReduceAsync 用）</summary>
    private sealed class ClassifiedReduceAppRow
    {
        public long OriginalApplicationId { get; set; }
        public string? ChainType { get; set; }
        public string? ClassificationResult { get; set; }
        public decimal ClassifiedSubsidyAmount { get; set; }
        public string? ApplicantName { get; set; }
    }

    /// <summary>已存在的分类施保减发行（EnsureChainClassifiedSubsidyReduceAsync 幂等判定用）</summary>
    private sealed class ClassifiedReduceExistingRow
    {
        public long Id { get; set; }
        public decimal OldGuaranteeAmount { get; set; }
        public decimal NewGuaranteeAmount { get; set; }
    }

    /// <inheritdoc/>
    public async Task<Result<long?>> EnsureChainClassifiedSubsidyReduceAsync(long applicationId, string operatorName, CancellationToken ct = default)
    {
        try
        {
            var appSql = @"SELECT original_application_id, chain_type, classification_result,
                                  classified_subsidy_amount, applicant_name
                           FROM nc_biz_applications
                           WHERE id = $1 AND deleted_at IS NULL";
            var appResult = await _db.QuerySingleAsync<ClassifiedReduceAppRow>(appSql, ct, applicationId);
            if (appResult.IsFailure)
                return Result.Failure<long?>(appResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    appResult.Message ?? "读取接续链档案失败");

            var app = appResult.Value;
            // 仅户主死亡链 + 已判定分类参与；其余链路/未判定显式跳过（不猜测、不写入）
            if (app == null
                || app.OriginalApplicationId <= 0
                || !string.Equals(app.ChainType, ChainTypeConstants.HOUSEHOLD_DEATH, StringComparison.Ordinal)
                || string.IsNullOrEmpty(app.ClassificationResult))
                return Result.Success<long?>(null);

            // 旧档（减发行挂载目标）：原分类施保与原户主
            var oldSql = @"SELECT original_application_id, chain_type, classification_result,
                                  classified_subsidy_amount, applicant_name
                           FROM nc_biz_applications
                           WHERE id = $1 AND deleted_at IS NULL";
            var oldResult = await _db.QuerySingleAsync<ClassifiedReduceAppRow>(oldSql, ct, app.OriginalApplicationId);
            if (oldResult.IsFailure)
                return Result.Failure<long?>(oldResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    oldResult.Message ?? "读取上游档案失败");
            var old = oldResult.Value;
            if (old == null)
                return Result.Success<long?>(null);

            // 幂等：同户同类已有减发行（取最新）
            var existSql = @"SELECT id, old_guarantee_amount, new_guarantee_amount
                             FROM nc_biz_change_records
                             WHERE application_id = $1 AND change_type = $2 AND deleted_at IS NULL
                             ORDER BY id DESC LIMIT 1";
            var exist = await _db.QuerySingleAsync<ClassifiedReduceExistingRow>(existSql, ct,
                app.OriginalApplicationId, DictionaryConstants.ChangeType.CLASSIFIED_SUBSIDY_REDUCE);
            if (exist.IsFailure)
                return Result.Failure<long?>(exist.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    exist.Message ?? "查询分类施保减发记录失败");

            var oldAmount = old.ClassifiedSubsidyAmount;
            var newAmount = app.ClassifiedSubsidyAmount;

            // 条件消失：原额 ≤ 现额（重判恢复原额等）→ 软删已有减发记录，防止月报/变更历史残留
            if (oldAmount <= newAmount)
            {
                if (exist.Value != null)
                {
                    var purge = await _db.ExecuteNonQueryAsync(
                        @"UPDATE nc_biz_change_records SET deleted_at = NOW()
                          WHERE id = $1 AND deleted_at IS NULL",
                        ct, exist.Value.Id);
                    if (purge.IsFailure)
                        return Result.Failure<long?>(purge.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                            purge.Message ?? "清理分类施保减发记录失败");
                    if (purge.Value > 0)
                        LogInfo($"分类施保减发条件消失，软删记录: ChangeId={exist.Value.Id}, "
                            + $"{oldAmount:F2}≤{newAmount:F2}");
                }
                return Result.Success<long?>(null);
            }

            // 减发成立但未进渐退（取消渐退等）→ 不创建：本记录语义限定"户主死亡进入渐退期"；
            // 已有记录保留作事件审计（渐退期满结清后不回收）
            var graceResult = await _db.ExecuteScalarAsync<bool>(
                "SELECT EXISTS(SELECT 1 FROM nc_biz_grace_periods WHERE application_id = $1 AND is_active AND deleted_at IS NULL)",
                ct, applicationId);
            if (graceResult.IsFailure)
                return Result.Failure<long?>(graceResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    graceResult.Message ?? "渐退期状态查询失败");
            if (!graceResult.Value)
                return Result.Success<long?>(exist.Value?.Id);

            var changeReason = $"户主死亡进入渐退期，减发分类施保：原{oldAmount:F2}元→现{newAmount:F2}元"
                + $"（原户主{old.ApplicantName ?? ""}死亡，减发{oldAmount - newAmount:F2}元/月）";

            if (exist.Value != null)
            {
                if (exist.Value.OldGuaranteeAmount == oldAmount
                    && exist.Value.NewGuaranteeAmount == newAmount)
                    return Result.Success<long?>(exist.Value.Id);

                var update = await _db.ExecuteNonQueryAsync(
                    @"UPDATE nc_biz_change_records SET
                        old_guarantee_amount = $1, new_guarantee_amount = $2,
                        change_reason = $3, operator_name = $4, changed_at = NOW()
                      WHERE id = $5",
                    ct, oldAmount, newAmount, changeReason, operatorName, exist.Value.Id);
                if (update.IsFailure)
                    return Result.Failure<long?>(update.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        update.Message ?? "更新分类施保减发记录失败");
                LogInfo($"分类施保减发记录更新: ChangeId={exist.Value.Id}, {oldAmount:F2}→{newAmount:F2}");
                return Result.Success<long?>(exist.Value.Id);
            }

            await using var tx = await _db.BeginTransactionScopeAsync(ct);

            var insertSql = @"INSERT INTO nc_biz_change_records
                (application_id, change_no, change_type, change_category, change_reason, change_reason_type,
                 change_date, old_classification, new_classification, old_guarantee_amount, new_guarantee_amount,
                 triggered_grace_period, triggered_stop, original_application_id, new_application_id,
                 operator_name, changed_at)
                VALUES ($1,$2,$3,NULL,$4,$5,$6,$7,$8,$9,$10,FALSE,FALSE,$11,$12,$13,NOW())
                RETURNING id;";
            var insert = await _db.ExecuteScalarAsync<long>(insertSql, ct,
                app.OriginalApplicationId,
                $"CHG{DateTime.Now:yyyyMMddHHmmssfff}",
                DictionaryConstants.ChangeType.CLASSIFIED_SUBSIDY_REDUCE,
                changeReason,
                ChangeReasonTypeConstants.ClassifiedSubsidyReduce,
                DateTime.Today,
                old.ClassificationResult, app.ClassificationResult,
                oldAmount, newAmount,
                app.OriginalApplicationId, applicationId,
                operatorName);
            if (insert.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<long?>(insert.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    insert.Message ?? "写入分类施保减发记录失败");
            }

            await tx.CommitAsync(ct);

            LogInfo($"分类施保减发记录写入: ChangeId={insert.Value}, 挂旧档{app.OriginalApplicationId}, "
                + $"{oldAmount:F2}→{newAmount:F2}");
            return Result.Success<long?>(insert.Value);
        }
        catch (Exception ex)
        {
            // await using 作用域在 try 内：异常展开时 Dispose 已自动回滚，无需显式回滚
            LogException(ex, "同步户主死亡渐退分类施保减发记录");
            return Result.FromException<long?>(ex);
        }
    }

    public async Task<Result> SaveDetailAsync(long changeId, string fieldName, string oldValue, string newValue, CancellationToken ct = default)
    {
        var sql = @"INSERT INTO nc_biz_change_details (change_id, field_name, old_value, new_value) VALUES ($1,$2,$3,$4);";
        var result = await _db.ExecuteNonQueryAsync(sql, ct, changeId, fieldName, oldValue, newValue);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? $"保存变更明细失败: ChangeId={changeId}, Field={fieldName}");
        return Result.Success();
    }

    /// <summary>
    /// 渐退期状态重建（复核/变更后按新分类决定去留）：
    /// - 新分类仍属低收入（IsCodeLowIncome）→ 保留：已激活维持现状（含"渐退期中间政策变动仍符合"的情形），
    ///   判定触发新渐退期则 ActivateAsync 覆盖续接（UPSERT）；
    /// - 新分类非低收入（回低保/特困/刚性/停保等）→ 结清既有渐退期。
    /// 必须在调用方已开启的环境事务内执行。
    /// </summary>
    private async Task<Result> ReconcileGracePeriodAsync(
        long applicationId, string newClassification, GracePeriodCheckResult graceCheck,
        string? oldClassification, decimal? oldGuaranteeAmount, bool forceClear = false,
        CancellationToken ct = default)
    {
        var stillLowIncome = !forceClear && ClassificationConstants.IsCodeLowIncome(newClassification);

        if (!stillLowIncome)
        {
            var clearRes = await _gracePeriodService.ClearAsync(applicationId, ct);
            if (clearRes.IsFailure)
                return Result.Failure(clearRes.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    clearRes.Message ?? "结清渐退期失败");
            return Result.Success();
        }

        if (graceCheck.IsEligible)
        {
            var activateRes = await _gracePeriodService.ActivateAsync(
                applicationId, graceCheck.Months,
                graceCheck.StartDate, graceCheck.EndDate,
                oldClassification, oldGuaranteeAmount,
                graceGrantAmount: null, ct: ct);
            if (activateRes.IsFailure)
                return Result.Failure(activateRes.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    activateRes.Message ?? "激活渐退期失败");
        }

        return Result.Success();
    }

    /// <summary>
    /// 照料护理费解析（H2）：非特困一律 0；特困集中供养 0；特困分散按能力鉴定重算。
    /// 禁止沿用旧档 DB 值——分类离开特困后旧照料费必须清零。
    /// </summary>
    private async Task<decimal> ResolveCaregiverSubsidyAsync(
        string classification, string supportMode, long applicationId, CancellationToken ct)
    {
        if (!ClassificationConstants.IsCodeDestitute(classification))
            return 0m;

        var isCentralized =
            classification is ClassificationConstants.RuralDestituteCentralized
                or ClassificationConstants.UrbanDestituteCentralized
            || string.Equals(supportMode, ClassificationConstants.SupportMode.CENTRALIZED,
                StringComparison.OrdinalIgnoreCase);

        return isCentralized
            ? 0m
            : await _classificationService.CalculateCareAllowanceAsync(applicationId, ct);
    }

    /// <summary>
    /// 补写"停保"伴随变更记录（CategoryStop + triggered_stop=true）：
    /// 档案输出的《档案_变更告知书》按该记录判定（application_id 或 new_application_id 匹配），
    /// 收入超标停保（经济复核/家庭成员变更）必须落这条记录。
    /// 必须在调用方已开启的环境事务内执行。
    /// </summary>
    private async Task<Result> WriteCategoryStopRecordAsync(
        long applicationId, string? oldClassification, string? newClassification,
        decimal oldGuaranteeAmount, decimal newGuaranteeAmount,
        bool triggeredGracePeriod, string reason, string reasonType,
        string operatorName, long newApplicationId, CancellationToken ct)
    {
        var sql = @"INSERT INTO nc_biz_change_records
            (application_id, change_no, change_type, change_reason, change_reason_type, change_date,
             old_classification, new_classification, old_guarantee_amount, new_guarantee_amount,
             triggered_grace_period, triggered_stop, operator_name, new_application_id, changed_at)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$14,NOW());";
        var result = await _db.ExecuteNonQueryAsync(sql, ct,
            applicationId,
            $"CHG{DateTime.Now:yyyyMMddHHmmssfff}",
            DictionaryConstants.ChangeType.CATEGORY_STOP,
            reason, reasonType, DateTime.Today,
            (object?)oldClassification, (object?)newClassification,
            (object?)oldGuaranteeAmount, (object?)newGuaranteeAmount,
            triggeredGracePeriod, true,
            operatorName,
            newApplicationId);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "写入停保变更记录失败");
        return Result.Success();
    }

    /// <summary>
    /// 停旧建新：复制旧档案（主表+家庭成员+经济明细+入户调查+照料人）→ 新档案（original_application_id=旧ID、Draft），
    /// 旧档案置 Stopped 并结清渐退期；支持对复制后的新档案 UPDATE 覆盖字段（分类/金额/收入等，如经济复核结果）。
    /// 返回新档案 ID 与旧成员ID→新成员ID 映射。同一事务内调用（应处于已开启事务的上下文）。
    /// </summary>
    private async Task<Result<(long NewApplicationId, Dictionary<long, long> MemberIdMap)>> StopAndRebuildAsync(
        long sourceApplicationId, string sourceType, string chainType, string changeReason, string? operatorName,
        Action<ApplicationEntity>? applyOverrides = null, CancellationToken ct = default)
    {
        // 1. 生成新申请编号
        var appNoResult = await _applicationService.GetNextApplicationNoAsync(ct);
        if (appNoResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(appNoResult.ErrorCode!, appNoResult.Message ?? "生成申请编号失败");

        // 2. 读取源档案与成员（供附属复制 + 成员ID映射）
        var srcResult = await _db.QuerySingleAsync<ApplicationEntity>(
            $"SELECT {ApplicationColumns.Full} FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL", ct, sourceApplicationId);
        if (srcResult.IsFailure || srcResult.Value == null)
            return Result.Failure<(long, Dictionary<long, long>)>(ErrorCodes.APPLICATION_NOT_FOUND, "源档案不存在");

        // 3. 复制主表 → 新档案（application_no=新、status=Draft、original_application_id=旧ID、source_type=给定类型、
        //    清空停保/渐退期字段、data_completed_at=NULL，其余复制源值）
        var newAppInsertSql = @"INSERT INTO nc_biz_applications (
                application_no, applicant_name, applicant_id_card, applicant_phone,
                gender, ethnicity, marital_status, hukou_type, education_level, political_status,
                hukou_address, disability_card_no,
                province, city, district, town, community, address,
                physical_condition, disease_name, secondary_disease_name,
                disability_type, disability_level, health_status,
                employment_status, work_unit, income_source, bank_name, bank_account,
                family_size, confirmed_family_size,
                is_single_rescue, support_mode,
                application_reason, application_reason_detail, caregiver_type, destitute_support_type, support_institution_id,
                work_income_total, business_income_total, property_income_total,
                transfer_income_total, other_income_total, total_family_income, per_capita_income, rigid_expenditure, alimony_income,
                is_eligible, classification_result,
                classified_subsidy_type, classified_subsidy_amount,
                household_monthly_guarantee_amount, person_category_protection_total_amount,
                caregiver_subsidy_amount, total_guarantee_amount,
                status, current_step,
                source_type, source_table, source_id,
                chain_type,
                original_application_id,
                first_approved_at,
                created_at, updated_at, created_by, updated_by,
                city_id, county_id, town_id, village_id,
                hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id,
                family_land_area, self_farmed_land_area, subleased_land_area, contracted_land_area,
                land_income_total, subsidy_total, total_confirmed_land_area, confirmed_person_count
            )
            SELECT $1, applicant_name, applicant_id_card, applicant_phone,
                gender, ethnicity, marital_status, hukou_type, education_level, political_status,
                hukou_address, disability_card_no,
                province, city, district, town, community, address,
                physical_condition, disease_name, secondary_disease_name,
                disability_type, disability_level, health_status,
                employment_status, work_unit, income_source, bank_name, bank_account,
                family_size, confirmed_family_size,
                is_single_rescue, support_mode,
                application_reason, application_reason_detail, caregiver_type, destitute_support_type, support_institution_id,
                work_income_total, business_income_total, property_income_total,
                transfer_income_total, other_income_total, total_family_income, per_capita_income, rigid_expenditure, alimony_income,
                is_eligible, classification_result,
                classified_subsidy_type, classified_subsidy_amount,
                household_monthly_guarantee_amount, person_category_protection_total_amount,
                caregiver_subsidy_amount, total_guarantee_amount,
                $7, $8,
                $2, source_table, source_id,
                $6,
                $3,
                first_approved_at,
                NOW(), NOW(), created_by, $4,
                city_id, county_id, town_id, village_id,
                hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id,
                family_land_area, self_farmed_land_area, subleased_land_area, contracted_land_area,
                land_income_total, subsidy_total, total_confirmed_land_area, confirmed_person_count
            FROM nc_biz_applications WHERE id = $5
            RETURNING id;";
        var newAppResult = await _db.ExecuteScalarAsync<long?>(newAppInsertSql, ct,
            appNoResult.Value, sourceType, sourceApplicationId, operatorName ?? "System", sourceApplicationId, chainType,
            ApplicationStatusCodes.DRAFT, WorkflowSteps.ENTRY_START);
        if (newAppResult.IsFailure || newAppResult.Value is not > 0)
            return Result.Failure<(long, Dictionary<long, long>)>(newAppResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                newAppResult.Message ?? "创建新档案失败");
        var newApplicationId = newAppResult.Value.Value;

        // 4. 复制家庭成员（member_id 映射）
        var oldMembersResult = await _db.QueryAsync<FamilyMember>(
            "SELECT * FROM nc_biz_family_members WHERE application_id = $1 AND deleted_at IS NULL", ct, sourceApplicationId);
        if (oldMembersResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(oldMembersResult.ErrorCode!, oldMembersResult.Message ?? "源成员查询失败");
        var oldMembers = oldMembersResult.Value ?? new List<FamilyMember>();

        var memberCopySql = @"INSERT INTO nc_biz_family_members (
                application_id, name, id_card, is_applicant, gender, birth_date, age, ethnicity, phone,
                hukou_type, hukou_address, marital_status, education_level, political_status,
                relationship_to_head, health_status, work_capacity, is_disabled,
                disability_type, disability_level, is_severe_disability, employment_status, annual_income,
                member_category, disability_certificate_no, disease_category, disease_name,
                home_province, home_city, home_district, home_town, home_address,
                hukou_province, hukou_city, hukou_district, hukou_town, home_village, secondary_disease,
                person_type, annual_support_fee, is_support_ability, monthly_income_capacity,
                family_size, work_unit, monthly_support_fee,
                support_months, main_income_source, is_severe_disease, is_labor_exempt, created_at, updated_at, deleted_at
            )
            SELECT $1, name, id_card, is_applicant, gender, birth_date, age, ethnicity, phone,
                hukou_type, hukou_address, marital_status, education_level, political_status,
                relationship_to_head, health_status, work_capacity, is_disabled,
                disability_type, disability_level, is_severe_disability, employment_status, annual_income,
                member_category, disability_certificate_no, disease_category, disease_name,
                home_province, home_city, home_district, home_town, home_address,
                hukou_province, hukou_city, hukou_district, hukou_town, home_village, secondary_disease,
                person_type, annual_support_fee, is_support_ability, monthly_income_capacity,
                family_size, work_unit, monthly_support_fee,
                support_months, main_income_source, is_severe_disease, is_labor_exempt, NOW(), NOW(), NULL
            FROM nc_biz_family_members WHERE application_id = $2 AND deleted_at IS NULL;";
        var memberCopyResult = await _db.ExecuteNonQueryAsync(memberCopySql, ct, newApplicationId, sourceApplicationId);
        if (memberCopyResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(memberCopyResult.ErrorCode!, memberCopyResult.Message ?? "复制家庭成员失败");

        // 成员ID映射（按身份证/姓名匹配）
        var newMembersResult = await _db.QueryAsync<FamilyMember>(
            "SELECT * FROM nc_biz_family_members WHERE application_id = $1 AND deleted_at IS NULL", ct, newApplicationId);
        if (newMembersResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(newMembersResult.ErrorCode!, newMembersResult.Message ?? "新档案成员查询失败");
        var newMembers = newMembersResult.Value ?? new List<FamilyMember>();
        var memberIdMap = new Dictionary<long, long>();
        foreach (var oldMember in oldMembers)
        {
            var match = newMembers.FirstOrDefault(m => !string.IsNullOrEmpty(m.IdCard)
                && string.Equals(m.IdCard, oldMember.IdCard, StringComparison.OrdinalIgnoreCase))
                ?? newMembers.FirstOrDefault(m => string.Equals(m.Name, oldMember.Name, StringComparison.Ordinal));
            if (match != null)
                memberIdMap[oldMember.Id] = match.Id;
        }
        long MapMemberId(long oldId) => oldId > 0 && memberIdMap.TryGetValue(oldId, out var nid) ? nid : 0;

        // 5. 复制经济明细（LoadAll → 重映射 member_id → SaveAll）
        var econLoadResult = await _economicDetailService.LoadAllAsync(sourceApplicationId, ct);
        if (econLoadResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(econLoadResult.ErrorCode!, econLoadResult.Message ?? "经济明细加载失败");
        var econ = econLoadResult.Value;
        foreach (var it in econ.LaborIncomes) it.MemberId = MapMemberId(it.MemberId);
        foreach (var it in econ.BusinessIncomes) it.MemberId = MapMemberId(it.MemberId);
        foreach (var it in econ.PropertyIncomes) it.MemberId = MapMemberId(it.MemberId);
        foreach (var it in econ.TransferIncomes) it.MemberId = MapMemberId(it.MemberId);
        foreach (var it in econ.RigidExpenditures) it.MemberId = MapMemberId(it.MemberId);
        foreach (var it in econ.FamilyProperties) it.MemberId = MapMemberId(it.MemberId);
        foreach (var it in econ.Vehicles) it.MemberId = MapMemberId(it.MemberId);
        foreach (var it in econ.Machineries) it.MemberId = MapMemberId(it.MemberId);
        var econSaveResult = await _economicDetailService.SaveAllAsync(newApplicationId,
            econ.LaborIncomes, econ.BusinessIncomes, econ.PropertyIncomes, econ.TransferIncomes,
            econ.OtherIncomes, econ.Subsidies, econ.BreedingIncomes, econ.RigidExpenditures,
            econ.FamilyProperties, econ.Vehicles, econ.Machineries, econ.FinancialAssets, econ.LandRegistrations,
            econ.LandConfirmationGroups, ct);
        if (econSaveResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(econSaveResult.ErrorCode!, econSaveResult.Message ?? "经济明细复制失败");

        // 6. 复制入户调查
        var surveyCopySql = @"INSERT INTO nc_biz_household_surveys
            (application_id, survey_date, surveyor_name, surveyor_organization,
             respondent_name, respondent_relation, survey_notes,
             created_at, updated_at, deleted_at,
             application_reason, application_reason_detail, survey_conclusion)
            SELECT $1, survey_date, surveyor_name, surveyor_organization,
                   respondent_name, respondent_relation, survey_notes,
                   NOW(), NOW(), NULL,
                   application_reason, application_reason_detail, survey_conclusion
            FROM nc_biz_household_surveys
            WHERE application_id = $2 AND deleted_at IS NULL;";
        var surveyCopyResult = await _db.ExecuteNonQueryAsync(surveyCopySql, ct, newApplicationId, sourceApplicationId);
        if (surveyCopyResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(surveyCopyResult.ErrorCode!, surveyCopyResult.Message ?? "复制入户调查失败");

        // 7. 复制照料人（cared_member_id 迁移）
        var caregiversResult = await _db.QueryAsync<Caregiver>(
            "SELECT * FROM nc_biz_caregivers WHERE application_id = $1 AND deleted_at IS NULL", ct, sourceApplicationId);
        if (caregiversResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(caregiversResult.ErrorCode!, caregiversResult.Message ?? "照料人查询失败");
        var caregivers = caregiversResult.Value ?? new List<Caregiver>();
        if (caregivers.Count > 0)
        {
            // 分批多行 VALUES 单语句（19 参数/行 + 行内 NOW()/NULL 字面量；500 行/批防参数上限）
            const int caregiverParamsPerRow = 19;
            const int caregiverBatchSize = 500;
            var caregiverInsertSql = @"INSERT INTO nc_biz_caregivers
                (application_id, cared_member_id, name, id_card, phone, relationship,
                 gender, age, ethnicity, health_status, employment_status, main_income_source,
                 work_unit, position, address,
                 marital_status, hukou_type, education_level, political_status,
                 created_at, deleted_at)
                VALUES ";
            for (var offset = 0; offset < caregivers.Count; offset += caregiverBatchSize)
            {
                var chunk = caregivers.GetRange(offset, Math.Min(caregiverBatchSize, caregivers.Count - offset));
                var (valuesClause, args) = NewCosmos.Helpers.MultiRowValuesBuilder.Build(
                    chunk.Count, caregiverParamsPerRow,
                    r =>
                    {
                        var cg = chunk[r];
                        return new object?[]
                        {
                            newApplicationId, MapMemberId(cg.CaredMemberId),
                            cg.Name, cg.IdCard, cg.Phone, cg.Relationship,
                            cg.Gender, cg.Age, cg.Ethnicity, cg.HealthStatus,
                            cg.EmploymentStatus, cg.MainIncomeSource,
                            cg.WorkUnit, cg.Position, cg.Address,
                            cg.MaritalStatus, cg.HukouType, cg.EducationLevel, cg.PoliticalStatus
                        };
                    },
                    o => "(" + string.Join(",", Enumerable.Range(o, caregiverParamsPerRow).Select(n => "$" + n)) + ",NOW(),NULL)");
                var insertCaregiverResult = await _db.ExecuteNonQueryAsync(caregiverInsertSql + valuesClause + ";", ct, args);
                if (insertCaregiverResult.IsFailure)
                    return Result.Failure<(long, Dictionary<long, long>)>(insertCaregiverResult.ErrorCode!, insertCaregiverResult.Message ?? "复制照料人失败");
            }
        }

        // 8. 应用覆盖字段（经济复核结果等）：UPDATE 新档案
        if (applyOverrides != null)
        {
            var newAppEntity = new ApplicationEntity { Id = newApplicationId };
            applyOverrides(newAppEntity);
            var updateSql = @"UPDATE nc_biz_applications SET
                total_family_income = $1, per_capita_income = $2, rigid_expenditure = $3,
                family_size = $4, classification_result = $5, is_eligible = $6,
                household_monthly_guarantee_amount = $7, total_guarantee_amount = $8,
                classified_subsidy_type = $9, classified_subsidy_amount = $10,
                total_annual_income = $11, per_capita_annual_income = $12,
                caregiver_subsidy_amount = $13,
                updated_at = NOW(), updated_by = $14
                WHERE id = $15;";
            var updResult = await _db.ExecuteNonQueryAsync(updateSql, ct,
                newAppEntity.TotalFamilyIncome, newAppEntity.PerCapitaIncome, newAppEntity.RigidExpenditure,
                newAppEntity.FamilySize, newAppEntity.ClassificationResult, newAppEntity.IsEligible,
                newAppEntity.HouseholdMonthlyGuaranteeAmount, newAppEntity.TotalGuaranteeAmount,
                newAppEntity.ClassifiedSubsidyType, newAppEntity.ClassifiedSubsidyAmount,
                newAppEntity.TotalAnnualIncome, newAppEntity.PerCapitaAnnualIncome,
                newAppEntity.CaregiverSubsidyAmount,
                operatorName ?? "System", newApplicationId);
            if (updResult.IsFailure)
                return Result.Failure<(long, Dictionary<long, long>)>(updResult.ErrorCode!, updResult.Message ?? "更新新档案失败");
        }

        // 9. 停止旧档案 + 结清渐退期
        // 状态写入统一走 ApplicationStatusService：状态机校验（Draft/Refused→Stopped 拒绝，
        // 避免直写产生"从未进入保障却已停保"的脏状态）+ stop_reason/stop_date 落库 + 审计留痕
        var stopResult = await _statusService.StopAsync(
            sourceApplicationId, changeReason, DateTime.Today, operatorName ?? "System",
            allowSubmittedOverride: true, ct: ct);
        if (stopResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(stopResult.ErrorCode!, stopResult.Message ?? "停止旧档案失败");
        var graceClearResult = await _gracePeriodService.ClearAsync(sourceApplicationId, ct);
        if (graceClearResult.IsFailure)
            return Result.Failure<(long, Dictionary<long, long>)>(graceClearResult.ErrorCode!, graceClearResult.Message ?? "结清旧档案渐退期失败");

        return Result.Success((newApplicationId, memberIdMap));
    }

    /// <summary>
    /// 执行经济复核
    /// </summary>
    public async Task<Result<ChangeResult>> ExecuteEconomicReviewAsync(EconomicReviewContext context, CancellationToken ct = default)
    {
        LogInfo("执行经济复核");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            // 1. 获取当前申请数据（已软删的申请不允许再做经济复核）
            var appSql = $"SELECT {ApplicationColumns.Full} FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL";
            var appResult = await _db.QuerySingleAsync<ApplicationEntity>(appSql, ct, context.ApplicationId);

            if (!appResult.IsSuccess || appResult.Value == null)
            {
                // 提前返回必须先回滚：环境事务的连接由作用域持有，
                // 不回滚会泄漏连接并在数据库端留下 idle in transaction 会话
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");
            }

            var application = appResult.Value;
            // 真旧值必须由调用方在 Step5 分类判定落库之前捕获（表单加载时刻）。
            // 这里读 application 的 ClassificationResult/TotalGuaranteeAmount 已被判定按钮覆写，
            // 直接用会让 needRebuild 的分类/金额比较恒判"无变化"，也会让
            // nc_biz_change_records.old_classification/old_guarantee_amount 记假旧值。
            var oldClassification = context.OldClassification ?? application.ClassificationResult ?? string.Empty;
            var oldGuaranteeAmount = context.OldGuaranteeAmount ?? application.TotalGuaranteeAmount;

            // 家庭信息修正模式：校验档案必须属于当前经济复核周期
            if (context.IsFamilyCorrection)
            {
                var currentTimeline = await _businessTimelineService.GetCurrentTimelineAsync(TimelineType.EconomicReview);
                var cycleStart = currentTimeline.CycleStartDate;
                var cycleEnd = currentTimeline.CycleEndDate;

                // 检查档案状态：必须是已批准状态
                if (application.Status != ApplicationStatusCodes.APPROVED)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<ChangeResult>(ErrorCodes.ECONOMIC_REVIEW_INVALID_STATUS, "只能修正已批准状态的档案");
                }

                // 检查档案的最近变更记录是否在当前周期内
                var changeHistory = await GetChangeHistoryAsync(context.ApplicationId, ct);
                if (changeHistory.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<ChangeResult>(changeHistory.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        changeHistory.Message ?? "变更历史查询失败");
                }
                if (changeHistory.Value != null && changeHistory.Value.Count > 0)
                {
                    var latestChange = changeHistory.Value.OrderByDescending(c => c.ChangedAt).First();
                    if (latestChange.ChangedAt < cycleStart || latestChange.ChangedAt > cycleEnd)
                    {
                        await tx.RollbackAsync(ct);
                        return Result.Failure<ChangeResult>(ErrorCodes.ECONOMIC_REVIEW_OUT_OF_PERIOD,
                            $"该档案最近一次变更不在当前复核周期内（{cycleStart:MM月dd日}-{cycleEnd:MM月dd日}）");
                    }
                }
            }

            // 变更链新建档案（户主死亡停旧建新等，original_application_id 非空）：
            // 注入上游档案原分类，供渐退期"低保→低收入"判定识别渐变前原分类；
            // 判定/落库使用上游分类，审计快照仍用本档案变更前分类
            var graceOldClassification = oldClassification;
            if (application.OriginalApplicationId > 0)
            {
                var upstreamResult = await _applicationService.GetByIdAsync(application.OriginalApplicationId, ct);
                if (upstreamResult.IsSuccess && upstreamResult.Value != null
                    && !string.IsNullOrEmpty(upstreamResult.Value.ClassificationResult))
                {
                    graceOldClassification = upstreamResult.Value.ClassificationResult!;
                    application.OriginalClassificationResult = graceOldClassification;
                }
            }

            // 变更前值必须在改写 application 之前落到局部变量，
            // 否则 Before 快照序列化的是改写后的对象，before==after，审计快照失去意义。
            // 经济复核入口（表单）在调本方法前已 SaveEconomicDetailsAsync 把新经济数据写回本档，
            // 读 application 拿到的实为覆写后值——优先取调用方覆写前捕获的 context.Old*（真旧值），
            // 同时修正 nc_biz_change_records.old_per_capita_income 列的覆写后失真。
            var oldTotalIncome = context.OldTotalFamilyIncome ?? application.TotalFamilyIncome;
            var oldPerCapitaIncome = context.OldPerCapitaIncome ?? application.PerCapitaIncome;
            var oldRigidExpenditure = context.OldRigidExpenditure ?? application.RigidExpenditure;
            var oldFamilySize = context.OldFamilySize ?? application.FamilySize;

            // 2. 获取家庭成员（查询失败不能按"空成员"继续——分类判定会漏掉
            //    残疾/疾病等成员型补贴，算出错误金额并写库）
            var membersSql = "SELECT * FROM nc_biz_family_members WHERE application_id = $1 AND deleted_at IS null";
            var membersResult = await _db.QueryAsync<FamilyMember>(membersSql, ct, context.ApplicationId);
            if (membersResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(membersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    membersResult.Message ?? "家庭成员查询失败");
            }
            var members = membersResult.Value ?? new List<FamilyMember>();

            // 3. 更新经济信息（月值 = 年值÷12 分解显示；年值为权威口径）
            application.TotalAnnualIncome = context.NewTotalAnnualIncome;
            application.PerCapitaAnnualIncome = context.NewPerCapitaAnnualIncome;
            application.TotalFamilyIncome = context.NewTotalFamilyIncome;
            application.PerCapitaIncome = context.NewPerCapitaIncome;
            application.RigidExpenditure = context.NewRigidExpenditure;
            application.FamilySize = context.NewFamilySize;

            // 4. 重新执行分类判定（需传真实赡养抚养扶养义务人，特困判定依赖"无义务人"条件）
            var supportersResult = await _supporterService.GetByApplicationIdAsync(context.ApplicationId, ct);
            if (supportersResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(supportersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    supportersResult.Message ?? "赡养抚养扶养人查询失败");
            }
            var supporters = supportersResult.Value ?? new List<Supporter>();
            var classificationResult = await _classificationService.DetermineClassificationAsync(
                application, members, supporters, new List<Caregiver>(), ct);

            if (!classificationResult.IsSuccess)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.CLASSIFICATION_FAILED, "分类判定失败");
            }

            var newClassification = classificationResult.Value.Classification;
            var newGuaranteeAmount = classificationResult.Value.GuaranteeAmount;
            var newClassifiedAmount = classificationResult.Value.ClassifiedSubsidy.TotalAmount;
            var newClassifiedType = classificationResult.Value.ClassifiedSubsidy.Types;
            var newCaregiverAmount = await ResolveCaregiverSubsidyAsync(
                newClassification, application.SupportMode, context.ApplicationId, ct);

            // 5. 停旧建新判定：保障金额 / 分类 / 停保 三项任一变化才生成新档案。
            //    金额必须用合计口径（户月 + 分类施保 + 照料费，同 applyOverrides 落库式）比较，
            //    不能用 ChangeResult.NewGuaranteeAmount（户月口径，会恒不相等）。
            var newTotalGuaranteeAmount = newGuaranteeAmount + newClassifiedAmount + newCaregiverAmount;
            var triggeredStop = ClassificationConstants.IsCodeStop(newClassification);
            var needRebuild = oldGuaranteeAmount != newTotalGuaranteeAmount
                           || newClassification != oldClassification
                           || triggeredStop;

            // 6. 渐退期状态重建（同事务）：按复核后新分类决定去留——
            //    新分类仍属低收入 → 保留/续接（含"渐退期中间政策变动仍符合"的情形）；
            //    非低收入（回低保/停保等）→ 结清既有渐退期。
            var reconcileResult = await ReconcileGracePeriodAsync(
                application.Id, newClassification, classificationResult.Value.GracePeriod,
                graceOldClassification, oldGuaranteeAmount, ct: ct);
            if (reconcileResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(reconcileResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    reconcileResult.Message ?? "渐退期状态重建失败");
            }
            var triggeredGracePeriod = classificationResult.Value.GracePeriod.IsEligible == true;
            if (triggeredGracePeriod)
            {
                _gracePeriodService.ApplyGracePeriod(application, new GracePeriodInfo
                {
                    IsInGracePeriod = true,
                    GracePeriodMonths = classificationResult.Value.GracePeriod.Months,
                    GracePeriodStartDate = classificationResult.Value.GracePeriod.StartDate,
                    GracePeriodEndDate = classificationResult.Value.GracePeriod.EndDate,
                    OriginalClassificationResult = graceOldClassification,
                    OriginalGuaranteeAmount = oldGuaranteeAmount
                });
            }

            // 7. 停旧建新：金额/分类/停保任一变化才复制为新档案（original_application_id=旧ID、Draft），
            //    旧档案置 Stopped。新档案应用复核后的收入/分类/金额/保障金；
            //    三项均未变（如 0 元保障户复核 0→0）→ 跳过，复核结果留在原档。
            long newApplicationId;
            if (needRebuild)
            {
                var rebuildResult = await StopAndRebuildAsync(
                    context.ApplicationId,
                    DictionaryConstants.ChangeType.FUND_CHANGE,
                    ChainTypeConstants.CATEGORY_REBUILD,
                    $"经济复核后 由 {ClassificationConstants.ConvertToMajorCategoryName(oldClassification ?? "")}转入{ClassificationConstants.ConvertToMajorCategoryName(newClassification ?? "")}",
                    context.OperatorName,
                    applyOverrides: target =>
                    {
                        target.TotalFamilyIncome = application.TotalFamilyIncome;
                        target.PerCapitaIncome = application.PerCapitaIncome;
                        target.TotalAnnualIncome = application.TotalAnnualIncome;
                        target.PerCapitaAnnualIncome = application.PerCapitaAnnualIncome;
                        target.RigidExpenditure = application.RigidExpenditure;
                        target.FamilySize = application.FamilySize;
                        target.ClassificationResult = newClassification;
                        target.IsEligible = classificationResult.Value.IsEligible;
                        target.HouseholdMonthlyGuaranteeAmount = newGuaranteeAmount;
                        target.TotalGuaranteeAmount = newTotalGuaranteeAmount;
                        target.ClassifiedSubsidyType = newClassifiedType;
                        target.ClassifiedSubsidyAmount = newClassifiedAmount;
                        target.CaregiverSubsidyAmount = newCaregiverAmount;
                    },
                    ct);
                if (rebuildResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<ChangeResult>(rebuildResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        rebuildResult.Message ?? "经济复核停旧建新失败");
                }
                newApplicationId = rebuildResult.Value.NewApplicationId;
            }
            else
            {
                // 金额/分类/停保均未变：不生成新档案。原档保持有效（不置 Stopped、不清渐退期，
                // 上方渐退重建对原档的 Activate/Clear 结果自然保留）；
                // 收入列已在表单阶段写回原档，分类/金额新旧同值无需回写。
                newApplicationId = context.ApplicationId;
                LogInfo($"经济复核无金额/分类/停保变化，跳过停旧建新: ApplicationId={context.ApplicationId}");
            }

            // 8. 创建变更记录（挂在旧档案上，After 快照含新档案ID）
            var changeContext = new ChangeContext
            {
                ApplicationId = context.ApplicationId,
                ChangeType = DictionaryConstants.ChangeType.FUND_CHANGE,
                ChangeReason = context.ReviewReason ?? "经济复核后",
                ChangeDate = DateTime.Today,
                OperatorName = context.OperatorName
            };

            // Before 快照用改写前捕获的局部变量
            var beforeJson = JsonSerializer.Serialize(new
            {
                Classification = oldClassification,
                GuaranteeAmount = oldGuaranteeAmount,
                TotalIncome = oldTotalIncome,
                PerCapitaIncome = oldPerCapitaIncome,
                RigidExpenditure = oldRigidExpenditure,
                FamilySize = oldFamilySize,
                // 表单覆写前捕获的真旧值：Old* 供审计核对，OldTotalAnnualIncome/Components
                // 供档案渐退审批表「变动情况说明」分项对比（旧档行在复核链已被覆写，不再可信）
                OldFamilySize = context.OldFamilySize,
                OldTotalFamilyIncome = context.OldTotalFamilyIncome,
                OldTotalAnnualIncome = context.OldTotalAnnualIncome,
                OldPerCapitaIncome = context.OldPerCapitaIncome,
                Components = context.OldComponents
            });

            var afterJson = JsonSerializer.Serialize(new
            {
                Classification = newClassification,
                GuaranteeAmount = newGuaranteeAmount,
                TotalIncome = application.TotalFamilyIncome,
                PerCapitaIncome = application.PerCapitaIncome,
                RigidExpenditure = application.RigidExpenditure,
                FamilySize = application.FamilySize,
                NewApplicationId = newApplicationId
            });

            var changeIdResult = await CreateChangeAsync(changeContext, beforeJson, afterJson, ct);
            if (changeIdResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(changeIdResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    changeIdResult.Message ?? "创建变更记录失败");
            }
            var changeId = changeIdResult.Value;

            // 填充 nc_biz_change_records 的结构化对比字段（与经济复核审计对应）
            var updateChangeSql = @"UPDATE nc_biz_change_records SET 
                change_reason_type = $1,
                old_classification = $2, new_classification = $3,
                old_per_capita_income = $4, new_per_capita_income = $5,
                old_guarantee_amount = $6, new_guarantee_amount = $7,
                triggered_grace_period = $8, triggered_stop = $9,
                new_application_id = $11,
                changed_at = NOW()
                WHERE id = $10;";
            var updateChangeResult = await _db.ExecuteNonQueryAsync(updateChangeSql, ct,
                ChangeReasonTypeConstants.EconomicReview, oldClassification, newClassification,
                oldPerCapitaIncome, application.PerCapitaIncome,
                oldGuaranteeAmount, newTotalGuaranteeAmount,
                triggeredGracePeriod, triggeredStop,
                changeId,
                // 无重建时留 NULL：写自身 ID 会形成 new_application_id = application_id 的自引用，
                // 档案链归一（DynamicManagementRecordService 递归 CTE）无法区分"无新档"与"新档=旧档"。
                needRebuild ? newApplicationId : null);
            if (updateChangeResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(updateChangeResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    updateChangeResult.Message ?? "更新变更记录对比字段失败");
            }

            // 跨大类变更（低保↔低收入/特困/刚性支出 等）：补写"原类别停止 + 新类别新增"两条记录，
            // 供月报"停保汇总表 / 新增救助明细"正确体现。
            if (IsCrossCategoryChange(oldClassification, newClassification))
            {
                var categoryReason = $"经济复核后 由 {ClassificationConstants.ConvertToMajorCategoryName(oldClassification ?? "")}转入{ClassificationConstants.ConvertToMajorCategoryName(newClassification ?? "")}";
                var crossSql = @"INSERT INTO nc_biz_change_records
                    (application_id, change_no, change_type, change_reason, change_reason_type, change_date,
                     old_classification, new_classification, old_guarantee_amount, new_guarantee_amount,
                     triggered_grace_period, triggered_stop, operator_name, new_application_id, changed_at)
                    VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$20,NOW()),
                           ($14,$2,$15,$4,$5,$6,$16,$17,$18,$19,$11,$12,$13,$21,NOW());";
                var crossResult = await _db.ExecuteNonQueryAsync(crossSql, ct,
                    context.ApplicationId,
                    $"CHG{DateTime.Now:yyyyMMddHHmmssfff}",
                    DictionaryConstants.ChangeType.CATEGORY_STOP,
                    categoryReason, ChangeReasonTypeConstants.EconomicReview, DateTime.Today,
                    (object?)oldClassification, (object?)newClassification,
                    (object?)oldGuaranteeAmount, (object?)newTotalGuaranteeAmount,
                    triggeredGracePeriod, triggeredStop,
                    context.OperatorName,
                    newApplicationId,
                    DictionaryConstants.ChangeType.CATEGORY_ADD,
                    (object?)null, (object?)newClassification,
                    (object?)null, (object?)newTotalGuaranteeAmount,
                    (object?)newApplicationId, (object?)null);
                if (crossResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<ChangeResult>(crossResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        crossResult.Message ?? "写入跨类变更记录失败");
                }
                LogInfo($"跨大类变更记录: {oldClassification} → {newClassification}（Stop/Add，新档案ID={newApplicationId}）");
            }
            else if (triggeredStop)
            {
                // 同大类转停保（如低保→收入超标）：补写 CategoryStop，供《档案_变更告知书》输出
                var stopWrite = await WriteCategoryStopRecordAsync(
                    context.ApplicationId, oldClassification, newClassification,
                    oldGuaranteeAmount, newTotalGuaranteeAmount, triggeredGracePeriod,
                    context.ReviewReason ?? "经济复核后", ChangeReasonTypeConstants.EconomicReview,
                    context.OperatorName, newApplicationId, ct);
                if (stopWrite.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<ChangeResult>(stopWrite.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        stopWrite.Message ?? "写入停保变更记录失败");
                }
                LogInfo($"经济复核停保记录: {oldClassification} → {newClassification}（新档案ID={newApplicationId}）");
            }

            await tx.CommitAsync(ct);

            LogInfo($"经济复核完成: 原分类={oldClassification} 新分类={newClassification}");

            // 联动：家庭经济/身份变化后，对已建高龄档案/在册发放成员写入"待复核"队列（失败不阻断）
            try
            {
                var reviewResult = await _elderlyApplicationService.TriggerReviewsForHouseholdAsync(
                    context.ApplicationId, NewCosmos.Constants.ElderlyBenefitConstants.ReviewTriggerLowIncomeChange, ct);
                if (reviewResult.IsSuccess && reviewResult.Value > 0)
                    LogInfo($"经济复核联动写入高龄待复核: ApplicationId={context.ApplicationId}, Count={reviewResult.Value}");
            }
            catch (Exception ex)
            {
                LogWarn($"高龄待复核联动失败（不阻断）: ApplicationId={context.ApplicationId}, {ex.Message}");
            }

            return Result.Success(new ChangeResult
            {
                ChangeId = changeId,
                IsSuccess = true,
                OldClassification = oldClassification ?? string.Empty,
                NewClassification = newClassification ?? string.Empty,
                OldGuaranteeAmount = oldGuaranteeAmount,
                NewGuaranteeAmount = newGuaranteeAmount,
                TriggeredGracePeriod = triggeredGracePeriod,
                TriggeredStop = triggeredStop,
                Rebuilt = needRebuild,
                NewApplicationId = newApplicationId
            });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, ChangeReasonTypeConstants.EconomicReview);
            return Result.FromException<ChangeResult>(ex);
        }
    }

    /// <summary>
    /// 执行家庭成员变更（增员/减员后重新认定）：重判分类 → 渐退期重建 → 停旧建新（chain_type=MemberChange）
    /// → 变更记录（change_category=MemberChange，供"增减员调整表"输出）→ 跨类/停保补记 CategoryStop
    /// （"停保告知书"依赖 triggered_stop=true 的 CategoryStop 记录）
    /// </summary>
    public async Task<Result<ChangeResult>> ExecuteMemberChangeAsync(MemberChangeContext context, CancellationToken ct = default)
    {
        LogInfo("执行家庭成员变更");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            // 1. 当前申请（已软删的申请不允许再做成员变更）
            var appSql = $"SELECT {ApplicationColumns.Full} FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL";
            var appResult = await _db.QuerySingleAsync<ApplicationEntity>(appSql, ct, context.ApplicationId);
            if (!appResult.IsSuccess || appResult.Value == null)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");
            }

            var application = appResult.Value;
            // 真旧值由调用方在 Step5 分类判定落库之前捕获（表单加载时刻）。
            // 直接读 application 会被判定按钮覆写，导致停旧建新判定恒判"无变化"。
            var oldClassification = context.OldClassification ?? application.ClassificationResult ?? string.Empty;
            var oldGuaranteeAmount = context.OldGuaranteeAmount ?? application.TotalGuaranteeAmount;

            // 变更链新建档案（户主死亡/既往复核停旧建新）：注入上游分类供渐退期"低保→低收入"判定
            var graceOldClassification = oldClassification;
            if (application.OriginalApplicationId > 0)
            {
                var upstreamResult = await _applicationService.GetByIdAsync(application.OriginalApplicationId, ct);
                if (upstreamResult.IsSuccess && upstreamResult.Value != null
                    && !string.IsNullOrEmpty(upstreamResult.Value.ClassificationResult))
                {
                    graceOldClassification = upstreamResult.Value.ClassificationResult!;
                    application.OriginalClassificationResult = graceOldClassification;
                }
            }

            // 变更前值必须在改写 application 之前落到局部变量（Before 快照用）；
            // 收入三项优先取表单入口（LoadApplicationAsync）固化的复核前快照——
            // 本事务 SaveEconomicDetailsAsync 已先写回新收入，读库是覆写后值；null 回退读库
            var oldTotalIncome = context.OldTotalFamilyIncome ?? application.TotalFamilyIncome;
            var oldPerCapitaIncome = context.OldPerCapitaIncome ?? application.PerCapitaIncome;
            var oldRigidExpenditure = context.OldRigidExpenditure ?? application.RigidExpenditure;
            var oldFamilySize = context.OldFamilySize > 0 ? context.OldFamilySize : application.FamilySize;

            // 2. 家庭成员（表单已保存增删结果；查询失败不能按"空成员"继续，否则漏算成员型补贴）
            var membersSql = "SELECT * FROM nc_biz_family_members WHERE application_id = $1 AND deleted_at IS null";
            var membersResult = await _db.QueryAsync<FamilyMember>(membersSql, ct, context.ApplicationId);
            if (membersResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(membersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    membersResult.Message ?? "家庭成员查询失败");
            }
            var members = membersResult.Value ?? new List<FamilyMember>();

            // 3. 应用新经济/人数口径（年值权威，月值为分解显示）
            application.TotalAnnualIncome = context.NewTotalAnnualIncome;
            application.PerCapitaAnnualIncome = context.NewPerCapitaAnnualIncome;
            application.TotalFamilyIncome = context.NewTotalFamilyIncome;
            application.PerCapitaIncome = context.NewPerCapitaIncome;
            application.RigidExpenditure = context.NewRigidExpenditure;
            application.FamilySize = context.NewFamilySize;

            // 4. 重新执行分类判定（需传真实赡养抚养扶养义务人，特困判定依赖"无义务人"条件）
            var supportersResult = await _supporterService.GetByApplicationIdAsync(context.ApplicationId, ct);
            if (supportersResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(supportersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    supportersResult.Message ?? "赡养抚养扶养人查询失败");
            }
            var supporters = supportersResult.Value ?? new List<Supporter>();
            var classificationResult = await _classificationService.DetermineClassificationAsync(
                application, members, supporters, new List<Caregiver>(), ct);
            if (!classificationResult.IsSuccess)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.CLASSIFICATION_FAILED, "分类判定失败");
            }

            var newClassification = classificationResult.Value.Classification;
            var newGuaranteeAmount = classificationResult.Value.GuaranteeAmount;
            var newClassifiedAmount = classificationResult.Value.ClassifiedSubsidy.TotalAmount;
            var newClassifiedType = classificationResult.Value.ClassifiedSubsidy.Types;
            var newCaregiverAmount = await ResolveCaregiverSubsidyAsync(
                newClassification, application.SupportMode, context.ApplicationId, ct);

            // 5. 渐退期状态重建（同复核：新分类仍低收入 → 保留/续接；否则结清）
            var reconcileResult = await ReconcileGracePeriodAsync(
                application.Id, newClassification, classificationResult.Value.GracePeriod,
                graceOldClassification, oldGuaranteeAmount, ct: ct);
            if (reconcileResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(reconcileResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    reconcileResult.Message ?? "渐退期状态重建失败");
            }
            var triggeredGracePeriod = classificationResult.Value.GracePeriod.IsEligible == true;
            if (triggeredGracePeriod)
            {
                _gracePeriodService.ApplyGracePeriod(application, new GracePeriodInfo
                {
                    IsInGracePeriod = true,
                    GracePeriodMonths = classificationResult.Value.GracePeriod.Months,
                    GracePeriodStartDate = classificationResult.Value.GracePeriod.StartDate,
                    GracePeriodEndDate = classificationResult.Value.GracePeriod.EndDate,
                    OriginalClassificationResult = graceOldClassification,
                    OriginalGuaranteeAmount = oldGuaranteeAmount
                });
            }

            // 6. 是否停保
            bool triggeredStop = ClassificationConstants.IsCodeStop(newClassification);

            var changeType = string.IsNullOrEmpty(context.ChangeType)
                ? DictionaryConstants.ChangeType.MEMBER_MODIFY
                : context.ChangeType;
            var reasonText = string.IsNullOrWhiteSpace(context.ChangeSummary)
                ? (string.IsNullOrWhiteSpace(context.ChangeReason) ? "家庭成员变更" : context.ChangeReason)
                : $"{context.ChangeReason}（{context.ChangeSummary}）";

            // 7. 停旧建新：旧档案 Stopped，新档案 Draft 承载变更后成员/经济/分类/金额
            var rebuiltReason = $"家庭成员变更后 由 {ClassificationConstants.ConvertToMajorCategoryName(oldClassification ?? "")}转入{ClassificationConstants.ConvertToMajorCategoryName(newClassification ?? "")}";
            var rebuildResult = await StopAndRebuildAsync(
                context.ApplicationId,
                changeType,
                ChainTypeConstants.MEMBER_CHANGE,
                rebuiltReason,
                context.OperatorName,
                applyOverrides: target =>
                {
                    target.TotalFamilyIncome = application.TotalFamilyIncome;
                    target.PerCapitaIncome = application.PerCapitaIncome;
                    target.TotalAnnualIncome = application.TotalAnnualIncome;
                    target.PerCapitaAnnualIncome = application.PerCapitaAnnualIncome;
                    target.RigidExpenditure = application.RigidExpenditure;
                    target.FamilySize = application.FamilySize;
                    target.ClassificationResult = newClassification;
                    target.IsEligible = classificationResult.Value.IsEligible;
                    target.HouseholdMonthlyGuaranteeAmount = newGuaranteeAmount;
                    target.TotalGuaranteeAmount = newGuaranteeAmount + newClassifiedAmount + newCaregiverAmount;
                    target.ClassifiedSubsidyType = newClassifiedType;
                    target.ClassifiedSubsidyAmount = newClassifiedAmount;
                    target.CaregiverSubsidyAmount = newCaregiverAmount;
                },
                ct);
            if (rebuildResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(rebuildResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    rebuildResult.Message ?? "家庭成员变更停旧建新失败");
            }
            var newApplicationId = rebuildResult.Value.NewApplicationId;

            // 8. 变更记录（挂在旧档案上；After 快照含新档案ID）
            var changeContext = new ChangeContext
            {
                ApplicationId = context.ApplicationId,
                ChangeType = changeType,
                ChangeCategory = DictionaryConstants.ChangeCategory.MEMBER_CHANGE,
                ChangeReason = reasonText,
                ChangeReasonType = ChangeReasonTypeConstants.MemberChange,
                ChangeDate = DateTime.Today,
                OperatorName = context.OperatorName
            };

            var beforeJson = JsonSerializer.Serialize(new
            {
                Classification = oldClassification,
                GuaranteeAmount = oldGuaranteeAmount,
                TotalIncome = oldTotalIncome,
                PerCapitaIncome = oldPerCapitaIncome,
                RigidExpenditure = oldRigidExpenditure,
                FamilySize = oldFamilySize
            });

            var entries = context.Entries ?? new List<MemberChangeEntry>();
            var addedDetails = entries
                .Where(e => string.Equals(e.Direction, DictionaryConstants.ChangeType.MEMBER_ADD, StringComparison.OrdinalIgnoreCase))
                .Select(e => new
                {
                    e.Name, e.IdCard, e.RelationshipToHead, e.MemberCategory,
                    e.ReasonCode, e.ReasonName,
                    EventDate = e.EventDate.ToString("yyyy-MM-dd"),
                    e.Remark
                }).ToList();
            var removedDetails = entries
                .Where(e => string.Equals(e.Direction, DictionaryConstants.ChangeType.MEMBER_REMOVE, StringComparison.OrdinalIgnoreCase))
                .Select(e => new
                {
                    e.Name, e.IdCard, e.RelationshipToHead, e.MemberCategory,
                    e.ReasonCode, e.ReasonName,
                    EventDate = e.EventDate.ToString("yyyy-MM-dd"),
                    e.Remark
                }).ToList();

            var afterJson = JsonSerializer.Serialize(new
            {
                Classification = newClassification,
                GuaranteeAmount = newGuaranteeAmount,
                TotalIncome = application.TotalFamilyIncome,
                PerCapitaIncome = application.PerCapitaIncome,
                RigidExpenditure = application.RigidExpenditure,
                FamilySize = application.FamilySize,
                ChangeSummary = context.ChangeSummary,
                AddedMembers = addedDetails,
                RemovedMembers = removedDetails,
                NewApplicationId = newApplicationId
            });

            var changeIdResult = await CreateChangeAsync(changeContext, beforeJson, afterJson, ct);
            if (changeIdResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(changeIdResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    changeIdResult.Message ?? "创建变更记录失败");
            }
            var changeId = changeIdResult.Value;

            // 填充结构化对比字段（与经济复核审计对齐）
            var updateChangeSql = @"UPDATE nc_biz_change_records SET 
                change_reason_type = $1,
                old_classification = $2, new_classification = $3,
                old_per_capita_income = $4, new_per_capita_income = $5,
                old_guarantee_amount = $6, new_guarantee_amount = $7,
                triggered_grace_period = $8, triggered_stop = $9,
                new_application_id = $11,
                changed_at = NOW()
                WHERE id = $10;";
            var updateChangeResult = await _db.ExecuteNonQueryAsync(updateChangeSql, ct,
                ChangeReasonTypeConstants.MemberChange, oldClassification, newClassification,
                oldPerCapitaIncome, application.PerCapitaIncome,
                oldGuaranteeAmount, newGuaranteeAmount,
                triggeredGracePeriod, triggeredStop,
                changeId, newApplicationId);
            if (updateChangeResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(updateChangeResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    updateChangeResult.Message ?? "更新变更记录对比字段失败");
            }

            // 逐人变更明细：结构化留痕（nc_biz_change_details），增/减员原因可查询审计
            if (entries.Count > 0)
            {
                var detailSb = new StringBuilder();
                var detailParams = new List<object>(entries.Count * 5);
                for (var i = 0; i < entries.Count; i++)
                {
                    var e = entries[i];
                    if (i > 0) detailSb.Append(',');
                    var p = i * 5;
                    detailSb.Append($"(${p + 1},${p + 2},${p + 3},${p + 4},${p + 5})");
                    detailParams.Add(changeId);
                    detailParams.Add(e.Direction);
                    detailParams.Add(e.Name);
                    detailParams.Add($"{e.Name}({e.IdCard}) {e.MemberCategory} {e.RelationshipToHead}".Trim());
                    detailParams.Add($"{e.ReasonName}|{e.EventDate:yyyy-MM-dd}|{e.Remark}");
                }
                var detailSql = $"INSERT INTO nc_biz_change_details (change_id, field_name, field_label, old_value, new_value) VALUES {detailSb};";
                var detailResult = await _db.ExecuteNonQueryAsync(detailSql, ct, detailParams.ToArray());
                if (detailResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<ChangeResult>(detailResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        detailResult.Message ?? "写入变更明细失败");
                }
            }

            // 死亡减员联动：减员原因为"人员死亡"时批量写死亡记录（与户主死亡同表，供统计/公示退出识别）
            // 单语句 CTE 保留每行 WHERE NOT EXISTS 去重语义（同批重复证件号由 DISTINCT ON 收口）
            var deathEntries = entries.Where(e =>
                string.Equals(e.Direction, DictionaryConstants.ChangeType.MEMBER_REMOVE, StringComparison.OrdinalIgnoreCase)
                && MemberChangeReasonConstants.IsDeath(e.ReasonCode)).ToList();
            if (deathEntries.Count > 0)
            {
                const int deathParamsPerRow = 9;
                var (deathValues, deathArgs) = NewCosmos.Helpers.MultiRowValuesBuilder.Build(
                    deathEntries.Count, deathParamsPerRow,
                    i =>
                    {
                        var e = deathEntries[i];
                        return new object?[]
                        {
                            context.ApplicationId, e.MemberId, e.Name, e.IdCard, e.RelationshipToHead,
                            e.EventDate,
                            string.IsNullOrWhiteSpace(e.Remark) ? "成员变更-人员死亡" : e.Remark,
                            e.Remark, context.OperatorName
                        };
                    });
                var deathSql = @"WITH input(application_id, member_id, member_name, member_id_card, relationship_to_head, death_date, death_reason, remark, operator_name) AS (
                        VALUES " + deathValues + @")
                    INSERT INTO nc_biz_death_records
                        (application_id, member_id, member_name, member_id_card, relationship_to_head,
                         death_date, death_reason, is_household_head, remark, operator_name, created_at)
                    SELECT DISTINCT ON (member_id_card)
                        application_id, member_id, member_name, member_id_card, relationship_to_head,
                        death_date, death_reason, false, remark, operator_name, NOW()
                    FROM input i
                    WHERE NOT EXISTS (
                        SELECT 1 FROM nc_biz_death_records d
                        WHERE d.application_id = i.application_id AND d.member_id_card = i.member_id_card)
                    ORDER BY member_id_card";
                var deathResult = await _db.ExecuteNonQueryAsync(deathSql, ct, deathArgs);
                if (deathResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<ChangeResult>(deathResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        deathResult.Message ?? "写入死亡记录失败");
                }
            }

            // 9. 跨大类变更补写"原类别停止+新类别新增"；同大类但触发停保时补写 CategoryStop
            if (IsCrossCategoryChange(oldClassification, newClassification))
            {
                var categoryReason = $"家庭成员变更后 由 {ClassificationConstants.ConvertToMajorCategoryName(oldClassification ?? "")}转入{ClassificationConstants.ConvertToMajorCategoryName(newClassification ?? "")}";
                var crossSql = @"INSERT INTO nc_biz_change_records
                    (application_id, change_no, change_type, change_reason, change_reason_type, change_date,
                     old_classification, new_classification, old_guarantee_amount, new_guarantee_amount,
                     triggered_grace_period, triggered_stop, operator_name, new_application_id, changed_at)
                    VALUES ($1,$2,$3,$4,$5,$6,$7,$8,$9,$10,$11,$12,$13,$20,NOW()),
                           ($14,$2,$15,$4,$5,$6,$16,$17,$18,$19,$11,$12,$13,$21,NOW());";
                var crossResult = await _db.ExecuteNonQueryAsync(crossSql, ct,
                    context.ApplicationId,
                    $"CHG{DateTime.Now:yyyyMMddHHmmssfff}",
                    DictionaryConstants.ChangeType.CATEGORY_STOP,
                    categoryReason, ChangeReasonTypeConstants.MemberChange, DateTime.Today,
                    (object?)oldClassification, (object?)newClassification,
                    (object?)oldGuaranteeAmount, (object?)newGuaranteeAmount,
                    triggeredGracePeriod, triggeredStop,
                    context.OperatorName,
                    newApplicationId,
                    DictionaryConstants.ChangeType.CATEGORY_ADD,
                    (object?)null, (object?)newClassification,
                    (object?)null, (object?)newGuaranteeAmount,
                    (object?)newApplicationId, (object?)null);
                if (crossResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<ChangeResult>(crossResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        crossResult.Message ?? "写入跨类变更记录失败");
                }
                LogInfo($"家庭成员变更跨类记录: {oldClassification} → {newClassification}（Stop/Add，新档案ID={newApplicationId}）");
            }
            else if (triggeredStop)
            {
                var stopWrite = await WriteCategoryStopRecordAsync(
                    context.ApplicationId, oldClassification, newClassification,
                    oldGuaranteeAmount, newGuaranteeAmount, triggeredGracePeriod,
                    reasonText, ChangeReasonTypeConstants.MemberChange,
                    context.OperatorName, newApplicationId, ct);
                if (stopWrite.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<ChangeResult>(stopWrite.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        stopWrite.Message ?? "写入停保变更记录失败");
                }
                LogInfo($"家庭成员变更停保记录: {oldClassification} → {newClassification}（新档案ID={newApplicationId}）");
            }

            await tx.CommitAsync(ct);

            LogInfo($"家庭成员变更完成: 原分类={oldClassification} 新分类={newClassification} 新档案ID={newApplicationId}");

            // 联动：家庭人口变化后，对已建高龄档案/在册发放成员写入"待复核"队列（失败不阻断）
            try
            {
                var reviewResult = await _elderlyApplicationService.TriggerReviewsForHouseholdAsync(
                    context.ApplicationId, NewCosmos.Constants.ElderlyBenefitConstants.ReviewTriggerLowIncomeChange, ct);
                if (reviewResult.IsSuccess && reviewResult.Value > 0)
                    LogInfo($"家庭成员变更联动写入高龄待复核: ApplicationId={context.ApplicationId}, Count={reviewResult.Value}");
            }
            catch (Exception ex)
            {
                LogWarn($"高龄待复核联动失败（不阻断）: ApplicationId={context.ApplicationId}, {ex.Message}");
            }

            return Result.Success(new ChangeResult
            {
                ChangeId = changeId,
                IsSuccess = true,
                OldClassification = oldClassification ?? string.Empty,
                NewClassification = newClassification ?? string.Empty,
                OldGuaranteeAmount = oldGuaranteeAmount,
                NewGuaranteeAmount = newGuaranteeAmount,
                TriggeredGracePeriod = triggeredGracePeriod,
                TriggeredStop = triggeredStop,
                NewApplicationId = newApplicationId
            });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, ChangeReasonTypeConstants.MemberChange);
            return Result.FromException<ChangeResult>(ex);
        }
    }

    /// <summary>
    /// 判断是否为跨大类变更（低保/低收入/特困/刚性支出 四大类之间跨越）。
    /// 与月报 CategoryCodes 分组一致：低保含单人保（RuralLowIncomeSingle/UrbanLowIncomeSingle）。
    /// 同大类内调整（如低保→低保单人）不算跨类；任一分组无法识别（空/无效码）不算跨类。
    /// </summary>
    private static bool IsCrossCategoryChange(string? oldClassification, string? newClassification)
    {
        var oldGroup = GetCategoryGroup(oldClassification);
        var newGroup = GetCategoryGroup(newClassification);
        return !string.IsNullOrEmpty(oldGroup) && !string.IsNullOrEmpty(newGroup)
            && !string.Equals(oldGroup, newGroup, StringComparison.Ordinal);
    }

    /// <summary>
    /// 分类码 → 四大类分组（与 MonthlyReportService.CategoryCodes 口径一致）。
    /// </summary>
    private static string? GetCategoryGroup(string? classification)
    {
        if (string.IsNullOrEmpty(classification)) return null;
        if (ClassificationConstants.IsCodeSubsistence(classification)) return "最低生活保障";
        if (ClassificationConstants.IsCodeLowIncome(classification)) return "最低生活保障边缘家庭";
        if (ClassificationConstants.IsCodeDestitute(classification)) return "特困人员";
        if (ClassificationConstants.IsCodeRigidExpenditure(classification)) return "刚性支出困难家庭";
        return null;
    }
}
