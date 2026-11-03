using NewCosmos.Constants;
using NewCosmos.Services.Domain.ElderlyBenefits;
using Xunit;

namespace NewCosmos.Tests.Domain;

/// <summary>
/// 高龄补发分段计算（ElderlyPaybackCalculator 唯一实现）。
/// 锁点：起算 = max(政策开始 2024-10, 满80周岁次月)；判龄按【上月最后一天】（生日当月不计）；
/// 分段按月标准调价切段，总额 = Σ(段月标准×段月数)。
/// </summary>
public class ElderlyPaybackCalculatorTests
{
    [Theory]
    // 判龄基准 = monthStart 前一天（上月末）：生日当月尚未满龄
    [InlineData(1940, 6, 15, 2024, 6, 83)]   // 2024-06 的上月末=05-31，未过 6 月生日 → 83
    [InlineData(1940, 6, 15, 2024, 7, 84)]   // 2024-07 的上月末=06-30，已过生日 → 84
    [InlineData(1940, 6, 15, 2024, 5, 83)]   // 2024-05 上月末=04-30 → 83
    [InlineData(2000, 1, 1, 2024, 1, 23)]    // 生日当月（2024-01 上月末 2023-12-31）→ 23 而非 24
    public void GetAgeAtMonth_按上月末判龄_生日当月不计(int y, int m, int d, int my, int mm, int expected)
    {
        var age = ElderlyPaybackCalculator.GetAgeAtMonth(new DateTime(y, m, d), new DateTime(my, mm, 1));
        Assert.Equal(expected, age);
    }

    [Fact]
    public void GetCategoryForAge_年龄档位映射()
    {
        Assert.Equal(ElderlyBenefitConstants.Cat100Plus, ElderlyPaybackCalculator.GetCategoryForAge(100, null));
        Assert.Equal(ElderlyBenefitConstants.Cat100Plus, ElderlyPaybackCalculator.GetCategoryForAge(105, null));
        Assert.Equal(ElderlyBenefitConstants.Cat90To99, ElderlyPaybackCalculator.GetCategoryForAge(90, null));
        Assert.Equal(ElderlyBenefitConstants.Cat90To99, ElderlyPaybackCalculator.GetCategoryForAge(99, null));
        // 80-89 且无高补身份 → 其他高龄
        Assert.Equal(ElderlyBenefitConstants.CatOtherElderly, ElderlyPaybackCalculator.GetCategoryForAge(85, null));
    }

    [Fact]
    public void Calculate_起算于满80次月_非政策起点()
    {
        // 1944-11-15 生日 → 满80 = 2024-11-15 → 次月起算 2024-12（晚于政策 2024-10）
        var r = ElderlyPaybackCalculator.Calculate(
            new DateTime(1944, 11, 15), null, "2025-02", _ => 50m);

        Assert.Equal("2024-12", r.StartMonth);
        Assert.Equal("2025-02", r.EndMonth);
        Assert.Equal(3, r.TotalMonths);      // 2024-12, 2025-01, 2025-02
        Assert.Equal(150m, r.TotalAmount);   // 50 × 3
    }

    [Fact]
    public void Calculate_满80早于政策开始_以政策起点起算()
    {
        // 1930 年生：满80 在 2010 年，远早于政策 2024-10 → 起算 2024-10
        var r = ElderlyPaybackCalculator.Calculate(
            new DateTime(1930, 1, 1), null, "2024-12", _ => 60m);

        Assert.Equal("2024-10", r.StartMonth);
        Assert.Equal(3, r.TotalMonths);      // 2024-10, 11, 12
        Assert.Equal(180m, r.TotalAmount);
    }

    [Fact]
    public void Calculate_跨年龄档自动切段_各段金额分算()
    {
        // 关键语义：getMonthlyAmount 的入参是【分类码】而非月份（标准按档配置）。
        // 1935-05-01 生日：2024-10..2025-05 为 89 岁（其他高龄档），2025-06 上月末满 90 → 进 90-99 档。
        var r = ElderlyPaybackCalculator.Calculate(
            new DateTime(1935, 5, 1), null, "2025-06",
            category => category == ElderlyBenefitConstants.Cat90To99 ? 80m : 50m);

        Assert.Equal(9, r.TotalMonths);      // 2024-10 .. 2025-06
        Assert.Equal(480m, r.TotalAmount);   // 50×8（89岁）+ 80×1（90岁档）
        Assert.Equal(2, r.Segments.Count);   // 跨档必须切段
        Assert.Equal(r.TotalAmount, r.Segments.Sum(s => s.SegmentAmount));
        Assert.Equal(ElderlyBenefitConstants.Cat90To99, r.Segments.Last().CategoryCode);
    }
}
