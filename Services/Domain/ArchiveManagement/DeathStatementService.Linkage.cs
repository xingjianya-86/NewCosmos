using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.DeathStatement;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.UserManagement;


namespace NewCosmos.Services.Domain.ArchiveManagement;

/// <summary>
/// 跨流程联动（只读视图 + 预填跳转）。
/// 2026-11 死亡说明简化·阶段3c：从 DeathStatementService.cs 纯移动拆出（零行为变更，接口与 DI 不动）。
/// </summary>
public partial class DeathStatementService
{

    #region 跨流程联动（只读视图 + 预填跳转）

    private async Task<Result<List<DeathLinkAction>>> BuildLinksAsync(
        DeathStatementContext context, CancellationToken ct)
    {
        var links = new List<DeathLinkAction>();
        var deathDate = context.DeathDateText;
        var deathTime = context.DeathTimeText;

        // ① 普惠高龄三态：在享→停发登记；草稿→立即处理（从未在享但挂账需清理）；已停发→已完成
        //    （追缴金额只提示不自动算，由经办在停发页核定）
        var elderlyRows = context.Identities.Where(i => i.SourceKey == "Elderly").ToList();
        var elderlyActive = elderlyRows.FirstOrDefault(i => i.StatusLabel ==
            ElderlyBenefitConstants.GetStatusName(ElderlyBenefitConstants.StatusConfirmed));
        var elderlyDraft = elderlyRows.FirstOrDefault(i => i.StatusLabel ==
            ElderlyBenefitConstants.GetStatusName(ElderlyBenefitConstants.StatusDraft));
        if (elderlyActive != null)
        {
            links.Add(new DeathLinkAction
            {
                Domain = "Elderly",
                Title = "普惠高龄在享 · 需办理停发",
                StateText = "停发原因应选「去世」，死亡日期/时刻已预填；是否追缴多发金额由经办人工核定",
                IsDone = false, ActionText = "去办理", NavKey = "ElderlyStop",
                ElderlyApplicationId = elderlyActive.SourceRecordId,
                DeathDateText = deathDate, DeathTimeText = deathTime,
                DeathReason = context.DeathReason, DeathCertNo = context.DeathCertNo
            });
        }
        else if (elderlyDraft != null)
        {
            // 草稿同样跳停发页带预填（目标页按 ID 加载档案，草稿可正常办理停发）
            links.Add(new DeathLinkAction
            {
                Domain = "Elderly",
                Title = "普惠高龄登记为草稿 · 需立即处理",
                StateText = "已登记为草稿，请立即处理（从未在享，无多发金额可追缴）",
                IsDone = false, ActionText = "去办理", NavKey = "ElderlyStop",
                ElderlyApplicationId = elderlyDraft.SourceRecordId,
                DeathDateText = deathDate, DeathTimeText = deathTime,
                DeathReason = context.DeathReason, DeathCertNo = context.DeathCertNo
            });
        }
        else if (elderlyRows.Count > 0)
        {
            links.Add(new DeathLinkAction
            {
                Domain = "Elderly", Title = "普惠高龄津贴", StateText = "已停发（非在享）",
                IsDone = true, ActionText = "已完成", NavKey = "None"
            });
        }

        // ② 低收入档案 → 户主死亡变更 / 成员减员
        var archiveRows = context.Identities.Where(i => i.SourceKey == "Archive").ToList();
        if (archiveRows.Count > 0)
        {
            var hasDeathRecord = await HasDeathRecordAsync(context.IdCard, ct);
            var isHead = archiveRows.Any(i => i.Note == "户主");

            if (isHead)
            {
                var headState = await GetHeadDeathStateAsync(context.IdCard, ct);
                var headDone = headState != HeadDeathState.None;
                var headStateText = headState switch
                {
                    HeadDeathState.Rebuilt => "已生成新档案（停旧建新完成）",
                    HeadDeathState.StoppedOnly => "单人户整户停止完成（旧档案已停保，无新档）",
                    _ => "旧档案未停止、新档案未生成"
                };
                links.Add(new DeathLinkAction
                {
                    Domain = "ArchiveHead",
                    Title = "在享档案户主 · 需办理户主死亡变更（停旧建新）",
                    StateText = headStateText,
                    IsDone = headDone, ActionText = headDone ? "已完成" : "去办理",
                    NavKey = headDone ? "None" : "HouseholdDeath",
                    ApplicationId = context.ApplicationId,
                    DeathDateText = deathDate, DeathTimeText = deathTime,
                    DeathReason = context.DeathReason, DeathCertNo = context.DeathCertNo
                });
            }
            else
            {
                links.Add(new DeathLinkAction
                {
                    Domain = "ArchiveMember",
                    Title = "在享档案成员 · 需办理减员（原因：人员死亡）",
                    StateText = hasDeathRecord ? "已登记死亡记录" : "尚未登记死亡/减员",
                    IsDone = hasDeathRecord, ActionText = hasDeathRecord ? "已完成" : "去办理",
                    NavKey = hasDeathRecord ? "None" : "MemberChange",
                    ApplicationId = context.ApplicationId,
                    DeathDateText = deathDate, DeathTimeText = deathTime,
                    DeathReason = context.DeathReason, DeathCertNo = context.DeathCertNo
                });
            }
        }

        // ③ 大学生在读 → 退出登记
        var collegeStudying = context.Identities.Any(i => i.SourceKey == "College" &&
            i.StatusLabel == CollegeStudentConstants.GetStatusDisplay(CollegeStudentConstants.StatusStudying));
        if (collegeStudying || context.Identities.Any(i => i.SourceKey == "College"))
        {
            links.Add(new DeathLinkAction
            {
                Domain = "College",
                Title = "在校大学生档案",
                StateText = collegeStudying ? "状态仍为在读" : "状态已更新",
                IsDone = !collegeStudying, ActionText = collegeStudying ? "去办理" : "已完成",
                NavKey = collegeStudying ? "College" : "None"
            });
        }

        // ④ 追缴在办 → 结案确认
        var recoveryOpen = context.Identities.FirstOrDefault(i => i.SourceKey == "Recovery" &&
            i.StatusLabel == RecoveryConstants.GetStatusDisplayName(RecoveryConstants.STATUS_DRAFT));
        links.Add(new DeathLinkAction
        {
            Domain = "Recovery",
            Title = "后补追缴记录",
            StateText = recoveryOpen != null ? "办理中，需确认是否结案" : "无办理中的追缴记录",
            IsDone = recoveryOpen == null, ActionText = recoveryOpen == null ? "无需处理" : "去办理",
            NavKey = recoveryOpen == null ? "None" : "Recovery"
        });

        // ⑤ 近亲属备案对象 → 备案编辑
        var near = context.Identities.FirstOrDefault(i => i.SourceKey == "NearRelative");
        if (near != null)
        {
            links.Add(new DeathLinkAction
            {
                Domain = "NearRelative",
                Title = "近亲属备案对象",
                StateText = "备案对象已死亡，需更新/注销备案",
                IsDone = false, ActionText = "去办理", NavKey = "NearRelative",
                ApplicationId = near.ApplicationId
            });
        }

        return Result.Success(links);
    }

