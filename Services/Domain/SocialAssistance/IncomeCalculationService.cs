using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>
/// 收入计算服务实现
/// 【年值基准·财政会计口径】
/// 1. 家庭收入以"年"为权威基准：年值 = Σ(月项×12) + 赡养年值 + 土地年值 + 补贴年值 − 刚性支出×12，
///    先汇总、最后一次性舍入到分（禁止分阶段舍入）。
/// 2. 月值只是年值的分解显示：月均 = 年值÷12（显示时舍入）；
///    永远禁止从已舍入的月值×12 反推年值（会造成 0.01~0.09 的 round-trip 误差）。
/// 3. 计算过程不提前舍入：赡养/补贴等明细保留全精度，仅最终汇总处舍入到分。
/// 4. 主表存储语义：work/business/property/transfer/other/rigid_expenditure 为月值；
///    alimony_income/land_income_total/subsidy_total 为年值；
///    total_annual_income/per_capita_annual_income 为年值；total_family_income/per_capita_income 为月值（=年值÷12）。
/// </summary>
public class IncomeCalculationService : BaseService, IIncomeCalculationService
{
    protected override string ServiceName => "IncomeCalculationService";

    public IncomeCalculationService(ILoggerService logger) : base(logger)
    {
    }

    /// <summary>
    /// 计算年家庭总收入 = Σ(月项×12) + 赡养年值 + 土地年值 + 补贴年值 − 刚性支出×12
    /// 【年值基准】一次性舍入到分。alimonyAnnual/landIncome/subsidyTotal 为年值，其余为月值。
    /// </summary>
    public decimal CalculateAnnualFamilyIncome(
        decimal workIncome,
        decimal businessNetIncome,
        decimal propertyIncome,
        decimal transferIncome,
        decimal otherIncome,
        decimal alimonyAnnual,
        decimal landIncome,
        decimal subsidyTotal,
        decimal rigidExpenditure)
    {
        var annual = workIncome * 12
                   + businessNetIncome * 12
                   + propertyIncome * 12
                   + transferIncome * 12
                   + otherIncome * 12
                   + alimonyAnnual
                   + landIncome
                   + subsidyTotal
                   - rigidExpenditure * 12;

        var result = Math.Round(annual, 2);
        LogInfo($"年收入计算: 务工×12={workIncome * 12:F2}, 经营×12={businessNetIncome * 12:F2}, " +
                $"财产×12={propertyIncome * 12:F2}, 转移×12={transferIncome * 12:F2}, 其他×12={otherIncome * 12:F2}, " +
                $"赡养年值={alimonyAnnual:F2}, 土地年值={landIncome:F2}, 补贴年值={subsidyTotal:F2}, " +
                $"刚性×12={rigidExpenditure * 12:F2}, 年总收入={result:F2}");
        return result;
    }

    /// <summary>
    /// 计算月值 = 年值÷12（分解显示口径，一次舍入）
    /// </summary>
    public decimal MonthlyFromAnnual(decimal annualIncome)
    {
        return Math.Round(annualIncome / 12m, 2);
    }

    /// <summary>
    /// 计算年人均收入 = 年总收入 / 家庭人数
    /// </summary>
    public decimal CalculatePerCapitaAnnual(decimal annualIncome, int familySize)
    {
        if (familySize <= 0) return 0;
        return Math.Round(annualIncome / familySize, 2);
    }

    /// <summary>
    /// 计算月人均收入 = 年总收入 / 家庭人数 / 12（一次舍入，避免二次舍入误差）
    /// </summary>
    public decimal PerCapitaMonthly(decimal annualIncome, int familySize)
    {
        if (familySize <= 0) return 0;
        return Math.Round(annualIncome / familySize / 12m, 2);
    }

    /// <summary>
    /// 计算月务工收入 = Σ(每人月收入)
    /// 【月收入体系】直接取各务工人员的月收入之和，不再乘以工作月数
    /// </summary>
    public decimal CalculateLaborIncome(List<LaborIncome> incomes)
    {
        decimal total = 0;
        foreach (var item in incomes)
        {
            var monthlyIncome = item.MonthlyIncome ?? 0;
            item.AnnualIncome = monthlyIncome; // 存储为月收入
            total += monthlyIncome;
        }
        return total;
    }

    /// <summary>
    /// 计算月经营净收入 = Σ(每人月收入)
    /// 【月收入体系】直接取各经营人员的月收入之和，不再乘以12
    /// </summary>
    public decimal CalculateBusinessNetIncome(List<BusinessIncome> incomes)
    {
        decimal total = 0;
        foreach (var item in incomes)
        {
            total += item.MonthlyIncome ?? 0;
        }
        return total;
    }

    /// <summary>
    /// 计算年土地收入 = 自种面积×单价 + 转包面积×单价 + 承包面积×单价
    /// 【年收入】土地收入按年计算，不除以12
    /// </summary>
    public decimal CalculateLandIncome(
        decimal selfFarmedArea, double selfFarmedPrice,
        decimal subleasedArea, double subleasedPrice,
        decimal contractedArea, double contractedPrice)
    {
        var annualIncome = selfFarmedArea * (decimal)selfFarmedPrice
                         + subleasedArea * (decimal)subleasedPrice
                         + contractedArea * (decimal)contractedPrice;

        return Math.Round(annualIncome, 2);
    }

    /// <summary>
    /// 计算年农业补贴 = Σ(面积 × 单价 × 数量 × 比例系数)
    /// 【年收入】补贴按年计算，不除以12
    /// </summary>
    public decimal CalculateSubsidyIncome(List<Subsidy> subsidies)
    {
        decimal annualTotal = 0;
        foreach (var item in subsidies)
        {
            item.CalculateAmount();
            annualTotal += item.Amount;
        }
        return annualTotal;
    }

    /// <summary>
    /// 计算年赡养费收入 = Σ(每笔年赡养费)（月费×月数×供养家庭人数）
    /// 【年收入】赡养费按年核算，逐笔年费已舍入到分，求和后不再舍入
    /// </summary>
    public decimal CalculateAlimonyAnnual(List<Supporter> supporters)
    {
        decimal total = 0;
        foreach (var item in supporters)
        {
            if (!item.IsSupportAbility) continue;
            total += item.AnnualSupportFee;
        }
        return total;
    }

    /// <summary>
    /// 计算月刚性支出 = Σ(月支出金额)
    /// 【月收入体系】支出为月支出
    /// </summary>
    public decimal CalculateRigidExpenditure(List<RigidExpenditure> expenditures)
    {
        decimal total = 0;
        foreach (var item in expenditures)
        {
            total += item.Amount;
        }
        return total;
    }
}
