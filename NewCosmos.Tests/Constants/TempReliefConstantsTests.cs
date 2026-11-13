using NewCosmos.Constants;
using Xunit;

namespace NewCosmos.Tests.Constants;

/// <summary>
/// 临时救助定额：户籍→低保标准解析、确定金额（可下调、不得超上限）校验。
/// </summary>
public class TempReliefConstantsTests
{
    [Theory]
    [InlineData("Rural", "RuralSubsistenceStandard")]
    [InlineData("rural", "RuralSubsistenceStandard")]
    [InlineData("农村", "RuralSubsistenceStandard")]
    [InlineData("农村户口", "RuralSubsistenceStandard")]
    [InlineData("Urban", "UrbanSubsistenceStandard")]
    [InlineData("城镇", "UrbanSubsistenceStandard")]
    [InlineData("城市户口", "UrbanSubsistenceStandard")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("未知", null)]
    public void ResolveSubsistenceStandardType_兼容多来源户籍写法(string? hukouType, string? expected)
        => Assert.Equal(expected, TempReliefConstants.ResolveSubsistenceStandardType(hukouType));

    [Theory]
    [InlineData("332", 332)]
    [InlineData("332.5", 332.5)]
    [InlineData("632", 632)]
    [InlineData("632.00", 632)]
    [InlineData("332元", 332)]
    [InlineData(" 500 ", 500)]
    [InlineData("332.555", 332.56)]
    [InlineData("", 0)]
    [InlineData("abc", 0)]
    [InlineData("0", 0)]
    [InlineData("-1", 0)]
    public void TryParseConfirmAmount_解析与舍入(string? input, decimal expected)
    {
        Assert.Equal(expected > 0, TempReliefConstants.TryParseConfirmAmount(input, out var amount));
        Assert.Equal(expected, amount);
    }

    [Theory]
    [InlineData("632", 632, true)]
    [InlineData("332", 632, true)]
    [InlineData("827", 827, true)]
    [InlineData("828", 827, false)]
    [InlineData("633", 632, false)]
    [InlineData("0", 632, false)]
    [InlineData("", 632, false)]
    [InlineData("99999", 0, true)] // 上限未知（标准缺失）时不拦上限
    public void TryValidateConfirmAmount_不得超低保月标准(string? input, int max, bool expected)
    {
        var actual = TempReliefConstants.TryValidateConfirmAmount(input, max, out _, out var error);
        Assert.Equal(expected, actual);
        Assert.Equal(expected, error is null);
    }
}
