using NewCosmos.Models.Entities;
using Xunit;

namespace NewCosmos.Tests.Models;

/// <summary>
/// 赡养年费乘数口径测试（2026-11-12 用户裁决）：
/// 年赡养费 = 月赡养费 × 月数 × **申请表家庭人口**（CalcFamilySize），
/// 赡养人自己家庭的人口（SupporterFamilySize）不参与计算（信息字段）。
/// CalcFamilySize=0（外部直读实体未下发）时回退赡养人家庭人口，保持历史行为。
/// 典型值：王兴久 126.40×12×2 = 3033.60/人。
/// </summary>
public class SupporterAnnualFeeTests
{
    private static Supporter NewSupporter(decimal monthly = 126.40m, int months = 12, int ownFamily = 1, int calcFamily = 0)
        => new()
        {
            MonthlySupportFee = monthly,
            SupportMonths = months,
            SupporterFamilySize = ownFamily,
            CalcFamilySize = calcFamily
        };

    [Xunit.Fact]
    public void 年费乘数_取申请表家庭人口()
    {
        var s = NewSupporter(calcFamily: 2);
        Assert.Equal(3033.60m, s.AnnualSupportFee);
    }

    [Xunit.Fact]
    public void 未下发时回退赡养人家庭人口_保持历史行为()
    {
        var s = NewSupporter(ownFamily: 3, calcFamily: 0);
        Assert.Equal(4550.40m, s.AnnualSupportFee);
    }

    [Xunit.Fact]
    public void 申请表家庭人口优先于赡养人自家人口()
    {
        // 裁决核心：自家 3 口、申请表 2 口 → 按 2 算（6067.20 场景的单人值）
        var s = NewSupporter(ownFamily: 3, calcFamily: 2);
        Assert.Equal(3033.60m, s.AnnualSupportFee);
    }

    [Xunit.Fact]
    public void 家庭人口变化_年费联动重算()
    {
        var s = NewSupporter(calcFamily: 2);
        s.CalcFamilySize = 3;
        Assert.Equal(4550.40m, s.AnnualSupportFee);

        s.CalcFamilySize = 1;
        Assert.Equal(1516.80m, s.AnnualSupportFee);
    }

    [Xunit.Fact]
    public void 赡养人自家人口变化_不改变年费()
    {
        var s = NewSupporter(ownFamily: 2, calcFamily: 2);
        Assert.Equal(3033.60m, s.AnnualSupportFee);

        s.SupporterFamilySize = 5;   // Step2 编辑信息字段 → 触发重算但乘数仍是申请表人口
        Assert.Equal(3033.60m, s.AnnualSupportFee);
    }

    [Xunit.Fact]
    public void 月费月数参与计算_月数缺省按1()
    {
        var s = NewSupporter(monthly: 100m, months: 0, calcFamily: 2);
        Assert.Equal(200m, s.AnnualSupportFee);   // 100×1×2

        s.SupportMonths = 6;
        Assert.Equal(1200m, s.AnnualSupportFee);  // 100×6×2
    }
}
