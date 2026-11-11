using NewCosmos.Services.Domain.SocialAssistance;
using Xunit;

namespace NewCosmos.Tests.Domain;

/// <summary>
/// 刚性支出困难家庭三条件（权威：RigidExpenditureRule 类注释，2026-11 上级口径）。
/// 锁点：① 占比分母=毛收入、开区间(50%,60%)；② 净人均 &lt; 低保×2；
/// ③ 扣减后人均=净人均（仅扣一次）&gt; 低保×1.5，严格大于；分项毛与(净+刚×12)差&gt;1分须标记；毛为0不除零。
/// 角点样例取上级给定数据：人均毛收入 22752~24802 元/年、刚性支出 11376~13426 元/年（按 1 人户代入人均值，
/// 多人户按人数等比缩放等价）。
/// </summary>
public class RigidExpenditureRuleTests
{
    /// <summary>农村低保月标准 632 元（2026-04 起 seed 配置；边缘标准 = ×1.5 = 948 元/月 = 11376 元/年）</summary>
    private const decimal MonthlyStandard = 632m;

    /// <summary>按年值构造（净 = 毛 − 刚性年值，保证分项一致性恒成立），刚性按月值传入（主表语义）</summary>
    private static RigidExpenditureRule.Evaluation ByAnnual(decimal grossAnnual, decimal rigidAnnual, int size = 1) =>
        RigidExpenditureRule.Evaluate(grossAnnual, grossAnnual - rigidAnnual, rigidAnnual / 12m, size, MonthlyStandard);

    // ── 上级数据四角点 ──

    [Fact]
    public void 角点_22752与11376_占比恰50且扣减后恰等于11376_不合格()
    {
        var ev = ByAnnual(22752m, 11376m);
        Assert.True(ev.RatioTooLow);      // 948/1896 = 50.0%，开区间拒
        Assert.True(ev.DeductionNotMet);  // 净 11376/12 = 948，非严格大于
        Assert.False(ev.IsEligible);
    }

    [Fact]
    public void 角点_24802与11376_占比低于50_不合格()
    {
        var ev = ByAnnual(24802m, 11376m);
        Assert.True(ev.RatioTooLow);      // 45.87%
        Assert.False(ev.DeductionNotMet); // 净 13426 → 1118.83 > 948
        Assert.False(ev.IsEligible);
    }

    [Fact]
    public void 角点_24802与13426_扣减后恰等于11376_严格大于卡掉()
    {
        var ev = ByAnnual(24802m, 13426m);
        Assert.False(ev.RatioTooLow);     // 54.13% 在窗口内
        Assert.False(ev.RatioTooHigh);
        Assert.True(ev.DeductionNotMet);  // 净恰 11376 → 948 ≤ 948
        Assert.False(ev.IsEligible);
    }

    [Fact]
    public void 角点_22752与13426_扣减后低于边缘标准_不合格()
    {
        var ev = ByAnnual(22752m, 13426m);
        Assert.False(ev.RatioTooLow);     // 59.01% 在窗口内
        Assert.False(ev.RatioTooHigh);
        Assert.True(ev.DeductionNotMet);  // 净 9326 → 777.17 ≤ 948
        Assert.False(ev.IsEligible);
    }

    [Fact]
    public void 合格带_24802与12800_三条件全过()
    {
        var ev = ByAnnual(24802m, 12800m);
        Assert.False(ev.RatioTooLow);     // 51.61%
        Assert.False(ev.RatioTooHigh);
        Assert.False(ev.DeductionNotMet); // 净 12002 → 1000.17 > 948
        Assert.False(ev.IncomeCapExceeded);
        Assert.False(ev.ComponentsInconsistent);
        Assert.True(ev.IsEligible);
    }

    [Fact]
    public void 多人户_与1人户人均等价_同样合格()
    {
        // 同一组人均值放大到 2 人户（毛/刚性/净均 ×2）
        var ev = ByAnnual(49604m, 25600m, size: 2);
        Assert.Equal(2066.83m, Math.Round(ev.GrossPerCapitaMonthly, 2)); // 人均毛 24802/12 = 2066.83
        Assert.True(ev.IsEligible);
    }

    // ── 占比窗口边界（开区间） ──

