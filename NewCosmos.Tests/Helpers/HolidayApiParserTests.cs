using NewCosmos.Constants;
using NewCosmos.Helpers;
using Xunit;

namespace NewCosmos.Tests.Helpers;

/// <summary>
/// 节假日接口解析器测试。样本取自 2026-11-01 实际抓取的真实响应，
/// 锚定两个口径：① 主源/备用源的日期判定必须一致；② "尚未发布"必须与"响应损坏"可区分
/// （2027 年安排当时尚未发布，这正是本次改动的触发场景）。
/// </summary>
public class HolidayApiParserTests
{
    #region 真实样本

    /// <summary>timor.tech 2026 年真实响应（节选，含除夕细分名与补班日）</summary>
    private const string Timor2026 = """
        {"code":0,"holiday":{
          "01-01":{"holiday":true,"name":"元旦","wage":3,"date":"2026-01-01","rest":54},
          "01-04":{"holiday":false,"name":"元旦后补班","wage":1,"after":true,"target":"元旦","date":"2026-01-04","rest":1},
          "02-16":{"holiday":true,"name":"除夕","wage":3,"date":"2026-02-16","rest":1},
          "02-17":{"holiday":true,"name":"初一","wage":3,"date":"2026-02-17","rest":1},
          "02-28":{"holiday":false,"name":"春节后补班","wage":1,"target":"春节","after":true,"date":"2026-02-28","rest":5},
          "10-01":{"holiday":true,"name":"国庆节","wage":3,"date":"2026-10-01","rest":1},
          "10-10":{"holiday":false,"after":true,"wage":1,"name":"国庆节后补班","target":"国庆节","date":"2026-10-10"}}
        }
        """;

    /// <summary>timor.tech 对尚未收录年份的真实响应（2027 实测）</summary>
    private const string TimorEmptyYear = """{"code":0,"holiday":{}}""";

    /// <summary>holiday-cn 2026 年真实响应（节选；注意节日名较粗、带 papers 公文链接）</summary>
    private const string HolidayCn2026 = """
        {
            "$schema": "https://raw.githubusercontent.com/NateScarlet/holiday-cn/master/schema.json",
            "year": 2026,
            "papers": ["https://www.gov.cn/zhengce/zhengceku/202511/content_7047091.htm"],
            "days": [
                { "name": "元旦", "date": "2026-01-01", "isOffDay": true },
                { "name": "元旦", "date": "2026-01-04", "isOffDay": false },
                { "name": "春节", "date": "2026-02-16", "isOffDay": true },
                { "name": "春节", "date": "2026-02-17", "isOffDay": true },
                { "name": "春节", "date": "2026-02-28", "isOffDay": false },
                { "name": "国庆节", "date": "2026-10-01", "isOffDay": true },
                { "name": "国庆节", "date": "2026-10-10", "isOffDay": false }
            ]
        }
        """;

    /// <summary>holiday-cn 对尚未发布年份的真实响应（2027 实测）</summary>
    private const string HolidayCnEmptyYear = """
        {
            "$schema": "https://raw.githubusercontent.com/NateScarlet/holiday-cn/master/schema.json",
            "year": 2027,
            "papers": [],
            "days": []
        }
        """;

    #endregion

    #region timor.tech（主源）

    [Fact]
    public void ParseTimor_真实响应_正确区分放假与补班()
    {
        var result = HolidayApiParser.ParseTimor(Timor2026);

        Assert.False(result.IsMalformed);
        Assert.False(result.IsEmptyYear);
        Assert.Equal(7, result.Rows.Count);

        var newYearDay = result.Rows.Single(r => r.Date == new DateOnly(2026, 1, 1));
        Assert.Equal(DutyConstants.HolidayRowTypes.HOLIDAY, newYearDay.DateType);

        var makeup = result.Rows.Single(r => r.Date == new DateOnly(2026, 1, 4));
        Assert.Equal(DutyConstants.HolidayRowTypes.MAKEUP, makeup.DateType);
    }

    [Fact]
    public void ParseTimor_保留除夕与初一的细分节日名()
    {
        var result = HolidayApiParser.ParseTimor(Timor2026);

        // 这是主源优于备用源的关键点：值班表需要"除夕/初一"而非一律"春节"
        Assert.Equal("除夕", result.Rows.Single(r => r.Date == new DateOnly(2026, 2, 16)).Name);
        Assert.Equal("初一", result.Rows.Single(r => r.Date == new DateOnly(2026, 2, 17)).Name);
        Assert.Equal("春节后补班", result.Rows.Single(r => r.Date == new DateOnly(2026, 2, 28)).Name);
    }

