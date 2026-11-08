namespace NewCosmos.Constants;

/// <summary>
/// 内置节假日种子数据（国务院办公厅年度放假安排，编译期内置）。
/// 唯一用途两处：
///  1) <see cref="Services.Utilities.HolidayService"/> 数据库快照加载前的兜底判定；
///  2) <see cref="Services.System.HolidayManageService.EnsureSeedDataAsync"/> 按年补齐数据库种子（source=SEED）。
/// 维护约定：国务院办公厅发布次年安排后在此追加一组，分组注释标明文号；日期/星期须与通知逐项核对。
/// 注意：接口下载（source=API）与手工维护（source=MANUAL）的数据优先，种子只在该年整年无数据时写入。
/// </summary>
public static class HolidaySeedConstants
{
    /// <summary>种子行（日期 / 行类型 / 节日名）</summary>
    public readonly record struct Row(DateOnly Date, string DateType, string Name)
    {
        /// <summary>所属年份（落库 year 列）</summary>
        public int Year => Date.Year;
    }

    /// <summary>
    /// 2026 年（《国务院办公厅关于2026年部分节假日安排的通知》，国办发明电〔2025〕7号）：
    /// 元旦 1/1-1/3（1/4 补班）；春节 2/15-2/23（2/14、2/28 补班）；清明 4/4-4/6；
    /// 劳动节 5/1-5/5（5/9 补班）；端午 6/19-6/21；中秋 9/25-9/27（9/20 补班）；国庆 10/1-10/7（10/10 补班）。
    /// </summary>
    public static readonly IReadOnlyList<Row> Year2026 = new Row[]
    {
        new(new DateOnly(2026, 1, 1), DutyConstants.HolidayRowTypes.HOLIDAY, "元旦"),
        new(new DateOnly(2026, 1, 2), DutyConstants.HolidayRowTypes.HOLIDAY, "元旦"),
        new(new DateOnly(2026, 1, 3), DutyConstants.HolidayRowTypes.HOLIDAY, "元旦"),
        new(new DateOnly(2026, 1, 4), DutyConstants.HolidayRowTypes.MAKEUP, "元旦后补班"),
        new(new DateOnly(2026, 2, 14), DutyConstants.HolidayRowTypes.MAKEUP, "春节前补班"),
        new(new DateOnly(2026, 2, 15), DutyConstants.HolidayRowTypes.HOLIDAY, "春节"),
        new(new DateOnly(2026, 2, 16), DutyConstants.HolidayRowTypes.HOLIDAY, "除夕"),
        new(new DateOnly(2026, 2, 17), DutyConstants.HolidayRowTypes.HOLIDAY, "春节"),
        new(new DateOnly(2026, 2, 18), DutyConstants.HolidayRowTypes.HOLIDAY, "春节"),
        new(new DateOnly(2026, 2, 19), DutyConstants.HolidayRowTypes.HOLIDAY, "春节"),
        new(new DateOnly(2026, 2, 20), DutyConstants.HolidayRowTypes.HOLIDAY, "春节"),
        new(new DateOnly(2026, 2, 21), DutyConstants.HolidayRowTypes.HOLIDAY, "春节"),
        new(new DateOnly(2026, 2, 22), DutyConstants.HolidayRowTypes.HOLIDAY, "春节"),
        new(new DateOnly(2026, 2, 23), DutyConstants.HolidayRowTypes.HOLIDAY, "春节"),
        new(new DateOnly(2026, 2, 28), DutyConstants.HolidayRowTypes.MAKEUP, "春节后补班"),
        new(new DateOnly(2026, 4, 4), DutyConstants.HolidayRowTypes.HOLIDAY, "清明节"),
        new(new DateOnly(2026, 4, 5), DutyConstants.HolidayRowTypes.HOLIDAY, "清明节"),
        new(new DateOnly(2026, 4, 6), DutyConstants.HolidayRowTypes.HOLIDAY, "清明节"),
        new(new DateOnly(2026, 5, 1), DutyConstants.HolidayRowTypes.HOLIDAY, "劳动节"),
        new(new DateOnly(2026, 5, 2), DutyConstants.HolidayRowTypes.HOLIDAY, "劳动节"),
        new(new DateOnly(2026, 5, 3), DutyConstants.HolidayRowTypes.HOLIDAY, "劳动节"),
        new(new DateOnly(2026, 5, 4), DutyConstants.HolidayRowTypes.HOLIDAY, "劳动节"),
        new(new DateOnly(2026, 5, 5), DutyConstants.HolidayRowTypes.HOLIDAY, "劳动节"),
        new(new DateOnly(2026, 5, 9), DutyConstants.HolidayRowTypes.MAKEUP, "劳动节后补班"),
        new(new DateOnly(2026, 6, 19), DutyConstants.HolidayRowTypes.HOLIDAY, "端午节"),
        new(new DateOnly(2026, 6, 20), DutyConstants.HolidayRowTypes.HOLIDAY, "端午节"),
        new(new DateOnly(2026, 6, 21), DutyConstants.HolidayRowTypes.HOLIDAY, "端午节"),
        new(new DateOnly(2026, 9, 20), DutyConstants.HolidayRowTypes.MAKEUP, "中秋节前补班"),
        new(new DateOnly(2026, 9, 25), DutyConstants.HolidayRowTypes.HOLIDAY, "中秋节"),
        new(new DateOnly(2026, 9, 26), DutyConstants.HolidayRowTypes.HOLIDAY, "中秋节"),
        new(new DateOnly(2026, 9, 27), DutyConstants.HolidayRowTypes.HOLIDAY, "中秋节"),
        new(new DateOnly(2026, 10, 1), DutyConstants.HolidayRowTypes.HOLIDAY, "国庆节"),
        new(new DateOnly(2026, 10, 2), DutyConstants.HolidayRowTypes.HOLIDAY, "国庆节"),
        new(new DateOnly(2026, 10, 3), DutyConstants.HolidayRowTypes.HOLIDAY, "国庆节"),
        new(new DateOnly(2026, 10, 4), DutyConstants.HolidayRowTypes.HOLIDAY, "国庆节"),
        new(new DateOnly(2026, 10, 5), DutyConstants.HolidayRowTypes.HOLIDAY, "国庆节"),
        new(new DateOnly(2026, 10, 6), DutyConstants.HolidayRowTypes.HOLIDAY, "国庆节"),
        new(new DateOnly(2026, 10, 7), DutyConstants.HolidayRowTypes.HOLIDAY, "国庆节"),
        new(new DateOnly(2026, 10, 10), DutyConstants.HolidayRowTypes.MAKEUP, "国庆节后补班"),
    };

