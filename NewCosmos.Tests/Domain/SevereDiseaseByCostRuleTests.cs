using NewCosmos.Services.Domain.SevereDisease;
using Xunit;

namespace NewCosmos.Tests.Domain;

/// <summary>
/// 费用口径重病规则（SevereDiseaseByCostRule）——黑龙政办法第六十八条单点实现口径锚点：
/// 前12个月个人累计自付 &gt; 上年度居民人均可支配收入（城乡两档）即重病患者。
/// </summary>
public class SevereDiseaseByCostRuleTests
{
    private const decimal Rural = 29381m;   // 2025 农村居民人均可支配收入
    private const decimal Urban = 41757m;   // 2025 城镇居民人均可支配收入

    [Xunit.Fact]
    public void 超过阈值_成立()
        => Assert.True(SevereDiseaseByCostRule.IsSevereByCost(29381.01m, Rural));

    [Xunit.Fact]
    public void 恰好等于阈值_不成立()
        // 第六十八条措辞为"超过"——等于不算（边界口径防回潮）
        => Assert.False(SevereDiseaseByCostRule.IsSevereByCost(Rural, Rural));

    [Xunit.Fact]
    public void 低于阈值_不成立()
        => Assert.False(SevereDiseaseByCostRule.IsSevereByCost(29380.99m, Rural));

    [Xunit.Fact]
    public void 城镇档_用城镇阈值()
    {
        Assert.False(SevereDiseaseByCostRule.IsSevereByCost(30000m, Urban));
        Assert.True(SevereDiseaseByCostRule.IsSevereByCost(41757.01m, Urban));
    }

    [Theory]
    [InlineData(0)]      // 配置缺失（GetStandardValueAsync 缺行返回0）
    [InlineData(-1)]
    public void 阈值未配置_一律不判定(decimal threshold)
        => Assert.False(SevereDiseaseByCostRule.IsSevereByCost(999999m, threshold));

    [Xunit.Fact]
    public void 自付为0_录入无效_不成立()     // 录入0=无效输入
        => Assert.False(SevereDiseaseByCostRule.IsSevereByCost(0m, Rural));

    [Xunit.Fact]
    public void 自付为负_录入无效_不成立()   // 负数=无效输入
        => Assert.False(SevereDiseaseByCostRule.IsSevereByCost(-5m, Rural));

    [Xunit.Fact]
    public void 自付未录入_不成立()
        => Assert.False(SevereDiseaseByCostRule.IsSevereByCost(null, Rural));

    [Xunit.Fact]
    public void 建议文案_含金额与城乡档()
    {
        var text = SevereDiseaseByCostRule.FormatSuggestion(45000m, Urban, isRural: false, autoChecked: true);
        Assert.Contains("45,000.00", text);
        Assert.Contains("41,757.00", text);
        Assert.Contains("城镇", text);
        Assert.Contains("已自动勾选", text);

        var suggest = SevereDiseaseByCostRule.FormatSuggestion(30000m, Rural, isRural: true, autoChecked: false);
        Assert.Contains("农村", suggest);
        Assert.Contains("请人工确认", suggest);
    }
}
