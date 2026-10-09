using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>
/// 收入计算服务接口
/// 【年值基准·财政会计口径】家庭收入以年为权威基准，月值=年值÷12 仅作分解显示；
/// 禁止从舍入月值×12 反推年值（round-trip 误差）。
/// 主表存储语义：work/business/property/transfer/other/rigid 为月值；
/// alimony_annual/land/subsidy 为年值；total_annual_income/per_capita_annual_income 为年值。
/// </summary>
public interface IIncomeCalculationService
{
    /// <summary>
    /// 计算年家庭总收入 = Σ(月项×12) + 赡养年值 + 土地年值 + 补贴年值 − 刚性支出×12（一次舍入到分）
    /// </summary>
    decimal CalculateAnnualFamilyIncome(
        decimal workIncome,
        decimal businessNetIncome,
        decimal propertyIncome,
        decimal transferIncome,
        decimal otherIncome,
        decimal alimonyAnnual,
        decimal landIncome,
        decimal subsidyTotal,
        decimal rigidExpenditure);

    /// <summary>
    /// 计算年家庭毛收入 = Σ(月项×12) + 赡养年值 + 土地年值 + 补贴年值（不扣刚性支出，一次舍入到分）。
    /// 供绑定 getter 等热路径调用（无日志）；净额口径仍以 CalculateAnnualFamilyIncome 为准。
    /// </summary>
    decimal CalculateGrossAnnualFamilyIncome(
        decimal workIncome,
        decimal businessNetIncome,
        decimal propertyIncome,
        decimal transferIncome,
        decimal otherIncome,
        decimal alimonyAnnual,
        decimal landIncome,
        decimal subsidyTotal);

    /// <summary>
    /// 计算月值 = 年值÷12（分解显示口径，一次舍入）
    /// </summary>
    decimal MonthlyFromAnnual(decimal annualIncome);

    /// <summary>
    /// 计算年人均收入 = 年总收入 / 家庭人数
    /// </summary>
    decimal CalculatePerCapitaAnnual(decimal annualIncome, int familySize);

    /// <summary>
    /// 计算月人均收入 = 年总收入 / 家庭人数 / 12（一次舍入）
    /// </summary>
    decimal PerCapitaMonthly(decimal annualIncome, int familySize);

    /// <summary>
    /// 计算月务工收入 = Σ(每人月收入)
    /// </summary>
    decimal CalculateLaborIncome(List<LaborIncome> incomes);

    /// <summary>
    /// 计算月经营净收入 = Σ(每人月收入)
    /// </summary>
    decimal CalculateBusinessNetIncome(List<BusinessIncome> incomes);

    /// <summary>
    /// 计算年土地收入 = 自种面积×单价 + 转包面积×单价 + 承包面积×单价
    /// </summary>
    decimal CalculateLandIncome(
        decimal selfFarmedArea, double selfFarmedPrice,
        decimal subleasedArea, double subleasedPrice,
        decimal contractedArea, double contractedPrice);

    /// <summary>
    /// 计算年农业补贴 = Σ(面积 × 单价 × 数量 × 比例系数)
    /// </summary>
    decimal CalculateSubsidyIncome(List<Subsidy> subsidies);

    /// <summary>
    /// 计算年赡养费收入 = Σ(每笔年赡养费)（月费×月数×供养家庭人数，逐笔已舍入到分）
    /// </summary>
    decimal CalculateAlimonyAnnual(List<Supporter> supporters);

    /// <summary>
    /// 计算月刚性支出 = Σ(月支出金额)
    /// </summary>
    decimal CalculateRigidExpenditure(List<RigidExpenditure> expenditures);
}
