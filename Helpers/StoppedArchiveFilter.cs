using NewCosmos.Constants;

namespace NewCosmos.Helpers;

/// <summary>
/// 停保/退出统计口径 SQL 片段（唯一事实来源）：
/// 停旧建新（家庭成员变更/同类别经济复核/户主变更等）后旧档案被置 Stopped，
/// 但户仍在同一大类继续享受 → 不属于"停保"；各统计/报表口径统一用本子句排除。
/// 跨大类变更（低保→低收入/特困等）与真正停保（收入超标/不符合）不受影响，仍按停保统计。
/// 分类码均为编译期常量（非用户输入），以 SQL 字面量拼接并做引号转义。
/// </summary>
public static class StoppedArchiveFilter
{
    /// <summary>
    /// "同大类停旧建新接续"排除子句（追加到 WHERE 条件中）。
    /// alias：nc_biz_applications 的别名或表名（如 "a" / "nc_biz_applications"）。
    /// 判定：存在未删除的下游新档案（original_application_id = 旧档案ID），
    /// 其分类非空且非停保类，且与旧档案属于同一大类。
    /// </summary>
    public static string NotRebuildContinuationSql(string alias)
    {
        var subsistence = Literals(ClassificationConstants.SubsistenceCategoryCodes);
        var lowIncome = Literals(ClassificationConstants.LowIncomeCategoryCodes);
        var destitute = Literals(ClassificationConstants.DestituteCategoryCodes);
        var rigid = Literals(ClassificationConstants.RigidExpenditureCategoryCodes);
        var stops = Literals(ClassificationConstants.StopCategoryCodes);

        return $@"
        AND NOT EXISTS (
            SELECT 1 FROM nc_biz_applications n
            WHERE n.original_application_id = {alias}.id
              AND n.deleted_at IS NULL
              AND n.classification_result IS NOT NULL AND n.classification_result <> ''
              AND n.classification_result NOT IN ({stops})
              AND (
                    ({alias}.classification_result IN ({subsistence}) AND n.classification_result IN ({subsistence}))
                 OR ({alias}.classification_result IN ({lowIncome}) AND n.classification_result IN ({lowIncome}))
                 OR ({alias}.classification_result IN ({destitute}) AND n.classification_result IN ({destitute}))
                 OR ({alias}.classification_result IN ({rigid}) AND n.classification_result IN ({rigid}))
              )
        )";
    }

    /// <summary>
    /// "停旧建新终结档"排除子句（追加到 WHERE 条件中）。
    /// 经济复核/成员变更触发停保时，除了被停的旧档，还会生成一条"新档分类即停保码"的终结记录，
    /// 两者是同一户：不排除则普通停保行按户重复计数（2026-10 月报停保卡片 8 户、表格仅 5 户实测）。
    /// 判定：original_application_id 非空（有上游旧档）且自身分类为停保码且上游档案未删；
    /// 上游已软删的终结档保留计数（无旧档可承载该户的停保行）。
    /// 分类码为编译期常量，以 SQL 字面量拼接，无注入风险。
    /// </summary>
    public static string TerminalRebuildArchiveSql(string alias)
    {
        var stops = Literals(ClassificationConstants.StopCategoryCodes);

        return $@"
        AND NOT ({alias}.original_application_id IS NOT NULL
                 AND {alias}.classification_result IN ({stops})
                 AND EXISTS (SELECT 1 FROM nc_biz_applications up
                             WHERE up.id = {alias}.original_application_id AND up.deleted_at IS NULL))";
    }

    private static string Literals(IEnumerable<string> codes) =>
        string.Join(",", codes.Select(c => $"'{c.Replace("'", "''")}'"));
}
