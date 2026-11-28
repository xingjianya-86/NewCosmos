using NewCosmos.Constants;
using NewCosmos.Services.Utilities;
using Xunit;

namespace NewCosmos.Tests.Constants;

/// <summary>
/// 临时救助申请日期口径（2026-11-25）：未来最多提前 1 个月；LZ 编号日期段跟随申请日期。
/// </summary>
public class TempReliefApplyDateTests
{
    private static readonly DateTime Today = new(2026, 11, 25);

    [Fact]
    public void UpperBound_今天加一个月()
        => Assert.Equal(new DateTime(2026, 12, 25), TempReliefConstants.GetApplyDateUpperBound(Today));

    [Theory]
    [InlineData(2026, 10, 1, true)]
    [InlineData(2026, 11, 25, true)]
    [InlineData(2026, 12, 25, true)]  // 上限含当日
    [InlineData(2026, 12, 26, false)] // 超过一天即拒绝
    public void IsApplyDateWithinRange_边界(int year, int month, int day, bool expected)
        => Assert.Equal(expected,
            TempReliefConstants.IsApplyDateWithinRange(new DateTime(year, month, day), Today));

    [Fact]
    public void BuildApplicationNo_按日期与序号拼号()
    {
        Assert.Equal("LZ202612010007",
            TempReliefConstants.BuildApplicationNo(new DateTime(2026, 12, 1), 7));
        Assert.Equal("LZ202612010000",
            TempReliefConstants.BuildApplicationNo(new DateTime(2026, 12, 1), 0));
    }

    [Fact]
    public void Curve_未来日期按逐笔五天曲线推算()
    {
        var d = new DateTime(2026, 12, 1);
        var curve = BusinessTimelineService.BuildTempReliefCurveCore(d);

        Assert.Equal(new DateTime(2026, 12, 2), curve.PublicityStartDate);
        Assert.Equal(new DateTime(2026, 12, 4), curve.PublicityEndDate);
        Assert.Equal(new DateTime(2026, 12, 5), curve.AuditDate);
        Assert.Equal(d, curve.InvestigationStartDate);
    }
}