    [Fact]
    public void 占比恰50_百分位_不合格()
    {
        var ev = RigidExpenditureRule.Evaluate(24000m, 12000m, 1000m, 1, MonthlyStandard);
        Assert.True(ev.RatioTooLow);
        Assert.False(ev.IsEligible);
    }

    [Fact]
    public void 占比略高于50_合格()
    {
        var ev = RigidExpenditureRule.Evaluate(24000.12m, 12000m, 1000.01m, 1, MonthlyStandard);
        Assert.False(ev.RatioTooLow);
        Assert.False(ev.RatioTooHigh);
        Assert.True(ev.IsEligible);
    }

    [Fact]
    public void 占比恰60_百分位_不合格()
    {
        // 人均毛 24000/12 = 2000、人均刚性 1200 → 恰 60.0%（取整除值避免除法舍入歧义）
        var ev = RigidExpenditureRule.Evaluate(24000m, 9600m, 1200m, 1, MonthlyStandard);
        Assert.True(ev.RatioTooHigh);
        Assert.False(ev.IsEligible);
    }

    [Fact]
    public void 占比略低于60_比率位通过()
    {
        var ev = RigidExpenditureRule.Evaluate(19999.88m, 8000m, 999.99m, 1, MonthlyStandard);
        Assert.False(ev.RatioTooHigh);
    }

    // ── 扣减后阈值边界（> 948 元/月 = 11376 元/年，严格大于） ──

    [Fact]
    public void 扣减后恰948每月_不合格()
    {
        var ev = RigidExpenditureRule.Evaluate(23376m, 11376m, 1000m, 1, MonthlyStandard);
        Assert.False(ev.RatioTooLow);      // 1000/1948 = 51.3% 在窗口
        Assert.True(ev.DeductionNotMet);   // 948 ≤ 948
        Assert.False(ev.IsEligible);
    }

    [Fact]
    public void 扣减后948点01每月_合格()
    {
        var ev = RigidExpenditureRule.Evaluate(23376.12m, 11376.12m, 1000m, 1, MonthlyStandard);
        Assert.False(ev.DeductionNotMet);  // 948.01 > 948
        Assert.True(ev.IsEligible);
    }

    // ── 收入上限边界（净 < 低保×2 = 1264 元/月，维持原状） ──

    [Fact]
    public void 净人均恰2倍低保_上限超线_不合格()
    {
        var ev = RigidExpenditureRule.Evaluate(33168m, 15168m, 1500m, 1, MonthlyStandard);
        Assert.True(ev.IncomeCapExceeded); // 1264 ≥ 1264
        Assert.False(ev.IsEligible);
    }

    [Fact]
    public void 净人均差0点01不到2倍低保_合格()
    {
        var ev = RigidExpenditureRule.Evaluate(33167.88m, 15167.88m, 1500m, 1, MonthlyStandard);
        Assert.False(ev.IncomeCapExceeded); // 1263.99 < 1264
        Assert.True(ev.IsEligible);
    }

    // ── 分项完整性守卫（分项求和法配套） ──

    [Fact]
    public void 分项毛与净额加刚性一致_不标记()
    {
        var ev = RigidExpenditureRule.Evaluate(24000m, 12000m, 1000m, 1, MonthlyStandard);
        Assert.False(ev.ComponentsInconsistent);
    }

    [Fact]
    public void 分项毛与净额加刚性差超1分_标记()
    {
        // 期望毛 = 10000 + 1000×12 = 22000，实为 24000（分项缺失/串档典型症状）
        var ev = RigidExpenditureRule.Evaluate(24000m, 10000m, 1000m, 1, MonthlyStandard);
        Assert.True(ev.ComponentsInconsistent);
    }

    [Fact]
    public void 毛收入为0_不除零_走占比不足拒绝()
    {
        var ev = RigidExpenditureRule.Evaluate(0m, 0m, 500m, 1, MonthlyStandard);
        Assert.Equal(0m, ev.Ratio);
        Assert.True(ev.RatioTooLow);
        Assert.False(ev.IsEligible);
    }

    [Fact]
    public void 家庭人数为0_按1人兜底不除零()
    {
        var ev = RigidExpenditureRule.Evaluate(24000m, 12000m, 1000m, 0, MonthlyStandard);
        Assert.Equal(2000m, ev.GrossPerCapitaMonthly);
    }
}