    /// <summary>户主死亡变更完成态（联动卡判定用）</summary>
    private enum HeadDeathState
    {
        /// <summary>未办理（无户主死亡记录）</summary>
        None,

        /// <summary>单人户整户停止完成（死亡记录 is_household_head=true 且无新档）</summary>
        StoppedOnly,

        /// <summary>停旧建新完成（死亡记录带回新档案ID）</summary>
        Rebuilt
    }

    /// <summary>
    /// 户主死亡变更完成态：死亡记录 is_household_head=true 即视为已办——同时覆盖
    /// 「停旧建新」（ExecuteHouseholdDeathAsync，new_application_id 有值）与
    /// 「单人户整户停止」（ExecuteHouseholdHeadDeathStopOnlyAsync，new_application_id 为 NULL）两条路径。
    /// 旧口径只认 new_application_id IS NOT NULL，致整户停止户永远显示「去办理」（2026-10-30 修复）。
    /// </summary>
    private async Task<HeadDeathState> GetHeadDeathStateAsync(string idCard, CancellationToken ct)
    {
        var sql = @"SELECT COALESCE(BOOL_OR(new_application_id IS NOT NULL), FALSE) AS has_new,
                           COALESCE(BOOL_OR(new_application_id IS NULL), FALSE) AS stop_only
                    FROM nc_biz_death_records
                    WHERE member_id_card = $1 AND is_household_head = TRUE";
        var result = await _db.QuerySingleAsync<HeadDeathStateRow>(sql, ct, idCard);
        if (result.IsFailure || result.Value == null)
        {
            if (result.IsFailure) LogWarn($"户主死亡完成态查询失败: {result.Message}");
            return HeadDeathState.None;
        }
        if (result.Value.HasNew) return HeadDeathState.Rebuilt;
        return result.Value.StopOnly ? HeadDeathState.StoppedOnly : HeadDeathState.None;
    }

    #endregion
}