    /// <summary>
    /// 2027 年（《国务院办公厅关于2027年部分节假日安排的通知》，国办发明电〔2026〕10号，2026-11-05 发布）：
    /// 元旦 1/1-1/3；春节 2/5（除夕）-2/13（1/31、2/14 补班）；清明 4/3-4/5；
    /// 劳动节 5/1-5/5（5/8 补班）；端午 6/9；中秋 9/15；国庆 10/1-10/7（9/26、10/9 补班）。
    /// </summary>
    public static readonly IReadOnlyList<Row> Year2027 = new Row[]
    {
        new(new DateOnly(2027, 1, 1), DutyConstants.HolidayRowTypes.HOLIDAY, "元旦"),
        new(new DateOnly(2027, 1, 2), DutyConstants.HolidayRowTypes.HOLIDAY, "元旦"),
        new(new DateOnly(2027, 1, 3), DutyConstants.HolidayRowTypes.HOLIDAY, "元旦"),
        new(new DateOnly(2027, 1, 31), DutyConstants.HolidayRowTypes.MAKEUP, "春节前补班"),
        new(new DateOnly(2027, 2, 5), DutyConstants.HolidayRowTypes.HOLIDAY, "除夕"),
        new(new DateOnly(2027, 2, 6), DutyConstants.HolidayRowTypes.HOLIDAY, "春节"),
        new(new DateOnly(2027, 2, 7), DutyConstants.HolidayRowTypes.HOLIDAY, "春节"),
        new(new DateOnly(2027, 2, 8), DutyConstants.HolidayRowTypes.HOLIDAY, "春节"),
        new(new DateOnly(2027, 2, 9), DutyConstants.HolidayRowTypes.HOLIDAY, "春节"),
        new(new DateOnly(2027, 2, 10), DutyConstants.HolidayRowTypes.HOLIDAY, "春节"),
        new(new DateOnly(2027, 2, 11), DutyConstants.HolidayRowTypes.HOLIDAY, "春节"),
        new(new DateOnly(2027, 2, 12), DutyConstants.HolidayRowTypes.HOLIDAY, "春节"),
        new(new DateOnly(2027, 2, 13), DutyConstants.HolidayRowTypes.HOLIDAY, "春节"),
        new(new DateOnly(2027, 2, 14), DutyConstants.HolidayRowTypes.MAKEUP, "春节后补班"),
        new(new DateOnly(2027, 4, 3), DutyConstants.HolidayRowTypes.HOLIDAY, "清明节"),
        new(new DateOnly(2027, 4, 4), DutyConstants.HolidayRowTypes.HOLIDAY, "清明节"),
        new(new DateOnly(2027, 4, 5), DutyConstants.HolidayRowTypes.HOLIDAY, "清明节"),
        new(new DateOnly(2027, 5, 1), DutyConstants.HolidayRowTypes.HOLIDAY, "劳动节"),
        new(new DateOnly(2027, 5, 2), DutyConstants.HolidayRowTypes.HOLIDAY, "劳动节"),
        new(new DateOnly(2027, 5, 3), DutyConstants.HolidayRowTypes.HOLIDAY, "劳动节"),
        new(new DateOnly(2027, 5, 4), DutyConstants.HolidayRowTypes.HOLIDAY, "劳动节"),
        new(new DateOnly(2027, 5, 5), DutyConstants.HolidayRowTypes.HOLIDAY, "劳动节"),
        new(new DateOnly(2027, 5, 8), DutyConstants.HolidayRowTypes.MAKEUP, "劳动节后补班"),
        new(new DateOnly(2027, 6, 9), DutyConstants.HolidayRowTypes.HOLIDAY, "端午节"),
        new(new DateOnly(2027, 9, 15), DutyConstants.HolidayRowTypes.HOLIDAY, "中秋节"),
        new(new DateOnly(2027, 9, 26), DutyConstants.HolidayRowTypes.MAKEUP, "国庆节前补班"),
        new(new DateOnly(2027, 10, 1), DutyConstants.HolidayRowTypes.HOLIDAY, "国庆节"),
        new(new DateOnly(2027, 10, 2), DutyConstants.HolidayRowTypes.HOLIDAY, "国庆节"),
        new(new DateOnly(2027, 10, 3), DutyConstants.HolidayRowTypes.HOLIDAY, "国庆节"),
        new(new DateOnly(2027, 10, 4), DutyConstants.HolidayRowTypes.HOLIDAY, "国庆节"),
        new(new DateOnly(2027, 10, 5), DutyConstants.HolidayRowTypes.HOLIDAY, "国庆节"),
        new(new DateOnly(2027, 10, 6), DutyConstants.HolidayRowTypes.HOLIDAY, "国庆节"),
        new(new DateOnly(2027, 10, 7), DutyConstants.HolidayRowTypes.HOLIDAY, "国庆节"),
        new(new DateOnly(2027, 10, 9), DutyConstants.HolidayRowTypes.MAKEUP, "国庆节后补班"),
    };

    /// <summary>全部内置年份种子（按年份顺序拼接）</summary>
    public static readonly IReadOnlyList<Row> All = Year2026.Concat(Year2027).ToArray();
}
