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
    /// <summary>
    /// 处理成员死亡
    /// </summary>
    public async Task<Result<ChangeResult>> ProcessMemberDeathAsync(MemberDeathContext context, CancellationToken ct = default)
    {
        LogInfo($"处理成员死亡: ApplicationId={context.ApplicationId}");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            // 1. 获取当前申请数据（已软删的申请不允许再做成员死亡处理）
            var appSql = $"SELECT {ApplicationColumns.Full} FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL";
            var appResult = await _db.QuerySingleAsync<ApplicationEntity>(appSql, ct, context.ApplicationId);

            if (!appResult.IsSuccess || appResult.Value == null)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");
            }

            var application = appResult.Value;
            var oldClassification = application.ClassificationResult ?? string.Empty;
            var oldGuaranteeAmount = application.TotalGuaranteeAmount;
            // 变更前家庭人数在任何改写发生前捕获，Before 快照直接用它，
            // 不再用 FamilySize + 1 反推（改写后反推得到的是错的）
            var oldFamilySize = application.FamilySize;

            // 2. 标记成员死亡（nc_biz_family_members 无 deleted_by 列，仅置 deleted_at）
            var updateMemberSql = @"UPDATE nc_biz_family_members
                                   SET deleted_at = NOW()
                                   WHERE id = $1 AND application_id = $2;";
            var markResult = await _db.ExecuteNonQueryAsync(updateMemberSql, ct, context.MemberId, context.ApplicationId);
            if (markResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(markResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    markResult.Message ?? "标记成员死亡失败");
            }

            // 3. 创建死亡记录
            var deathSql = @"INSERT INTO nc_biz_death_records
                            (application_id, member_id, member_name, member_id_card, death_date, death_reason, operator_name)
                            VALUES ($1,$2,$3,$4,$5,$6,$7);";
            var deathInsertResult = await _db.ExecuteNonQueryAsync(deathSql, ct,
                context.ApplicationId, context.MemberId, context.MemberName,
                context.MemberIdCard, context.DeathDate, context.DeathReason, context.OperatorName);
            if (deathInsertResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(deathInsertResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    deathInsertResult.Message ?? "创建死亡记录失败");
            }

            // 4. 重新计算家庭人数。
            //    统计失败绝不能回退为"1 人"继续算：家庭人数直接决定保障金额，
            //    按错误人数算出的补贴会写进数据库，必须回滚并失败返回。
            //    剔除赡养抚养扶养人（member_category='Support'），该人群不计入共同生活家庭人数。
            var countSql = @"SELECT COUNT(*) FROM nc_biz_family_members 
                            WHERE application_id = $1 AND deleted_at IS null
                              AND (member_category IS NULL OR member_category <> 'Support')";
            var countResult = await _db.ExecuteScalarAsync(countSql, ct, context.ApplicationId);
            if (countResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(countResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    countResult.Message ?? "家庭人数统计失败");
            }
            var newFamilySize = (int)countResult.Value;

            // 5. 获取剩余成员（同经济复核：查询失败不能按空成员继续做分类判定）
            var membersSql = "SELECT * FROM nc_biz_family_members WHERE application_id = $1 AND deleted_at IS null";
            var membersResult = await _db.QueryAsync<FamilyMember>(membersSql, ct, context.ApplicationId);
            if (membersResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(membersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    membersResult.Message ?? "家庭成员查询失败");
            }
            var members = membersResult.Value ?? new List<FamilyMember>();

            // 6. 更新家庭人数
            application.FamilySize = newFamilySize;

            // 7. 重新执行分类判定（需传真实赡养抚养扶养义务人，特困判定依赖"无义务人"条件）
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

            string newClassification = oldClassification;
            decimal newGuaranteeAmount = oldGuaranteeAmount;
            bool triggeredStop = false;

            if (classificationResult.IsSuccess)
            {
                newClassification = classificationResult.Value.Classification;
                newGuaranteeAmount = classificationResult.Value.GuaranteeAmount;
                triggeredStop = ClassificationConstants.IsCodeStop(newClassification);

                // 保障金三项合成（户月 + 分类施保 + 照料），与 GuaranteeAmountService 同口径；
                // 照料费：特困分散按能力鉴定重算，集中/非特困一律 0（禁止沿用旧档 DB 值）
                var newClassifiedAmount = classificationResult.Value.ClassifiedSubsidy.TotalAmount;
                var newClassifiedType = classificationResult.Value.ClassifiedSubsidy.Types;
                var newCaregiverAmount = await ResolveCaregiverSubsidyAsync(
                    newClassification, application.SupportMode, application.Id, ct);

                // 人数变化后重算人均（年值权威；月值 = 年÷人数÷12 一次舍入）
                if (newFamilySize > 0)
                {
                    if (application.TotalAnnualIncome > 0)
                    {
                        application.PerCapitaAnnualIncome = Math.Round(application.TotalAnnualIncome / newFamilySize, 2);
                        application.PerCapitaIncome = Math.Round(application.TotalAnnualIncome / newFamilySize / 12m, 2);
                    }
                    else
                    {
                        application.PerCapitaAnnualIncome = 0m;
                        application.PerCapitaIncome = Math.Round(application.TotalFamilyIncome / newFamilySize, 2);
                    }
                }

                // 更新申请
                application.ClassificationResult = newClassification;
                application.IsEligible = classificationResult.Value.IsEligible;
                application.ClassifiedSubsidyType = newClassifiedType;
                application.ClassifiedSubsidyAmount = newClassifiedAmount;
                application.CaregiverSubsidyAmount = newCaregiverAmount;
                application.HouseholdMonthlyGuaranteeAmount = newGuaranteeAmount;
                application.TotalGuaranteeAmount = newGuaranteeAmount + newClassifiedAmount + newCaregiverAmount;
                application.FamilySize = newFamilySize;
                application.UpdatedAt = DateTime.Now;
                application.UpdatedBy = context.OperatorName;

                // 如果最后成员死亡，停止档案。
                // 状态变更必须过状态机，不能从任意状态直写 "Stopped"：
                // - Draft（草稿）/ Refused（不予受理）从未进入保障，不存在"停保"一说，
                //   直写 Stopped 属于绕过状态机产生脏状态 → 拒绝（INVALID_TRANSITION）并回滚整个死亡处理；
                // - 业务意图确实是"最后成员死亡 → 停保"（死亡是客观事件），因此仅对
                //   Draft/Refused 阻断：Approved/Completed→Stopped 状态机本就允许；
                //   Submitted→Stopped 严格状态机不允许，但阻断会让全员死亡的在途申请
                //   永远无法关闭，故放行并记录警告；
                // - 已是 Stopped 的申请无需重复转换，保持现状。
                if (newFamilySize <= 0)
                {
                    var currentStatus = ApplicationStatusExtensions.FromCode(application.Status ?? string.Empty);
                    if (currentStatus == ApplicationStatus.Stopped)
                    {
                        triggeredStop = true;
                    }
                    else
                    {
                        var transition = ApplicationStateMachine.ValidateTransition(currentStatus, ApplicationStatus.Stopped);
                        if (transition.IsFailure &&
                            currentStatus is ApplicationStatus.Draft or ApplicationStatus.Refused)
                        {
                            await tx.RollbackAsync(ct);
                            return Result.Failure<ChangeResult>(
                                ErrorCodes.INVALID_TRANSITION,
                                transition.Message ?? $"不允许从 {currentStatus.GetDescription()} 转换到 已停保");
                        }
                        if (transition.IsFailure)
                        {
                            LogWarn($"成员死亡触发停保: 状态 {application.Status} → Stopped 不在严格状态机允许范围内，按业务规则（最后成员死亡）放行");
                        }

                        application.Status = ApplicationStatus.Stopped.GetCode();
                        application.StopReason = "最后成员死亡";
                        application.StopDate = DateTime.Today;
                        triggeredStop = true;
                    }
                }

                // 渐退期状态重建（同事务）：按成员死亡后新分类决定去留；
                // 最后成员死亡触发停保 → 强制结清既有渐退期。
                var reconcileRes = await ReconcileGracePeriodAsync(
                    context.ApplicationId, newClassification, classificationResult.Value.GracePeriod,
                    oldClassification, oldGuaranteeAmount,
                    forceClear: newFamilySize <= 0, ct: ct);
                if (reconcileRes.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<ChangeResult>(reconcileRes.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        reconcileRes.Message ?? "渐退期状态重建失败");
                }

                // 业务字段更新（不含 status/stop_*：状态写入统一走 ApplicationStatusService）
                var updateSql = @"UPDATE nc_biz_applications SET
                    family_size = $1, classification_result = $2, is_eligible = $3,
                    household_monthly_guarantee_amount = $4, total_guarantee_amount = $5,
                    classified_subsidy_type = $6, classified_subsidy_amount = $7,
                    caregiver_subsidy_amount = $8,
                    per_capita_income = $9, per_capita_annual_income = $10,
                    updated_at = $11, updated_by = $12
                    WHERE id = $13;";

                var appUpdateResult = await _db.ExecuteNonQueryAsync(updateSql, ct,
                    application.FamilySize, application.ClassificationResult, application.IsEligible,
                    application.HouseholdMonthlyGuaranteeAmount, application.TotalGuaranteeAmount,
                    application.ClassifiedSubsidyType, application.ClassifiedSubsidyAmount,
                    application.CaregiverSubsidyAmount,
                    application.PerCapitaIncome, application.PerCapitaAnnualIncome,
                    application.UpdatedAt, application.UpdatedBy,
                    application.Id);
                if (appUpdateResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<ChangeResult>(appUpdateResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        appUpdateResult.Message ?? "更新业务字段失败");
                }

                // 状态写入统一走 ApplicationStatusService（状态机校验 + stop_* 落库 + 审计留痕）
                if (triggeredStop)
                {
                    var deathStop = await _statusService.StopAsync(
                        context.ApplicationId, application.StopReason ?? "最后成员死亡",
                        application.StopDate == default ? DateTime.Today : application.StopDate, context.OperatorName,
                        allowSubmittedOverride: true, ct: ct);
                    if (deathStop.IsFailure)
                    {
                        await tx.RollbackAsync(ct);
                        return Result.Failure<ChangeResult>(deathStop.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                            deathStop.Message ?? "停保失败");
                    }
                }
            }
            else
            {
                // 分类判定失败绝不能按"沿用旧分类"静默提交：家庭人数已因死亡变化，
                // 沿用旧结果会写出与实际不符的补贴，必须回滚并失败返回。
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(classificationResult.ErrorCode ?? ErrorCodes.CLASSIFICATION_FAILED,
                    classificationResult.Message ?? "分类判定失败");
            }

            // 8. 创建变更记录（写入三要素：category=MemberChange、reason 可解析、随后回填金额）
            var changeContext = new ChangeContext
            {
                ApplicationId = context.ApplicationId,
                ChangeType = DictionaryConstants.ChangeType.MEMBER_DEATH,
                ChangeCategory = DictionaryConstants.ChangeCategory.MEMBER_CHANGE,
                ChangeReason = $"成员死亡: {context.MemberName}({context.MemberIdCard})",
                ChangeDate = context.DeathDate,
                OperatorName = context.OperatorName
            };

            // Before 快照使用改写前捕获的 oldFamilySize（此前用 FamilySize + 1 反推，
            // 一旦 FamilySize 与成员表行数不同步，反推值就是错的）
            var beforeJson = JsonSerializer.Serialize(new
            {
                FamilySize = oldFamilySize,
                Classification = oldClassification,
                GuaranteeAmount = oldGuaranteeAmount
            });

            var afterJson = JsonSerializer.Serialize(new
            {
                FamilySize = newFamilySize,
                Classification = newClassification,
                GuaranteeAmount = newGuaranteeAmount,
                DeceasedMember = context.MemberName
            });

            var changeIdResult = await CreateChangeAsync(changeContext, beforeJson, afterJson, ct);
            if (changeIdResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(changeIdResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    changeIdResult.Message ?? "创建变更记录失败");
            }
            var changeId = changeIdResult.Value;

            // 结构化金额回填（增减员调整表 old/new_guarantee 取数）
            var updateDeathChange = await _db.ExecuteNonQueryAsync(
                @"UPDATE nc_biz_change_records SET
                    old_classification = $1, new_classification = $2,
                    old_guarantee_amount = $3, new_guarantee_amount = $4,
                    change_reason_type = $5,
                    changed_at = NOW()
                  WHERE id = $6",
                ct,
                oldClassification, newClassification,
                oldGuaranteeAmount, newGuaranteeAmount,
                ChangeReasonTypeConstants.MemberChange,
                changeId);
            if (updateDeathChange.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(updateDeathChange.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    updateDeathChange.Message ?? "回填成员死亡变更金额失败");
            }

            await tx.CommitAsync(ct);

            LogInfo($"成员死亡处理完成: 新家庭人数={newFamilySize}");

            return Result.Success(new ChangeResult
            {
                ChangeId = changeId,
                IsSuccess = true,
                OldClassification = oldClassification,
                NewClassification = newClassification,
                OldGuaranteeAmount = oldGuaranteeAmount,
                NewGuaranteeAmount = newGuaranteeAmount,
                TriggeredStop = triggeredStop
            });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "成员死亡处理");
            return Result.FromException<ChangeResult>(ex);
        }
    }

    /// <summary>
    /// 执行户主变更（停旧建新：所有信息变更建新档案，旧档案停止，便于追踪变更记录）
    /// </summary>
    public async Task<Result<ChangeResult>> ExecuteHeadChangeAsync(HeadChangeContext context, CancellationToken ct = default)
    {
        LogInfo("执行户主变更（停旧建新）");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            // 1. 获取新户主信息（必须限定归属本申请且未删除——否则传入别户的成员 ID
            //    会把别户成员的姓名/身份证写进本申请的户主字段，数据归属错乱）
            var memberSql = "SELECT * FROM nc_biz_family_members WHERE id = $1 AND application_id = $2 AND deleted_at IS NULL";
            var memberResult = await _db.QuerySingleAsync<FamilyMember>(memberSql, ct, context.NewHeadMemberId, context.ApplicationId);
            if (!memberResult.IsSuccess || memberResult.Value == null)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.APPLICATION_NOT_FOUND, "新户主成员不存在");
            }
            var newHead = memberResult.Value;

            // 2. 停旧建新：复制旧档案 → 新档案（original_application_id=旧ID、Draft），旧档案停止
            var rebuildResult = await StopAndRebuildAsync(
                context.ApplicationId,
                DictionaryConstants.ChangeType.HOUSEHOLD_HEAD_CHANGE,
                ChainTypeConstants.HEAD_CHANGE,
                "停止",
                context.OperatorName,
                ct: ct);
            if (rebuildResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(rebuildResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    rebuildResult.Message ?? "户主变更停旧建新失败");
            }
            var newApplicationId = rebuildResult.Value.NewApplicationId;

            // 3. 更新新档案申请户主信息（applicant_name 等）
            var updateAppSql = @"UPDATE nc_biz_applications SET
                applicant_name = $1, applicant_id_card = $2, gender = $3,
                ethnicity = $4, marital_status = $5, updated_at = NOW()
                WHERE id = $6;";
            var appHeadResult = await _db.ExecuteNonQueryAsync(updateAppSql, ct,
                newHead.Name, newHead.IdCard, newHead.Gender,
                newHead.Ethnicity, newHead.MaritalStatus,
                newApplicationId);
            if (appHeadResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(appHeadResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    appHeadResult.Message ?? "更新新档案户主信息失败");
            }

            // 4. 更新新档案成员户主关系：新户主→本人/户主，旧户主→家庭成员
            long MapMember(long id) => rebuildResult.Value.MemberIdMap.TryGetValue(id, out var nid) ? nid : id;
            var newHeadMemberId = MapMember(context.NewHeadMemberId);
            var oldHeadMemberId = context.OldHeadMemberId > 0 ? MapMember(context.OldHeadMemberId) : 0;
            if (oldHeadMemberId > 0)
            {
                var updOld = await _db.ExecuteNonQueryAsync(
                    "UPDATE nc_biz_family_members SET relationship_to_head = '家庭成员', updated_at = NOW() WHERE id = $1 AND application_id = $2;",
                    ct, oldHeadMemberId, newApplicationId);
                if (updOld.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<ChangeResult>(updOld.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                        updOld.Message ?? "更新旧户主关系失败");
                }
            }
            var updNew = await _db.ExecuteNonQueryAsync(
                "UPDATE nc_biz_family_members SET relationship_to_head = '本人/户主', is_applicant = true, updated_at = NOW() WHERE id = $1 AND application_id = $2;",
                ct, newHeadMemberId, newApplicationId);
            if (updNew.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(updNew.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    updNew.Message ?? "更新新户主关系失败");
            }

            // 5. 创建变更记录（挂在旧档案上，After 快照含新档案ID）
            //    写入三要素：category=MemberChange、reason 可解析、随后回填金额
            var changeContext = new ChangeContext
            {
                ApplicationId = context.ApplicationId,
                ChangeType = DictionaryConstants.ChangeType.HOUSEHOLD_HEAD_CHANGE,
                ChangeCategory = DictionaryConstants.ChangeCategory.MEMBER_CHANGE,
                ChangeReason = string.IsNullOrWhiteSpace(context.ChangeReason)
                    ? $"户主变更: {context.OldHeadName} -> {context.NewHeadName}"
                    : $"户主变更: {context.OldHeadName} -> {context.NewHeadName}；{context.ChangeReason}",
                ChangeDate = DateTime.Today,
                OperatorName = context.OperatorName
            };

            var beforeJson = JsonSerializer.Serialize(new { HeadName = context.OldHeadName });
            var afterJson = JsonSerializer.Serialize(new { HeadName = context.NewHeadName, NewApplicationId = newApplicationId });

            var changeIdResult = await CreateChangeAsync(changeContext, beforeJson, afterJson, ct);
            if (changeIdResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(changeIdResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    changeIdResult.Message ?? "创建变更记录失败");
            }
            var changeId = changeIdResult.Value;

            // 结构化金额回填（增减员调整表取数；户主变更本身金额可能不变）
            var updateHeadChange = await _db.ExecuteNonQueryAsync(
                @"UPDATE nc_biz_change_records SET
                    change_reason_type = $1,
                    new_application_id = $2,
                    changed_at = NOW()
                  WHERE id = $3",
                ct,
                ChangeReasonTypeConstants.MemberChange,
                newApplicationId,
                changeId);
            if (updateHeadChange.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(updateHeadChange.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    updateHeadChange.Message ?? "回填户主变更记录失败");
            }

            await tx.CommitAsync(ct);

            LogInfo($"户主变更完成（停旧建新）: 新档案ID={newApplicationId}");

            return Result.Success(new ChangeResult
            {
                ChangeId = changeId,
                IsSuccess = true,
                NewApplicationId = newApplicationId
            });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "户主变更");
            return Result.FromException<ChangeResult>(ex);
        }
    }

    /// <summary>
    /// 执行户主死亡变更（停旧建新）
    /// 事务内：停旧 → 身份证查重 → 建新（Draft）→ 复制成员/赡养人/经济明细/入户调查/照料人 → 死亡记录 → 变更记录
    /// </summary>
    public async Task<Result<ChangeResult>> ExecuteHouseholdDeathAsync(HouseholdDeathContext context, CancellationToken ct = default)
    {
        LogInfo($"执行户主死亡变更: ApplicationId={context.ApplicationId}, 新户主={DataMasker.MaskName(context.NewHeadName)}");

        if (context.NewHeadMemberId <= 0)
            return Result.Failure<ChangeResult>(ErrorCodes.VALIDATION_FAILED, "请选择新户主");

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        try
        {
            // 1. 获取旧档案（已软删的申请不允许再变更）
            var appResult = await _db.QuerySingleAsync<ApplicationEntity>(
                $"SELECT {ApplicationColumns.Full} FROM nc_biz_applications WHERE id = $1 AND deleted_at IS NULL", ct, context.ApplicationId);
            if (!appResult.IsSuccess || appResult.Value == null)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.APPLICATION_NOT_FOUND, "申请不存在");
            }
            var application = appResult.Value;
            var oldClassification = application.ClassificationResult ?? string.Empty;
            var oldGuaranteeAmount = application.TotalGuaranteeAmount;
            var oldFamilySize = application.FamilySize;
            var oldHeadName = application.ApplicantName;
            var oldHeadIdCard = application.ApplicantIdCard;

            // 2. 状态校验：已停保幂等拒绝；不予受理（Refused，从未进入保障）不允许停保建新；
            //    草稿（含导入库建档未归档户）/已提交按业务规则（户主死亡客观事件）放行 LogWarn，
            //    与成员变更/户主变更/经济复核对 Draft 的既有容忍度一致。
            var currentStatus = ApplicationStatusExtensions.FromCode(application.Status ?? string.Empty);
            if (currentStatus == ApplicationStatus.Stopped)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.INVALID_TRANSITION, "该档案已停保，不能重复执行户主死亡变更");
            }
            var transition = ApplicationStateMachine.ValidateTransition(currentStatus, ApplicationStatus.Stopped);
            if (transition.IsFailure && currentStatus is ApplicationStatus.Refused)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(
                    ErrorCodes.INVALID_TRANSITION,
                    transition.Message ?? $"不允许从 {currentStatus.GetDescription()} 转换到 已停保");
            }
            if (transition.IsFailure)
            {
                LogWarn($"户主死亡触发停保: 状态 {application.Status} → Stopped 不在严格状态机允许范围内，按业务规则（户主死亡）放行");
            }

            // 3. 加载全部家庭成员，定位死亡原户主成员。
            //    未显式传入时按 优先级：is_applicant=true / member_category='HouseholdHead' /
            //    身份证号或姓名与旧档案申请人一致（存量数据常存在 is_applicant 未置位的申请人成员）
            var membersResult = await _db.QueryAsync<FamilyMember>(
                "SELECT * FROM nc_biz_family_members WHERE application_id = $1 AND deleted_at IS NULL", ct, context.ApplicationId);
            if (membersResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(membersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    membersResult.Message ?? "家庭成员查询失败");
            }
            var allMembers = membersResult.Value ?? new List<FamilyMember>();
            var deceasedHead = context.DeceasedHeadMemberId > 0
                ? allMembers.FirstOrDefault(m => m.Id == context.DeceasedHeadMemberId)
                : allMembers.FirstOrDefault(m => m.IsApplicant
                    || string.Equals(m.MemberCategory, MemberCategoryConstants.HOUSEHOLD_HEAD, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrEmpty(oldHeadIdCard)
                        && string.Equals(m.IdCard, oldHeadIdCard, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrEmpty(oldHeadName)
                        && string.Equals(m.Name, oldHeadName, StringComparison.Ordinal)));
            if (deceasedHead == null)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.FAMILY_MEMBER_NOT_FOUND, "未找到死亡原户主成员记录");
            }

            // 4. 校验新户主成员（必须属于本申请、未删除、且不是死亡户主本人）
            var newHeadMember = allMembers.FirstOrDefault(m => m.Id == context.NewHeadMemberId);
            if (newHeadMember == null)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.FAMILY_MEMBER_NOT_FOUND, "新户主成员不存在或已删除");
            }
            if (newHeadMember.Id == deceasedHead.Id)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.VALIDATION_FAILED, "新户主不能是死亡人员本人");
            }
            if (string.IsNullOrWhiteSpace(newHeadMember.IdCard))
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.VALIDATION_FAILED,
                    $"新户主 {DataMasker.MaskName(newHeadMember.Name)} 缺少身份证号，无法建立新档案");
            }

            // 5. 身份证查重（新户主已是其他档案的申请人则明确报错，不绕过——多户挂靠同一身份会污染在保名单）
            var dupResult = await _applicationService.CheckIdCardExistsAsync(newHeadMember.IdCard ?? string.Empty, null, ct);
            if (dupResult.IsSuccess && dupResult.Value)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.DUPLICATE_ID_CARD,
                    $"新户主 {DataMasker.MaskName(newHeadMember.Name)} 的身份证号已存在于其他申请档案");
            }

            // 6. 计算新家庭人数（剔除死亡原户主，且不计赡养抚养扶养人 member_category='Support'）
            var newFamilySize = allMembers.Count(m => m.Id != deceasedHead.Id
                && !string.Equals(m.MemberCategory, MemberCategoryConstants.SUPPORT, StringComparison.OrdinalIgnoreCase));
            if (newFamilySize <= 0)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(ErrorCodes.VALIDATION_FAILED, "户主死亡后无共同生活成员，无法建立新档案");
            }

            // 6.5 现算幸存家庭收入（1B 变体：死亡只建链不判渐退；年值权威，剔除已故者成员级明细，
            //     土地有确权组走组汇总（LandShareCalculator），否则面积×单价；公式单点走 IncomeCalculationService）
            var econLoadResult = await _economicDetailService.LoadAllAsync(context.ApplicationId, ct);
            if (econLoadResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(econLoadResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    econLoadResult.Message ?? "经济明细加载失败");
            }
            var econ = econLoadResult.Value;

            var survivorSupportersResult = await _supporterService.GetByApplicationIdAsync(context.ApplicationId, ct);
            if (survivorSupportersResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(survivorSupportersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    survivorSupportersResult.Message ?? "赡养义务人加载失败");
            }
            var survivorSupporters = survivorSupportersResult.Value ?? new List<Supporter>();

            var deadMemberId = deceasedHead.Id;
            var laborSurv = econ.LaborIncomes.Where(x => x.MemberId != deadMemberId).ToList();
            var businessSurv = econ.BusinessIncomes.Where(x => x.MemberId != deadMemberId).ToList();
            var propertySurv = econ.PropertyIncomes.Where(x => x.MemberId != deadMemberId).ToList();
            var transferSurv = econ.TransferIncomes.Where(x => x.MemberId != deadMemberId).ToList();
            var rigidSurv = econ.RigidExpenditures.Where(x => x.MemberId != deadMemberId).ToList();

            var workIncome = _incomeCalculationService.CalculateLaborIncome(laborSurv);
            var businessIncome = _incomeCalculationService.CalculateBusinessNetIncome(businessSurv);
            var propertyIncome = propertySurv.Sum(p => p.Amount);
            var transferIncome = transferSurv.Sum(t => t.TotalAmount);
            var otherIncome = econ.OtherIncomes.Sum(o => o.Amount);
            var alimonyIncome = _incomeCalculationService.CalculateAlimonyAnnual(survivorSupporters);
            var subsidyIncome = _incomeCalculationService.CalculateSubsidyIncome(econ.Subsidies);
            var rigidExpenditure = _incomeCalculationService.CalculateRigidExpenditure(rigidSurv);

            decimal landIncome;
            decimal familyLandArea = application.FamilyLandArea;
            decimal selfFarmedArea = application.SelfFarmedLandArea;
            decimal subleasedArea = application.SubleasedLandArea;
            decimal contractedArea = application.ContractedLandArea;
            decimal totalConfirmedLandArea = application.TotalConfirmedLandArea;
            var confirmedPersonCount = application.ConfirmedPersonCount;
            if (econ.LandConfirmationGroups.Count > 0)
            {
                var survivorFamilyNames = new HashSet<string>(StringComparer.Ordinal);
                if (!string.IsNullOrWhiteSpace(newHeadMember.Name))
                    survivorFamilyNames.Add(newHeadMember.Name);
                foreach (var m in allMembers)
                {
                    if (m.Id == deadMemberId) continue;
                    if (string.Equals(m.MemberCategory, MemberCategoryConstants.SUPPORT, StringComparison.OrdinalIgnoreCase)) continue;
                    if (m.IsHouseholdHead || string.Equals(m.MemberCategory, MemberCategoryConstants.SHARED_LIVING, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(m.Name))
                            survivorFamilyNames.Add(m.Name);
                    }
                }

                totalConfirmedLandArea = (decimal)econ.LandConfirmationGroups.Sum(g => g.TotalArea);
                var totalLandShares = (decimal)econ.LandConfirmationGroups.Sum(g => g.TotalShares);
                confirmedPersonCount = econ.LandConfirmationGroups.Sum(g => g.PersonCount);

                decimal selfTotal = 0, subTotal = 0, conTotal = 0;
                foreach (var group in econ.LandConfirmationGroups)
                {
                    foreach (var record in group.Records)
                    {
                        switch (record.LandUsage)
                        {
                            case DictionaryConstants.LandUsage.SELF_FARM: selfTotal += record.LandArea; break;
                            case DictionaryConstants.LandUsage.SUBLEASE: subTotal += record.LandArea; break;
                            case DictionaryConstants.LandUsage.CONTRACT: conTotal += record.LandArea; break;
                        }
                    }
                }

                decimal familyIncome = 0;
                decimal familyLandShares = 0;
                foreach (var group in econ.LandConfirmationGroups)
                {
                    var gShares = (decimal)group.TotalShares;
                    if (gShares <= 0) continue;
                    var effectiveShares = LandShareCalculator.ComputeEffectiveShares(group);
                    decimal familyRatio = 0;
                    foreach (var kv in effectiveShares)
                    {
                        if (survivorFamilyNames.Contains(kv.Key))
                        {
                            familyLandShares += kv.Value;
                            familyRatio += kv.Value;
                        }
                    }
                    familyRatio /= gShares;
                    decimal groupIncome = 0;
                    foreach (var record in group.Records)
                        groupIncome += record.LandValue;
                    familyIncome += Math.Round(groupIncome * familyRatio, 2);
                }
                landIncome = Math.Round(familyIncome, 2);
                var familyRatioAll = totalLandShares > 0 ? familyLandShares / totalLandShares : 0m;
                familyLandArea = totalLandShares > 0
                    ? Math.Round(totalConfirmedLandArea / totalLandShares * familyLandShares, 2)
                    : 0m;
                selfFarmedArea = Math.Round(selfTotal * familyRatioAll, 2);
                subleasedArea = Math.Round(subTotal * familyRatioAll, 2);
                contractedArea = Math.Round(conTotal * familyRatioAll, 2);
            }
            else
            {
                landIncome = _incomeCalculationService.CalculateLandIncome(
                    selfFarmedArea, (double)IncomeTypeConstants.LandUnitPrice.SELF_FARM,
                    subleasedArea, (double)IncomeTypeConstants.LandUnitPrice.SUBLEASE,
                    contractedArea, (double)IncomeTypeConstants.LandUnitPrice.CONTRACT);
            }

            // 年值权威 → 月值/人均一次舍入；渐退期留给表单 Step5 正式判定（1B：死亡瞬间不判、不建）
            var recalculatedAnnualIncome = _incomeCalculationService.CalculateAnnualFamilyIncome(
                workIncome, businessIncome, propertyIncome, transferIncome, otherIncome,
                alimonyIncome, landIncome, subsidyIncome, rigidExpenditure);
            var recalculatedMonthlyIncome = _incomeCalculationService.MonthlyFromAnnual(recalculatedAnnualIncome);
            var newPerCapitaMonthly = _incomeCalculationService.PerCapitaMonthly(recalculatedAnnualIncome, newFamilySize);
            var newPerCapitaAnnual = _incomeCalculationService.CalculatePerCapitaAnnual(recalculatedAnnualIncome, newFamilySize);
            var inGraceBand = false;
            var grantAmount = 0m;

            // 7. 生成新申请编号
            var appNoResult = await _applicationService.GetNextApplicationNoAsync(ct);
            if (appNoResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(appNoResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    appNoResult.Message ?? "生成申请编号失败");
            }

            // 8. 复制主表 → 新档案（Draft、换户主、family_size 重算、original_application_id=旧ID、
            //    清空分类/渐退/停保/银行卡字段——新档案重走审批，旧账号不能沿用给新户主）
            var newAppInsertSql = @"INSERT INTO nc_biz_applications (
                application_no, applicant_name, applicant_id_card, applicant_phone,
                gender, ethnicity, marital_status, hukou_type, education_level, political_status,
                hukou_address, disability_card_no,
                province, city, district, town, community, address,
                physical_condition, disease_name, secondary_disease_name,
                disability_type, disability_level, health_status,
                employment_status, work_unit, income_source,
                family_size, confirmed_family_size,
                is_single_rescue, support_mode,
                application_reason, application_reason_detail, caregiver_type, destitute_support_type, support_institution_id,
                work_income_total, business_income_total, property_income_total,
                transfer_income_total, other_income_total, total_family_income, per_capita_income, rigid_expenditure, alimony_income,
                total_annual_income, per_capita_annual_income,
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
            SELECT $1, $2, $3, $4,
                $5, $6, $7, $8, $9, $10,
                hukou_address, disability_card_no,
                province, city, district, town, community, address,
                physical_condition, disease_name, secondary_disease_name,
                disability_type, disability_level, health_status,
                employment_status, work_unit, income_source,
                $11, $12,
                is_single_rescue, support_mode,
                application_reason, application_reason_detail, caregiver_type, destitute_support_type, support_institution_id,
                $22, $23, $24,
                $25, $26, $27, $19, $28, $29,
                $20, $21,
                false, NULL,
                NULL, 0,
                $18, 0, 0, $18,
                $38, $39,
                'HouseholdDeath', 'nc_biz_applications', $13,
                'HouseholdDeath',
                $14,
                first_approved_at,
                NOW(), NOW(), $15, $16,
                city_id, county_id, town_id, village_id,
                hukou_city_id, hukou_county_id, hukou_town_id, hukou_village_id,
                $30, $31, $32, $33,
                $34, $35, $36, $37
            FROM nc_biz_applications WHERE id = $17
            RETURNING id;";

            var newAppResult = await _db.ExecuteScalarAsync(newAppInsertSql, ct,
                appNoResult.Value,
                newHeadMember.Name, newHeadMember.IdCard, newHeadMember.Phone,
                newHeadMember.Gender, newHeadMember.Ethnicity, newHeadMember.MaritalStatus, newHeadMember.HukouType,
                newHeadMember.EducationLevel, newHeadMember.PoliticalStatus,
                newFamilySize, newFamilySize,
                context.ApplicationId,
                context.ApplicationId,
                context.OperatorName, context.OperatorName,
                context.ApplicationId,
                grantAmount,
                newPerCapitaMonthly,
                recalculatedAnnualIncome,
                newPerCapitaAnnual,
                workIncome, businessIncome, propertyIncome,
                transferIncome, otherIncome, recalculatedMonthlyIncome,
                rigidExpenditure, alimonyIncome,
                familyLandArea, selfFarmedArea, subleasedArea, contractedArea,
                landIncome, subsidyIncome, totalConfirmedLandArea, confirmedPersonCount,
                ApplicationStatusCodes.DRAFT, WorkflowSteps.ENTRY_START);
            if (newAppResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(newAppResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    newAppResult.Message ?? "创建新档案失败");
            }
            var newApplicationId = newAppResult.Value;

            // 9. 复制家庭成员（剔除死亡原户主；新户主 is_applicant=true、关系='本人/户主'）
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
            SELECT $1, name, id_card,
                CASE WHEN id = $2 THEN true ELSE is_applicant END,
                gender, birth_date, age, ethnicity, phone,
                hukou_type, hukou_address, marital_status, education_level, political_status,
                CASE WHEN id = $2 THEN '本人/户主' ELSE relationship_to_head END,
                health_status, work_capacity, is_disabled,
                disability_type, disability_level, is_severe_disability, employment_status, annual_income,
                member_category, disability_certificate_no, disease_category, disease_name,
                home_province, home_city, home_district, home_town, home_address,
                hukou_province, hukou_city, hukou_district, hukou_town, home_village, secondary_disease,
                person_type, annual_support_fee, is_support_ability, monthly_income_capacity,
                family_size, work_unit, monthly_support_fee,
                support_months, main_income_source, is_severe_disease, is_labor_exempt, NOW(), NOW(), NULL
            FROM nc_biz_family_members
            WHERE application_id = $3 AND id != $4 AND deleted_at IS NULL;";
            var memberCopyResult = await _db.ExecuteNonQueryAsync(memberCopySql, ct,
                newApplicationId, context.NewHeadMemberId, context.ApplicationId, deceasedHead.Id);
            if (memberCopyResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(memberCopyResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    memberCopyResult.Message ?? "复制家庭成员失败");
            }

            // 构建 旧成员ID → 新成员ID 映射（按身份证号匹配，用于照料人被照料成员与经济明细 member_id 的指向迁移）
            var newMembersResult = await _db.QueryAsync<FamilyMember>(
                "SELECT * FROM nc_biz_family_members WHERE application_id = $1 AND deleted_at IS NULL", ct, newApplicationId);
            if (newMembersResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(newMembersResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    newMembersResult.Message ?? "新档案成员查询失败");
            }
            var newMembers = newMembersResult.Value ?? new List<FamilyMember>();
            var memberIdMap = new Dictionary<long, long>();
            foreach (var oldMember in allMembers.Where(m => m.Id != deceasedHead.Id))
            {
                var match = newMembers.FirstOrDefault(m => !string.IsNullOrEmpty(m.IdCard)
                    && string.Equals(m.IdCard, oldMember.IdCard, StringComparison.OrdinalIgnoreCase))
                    ?? newMembers.FirstOrDefault(m => string.Equals(m.Name, oldMember.Name, StringComparison.Ordinal));
                if (match != null)
                    memberIdMap[oldMember.Id] = match.Id;
            }

            // 10. 复制经济明细（复用 6.5 已加载的 econ → 重映射 member_id → SaveAll；死亡原户主的明细行 member_id 置 0，文本字段保留）
            long MapMemberId(long oldId) => oldId > 0 && memberIdMap.TryGetValue(oldId, out var nid) ? nid : 0;
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
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(econSaveResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    econSaveResult.Message ?? "经济明细复制失败");
            }

            // 11. 复制入户调查（若存在）
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
            var surveyCopyResult = await _db.ExecuteNonQueryAsync(surveyCopySql, ct, newApplicationId, context.ApplicationId);
            if (surveyCopyResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(surveyCopyResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    surveyCopyResult.Message ?? "复制入户调查失败");
            }

            // 12. 复制照料人（cared_member_id 迁移到新成员ID，无法映射置 0）
            var caregiversResult = await _db.QueryAsync<Caregiver>(
                "SELECT * FROM nc_biz_caregivers WHERE application_id = $1 AND deleted_at IS NULL", ct, context.ApplicationId);
            if (caregiversResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(caregiversResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    caregiversResult.Message ?? "照料人查询失败");
            }
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
                    {
                        await tx.RollbackAsync(ct);
                        return Result.Failure<ChangeResult>(insertCaregiverResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                            insertCaregiverResult.Message ?? "复制照料人失败");
                    }
                }
            }

            // 13. 写死亡记录（is_household_head=true，回填新档案ID）
            var deathSql = @"INSERT INTO nc_biz_death_records
                (application_id, member_id, member_name, member_id_card, relationship_to_head,
                 death_date, death_reason, death_certificate_no,
                 is_household_head, new_head_id_card, new_head_name, new_application_id,
                 operator_name, created_by, created_at)
                VALUES ($1,$2,$3,$4,$5,$6,$7,$8,true,$9,$10,$11,$12,$12,NOW());";
            var deathInsertResult = await _db.ExecuteNonQueryAsync(deathSql, ct,
                context.ApplicationId, deceasedHead.Id, deceasedHead.Name, deceasedHead.IdCard,
                string.IsNullOrEmpty(deceasedHead.RelationshipToHead) ? "本人/户主" : deceasedHead.RelationshipToHead,
                context.DeathDate, context.DeathReason, context.DeathCertificateNo,
                context.NewHeadIdCard, context.NewHeadName, newApplicationId,
                context.OperatorName);
            if (deathInsertResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(deathInsertResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    deathInsertResult.Message ?? "创建死亡记录失败");
            }

            // 14. 停止旧档案（户主死亡）——状态写入统一走 ApplicationStatusService（状态机校验 + 审计留痕）
            var stopResult = await _statusService.StopAsync(
                context.ApplicationId, ChangeReasonTypeConstants.HeadDeceased, context.DeathDate,
                context.OperatorName, allowSubmittedOverride: true, ct: ct);
            if (stopResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(stopResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    stopResult.Message ?? "停止旧档案失败");
            }

            // 14.1 旧档案停保，结清其渐退期（渐退期仅对在保的低收入降档户有意义）
            var graceClearResult = await _gracePeriodService.ClearAsync(context.ApplicationId, ct);
            if (graceClearResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(graceClearResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    graceClearResult.Message ?? "结清旧档案渐退期失败");
            }

            // 14.2（1B 变体）死亡瞬间不判定/不预建渐退期——渐退资格在表单 Step5 正式分类判定后由确认页决定；
            //     新档此刻 triggered_grace_period=false，收入列已是 6.5 现算的幸存家庭口径。

            // 15. 创建变更记录（挂在旧档上，After 快照含新档ID）
            //     change_category=MemberChange：户主死亡必然人员变动，供"增减员调整表"按类别取数
            //     change_reason 可解析格式：与成员死亡同构，冒号后 "姓名(身份证)" 供 ADJ 减员槽解析
            var changeContext = new ChangeContext
            {
                ApplicationId = context.ApplicationId,
                ChangeType = DictionaryConstants.ChangeType.HOUSEHOLD_DEATH,
                ChangeCategory = DictionaryConstants.ChangeCategory.MEMBER_CHANGE,
                ChangeReason = $"户主死亡: {deceasedHead.Name}({oldHeadIdCard}) → 新户主 {context.NewHeadName}",
                ChangeDate = context.DeathDate,
                OperatorName = context.OperatorName
            };
            var beforeJson = JsonSerializer.Serialize(new
            {
                HeadName = oldHeadName,
                HeadIdCard = oldHeadIdCard,
                FamilySize = oldFamilySize,
                Classification = oldClassification,
                GuaranteeAmount = oldGuaranteeAmount
            });
            var afterJson = JsonSerializer.Serialize(new
            {
                NewHeadName = context.NewHeadName,
                NewApplicationId = newApplicationId,
                FamilySize = newFamilySize,
                DeathDate = context.DeathDate,
                DeathReason = context.DeathReason,
                DeceasedHeadName = deceasedHead.Name
            });
            var changeIdResult = await CreateChangeAsync(changeContext, beforeJson, afterJson, ct);
            if (changeIdResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(changeIdResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    changeIdResult.Message ?? "创建变更记录失败");
            }
            var changeId = changeIdResult.Value;

            // 填充 nc_biz_change_records 的结构化对比字段（户主死亡审计对应）：
            // 旧值一律取旧档保存值（与经济复核 L1073 同源），新值取判定落库值（与新档 INSERT $19 同源）
            var updateChangeSql = @"UPDATE nc_biz_change_records SET
                change_reason_type = $1,
                old_classification = $2, new_classification = $3,
                old_per_capita_income = $4, new_per_capita_income = $5,
                old_guarantee_amount = $6, new_guarantee_amount = $7,
                triggered_stop = $8,
                triggered_grace_period = $9,
                new_application_id = $10,
                original_application_id = $11,
                changed_at = NOW()
                WHERE id = $12;";
            var updateChangeResult = await _db.ExecuteNonQueryAsync(updateChangeSql, ct,
                ChangeReasonTypeConstants.HeadDeceased,
                oldClassification, (object?)null,
                application.PerCapitaIncome, newPerCapitaMonthly,
                oldGuaranteeAmount, grantAmount,
                true, inGraceBand,
                newApplicationId, context.ApplicationId,
                changeId);
            if (updateChangeResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<ChangeResult>(updateChangeResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    updateChangeResult.Message ?? "更新变更记录对比字段失败");
            }

            await tx.CommitAsync(ct);

            LogInfo($"户主死亡变更完成: 旧档案={context.ApplicationId} → 新档案={newApplicationId}, 新家庭人数={newFamilySize}, 幸存家庭年收入={recalculatedAnnualIncome:F2}, 渐退期=Step5待判定");

            return Result.Success(new ChangeResult
            {
                ChangeId = changeId,
                IsSuccess = true,
                OldClassification = oldClassification,
                OldGuaranteeAmount = oldGuaranteeAmount,
                TriggeredStop = true,
                TriggeredGracePeriod = inGraceBand,
                NewApplicationId = newApplicationId
            });
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "户主死亡变更");
            return Result.FromException<ChangeResult>(ex);
        }
    }

    /// <summary>
    /// 获取变更历史
    /// </summary>
    public async Task<Result<List<ChangeRecord>>> GetChangeHistoryAsync(long applicationId, CancellationToken ct = default)
    {
        // nc_biz_change_records 无 created_at 列（只有 changed_at）：
        // 查询改用 changed_at，避免 42703 使环境事务进入 aborted 状态而引发后续 25P02
        var sql = @"SELECT id, application_id, change_no, change_type, change_reason, 
                    change_date, operator_name, changed_at
                    FROM nc_biz_change_records 
                    WHERE application_id = $1 
                    ORDER BY changed_at DESC LIMIT 200;";

        var result = await _db.QueryAsync<ChangeRecord>(sql, ct, applicationId);
        return result.IsSuccess
            ? Result.Success(result.Value ?? new List<ChangeRecord>())
            : Result.Failure<List<ChangeRecord>>(result.ErrorCode!, result.Message!);
    }

    public async Task<Result<TriggeredStopChangeRecord?>> GetLatestTriggeredStopRecordAsync(long applicationId, IReadOnlyCollection<string> newClassifications, CancellationToken ct = default)
    {
        var sql = @"SELECT old_classification, new_classification FROM nc_biz_change_records
                          WHERE (application_id = $1 OR new_application_id = $1)
                            AND triggered_stop = true AND change_type = 'CategoryStop'
                            AND new_classification = ANY($2::text[])
                            AND deleted_at IS NULL
                          ORDER BY changed_at DESC LIMIT 1";
        var result = await _db.QuerySingleAsync<TriggeredStopChangeRecord>(sql, ct, applicationId, newClassifications.ToArray());
        if (result.IsFailure)
            return Result.Failure<TriggeredStopChangeRecord?>(result.ErrorCode!, result.Message!);
        return Result.Success<TriggeredStopChangeRecord?>(result.Value);
    }

    public async Task<Result<bool>> HasTriggeredCategoryStopAsync(long applicationId, IReadOnlyCollection<string> stopCategoryCodes, CancellationToken ct = default)
    {
        var sql = @"SELECT EXISTS(SELECT 1 FROM nc_biz_change_records
                                  WHERE (application_id = $1 OR new_application_id = $1)
                                    AND triggered_stop = true AND change_type = 'CategoryStop'
                                    AND new_classification = ANY($2::text[])
                                    AND deleted_at IS NULL)";
        var result = await _db.ExecuteScalarAsync<bool>(sql, ct, applicationId, stopCategoryCodes.ToArray());
        return result.IsSuccess
            ? Result.Success(result.Value)
            : Result.Failure<bool>(result.ErrorCode!, result.Message!);
    }

    /// <summary>
    /// 查询家庭成员类变更记录（增减员调整表字段装配用，按变更日期倒序）。
    /// 规范：停旧建新/重建链场景下，变更可能挂在旧档（application_id）而新档经 new_application_id 关联；
    /// 存量记录 change_category 可能为空，用 change_type 兜底，保证人员变动类流程都能取到数。
    /// </summary>
    public async Task<Result<List<MemberChangeRecord>>> GetMemberChangeRecordsAsync(long applicationId, int limit, CancellationToken ct = default)
    {
        var sql = @"SELECT id, application_id, new_application_id, change_type, change_reason, change_date,
                           old_classification, new_classification,
                           old_guarantee_amount, new_guarantee_amount
                    FROM nc_biz_change_records
                    WHERE (application_id = $1 OR new_application_id = $1)
                      AND deleted_at IS NULL
                      AND (
                        change_category = 'MemberChange'
                        OR change_type IN ('HouseholdDeath','MemberDeath','MemberRemove','MemberAdd','HouseholdHeadChange')
                      )
                    ORDER BY change_date DESC, id DESC LIMIT $2";
        var result = await _db.QueryAsync<MemberChangeRecord>(sql, ct, applicationId, limit);
        return result.IsSuccess
            ? Result.Success(result.Value ?? new List<MemberChangeRecord>())
            : Result.Failure<List<MemberChangeRecord>>(result.ErrorCode!, result.Message!);
    }

    /// <summary>nc_biz_change_details 行（增减员逐人明细读取用）</summary>
    private sealed class MemberAdjustDetailRow
    {
        public long ChangeId { get; set; }
        public string? FieldName { get; set; }
        public string? FieldLabel { get; set; }
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
    }

    /// <summary>nc_biz_family_members 行（逐人明细补齐展示字段用，含软删除历史行）</summary>
    private sealed class MemberAdjustProfileRow
    {
        public long ApplicationId { get; set; }
        public string? IdCard { get; set; }
        public string? Name { get; set; }
        public string? Gender { get; set; }
        public string? RelationshipToHead { get; set; }
        public string? HealthStatus { get; set; }
        public string? WorkUnit { get; set; }
        public decimal AnnualIncome { get; set; }
        public DateTime? DeletedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }

    public async Task<Result<List<MemberAdjustEntry>>> GetMemberAdjustEntriesAsync(long applicationId, int limit, CancellationToken ct = default)
    {
        var recordsRes = await GetMemberChangeRecordsAsync(applicationId, limit, ct);
        if (recordsRes.IsFailure)
            return Result.Failure<List<MemberAdjustEntry>>(recordsRes.ErrorCode!, recordsRes.Message!);

        var records = recordsRes.Value ?? new List<MemberChangeRecord>();
        if (records.Count == 0)
            return Result.Success(new List<MemberAdjustEntry>());

        var entries = new List<MemberAdjustEntry>();

        // ① 成员增减流程的逐人明细（唯一权威源，含逐人原因）
        var changeIds = records.Select(r => r.Id).Distinct().ToArray();
        var detailSql = @"SELECT change_id, field_name, field_label, old_value, new_value
                          FROM nc_biz_change_details
                          WHERE change_id = ANY($1::bigint[])
                            AND field_name IN ('MemberAdd','MemberRemove')
                          ORDER BY id";
        var detailRes = await _db.QueryAsync<MemberAdjustDetailRow>(detailSql, ct, new object[] { changeIds });
        if (detailRes.IsFailure)
            return Result.Failure<List<MemberAdjustEntry>>(detailRes.ErrorCode!, detailRes.Message!);

        var detailsByChange = (detailRes.Value ?? new List<MemberAdjustDetailRow>())
            .GroupBy(d => d.ChangeId)
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var rec in records)
        {
            if (detailsByChange.TryGetValue(rec.Id, out var rows))
            {
                foreach (var row in rows)
                {
                    if (string.IsNullOrWhiteSpace(row.FieldName)) continue;
                    ParseMemberDetailRow(row, rec, entries);
                }
                continue;
            }

            // ② 成员死亡/户主死亡/旧版减员流程不写明细：按 change_reason "动词: 姓名(身份证)" 解析兜底
            var recType = rec.ChangeType ?? string.Empty;
            if (recType == DictionaryConstants.ChangeType.MEMBER_DEATH ||
                recType == DictionaryConstants.ChangeType.HOUSEHOLD_DEATH ||
                recType == DictionaryConstants.ChangeType.MEMBER_REMOVE)
            {
                if (!TryParseMemberFromReason(rec.ChangeReason, out var dName, out var dIdCard)) continue;
                entries.Add(new MemberAdjustEntry
                {
                    Direction = DictionaryConstants.ChangeType.MEMBER_REMOVE,
                    Name = dName,
                    IdCard = dIdCard,
                    ChangeDate = rec.ChangeDate,
                    ChangeType = recType
                });
            }
        }

        // ③ 回查成员表补齐展示字段（本档案行优先 → 在册行优先 → 其次最新历史行）；
        //    历史原因无证件号的减员行按姓名回填，并补回证件号
        var appIds = records
            .SelectMany(r => new[] { r.ApplicationId, r.NewApplicationId })
            .Append(applicationId)
            .Where(id => id > 0)
            .Distinct()
            .ToArray();
        var idCards = entries
            .Select(e => e.IdCard?.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var names = entries
            .Select(e => e.Name?.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (appIds.Length > 0 && (idCards.Length > 0 || names.Length > 0))
        {
            var profileSql = @"SELECT application_id, id_card, name, gender, relationship_to_head,
                                      health_status, work_unit, annual_income, deleted_at, updated_at
                               FROM nc_biz_family_members
                               WHERE application_id = ANY($1::bigint[])
                                 AND (id_card = ANY($2::text[]) OR name = ANY($3::text[]))";
            var profileRes = await _db.QueryAsync<MemberAdjustProfileRow>(profileSql, ct,
                new object[] { appIds, idCards, names });
            if (profileRes.IsFailure)
                return Result.Failure<List<MemberAdjustEntry>>(profileRes.ErrorCode!, profileRes.Message!);

            var rows = profileRes.Value ?? new List<MemberAdjustProfileRow>();
            static IEnumerable<MemberAdjustProfileRow> Prefer(IEnumerable<MemberAdjustProfileRow> group, long currentAppId) =>
                group.OrderByDescending(p => p.ApplicationId == currentAppId)
                     .ThenByDescending(p => p.DeletedAt == null)
                     .ThenByDescending(p => p.UpdatedAt ?? DateTime.MinValue);

            var byIdCard = rows
                .Where(p => !string.IsNullOrWhiteSpace(p.IdCard))
                .GroupBy(p => (p.IdCard ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => Prefer(g, applicationId).First(), StringComparer.OrdinalIgnoreCase);
            var byName = rows
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .GroupBy(p => (p.Name ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => Prefer(g, applicationId).First(), StringComparer.OrdinalIgnoreCase);

            foreach (var e in entries)
            {
                var idKey = e.IdCard?.Trim();
                var nameKey = e.Name?.Trim();
                MemberAdjustProfileRow? p = null;
                if (!string.IsNullOrEmpty(idKey) && byIdCard.TryGetValue(idKey, out var byId))
                    p = byId;
                if (p == null && !string.IsNullOrEmpty(nameKey) && byName.TryGetValue(nameKey, out var byNameHit))
                    p = byNameHit;
                if (p == null) continue;

                e.Gender = p.Gender;
                e.HealthStatus = p.HealthStatus;
                e.WorkUnit = p.WorkUnit;
                e.AnnualIncome = p.AnnualIncome;
                // 关系/姓名以成员表为准（明细登记的是变更当刻的值）
                if (!string.IsNullOrWhiteSpace(p.RelationshipToHead)) e.RelationshipToHead = p.RelationshipToHead!;
                if (!string.IsNullOrWhiteSpace(p.Name)) e.Name = p.Name!;
                if (string.IsNullOrWhiteSpace(e.IdCard) && !string.IsNullOrWhiteSpace(p.IdCard)) e.IdCard = p.IdCard!;
            }
        }

        // ④ 按证件号（无证件号按姓名）去重：同人多次变更只保留最近一次，保持变更日期倒序
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var distinct = new List<MemberAdjustEntry>(entries.Count);
        foreach (var e in entries)
        {
            var key = (!string.IsNullOrWhiteSpace(e.IdCard) ? e.IdCard : e.Name)?.Trim() ?? string.Empty;
            if (key.Length > 0 && !seen.Add(key)) continue;
            distinct.Add(e);
        }

        return Result.Success(distinct);
    }

    /// <summary>
    /// 解析 nc_biz_change_details 一行：
    /// field_label=姓名；old_value="{姓名}({身份证}) {成员分类} {与户主关系}"；new_value="{原因}|{yyyy-MM-dd}|{备注}"
    /// </summary>
    private static void ParseMemberDetailRow(MemberAdjustDetailRow row, MemberChangeRecord rec, List<MemberAdjustEntry> entries)
    {
        var direction = string.Equals(row.FieldName, DictionaryConstants.ChangeType.MEMBER_ADD, StringComparison.OrdinalIgnoreCase)
            ? DictionaryConstants.ChangeType.MEMBER_ADD
            : DictionaryConstants.ChangeType.MEMBER_REMOVE;

        var name = row.FieldLabel?.Trim() ?? string.Empty;
        var idCard = string.Empty;
        var relation = string.Empty;
        var category = string.Empty;

        var oldValue = row.OldValue?.Trim() ?? string.Empty;
        if (oldValue.Length > 0)
        {
            var match = global::System.Text.RegularExpressions.Regex.Match(
                oldValue, @"^(?<name>.*?)\((?<id>[^()]*)\)(?<rest>.*)$");
            if (match.Success)
            {
                if (string.IsNullOrWhiteSpace(name)) name = match.Groups["name"].Value.Trim();
                idCard = match.Groups["id"].Value.Trim();

                var tokens = match.Groups["rest"].Value
                    .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                var start = 0;
                if (tokens.Length > 0 && IsMemberCategory(tokens[0])) { category = tokens[0]; start = 1; }
                if (tokens.Length > start) relation = string.Join(' ', tokens.Skip(start));
            }
        }

        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(idCard)) return;

        DateTime? eventDate = null;
        var reasonName = string.Empty;
        var newValueParts = (row.NewValue ?? string.Empty).Split('|');
        if (newValueParts.Length > 0) reasonName = newValueParts[0].Trim();
        if (newValueParts.Length > 1 &&
            DateTime.TryParseExact(newValueParts[1].Trim(), "yyyy-MM-dd",
                global::System.Globalization.CultureInfo.InvariantCulture,
                global::System.Globalization.DateTimeStyles.None, out var parsedDate))
        {
            eventDate = parsedDate;
        }

        entries.Add(new MemberAdjustEntry
        {
            Direction = direction,
            Name = name,
            IdCard = idCard,
            RelationshipToHead = relation,
            MemberCategory = category,
            ReasonName = reasonName,
            EventDate = eventDate,
            ChangeDate = rec.ChangeDate,
            ChangeType = rec.ChangeType
        });
    }

    /// <summary>成员分类标识（nc_biz_family_members.member_category 的取值）</summary>
    private static bool IsMemberCategory(string token) =>
        token is MemberCategoryConstants.SHARED_LIVING or MemberCategoryConstants.SUPPORT or MemberCategoryConstants.HOUSEHOLD_HEAD;

    /// <summary>
    /// 从变更原因解析被减员人：
    /// "成员死亡: 张三(身份证)" / "户主死亡: 张三(身份证) → 新户主 李四"（有证件号）；
    /// "户主死亡变更: 贺传波 → 新户主 李建英"（历史原因无证件号 → 只取姓名，IdCard 留空由成员表回填）。
    /// </summary>
    private static bool TryParseMemberFromReason(string? reason, out string name, out string idCard)
    {
        name = string.Empty;
        idCard = string.Empty;
        if (string.IsNullOrWhiteSpace(reason)) return false;

        var colonIdx = reason.IndexOf(':');
        var fullColonIdx = reason.IndexOf('：');
        if (colonIdx < 0) colonIdx = fullColonIdx;
        else if (fullColonIdx >= 0) colonIdx = Math.Min(colonIdx, fullColonIdx);
        if (colonIdx < 0 || colonIdx >= reason.Length - 1) return false;

        var rest = reason.Substring(colonIdx + 1).Trim();
        // 冒号后可能接长句说明，截断到箭头/换行/标点为止
        var stopIdx = rest.IndexOfAny(new[] { '→', '\n', '\r', '，', '。', '；', ',' });
        if (stopIdx > 0) rest = rest[..stopIdx].Trim();

        var parenStart = rest.IndexOf('(');
        var parenEnd = rest.IndexOf(')');
        if (parenStart > 0 && parenEnd > parenStart)
        {
            name = rest[..parenStart].Trim();
            idCard = rest[(parenStart + 1)..parenEnd].Trim();
            return name.Length > 0 && idCard.Length > 0;
        }

        name = rest.Trim();
        return IsPersonName(name);
    }

    /// <summary>姓名形态校验（无证件号兜底时用）：2~8 字中文，不含数字与标点</summary>
    private static bool IsPersonName(string name)
    {
        if (name.Length is < 2 or > 8) return false;
        foreach (var c in name)
        {
            if (c < 0x4E00 || c > 0x9FA5) return false;
        }
        return true;
    }

    /// <summary>
    /// 查询最近一条与渐退退出相关的变更（经济复核/户主死亡/触发停保）。
    /// 档案渐退审批表「退出渐退期情况」分型拼句取数；无匹配则 Value=null（非失败）。
    /// </summary>
    public async Task<Result<GraceExitChangeRecord?>> GetLatestGraceExitChangeAsync(long applicationId, CancellationToken ct = default)
    {
        var sql = @"SELECT change_date, change_reason_type, change_type, change_reason,
                           triggered_stop, new_classification, old_classification
                    FROM nc_biz_change_records
                    WHERE (application_id = $1 OR new_application_id = $1)
                      AND deleted_at IS NULL
                      AND (
                        change_reason_type IN ('经济复核','户主死亡')
                        OR change_type IN ('FundChange','HouseholdDeath','CategoryStop')
                        OR triggered_stop = true
                      )
                    ORDER BY change_date DESC, id DESC LIMIT 1";
        var result = await _db.QuerySingleAsync<GraceExitChangeRecord>(sql, ct, applicationId);
        if (result.IsFailure)
            return Result.Failure<GraceExitChangeRecord?>(result.ErrorCode!, result.Message!);
        return Result.Success<GraceExitChangeRecord?>(result.Value);
    }

    /// <summary>
    /// 查询最近一条复核/变更记录（定期复核审批表复核组字段取数；经济复核或死亡/成员变更等完整流程均覆盖；无匹配则 Value=null 非失败）。
    /// </summary>
    public async Task<Result<ReviewChangeRecord?>> GetLatestReviewChangeRecordAsync(long applicationId, CancellationToken ct = default)
    {
        var sql = @"SELECT id, change_date, change_reason, change_reason_type, change_type,
                           old_classification, new_classification,
                           old_guarantee_amount, new_guarantee_amount,
                           old_per_capita_income
                    FROM nc_biz_change_records
                    WHERE (application_id = $1 OR new_application_id = $1)
                      AND deleted_at IS NULL
                      AND (
                        change_reason_type IN ('经济复核','户主死亡','成员变更')
                        OR change_type IN ('FundChange','CategoryAdd','CategoryStop','HouseholdDeath','MemberDeath','MemberRemove')
                      )
                    ORDER BY change_date DESC, id DESC LIMIT 1";
        var result = await _db.QuerySingleAsync<ReviewChangeRecord>(sql, ct, applicationId);
        if (result.IsFailure)
            return Result.Failure<ReviewChangeRecord?>(result.ErrorCode!, result.Message!);
        return Result.Success<ReviewChangeRecord?>(result.Value);
    }

    /// <summary>
    /// 查询指定变更记录的 Before/After 快照 JSON（无匹配子查询返回 NULL 行，两键均空时 Value=null 非失败）。
    /// </summary>
    public async Task<Result<ChangeSnapshotPair?>> GetChangeSnapshotsAsync(long changeId, CancellationToken ct = default)
    {
        var sql = @"SELECT (SELECT snapshot_data::text FROM nc_biz_change_snapshots
                            WHERE change_id = $1 AND snapshot_type = 'Before') AS before_json,
                           (SELECT snapshot_data::text FROM nc_biz_change_snapshots
                            WHERE change_id = $1 AND snapshot_type = 'After') AS after_json";
        var result = await _db.QuerySingleAsync<ChangeSnapshotPair>(sql, ct, changeId);
        if (result.IsFailure)
            return Result.Failure<ChangeSnapshotPair?>(result.ErrorCode!, result.Message!);
        var pair = result.Value;
        if (pair != null && string.IsNullOrEmpty(pair.BeforeJson) && string.IsNullOrEmpty(pair.AfterJson))
            return Result.Success<ChangeSnapshotPair?>(null);
        return Result.Success<ChangeSnapshotPair?>(pair);
    }

    /// <inheritdoc/>
    public async Task<Result<ChangeSnapshotPair?>> GetLinkedChangeSnapshotsAsync(long applicationId, CancellationToken ct = default)
    {
        // 仅取含快照的关联记录（CategoryAdd/CategoryStop 跨类行本身无快照，会被 EXISTS 排除），
        // 无关联记录时 0 行 → QuerySingleAsync 返回 Success(null)
        var sql = @"SELECT (SELECT s.snapshot_data::text FROM nc_biz_change_snapshots s
                            WHERE s.change_id = c.id AND s.snapshot_type = 'Before') AS before_json,
                           (SELECT s.snapshot_data::text FROM nc_biz_change_snapshots s
                            WHERE s.change_id = c.id AND s.snapshot_type = 'After') AS after_json
                    FROM nc_biz_change_records c
                    WHERE (c.application_id = $1 OR c.new_application_id = $1)
                      AND c.deleted_at IS NULL
                      AND EXISTS (SELECT 1 FROM nc_biz_change_snapshots s WHERE s.change_id = c.id)
                    ORDER BY c.change_date DESC, c.id DESC
                    LIMIT 1";
        var result = await _db.QuerySingleAsync<ChangeSnapshotPair>(sql, ct, applicationId);
        if (result.IsFailure)
            return Result.Failure<ChangeSnapshotPair?>(result.ErrorCode!, result.Message!);
        var pair = result.Value;
        if (pair != null && string.IsNullOrEmpty(pair.BeforeJson) && string.IsNullOrEmpty(pair.AfterJson))
            return Result.Success<ChangeSnapshotPair?>(null);
        return Result.Success<ChangeSnapshotPair?>(pair);
    }

    /// <summary>
    /// 查询最近一条含新旧人均收入的变更记录（渐退期审批表「变动情况说明」经济复核场景取数）。
    /// 经济复核按同档更新，旧人均收入仅存于变更记录；无匹配则 Value=null（非失败）。
    /// </summary>
    public async Task<Result<IncomeComparisonRecord?>> GetLatestIncomeComparisonAsync(long applicationId, CancellationToken ct = default)
    {
        var sql = @"SELECT old_per_capita_income, new_per_capita_income, change_date, change_type
                    FROM nc_biz_change_records
                    WHERE (application_id = $1 OR new_application_id = $1)
                      AND deleted_at IS NULL
                      AND old_per_capita_income IS NOT NULL
                      AND new_per_capita_income IS NOT NULL
                    ORDER BY change_date DESC, id DESC LIMIT 1";
        var result = await _db.QuerySingleAsync<IncomeComparisonRecord>(sql, ct, applicationId);
        if (result.IsFailure)
            return Result.Failure<IncomeComparisonRecord?>(result.ErrorCode!, result.Message!);
        return Result.Success<IncomeComparisonRecord?>(result.Value);
    }

    /// <summary>
    /// 查询最近一条挂在旧档上的、Before 快照含 Components 的变更（渐退审批表分项对比取复核前旧值用）。
    /// 仅经济复核入口写入 Components；死亡/户主变更链无此键 → Value=null（消费方回退旧档行）。
    /// </summary>
    public async Task<Result<BeforeSnapshotOldValues?>> GetLatestBeforeSnapshotAsync(long originalApplicationId, CancellationToken ct = default)
    {
        var sql = @"SELECT s.snapshot_data::text AS snapshot_json
                    FROM nc_biz_change_snapshots s
                    JOIN nc_biz_change_records c ON c.id = s.change_id
                    WHERE c.application_id = $1
                      AND c.deleted_at IS NULL
                      AND s.snapshot_type = 'Before'
                      AND jsonb_typeof(s.snapshot_data -> 'Components') = 'object'
                    ORDER BY c.id DESC LIMIT 1";
        var result = await _db.QuerySingleAsync<SnapshotJsonRow>(sql, ct, originalApplicationId);
        if (result.IsFailure)
            return Result.Failure<BeforeSnapshotOldValues?>(result.ErrorCode!, result.Message!);

        var json = result.Value?.SnapshotJson;
        if (string.IsNullOrEmpty(json))
            return Result.Success<BeforeSnapshotOldValues?>(null);

        try
        {
            // 快照含 Classification/TotalIncome 等额外键，未映射成员默认忽略
            return Result.Success<BeforeSnapshotOldValues?>(JsonSerializer.Deserialize<BeforeSnapshotOldValues>(json));
        }
        catch (JsonException ex)
        {
            LogWarn($"解析 Before 快照旧值失败: OriginalApplicationId={originalApplicationId}, {ex.Message}");
            return Result.Success<BeforeSnapshotOldValues?>(null);
        }
    }

    private sealed class SnapshotJsonRow
    {
        public string? SnapshotJson { get; set; }
    }
}
