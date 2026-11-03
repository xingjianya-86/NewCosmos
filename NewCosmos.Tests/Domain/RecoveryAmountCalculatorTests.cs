using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Services.Domain.Recovery;
using Xunit;

namespace NewCosmos.Tests.Domain;

/// <summary>
/// 追缴应追缴总额分段计算（AGENTS §14：RecoveryAmountCalculator 为分段唯一实现，
/// 口径权威 docs/20261019_追缴金额来源与分段计算.md）。
/// 锁点：非法区间/超 36 月转人工、起始缺标准转人工、时间轴分段累加、无时间轴退化 Flat、总额一次舍入到分。
/// </summary>
public class RecoveryAmountCalculatorTests
{
    private static RecoveryMonthlyStandardPoint P(string month, decimal amount) =>
        new() { EffectiveMonth = month, MonthlyAmount = amount };

    [Theory]
    [InlineData(null, "2026-03")]
    [InlineData("2026-13", "2026-03")]
    [InlineData("2026-01", "abc")]
    public void 解析失败的起止月_不可自动计算_返回区间无效(string? start, string? end)
    {
        var r = RecoveryAmountCalculator.Calculate(start, end, new List<RecoveryMonthlyStandardPoint> { P("2025-01", 100) });
        Assert.False(r.CanCalculate);
        Assert.Equal(RecoveryConstants.REASON_INVALID_RANGE, r.Reason);
    }

    [Fact]
    public void 止早于起_不可自动计算_返回月数为零()
    {
        var r = RecoveryAmountCalculator.Calculate("2026-03", "2026-01",
            new List<RecoveryMonthlyStandardPoint> { P("2025-01", 100) });
        Assert.False(r.CanCalculate);
        Assert.Equal(RecoveryConstants.REASON_NO_MONTHS, r.Reason);
    }

    [Fact]
    public void 超过36个月_不可自动计算()
    {
        // 2020-01 .. 2023-06 = 42 个月 > MaxAutoMonths(36)
        var r = RecoveryAmountCalculator.Calculate("2020-01", "2023-06",
            new List<RecoveryMonthlyStandardPoint> { P("2019-01", 100) });
        Assert.False(r.CanCalculate);
        Assert.False(string.IsNullOrEmpty(r.Reason));
    }

    [Fact]
    public void 起始月之前无任何月标准_不可自动计算()
    {
        // 时间轴从 2026-02 才开始，起算月 2026-01 缺标准 → 历史数据不全，转人工填写
        var r = RecoveryAmountCalculator.Calculate("2026-01", "2026-03",
            new List<RecoveryMonthlyStandardPoint> { P("2026-02", 100) });
        Assert.False(r.CanCalculate);
        Assert.False(string.IsNullOrEmpty(r.Reason));
    }

    [Fact]
    public void 单一时间轴标准_按月数累加()
    {
        var r = RecoveryAmountCalculator.Calculate("2026-01", "2026-03",
            new List<RecoveryMonthlyStandardPoint> { P("2025-01", 100) });

        Assert.True(r.CanCalculate);
        Assert.Equal(3, r.TotalMonths);
        Assert.Equal(300m, r.TotalAmount);
    }

    [Fact]
    public void 时间轴标准变化_自动切段_各段金额分算()
    {
        // 2026-01 用 100（2025-01 生效），2026-02 起用 200（2026-02 生效），区间 2026-01..2026-03
        var r = RecoveryAmountCalculator.Calculate("2026-01", "2026-03",
            new List<RecoveryMonthlyStandardPoint> { P("2025-01", 100), P("2026-02", 200) });

        Assert.True(r.CanCalculate);
        Assert.Equal(3, r.TotalMonths);
        Assert.Equal(500m, r.TotalAmount);   // 100×1 + 200×2
        Assert.True(r.Segments.Count >= 2);  // 标准变化必须切段
        Assert.Equal(r.TotalAmount, r.Segments.Sum(s => s.MonthlyAmount * s.Months));
    }

    [Fact]
    public void 无时间轴但有兜底月额_退化Flat口径()
    {
        var r = RecoveryAmountCalculator.Calculate("2026-01", "2026-03", timeline: null, fallbackMonthlyAmount: 50m);

        Assert.True(r.CanCalculate);
        Assert.Equal(RecoveryConstants.AMOUNT_SOURCE_FLAT, r.AmountSource);
        Assert.Equal(3, r.TotalMonths);
        Assert.Equal(150m, r.TotalAmount);
    }

    [Fact]
    public void 无时间轴且无兜底月额_不可自动计算()
    {
        var r = RecoveryAmountCalculator.Calculate("2026-01", "2026-03", timeline: null, fallbackMonthlyAmount: 0m);
        Assert.False(r.CanCalculate);
        Assert.False(string.IsNullOrEmpty(r.Reason));
    }

    [Fact]
    public void 总额一次舍入到分_不从段金额二次舍入()
    {
        // 每月 33.333 元：逐月全精度累计后一次舍入（§9 铁律同精神）
        var r = RecoveryAmountCalculator.Calculate("2026-01", "2026-03",
            new List<RecoveryMonthlyStandardPoint> { P("2025-01", 33.333m) });

        Assert.True(r.CanCalculate);
        Assert.Equal(100m, r.TotalAmount);   // 99.999 → 100.00
    }
}
