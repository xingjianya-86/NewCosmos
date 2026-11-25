using NewCosmos.Constants;
using Xunit;

namespace NewCosmos.Tests.Constants;

/// <summary>
/// 两项政策（临时救助）：城市低保基数 × 遇困人数 × 档位系数，进一法到元。
/// 对照 Rust `nc-core::temp_relief` 的 P0-1/P0-2 验收用例。
/// </summary>
public class TempReliefPolicyTierTests
{
    private const decimal Base = 827m;

    [Theory]
    [InlineData(TempReliefConstants.TierSmall50, 0.5)]
    [InlineData(TempReliefConstants.TierSmall75, 0.75)]
    [InlineData(TempReliefConstants.TierSmall100, 1)]
    public void TierMultiplier_政策三档(string code, decimal expected)
        => Assert.Equal(expected, TempReliefConstants.TierMultiplier(code));

    [Theory]
    [InlineData("")]
    [InlineData("R1_2")]
    [InlineData("S49")]
    public void TierMultiplier_未知档位返回零(string code)
        => Assert.Equal(0m, TempReliefConstants.TierMultiplier(code));

    [Fact]
    public void ComputeSmallTotal_三人七五档进一法到元()
    {
        // 3 × 827 × 0.75 = 1860.75 → 1861
        Assert.Equal(1861m, TempReliefConstants.ComputeSmallTotal(3, Base, TempReliefConstants.TierSmall75));
        Assert.Equal(2481m, TempReliefConstants.ComputeSmallTotal(4, Base, TempReliefConstants.TierSmall75));
        // 2 × 827 × 0.5 = 827.00（整数不变）
        Assert.Equal(827m, TempReliefConstants.ComputeSmallTotal(2, Base, TempReliefConstants.TierSmall50));
        // 1 × 827 × 0.75 = 620.25 → 621
        Assert.Equal(621m, TempReliefConstants.ComputeSmallTotal(1, Base, TempReliefConstants.TierSmall75));
    }

    [Theory]
    [InlineData(TempReliefConstants.TierSmall50, 414)]
    [InlineData(TempReliefConstants.TierSmall75, 621)]
    [InlineData(TempReliefConstants.TierSmall100, 827)]
    public void GetSmallTierCeiling_单人档位上限(string code, decimal expected)
        => Assert.Equal(expected, TempReliefConstants.GetSmallTierCeiling(Base, 1, code));

    [Theory]
    [InlineData("413.50", TempReliefConstants.TierSmall50, true)]
    [InlineData("414.00", TempReliefConstants.TierSmall50, true)]
    [InlineData("414.01", TempReliefConstants.TierSmall50, false)]
    [InlineData("620.25", TempReliefConstants.TierSmall75, true)]
    [InlineData("621.00", TempReliefConstants.TierSmall75, true)]
    [InlineData("621.01", TempReliefConstants.TierSmall75, false)]
    [InlineData("827.00", TempReliefConstants.TierSmall100, true)]
    [InlineData("827.01", TempReliefConstants.TierSmall100, false)]
    [InlineData("100", TempReliefConstants.TierSmall50, true)] // 允许下调
    [InlineData("0", TempReliefConstants.TierSmall50, false)]
    [InlineData("", TempReliefConstants.TierSmall50, false)]
    public void TryValidateSmallAmount_上限与下调(string? input, string tier, bool expected)
    {
        var actual = TempReliefConstants.TryValidateSmallAmount(input, Base, 1, tier, out _, out var error);
        Assert.Equal(expected, actual);
        Assert.Equal(expected, error is null);
    }

    [Fact]
    public void TryValidateSmallAmount_人数上限按总额()
    {
        // 3 人 75% 档上限 = 1861；1861 通过，1861.01 拒绝
        Assert.True(TempReliefConstants.TryValidateSmallAmount(
            "1861.00", Base, 3, TempReliefConstants.TierSmall75, out _, out _));
        Assert.False(TempReliefConstants.TryValidateSmallAmount(
            "1861.01", Base, 3, TempReliefConstants.TierSmall75, out _, out _));
    }

    [Fact]
    public void TryValidateSmallAmount_缺基数或缺档位显式失败()
    {
        Assert.False(TempReliefConstants.TryValidateSmallAmount(
            "100", 0m, 1, TempReliefConstants.TierSmall50, out _, out var noBase));
        Assert.Contains("城市低保月标准", noBase);

        Assert.False(TempReliefConstants.TryValidateSmallAmount(
            "100", Base, 1, null, out _, out var noTier));
        Assert.Contains("请选择小额快救档位", noTier);
    }

    [Theory]
    [InlineData(3, 5, 3)]
    [InlineData(0, 5, 5)]
    [InlineData(null, 5, 5)]
    [InlineData(0, null, 1)]
    [InlineData(null, null, 1)]
    public void ResolveReliefPersonCount_显式值优先否则家庭人口兜底(int? personCount, int? familySize, int expected)
        => Assert.Equal(expected, TempReliefConstants.ResolveReliefPersonCount(personCount, familySize));
}