    [Fact]
    public void ParseTimor_年份尚未发布_标记为空年份而非损坏()
    {
        var result = HolidayApiParser.ParseTimor(TimorEmptyYear);

        // 2027 实测场景：code=0 + 空 holiday，属"尚未发布"，不是"响应损坏"
        Assert.True(result.IsEmptyYear);
        Assert.False(result.IsMalformed);
        Assert.Empty(result.Rows);
    }

    [Theory]
    [InlineData("not a json at all")]
    [InlineData("{broken")]
    [InlineData("null")]
    public void ParseTimor_响应损坏_标记为Malformed(string json)
    {
        var result = HolidayApiParser.ParseTimor(json);

        Assert.True(result.IsMalformed);
        Assert.False(result.IsEmptyYear);
    }

    [Fact]
    public void ParseTimor_非零错误码_视为异常响应()
    {
        // timor 用非 0 code 表示错误，不能当作"该年无数据"
        var result = HolidayApiParser.ParseTimor("""{"code":400,"holiday":null}""");

        Assert.True(result.IsMalformed);
        Assert.False(result.IsEmptyYear);
    }

    #endregion

    #region holiday-cn（备用源）

    [Fact]
    public void ParseHolidayCn_真实响应_正确区分放假与补班()
    {
        var result = HolidayApiParser.ParseHolidayCn(HolidayCn2026, 2026);

        Assert.False(result.IsMalformed);
        Assert.False(result.IsEmptyYear);
        Assert.Equal(7, result.Rows.Count);

        Assert.Equal(DutyConstants.HolidayRowTypes.HOLIDAY,
            result.Rows.Single(r => r.Date == new DateOnly(2026, 1, 1)).DateType);
        Assert.Equal(DutyConstants.HolidayRowTypes.MAKEUP,
            result.Rows.Single(r => r.Date == new DateOnly(2026, 1, 4)).DateType);
    }

    [Fact]
    public void ParseHolidayCn_提取国务院公文链接作审计线索()
    {
        var result = HolidayApiParser.ParseHolidayCn(HolidayCn2026, 2026);

        Assert.Equal("https://www.gov.cn/zhengce/zhengceku/202511/content_7047091.htm", result.PaperUrl);
    }

    [Fact]
    public void ParseHolidayCn_年份尚未发布_标记为空年份()
    {
        var result = HolidayApiParser.ParseHolidayCn(HolidayCnEmptyYear, 2027);

        Assert.True(result.IsEmptyYear);
        Assert.False(result.IsMalformed);
        Assert.Empty(result.Rows);
    }

    [Fact]
    public void ParseHolidayCn_年份不符_视为空年份并清空公文链接()
    {
        // 备用源返回了别的年份（占位/异常），不能把 A 年数据当成 B 年用
        var result = HolidayApiParser.ParseHolidayCn(HolidayCn2026, 2027);

        Assert.True(result.IsEmptyYear);
        Assert.Empty(result.Rows);
        Assert.Null(result.PaperUrl);
    }

    [Fact]
    public void ParseHolidayCn_跨年调休行_按目标年份过滤()
    {
        // 某些源会把相邻年份的调休行放进同一文件，必须只留目标年份
        var json = """
            { "year": 2027, "papers": [], "days": [
                { "name": "元旦", "date": "2026-12-31", "isOffDay": true },
                { "name": "元旦", "date": "2027-01-01", "isOffDay": true }
            ] }
            """;

        var result = HolidayApiParser.ParseHolidayCn(json, 2027);

        Assert.Single(result.Rows);
        Assert.Equal(new DateOnly(2027, 1, 1), result.Rows[0].Date);
    }

    [Fact]
    public void ParseHolidayCn_响应损坏_标记为Malformed()
    {
        var result = HolidayApiParser.ParseHolidayCn("{broken", 2026);

        Assert.True(result.IsMalformed);
        Assert.False(result.IsEmptyYear);
    }

    #endregion

    #region 双源一致性（主备切换不能改判定结果）

    [Fact]
    public void 双源同日_日期类型判定一致()
    {
        // 备用源的节日名较粗（春节各日均名为"春节"），但放假/补班判定必须与主源完全一致，
        // 否则主备切换会静默改变值班表的排班口径。
        var primary = HolidayApiParser.ParseTimor(Timor2026);
        var fallback = HolidayApiParser.ParseHolidayCn(HolidayCn2026, 2026);

        // HolidayRow 是值类型，不能用 Assert.NotNull（装箱后永远非 null），改用字典显式查缺失
        var primaryByDate = primary.Rows.ToDictionary(r => r.Date, r => r.DateType);

        foreach (var fallbackRow in fallback.Rows)
        {
            Assert.True(primaryByDate.ContainsKey(fallbackRow.Date),
                $"主源缺少备用源存在的日期: {fallbackRow.Date:yyyy-MM-dd}");
            Assert.Equal(primaryByDate[fallbackRow.Date], fallbackRow.DateType);
        }
    }

    #endregion
}
