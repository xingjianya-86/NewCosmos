namespace NewCosmos.Helpers;

/// <summary>
/// A 线核查周期：以每月 11 日 00:00 为界的无缝左闭右开区间。
///
/// GetPeriod(y, m) = [上月11日 00:00, 本月11日 00:00)
/// 即"m 月周期"覆盖 上月11日 ~ 本月10日（含 10 日全天）。
///
/// 为什么取消旧实现的周末顺延：旧实现把落在周六/周日的 start 顺延 1~2 天，
/// 但上一周期的 end（10 日）固定不动，被顺延跳过的 11/12 日不属于任何周期，
/// 形成日期黑洞（落在这两天的数据任何月份都统计不到）；同时 GetYearPeriod
/// 也不再等于 12 个月周期之和。周期是数据统计口径而不是工作日安排，必须
/// 首尾无缝衔接，故不做任何周末调整。
///
/// 约定：end 是排他上界（半开区间）。调用方 SQL 一律写
///   application_date &gt;= start AND application_date &lt; end
/// （与 AssetVerificationService 现有全部查询一致；旧实现 end=10日 00:00 时
/// 这些 &lt; end 查询会把 10 日整天漏掉，本实现同时修复该问题）。
/// UI 展示"最后一天"请用 end.AddDays(-1)（即本月 10 日）。
/// </summary>
public static class ALinePeriodHelper
{
    public static (DateTime start, DateTime end) GetPeriod(int year, int month)
    {
        var end = new DateTime(year, month, 11);   // 本月11日 00:00（排他上界）
        var start = end.AddMonths(-1);             // 上月11日 00:00（包含，1 月自动回退到上一年 12 月）
        return (start, end);
    }

    public static (DateTime start, DateTime end) GetYearPeriod(int year)
    {
        // 年度周期 = 1 月周期起点 ~ 12 月周期终点，恰为 12 个月周期的无缝并集
        return (GetPeriod(year, 1).start, GetPeriod(year, 12).end);
    }
}
