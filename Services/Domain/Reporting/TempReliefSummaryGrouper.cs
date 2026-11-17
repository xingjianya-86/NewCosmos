using NewCosmos.Constants;

namespace NewCosmos.Services.Domain.Reporting;

/// <summary>
/// 月报「临时救助新增汇总表」分段（纯函数，无 DB/DI 依赖，供 PrintService 与单元测试共用）：
/// 无额度段（大额救助 → 报县局审批）在前，有额度段（小额快速救助定额 → 备用金发放）在后；
/// 段内保持入参顺序（数据源 SQL 已按 confirmed_at, id 升序）。
/// 分类判定点唯一：<see cref="TempReliefConstants.IsFixedQuotaType"/>。
/// </summary>
public static class TempReliefSummaryGrouper
{
    /// <summary>
    /// 按额度拆两段：返回 (无额度段, 有额度段)。历史空值/未知类型归无额度段
    /// （主表 relief_type 默认值即 Large，与默认一致）。
    /// </summary>
    public static (List<MonthlyTempReliefRow> NoQuota, List<MonthlyTempReliefRow> WithQuota) SplitByQuota(
        IReadOnlyCollection<MonthlyTempReliefRow> rows)
    {
        var noQuota = new List<MonthlyTempReliefRow>();
        var withQuota = new List<MonthlyTempReliefRow>();
        foreach (var row in rows)
        {
            if (TempReliefConstants.IsFixedQuotaType(row.ReliefType)) withQuota.Add(row);
            else noQuota.Add(row);
        }
        return (noQuota, withQuota);
    }
}
