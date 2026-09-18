using System.Globalization;

namespace NewCosmos.Helpers;

/// <summary>
/// 报表周期模式
/// </summary>
public enum ReportPeriodMode
{
    Monthly,
    Weekly
}

/// <summary>
/// 自然周信息
/// </summary>
public class WeekInfo
{
    /// <summary>ISO周序号（1-53）</summary>
    public int WeekNumber { get; set; }

    /// <summary>周所属年份（跨年时可能与选择年不同）</summary>
    public int Year { get; set; }

    /// <summary>周一起始日</summary>
    public DateTime StartDate { get; set; }

    /// <summary>周日结束日</summary>
    public DateTime EndDate { get; set; }

    /// <summary>展示文本：第31周: 8月3日~8月9日</summary>
    public string DisplayText { get; set; } = string.Empty;

    public override string ToString() => DisplayText;

    /// <summary>
    /// 使用 WeekNumber + Year 作为相等性判断依据，确保 Picker 能正确匹配选中项
    /// </summary>
    public override bool Equals(object? obj)
    {
        if (obj is not WeekInfo other) return false;
        return WeekNumber == other.WeekNumber && Year == other.Year;
    }

    /// <summary>
    /// 与 Equals 保持一致的哈希码
    /// </summary>
    public override int GetHashCode() => HashCode.Combine(WeekNumber, Year);
}

/// <summary>
/// 报表周期帮助类：统一处理月报/周报的周期计算。
///
/// 切换规则：2026年8月1日起按周提交，之前按月（A线周期）。
/// 周报使用自然周：周一00:00 ~ 周日23:59:59。
/// </summary>
public static class ReportPeriodHelper
{
    /// <summary>
    /// 切换阈值：2026年8月1日起按周提交。
    /// </summary>
    private static readonly DateTime WeeklySwitchDate = new(2026, 8, 1);

    /// <summary>
    /// 判断某年某月应使用的报表模式。
    /// 2026年8月之前（含7月）= 月报，之后 = 周报。
    /// </summary>
    public static ReportPeriodMode GetMode(int year, int month)
    {
        var date = new DateTime(year, month, 1);
        return date < WeeklySwitchDate ? ReportPeriodMode.Monthly : ReportPeriodMode.Weekly;
    }

    /// <summary>
    /// 月报周期：委托 ALinePeriodHelper。
    /// 返回排他上界 (start, end)，调用方用 end.AddDays(-1) 展示最后一天。
    /// </summary>
    public static (DateTime start, DateTime end) GetMonthlyPeriod(int year, int month)
        => ALinePeriodHelper.GetPeriod(year, month);

    /// <summary>
    /// 获取指定年月内的所有自然周（周一~周日）。
    /// 切换月额外在列表头部插入过渡周（weekNumber=0）。
    /// 返回的列表按周一起始日排序。
    /// </summary>
    public static List<WeekInfo> GetWeeksInMonth(int year, int month)
    {
        var weeks = new List<WeekInfo>();
        var monthStart = new DateTime(year, month, 1);
        var monthEnd = monthStart.AddMonths(1);

        // 回退到该月第一天所在周的周一
        var d = monthStart;
        while (d.DayOfWeek != DayOfWeek.Monday)
            d = d.AddDays(-1);

        while (d < monthEnd)
        {
            var weekStart = d;                                // 周一
            var weekEnd = d.AddDays(6);                       // 周日

            // 只保留周一起始日在该月内的周（避免跨月周在两个月份重复显示）
            if (weekStart >= monthStart && weekStart < monthEnd)
            {
                var weekNumber = ISOWeek.GetWeekOfYear(weekStart);
                var weekYear = ISOWeek.GetYear(weekStart);

                weeks.Add(new WeekInfo
                {
                    WeekNumber = weekNumber,
                    Year = weekYear,
                    StartDate = weekStart,
                    EndDate = weekEnd,
                    DisplayText = $"第{weekNumber}周: {weekStart:M月d日}~{weekEnd:M月d日}"
                });
            }

            d = weekEnd.AddDays(1); // 下周一
        }

        // 切换月：在列表头部插入过渡周（上月11日 ~ 本月第一个周一前一日）
        if (year == WeeklySwitchDate.Year && month == WeeklySwitchDate.Month)
        {
            var transitionStart = new DateTime(year, month - 1, 11);
            var firstMonday = monthStart;
            while (firstMonday.DayOfWeek != DayOfWeek.Monday)
                firstMonday = firstMonday.AddDays(1);
            var transitionEnd = firstMonday.AddDays(-1);

            weeks.Add(new WeekInfo
            {
                WeekNumber = 0,
                Year = year,
                StartDate = transitionStart,
                EndDate = transitionEnd,
                DisplayText = $"过渡周: {transitionStart:M月d日}~{transitionEnd:M月d日}"
            });
        }

        return weeks;
    }

    /// <summary>
    /// 获取指定年/周序号的日期范围。
    /// 返回 (周一00:00, 排他上界周日24:00)。
    /// 特殊处理：weekNumber=0 为过渡周（上月11日~本月第一个周一前一日）。
    /// </summary>
    public static (DateTime start, DateTime end) GetWeeklyPeriod(int year, int weekNumber)
    {
        if (weekNumber == 0)
        {
            // 过渡周：上月11日 ~ 本月第一个周一的前一天
            var monthStart = new DateTime(year, WeeklySwitchDate.Month, 1);
            var firstMonday = monthStart;
            while (firstMonday.DayOfWeek != DayOfWeek.Monday)
                firstMonday = firstMonday.AddDays(1);
            var transitionStart = new DateTime(year, WeeklySwitchDate.Month - 1, 11); // 上月11日
            return (transitionStart, firstMonday); // 排他上界：周一00:00
        }

        // ISO周规则：该年1月4日所在周为第1周
        // 1月4日为周日（2026/2032等）时原公式会把第1周算到下周一，整体错位一周。
        // 改为先算"周一到当日的偏移"（周一=0…周日=6），再回退到第1周周一。
        var jan4 = new DateTime(year, 1, 4);
        var offset = ((int)jan4.DayOfWeek + 6) % 7;
        var startOfWeek1 = jan4.AddDays(-offset);
        var weekStart = startOfWeek1.AddDays((weekNumber - 1) * 7);
        var weekEndExclusive = weekStart.AddDays(7); // 排他上界
        return (weekStart, weekEndExclusive);
    }

    /// <summary>
    /// 某日期所在的ISO周序号。
    /// </summary>
    public static int GetWeekOfYear(DateTime date)
        => ISOWeek.GetWeekOfYear(date);

    /// <summary>
    /// 某日期所在的ISO周所属年份。
    /// </summary>
    public static int GetWeekYear(DateTime date)
        => ISOWeek.GetYear(date);
}
