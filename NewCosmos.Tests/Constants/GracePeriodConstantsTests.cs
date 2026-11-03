using NewCosmos.Constants;
using Xunit;

namespace NewCosmos.Tests.Constants;

/// <summary>
/// 渐退期常量口径（AGENTS §14：GracePeriodConstants 为唯一权威）：
/// 范围 1–12 月、默认 6 月、起算=业务归属月的次月 1 日、结束=起算月+月数-1 的当月末。
/// </summary>
public class GracePeriodConstantsTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(6, true)]
    [InlineData(12, true)]
    [InlineData(13, false)]
    [InlineData(-1, false)]
    public void IsValidMonths_边界(int months, bool expected)
        => Assert.Equal(expected, GracePeriodConstants.IsValidMonths(months));

    [Fact]
    public void IsValidMonths_默认月数在有效范围内()
        => Assert.True(GracePeriodConstants.IsValidMonths(GracePeriodConstants.DEFAULT_MONTHS));

    [Fact]
    public void CalculatePeriod_起算于业务归属月次月1日()
    {
        var (start, end) = GracePeriodConstants.CalculatePeriod(2026, 3, 6);

        // 业务归属月 2026-03 → 起算 2026-04-01
        Assert.Equal(new DateTime(2026, 4, 1), start);
        // 6 个月 → 2026-04-01 起共 6 个整月，止于 2026-09-30
        Assert.Equal(new DateTime(2026, 9, 30), end);
    }

    [Fact]
    public void CalculatePeriod_跨年边界_12月()
    {
        var (start, end) = GracePeriodConstants.CalculatePeriod(2026, 11, 12);
        Assert.Equal(new DateTime(2026, 12, 1), start);
        Assert.Equal(new DateTime(2027, 11, 30), end);
    }

    [Fact]
    public void CalculatePeriod_最短1月_止于次月末()
    {
        var (start, end) = GracePeriodConstants.CalculatePeriod(2026, 1, 1);
        Assert.Equal(new DateTime(2026, 2, 1), start);
        Assert.Equal(new DateTime(2026, 2, 28), end);
    }
}
