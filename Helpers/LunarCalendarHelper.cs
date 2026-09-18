using System.Globalization;

namespace NewCosmos.Helpers;

/// <summary>
/// 农历日期 Helper：基于 .NET 内置 ChineseLunisolarCalendar（中国农历，1900-2100 有效范围），
/// 供政务值班表 {星期与农历N} 单元格使用。
/// </summary>
public static class LunarCalendarHelper
{
    private static readonly ChineseLunisolarCalendar _calendar = new();

    private static readonly string[] WeekNames =
    {
        "星期日", "星期一", "星期二", "星期三", "星期四", "星期五", "星期六"
    };

    private static readonly string[] LunarMonths =
    {
        "正月", "二月", "三月", "四月", "五月", "六月",
        "七月", "八月", "九月", "十月", "冬月", "腊月"
    };

    private static readonly string[] LunarDays =
    {
        "初一", "初二", "初三", "初四", "初五", "初六", "初七", "初八", "初九", "初十",
        "十一", "十二", "十三", "十四", "十五", "十六", "十七", "十八", "十九", "二十",
        "廿一", "廿二", "廿三", "廿四", "廿五", "廿六", "廿七", "廿八", "廿九", "三十"
    };

    /// <summary>
    /// {星期与农历N} 单元格文本：星期缩写 + 农历（去"农历"前缀），如 "一 腊月初三"；
    /// 法定节假日以节日名替代星期，如 "春节 腊月廿五"；闰月带"闰"前缀（如 "六 闰六月十五"）。
    /// </summary>
    public static string GetWeekAndLunarString(DateTime date, string? holidayName = null)
    {
        var week = WeekNames[(int)date.DayOfWeek].Replace("星期", string.Empty);
        var label = string.IsNullOrWhiteSpace(holidayName) ? week : holidayName.Trim();
        var lunar = GetLunarDateString(date);
        return string.IsNullOrEmpty(lunar) ? label : $"{label} {lunar}";
    }

    /// <summary>农历月日文本：如 "正月初三"、"闰六月十五"；超出有效范围时返回空串（仅显示星期）</summary>
    public static string GetLunarDateString(DateTime date)
    {
        try
        {
            var year = _calendar.GetYear(date);
            var leapMonth = _calendar.GetLeapMonth(year);
            var month = _calendar.GetMonth(date);
            var day = _calendar.GetDayOfMonth(date);

            var monthName = GetLunarMonthName(month, leapMonth);
            var dayName = LunarDays[Math.Clamp(day, 1, 30) - 1];
            return $"{monthName}{dayName}";
        }
        catch (ArgumentOutOfRangeException)
        {
            // 超出 ChineseLunisolarCalendar 支持范围（<1901 或 >2100 等），降级为仅星期
            return string.Empty;
        }
    }

    /// <summary>月名（含闰月标识）：leapMonth 为该年闰月序号（1-12，0=无闰月；.NET 语义：leapMonth N 表示闰 N 月之前插入）</summary>
    private static string GetLunarMonthName(int month, int leapMonth)
    {
        if (leapMonth > 0 && month == leapMonth)
        {
            // 当前月即闰月（.NET 将闰月计为 leapMonth 本身，真实月序为 month-1）
            var realMonth = month - 1;
            return "闰" + LunarMonths[Math.Clamp(realMonth, 1, 12) - 1];
        }
        var index = leapMonth > 0 && month > leapMonth ? month - 1 : month;
        return LunarMonths[Math.Clamp(index, 1, 12) - 1];
    }
}
