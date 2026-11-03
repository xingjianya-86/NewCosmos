using NewCosmos.Constants;

namespace NewCosmos.Helpers;

/// <summary>
/// 普惠高龄「新增」统计口径 SQL 片段（唯一事实来源）：
/// 「月度统计/月报新增明细、主页本月新增高龄老人数、模块统计、补打中心按月名单」须同时满足：
/// ① status ∈ (Confirmed, Stopped)——状态会流转，停发档仍可回溯为"当月新增"；
/// ② source_type IS NULL——导入名册补建/复核补建档的 apply_date 是补录当天，非真实新增；
/// ③ 建档时人未死亡（死亡兜底）——死亡日期 ≤ 受理日期的记录不计新增：
///    补录已故人员即使把 apply_date 填错成补录当天也不会再进新增
///    （2026-10 实测错例：苏建宝、李玉兰 10-25 已死亡，10-29/10-31 才补录建档，
///      却同时出现在 10 月"新增明细"与"停发明细"）。
/// 时间范围（apply_date ∈ 周期）由调用方按各自周期传参，不并入本片段；
/// 停发明细口径与本片段无关，勿混用。
/// 状态码/表名为编译期常量与字面量，以 SQL 拼接并做引号转义，无注入风险。
/// </summary>
public static class ElderlyNewArchiveFilter
{
    /// <summary>
    /// "当月真正新增"判定表达式（裸布尔式，可用于 WHERE 条件或 COUNT(*) FILTER (WHERE …) 子句）。
    /// alias：nc_biz_elderly_applications 的别名或表名（如 "e" / "nc_biz_elderly_applications"）。
    /// </summary>
    public static string IsNewArchiveExpr(string alias)
    {
        var confirmed = Escape(ElderlyBenefitConstants.StatusConfirmed);
        var stopped = Escape(ElderlyBenefitConstants.StatusStopped);
        return $@"({alias}.status IN ('{confirmed}', '{stopped}')
                    AND {alias}.source_type IS NULL
                    AND ({alias}.death_date IS NULL OR {alias}.death_date > {alias}.apply_date)
                    AND NOT EXISTS (
                        SELECT 1 FROM nc_biz_death_records d
                        WHERE d.member_id_card = {alias}.id_card
                          AND d.death_date IS NOT NULL
                          AND d.death_date <= {alias}.apply_date))";
    }

    /// <summary>
    /// "非当月新增"排除子句（追加到 WHERE 条件中，自带 AND 前缀）。
    /// alias：nc_biz_elderly_applications 的别名或表名。
    /// </summary>
    public static string ExcludeSql(string alias) => $"AND {IsNewArchiveExpr(alias)}";

    private static string Escape(string value) => value.Replace("'", "''");
}
