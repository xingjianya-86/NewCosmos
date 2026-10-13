using NewCosmos.Constants;

namespace NewCosmos.Helpers;

/// <summary>
/// 新增（入保）档案统计口径 SQL 片段（唯一事实来源）：
/// 「新增救助明细 / 本月总新增」须排除停旧建新接续档案（original_application_id 非空，
/// 属变更接续、非真正新增），但必须放行户内单人保档案（chain_type='SingleRescue'：
/// 旧户档案继续有效，新档案是新纳入保障的人员，属真正新增——单人保分类在创建时已锁定，
/// 不走分类判定保存路径，不会产生 CategoryAdd 跨类记录，若按"有上游档案"一刀切排除会永久漏计）。
/// 老数据（chain_type 为空的旧式停旧建新接续档案）仍按接续排除，语义不变。
/// 链类型为编译期常量（非用户输入），以 SQL 字面量拼接并做引号转义。
/// </summary>
public static class NewArchiveFilter
{
    /// <summary>
    /// "真正新增档案"判定表达式（裸布尔式，可用于 WHERE 条件或 COUNT(*) FILTER (WHERE …) 子句）。
    /// alias：nc_biz_applications 的别名或表名（如 "a" / "nc_biz_applications"）。
    /// </summary>
    public static string IsNewArchiveExpr(string alias)
    {
        var singleRescue = ChainTypeConstants.SINGLE_RESCUE.Replace("'", "''");
        return $"({alias}.original_application_id IS NULL OR {alias}.chain_type = '{singleRescue}')";
    }

    /// <summary>
    /// "非停旧建新接续"排除子句（追加到 WHERE 条件中，自带 AND 前缀）。
    /// alias：nc_biz_applications 的别名或表名（如 "a" / "nc_biz_applications"）。
    /// </summary>
    public static string ExcludeRebuildContinuationSql(string alias)
        => $"AND {IsNewArchiveExpr(alias)}";
}
