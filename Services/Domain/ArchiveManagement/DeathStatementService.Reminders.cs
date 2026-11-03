using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.DeathStatement;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Domain.UserManagement;


namespace NewCosmos.Services.Domain.ArchiveManagement;

/// <summary>
/// 数据提醒（12 条规则）。
/// 2026-11 死亡说明简化·阶段3c：从 DeathStatementService.cs 纯移动拆出（零行为变更，接口与 DI 不动）。
/// </summary>
public partial class DeathStatementService
{

    #region 数据提醒（12 条规则）

    private async Task<Result<List<DeathReminderItem>>> BuildRemindersAsync(
        DeathStatementContext context, CancellationToken ct)
    {
        var list = new List<DeathReminderItem>();

        // R1 高龄在享且未登记死亡 → 需办停发
        var elderlyActive = context.Identities.Any(i =>
            i.SourceKey == "Elderly" && i.StatusLabel == ElderlyBenefitConstants.GetStatusName(ElderlyBenefitConstants.StatusConfirmed));
        if (elderlyActive)
        {
            list.Add(new DeathReminderItem
            {
                Level = "警告", Code = "ELDERLY_ACTIVE",
                Message = "该人员的普惠高龄津贴仍为「已确认」在享状态，且尚未登记死亡",
                SuggestAction = "前往「高龄津贴发放 → 停发登记」办理（原因选「去世」）", NavKey = "ElderlyStop"
            });
        }

        // R2 低收入档案在享但未登记死亡 → 需办减员/户主死亡变更
        var hasDeathRecord = await HasDeathRecordAsync(context.IdCard, ct);
        var archiveActive = context.Identities.Any(i =>
            i.SourceKey == "Archive" && (i.StatusLabel == "在享" || i.StatusLabel == "已提交" || i.StatusLabel == "已办结"));
        if (archiveActive && !hasDeathRecord)
        {
            var isHead = context.Identities.Any(i => i.SourceKey == "Archive" && i.Note == "户主");
            list.Add(new DeathReminderItem
            {
                Level = "警告", Code = "ARCHIVE_NOT_CHANGED",
                Message = isHead
                    ? "该人员为在享档案户主，档案尚未办理户主死亡变更（停旧建新）"
                    : "该人员在享档案尚未办理成员减员（减员原因：人员死亡）",
                SuggestAction = "前往「保障对象动态管理」办理变更", NavKey = isHead ? "HouseholdDeath" : "MemberChange"
            });
        }

        // R3 缺死亡证明编号
        if (string.IsNullOrWhiteSpace(context.DeathCertNo))
        {
            list.Add(new DeathReminderItem
            {
                Level = "警告", Code = "NO_CERT_NO",
                Message = "死亡证明编号为空，无法凭编号核验死亡事实",
                SuggestAction = "补录死亡证明编号后重新出具", NavKey = "None"
            });
        }

        // R4 疑似重复申报（同身份证存在多条有效申报）
        var validDeclareCount = await CountValidDeclarationsAsync(context.IdCard, ct);
        if (validDeclareCount > 1)
        {
            list.Add(new DeathReminderItem
            {
                Level = "警告", Code = "DUPLICATE_DECLARE",
                Message = $"该死者存在 {validDeclareCount} 条有效申报记录，疑似重复申报",
                SuggestAction = "核对后作废多余申报", NavKey = "None"
            });
        }

        // R5 跨村数据（只计数，不输出明细）
        if (context.CrossVillageCount > 0)
        {
            list.Add(new DeathReminderItem
            {
                Level = "警告", Code = "CROSS_VILLAGE",
                Message = $"另有 {context.CrossVillageCount} 条身份记录挂在其他村社区，已排除出本说明",
                SuggestAction = "如属本村，请到档案/区划中修正村字段", NavKey = "None"
            });
        }

        // R6 大学生仍在读
        if (context.Identities.Any(i => i.SourceKey == "College" &&
                i.StatusLabel == CollegeStudentConstants.GetStatusDisplay(CollegeStudentConstants.StatusStudying)))
        {
            list.Add(new DeathReminderItem
            {
                Level = "提示", Code = "COLLEGE_STUDYING",
                Message = "关联的在校大学生档案状态仍为「在读」",
                SuggestAction = "前往「大学生管理」更新状态", NavKey = "College"
            });
        }

        // R7 追缴未结案
        if (context.Identities.Any(i => i.SourceKey == "Recovery" &&
                i.StatusLabel == RecoveryConstants.GetStatusDisplayName(RecoveryConstants.STATUS_DRAFT)))
        {
            list.Add(new DeathReminderItem
            {
                Level = "提示", Code = "RECOVERY_OPEN",
                Message = "该人员存在办理中的后补追缴记录",
                SuggestAction = "前往「后补追缴」确认是否结案", NavKey = "Recovery"
            });
        }

        // R8 残疾信息缺失
        if (context.Identities.Any(i => i.SourceKey == "Disability" &&
                (i.StatusLabel == "等级未登记" || i.Period == "残疾证号缺失")))
        {
            list.Add(new DeathReminderItem
            {
                Level = "提示", Code = "DISABILITY_INCOMPLETE",
                Message = "残疾人档案的等级或残疾证号不完整",
                SuggestAction = "在档案成员信息中补全残疾信息", NavKey = "None"
            });
        }

        // R9 身份证校验不通过
        if (!IdCardValidator.IsValid(context.IdCard))
        {
            list.Add(new DeathReminderItem
            {
                Level = "警告", Code = "IDCARD_INVALID",
                Message = $"身份证号校验不通过（{context.IdCard}），跨表身份聚合可能不完整",
                SuggestAction = "核对并更正身份证号", NavKey = "None"
            });
        }

        // R10 库内无身份命中（导入库候选带出的人已在库——历史导入台账，不算"系统无记录"，否则与搜索结果自相矛盾）
        if (context.Identities.Count == 0 && context.CrossVillageCount == 0
            && string.IsNullOrEmpty(context.ImportSourceLabel))
        {
            list.Add(new DeathReminderItem
            {
                Level = "提示", Code = "NO_IDENTITY",
                Message = "系统内未查到该人员的任何在册身份记录",
                SuggestAction = "可正常出具情况说明（文书将注明系统无记录）", NavKey = "None"
            });
        }

        // R11 死亡确切时刻缺失（历史登记未采集）
        if (string.IsNullOrWhiteSpace(context.DeathTimeText))
        {
            list.Add(new DeathReminderItem
            {
                Level = "警告", Code = "NO_DEATH_TIME",
                Message = "该死亡记录未采集确切时刻（历史数据），文书将只到日期",
                SuggestAction = "提交一条新申报补录确切时刻", NavKey = "Declare"
            });
        }

        // R12 仅存在于历史导入台账（第⑧源命中、且无在册档案与高龄记录）。
        //     台账无 applicationId → 只提示人工核对，不建联动卡、不派五域任务。
        if (context.Identities.Any(i => i.SourceKey == "ImportArchive")
            && !context.Identities.Any(i => i.SourceKey == "Archive" || i.SourceKey == "Elderly"))
        {
            var libName = context.Identities.First(i => i.SourceKey == "ImportArchive").TypeLabel;
            list.Add(new DeathReminderItem
            {
                Level = "提示", Code = "IMPORT_LEDGER_ONLY",
                Message = $"该人员仅存在于历史导入台账（{libName}），系统内无在册档案与高龄记录",
                SuggestAction = "如需办理减员/停发等业务，请先到对应模块核对或补建档案", NavKey = "None"
            });
        }

        return Result.Success(list);
    }

    private async Task<bool> HasDeathRecordAsync(string idCard, CancellationToken ct)
    {
        var sql = "SELECT COUNT(*) FROM nc_biz_death_records WHERE member_id_card = $1";
        var result = await _db.ExecuteScalarAsync<long>(sql, ct, idCard);
        if (result.IsFailure)
        {
            LogWarn($"死亡登记存在性查询失败: {result.Message}");
            return false;
        }
        return result.Value > 0;
    }

    private async Task<int> CountValidDeclarationsAsync(string idCard, CancellationToken ct)
    {
        var sql = @"SELECT COUNT(*) FROM nc_biz_death_declarations
                    WHERE deceased_id_card = $1 AND deleted_at IS NULL AND status <> 'Revoked'";
        var result = await _db.ExecuteScalarAsync<long>(sql, ct, idCard);
        if (result.IsFailure)
        {
            LogWarn($"有效申报计数查询失败: {result.Message}");
            return 0;
        }
        return (int)result.Value;
    }

    #endregion
}
