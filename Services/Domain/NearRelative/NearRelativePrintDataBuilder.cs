using NewCosmos.Constants;
using NewCosmos.Models.Entities;

namespace NewCosmos.Services.Domain.NearRelative;

/// <summary>
/// 近亲属备案打印数据组装（填充月报表两表 + 档案两表的字段字典）
/// 字段键见 Constants/FieldKeys.cs 的 NEAR_RELATIVE_* 常量；模板 config_json 将中文占位符映射到这些键。
/// 月报口径：忽略年月，任何月份输出同一份全量名册。
/// </summary>
public static class NearRelativePrintDataBuilder
{
    /// <summary>
    /// 批量备案信息：单页字段（1 名工作人员 + 本页 5 行对象槽位）
    /// </summary>
    /// <param name="staff">工作人员</param>
    /// <param name="pageLinks">本页对象行（≤5）</param>
    /// <param name="auditTime">审核确认时间（填报时间）</param>
    public static Dictionary<string, string> BuildStaffBatchPageFields(
        NearRelativeStaff staff,
        List<NearRelativeLink> pageLinks,
        string auditTime)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FieldKeys.NEAR_RELATIVE_STAFF_NAME] = staff.StaffName,
            [FieldKeys.NEAR_RELATIVE_STAFF_ID_CARD] = staff.StaffIdCard,
            [FieldKeys.NEAR_RELATIVE_STAFF_PHONE] = staff.StaffPhone,
            [FieldKeys.NEAR_RELATIVE_WORK_UNIT] = staff.WorkUnit,
            [FieldKeys.NEAR_RELATIVE_POSITION] = staff.Position,
            [FieldKeys.NEAR_RELATIVE_AUDIT_TIME] = auditTime,
        };

        for (var i = 0; i < NearRelativeConstants.StaffPageRowLimit; i++)
        {
            var idx = i + 1;
            var link = i < pageLinks.Count ? pageLinks[i] : null;
            fields[FieldKeys.NEAR_RELATIVE_ROW_RELATION + "_" + idx] = link?.Relation ?? string.Empty;
            fields[FieldKeys.NEAR_RELATIVE_ROW_NAME + "_" + idx] = link?.Name ?? string.Empty;
            fields[FieldKeys.NEAR_RELATIVE_ROW_ID_CARD + "_" + idx] = link?.IdCard ?? string.Empty;
            fields[FieldKeys.NEAR_RELATIVE_ROW_HUKOU + "_" + idx] = link?.HukouAddress ?? string.Empty;
            fields[FieldKeys.NEAR_RELATIVE_ROW_RESIDENCE + "_" + idx] = link?.ResidenceAddress ?? string.Empty;
            fields[FieldKeys.NEAR_RELATIVE_ROW_HELP_TYPE + "_" + idx] = link?.HelpType ?? string.Empty;
            fields[FieldKeys.NEAR_RELATIVE_ROW_MONTH_AMOUNT + "_" + idx] =
                link?.MonthAmount is { } amount ? amount.ToString("F2") : string.Empty;
        }

        return fields;
    }

    /// <summary>
    /// 汇总表：单页字段（页公共 = 填报单位/填报日期 + 本页 ≤9 组槽位）
    /// </summary>
    /// <param name="pagePairs">本页对列表（每组 = 工作人员 + 对象）</param>
    /// <param name="reportDate">填报日期</param>
    /// <param name="reportUnit">填报单位</param>
    public static Dictionary<string, string> BuildSummaryPageFields(
        List<NearRelativePair> pagePairs,
        string reportDate,
        string reportUnit)
    {
        var fields = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FieldKeys.NEAR_RELATIVE_REPORT_DATE] = reportDate,
            [FieldKeys.NEAR_RELATIVE_REPORT_UNIT] = reportUnit,
        };

        for (var i = 0; i < NearRelativeConstants.SummaryPageRowLimit; i++)
        {
            var idx = i + 1;
            var pair = i < pagePairs.Count ? pagePairs[i] : null;
            var staff = pair?.Staff;
            var link = pair?.Link;
            fields[FieldKeys.NEAR_RELATIVE_SUMMARY_STAFF_NAME + "_" + idx] = staff?.StaffName ?? string.Empty;
            fields[FieldKeys.NEAR_RELATIVE_SUMMARY_WORK_UNIT + "_" + idx] = staff?.WorkUnit ?? string.Empty;
            fields[FieldKeys.NEAR_RELATIVE_SUMMARY_POSITION + "_" + idx] = staff?.Position ?? string.Empty;
            fields[FieldKeys.NEAR_RELATIVE_SUMMARY_RELATION + "_" + idx] = link?.Relation ?? string.Empty;
            fields[FieldKeys.NEAR_RELATIVE_SUMMARY_NAME + "_" + idx] = link?.Name ?? string.Empty;
            fields[FieldKeys.NEAR_RELATIVE_SUMMARY_ID_CARD + "_" + idx] = link?.IdCard ?? string.Empty;
            fields[FieldKeys.NEAR_RELATIVE_SUMMARY_HUKOU + "_" + idx] = link?.HukouAddress ?? string.Empty;
            fields[FieldKeys.NEAR_RELATIVE_SUMMARY_HELP_TYPE + "_" + idx] = link?.HelpType ?? string.Empty;
            fields[FieldKeys.NEAR_RELATIVE_SUMMARY_FAMILY_SIZE + "_" + idx] =
                link?.FamilySize is { } fs ? fs.ToString() : string.Empty;
            fields[FieldKeys.NEAR_RELATIVE_SUMMARY_REPORT_AMOUNT + "_" + idx] =
                link?.ReportAmount is { } ra ? ra.ToString("F2") : string.Empty;
        }

        return fields;
    }

    /// <summary>
    /// 档案·工作人员关联信息表：单值字段（每对一页：户主 + 近亲属）
    /// </summary>
    public static Dictionary<string, string> BuildArchiveLinkInfoFields(NearRelativePair pair, string auditTime)
    {
        var s = pair.Staff;
        var l = pair.Link;
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FieldKeys.NEAR_RELATIVE_STAFF_NAME] = s.StaffName,
            [FieldKeys.NEAR_RELATIVE_STAFF_PHONE] = s.StaffPhone,
            [FieldKeys.NEAR_RELATIVE_WORK_UNIT] = s.WorkUnit,
            [FieldKeys.NEAR_RELATIVE_POSITION] = s.Position,
            [FieldKeys.NEAR_RELATIVE_RELATION] = l.Relation,
            [FieldKeys.NEAR_RELATIVE_NAME] = l.Name,
            [FieldKeys.NEAR_RELATIVE_GENDER] = l.Gender,
            [FieldKeys.NEAR_RELATIVE_BIRTH_DATE] = l.BirthDate,
            [FieldKeys.NEAR_RELATIVE_FAMILY_ADDRESS] = l.FamilyAddress,
            [FieldKeys.NEAR_RELATIVE_FAMILY_SIZE] = l.FamilySize?.ToString() ?? string.Empty,
            [FieldKeys.NEAR_RELATIVE_APPLY_REASON] = l.ApplyReason,
            [FieldKeys.NEAR_RELATIVE_APPLY_TIME] = l.ApplyTime,
            [FieldKeys.NEAR_RELATIVE_ECONOMY_INVESTIGATE] = l.EconomyInvestigate,
            [FieldKeys.NEAR_RELATIVE_TOWN_OPINION] = l.TownOpinion,
            [FieldKeys.NEAR_RELATIVE_AUDIT_TIME] = auditTime,
        };
    }

    /// <summary>
    /// 档案·救助对象信息表：单值字段（每对一页：工作人员 + 户主对象）
    /// </summary>
    public static Dictionary<string, string> BuildArchiveObjectInfoFields(NearRelativePair pair, string auditTime)
    {
        var s = pair.Staff;
        var l = pair.Link;
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FieldKeys.NEAR_RELATIVE_STAFF_NAME] = s.StaffName,
            [FieldKeys.NEAR_RELATIVE_STAFF_ID_CARD] = s.StaffIdCard,
            [FieldKeys.NEAR_RELATIVE_WORK_UNIT] = s.WorkUnit,
            [FieldKeys.NEAR_RELATIVE_POSITION] = s.Position,
            [FieldKeys.NEAR_RELATIVE_NAME] = l.Name,
            [FieldKeys.NEAR_RELATIVE_ID_CARD] = l.IdCard,
            [FieldKeys.NEAR_RELATIVE_HUKOU_ADDRESS] = l.HukouAddress,
            [FieldKeys.NEAR_RELATIVE_RESIDENCE_ADDRESS] = l.ResidenceAddress,
            [FieldKeys.NEAR_RELATIVE_FAMILY_SIZE] = l.FamilySize?.ToString() ?? string.Empty,
            [FieldKeys.NEAR_RELATIVE_HELP_TYPE] = l.HelpType,
            [FieldKeys.NEAR_RELATIVE_MONTH_AMOUNT] = l.MonthAmount?.ToString("F2") ?? string.Empty,
            [FieldKeys.NEAR_RELATIVE_RELATION] = l.Relation,
            [FieldKeys.NEAR_RELATIVE_START_TIME] = l.StartTime,
            [FieldKeys.NEAR_RELATIVE_FAMILY_RELATION] = l.FamilyRelation,
            [FieldKeys.NEAR_RELATIVE_FAMILY_DIFFICULTY] = l.FamilyDifficulty,
            [FieldKeys.NEAR_RELATIVE_TOWN_OPINION] = l.TownOpinion,
            [FieldKeys.NEAR_RELATIVE_DYNAMIC_RECORD] = l.DynamicRecord,
            [FieldKeys.NEAR_RELATIVE_AUDIT_TIME] = auditTime,
        };
    }

    /// <summary>
    /// 档案两表共用：一对备案的合并字段（关联信息表 + 救助对象信息表全部键，
    /// 各模板只取自身 config.Fields 覆盖的键，避免多次装配）
    /// </summary>
    public static Dictionary<string, string> BuildArchivePairFields(NearRelativePair pair, string auditTime)
    {
        var fields = BuildArchiveLinkInfoFields(pair, auditTime);
        foreach (var kv in BuildArchiveObjectInfoFields(pair, auditTime))
            fields[kv.Key] = kv.Value;
        return fields;
    }

    /// <summary>
    /// 汇总表全量对（staff × 对象展开）
    /// </summary>
    public static List<NearRelativePair> FlattenPairs(List<NearRelativeEntry> entries)
    {
        var pairs = new List<NearRelativePair>();
        foreach (var entry in entries)
        {
            foreach (var link in entry.Links)
            {
                pairs.Add(new NearRelativePair { Staff = entry.Staff, Link = link });
            }
        }
        return pairs;
    }
}
