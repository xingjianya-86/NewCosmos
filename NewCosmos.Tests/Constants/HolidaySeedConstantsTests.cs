using NewCosmos.Constants;
using Xunit;

namespace NewCosmos.Tests.Constants;

/// <summary>
/// 内置节假日种子数据校验（AGENTS §14：HolidaySeedConstants 为唯一数据源）：
/// 日期/星期/数量须与国务院办公厅正式通知逐项一致，写错即失败。
/// 2026：国办发明电〔2025〕7号；2027：国办发明电〔2026〕10号（2026-11-05 发布）。
/// </summary>
public class HolidaySeedConstantsTests
{
    private static Dictionary<DateOnly, HolidaySeedConstants.Row> MapOf(int year) =>
        HolidaySeedConstants.All
            .Where(r => r.Year == year)
            .ToDictionary(r => r.Date);

    [Fact]
    public void 二〇二七_放假日与补班日数量符合通知()
    {
        var rows = HolidaySeedConstants.All.Where(r => r.Year == 2027).ToList();

        Assert.Equal(29, rows.Count(r => r.DateType == DutyConstants.HolidayRowTypes.HOLIDAY));
        Assert.Equal(5, rows.Count(r => r.DateType == DutyConstants.HolidayRowTypes.MAKEUP));
    }

    [Fact]
    public void 二〇二七_七个节日放假区间与通知一致()
    {
        var map = MapOf(2027);

        // 元旦：1/1（周五）-1/3（周日）
        AssertHolidayRange(map, new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 3), "元旦");
        // 春节：2/5（除夕、周五）-2/13（周六），共 9 天
        var eve = map[new DateOnly(2027, 2, 5)];
        Assert.Equal(DutyConstants.HolidayRowTypes.HOLIDAY, eve.DateType);
        Assert.Equal("除夕", eve.Name);
        for (var d = new DateOnly(2027, 2, 6); d <= new DateOnly(2027, 2, 13); d = d.AddDays(1))
        {
            Assert.Equal("春节", map[d].Name);
        }
        // 清明节：4/3-4/5
        AssertHolidayRange(map, new DateOnly(2027, 4, 3), new DateOnly(2027, 4, 5), "清明节");
        // 劳动节：5/1-5/5
        AssertHolidayRange(map, new DateOnly(2027, 5, 1), new DateOnly(2027, 5, 5), "劳动节");
        // 端午节：6/9 单日（周三）
        AssertHolidayRange(map, new DateOnly(2027, 6, 9), new DateOnly(2027, 6, 9), "端午节");
        // 中秋节：9/15 单日（周三）
        AssertHolidayRange(map, new DateOnly(2027, 9, 15), new DateOnly(2027, 9, 15), "中秋节");
        // 国庆节：10/1（周五）-10/7（周四）
        AssertHolidayRange(map, new DateOnly(2027, 10, 1), new DateOnly(2027, 10, 7), "国庆节");
    }

    [Fact]
    public void 二〇二七_补班日与通知一致且均为周末()
    {
        var map = MapOf(2027);
        var expectedMakeup = new[]
        {
            new DateOnly(2027, 1, 31), // 春节前补班（周日）
            new DateOnly(2027, 2, 14), // 春节后补班（周日）
            new DateOnly(2027, 5, 8),  // 劳动节后补班（周六）
            new DateOnly(2027, 9, 26), // 国庆节前补班（周日）
            new DateOnly(2027, 10, 9), // 国庆节后补班（周六）
        };

        foreach (var date in expectedMakeup)
        {
            Assert.True(map.ContainsKey(date), $"{date:yyyy-MM-dd} 缺少补班日");
            Assert.Equal(DutyConstants.HolidayRowTypes.MAKEUP, map[date].DateType);
            Assert.True(date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday,
                $"{date:yyyy-MM-dd} 应为周末补班日，实际 {date.DayOfWeek}");
        }

        // 放假日与补班日不得重叠（同一年）
        var holidayDates = map.Values
            .Where(r => r.DateType == DutyConstants.HolidayRowTypes.HOLIDAY)
            .Select(r => r.Date)
            .ToHashSet();
        Assert.DoesNotContain(expectedMakeup, holidayDates.Contains);
    }

    [Fact]
    public void 种子数据_日期全局唯一且行类型与年份合法()
    {
        Assert.Equal(HolidaySeedConstants.All.Count,
            HolidaySeedConstants.All.Select(r => r.Date).Distinct().Count());

        foreach (var row in HolidaySeedConstants.All)
        {
            Assert.Equal(row.Date.Year, row.Year);
            Assert.True(row.DateType is DutyConstants.HolidayRowTypes.HOLIDAY or DutyConstants.HolidayRowTypes.MAKEUP,
                $"{row.Date:yyyy-MM-dd} 行类型非法: {row.DateType}");
        }
    }

    [Fact]
    public void 二〇二六_行数保持回归()
    {
        var rows = HolidaySeedConstants.All.Where(r => r.Year == 2026).ToList();

        Assert.Equal(33, rows.Count(r => r.DateType == DutyConstants.HolidayRowTypes.HOLIDAY));
        Assert.Equal(6, rows.Count(r => r.DateType == DutyConstants.HolidayRowTypes.MAKEUP));
    }

    private static void AssertHolidayRange(
        IReadOnlyDictionary<DateOnly, HolidaySeedConstants.Row> map, DateOnly start, DateOnly end, string name)
    {
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            Assert.True(map.ContainsKey(d), $"{d:yyyy-MM-dd} 缺少 {name} 放假日");
            Assert.Equal(DutyConstants.HolidayRowTypes.HOLIDAY, map[d].DateType);
            Assert.Equal(name, map[d].Name);
        }
    }
}
