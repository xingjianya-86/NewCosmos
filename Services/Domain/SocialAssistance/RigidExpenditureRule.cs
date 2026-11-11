using NewCosmos.Constants;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>
/// 刚性支出困难家庭三条件求值（判定与 H4 停保告知书共用单点，AGENTS §14 分类认定权威在 ClassificationService，
/// 本类是其可测化的纯函数拆出——两处调用方必须共用，否则文书数字与判定打架）。
///
/// 口径（2026-11 上级确认，详见 plan\20261111_刚性支出困难家庭判定口径对齐.md）：
/// ① 占比 = 人均刚性 ÷ 人均毛收入 ∈ (50%, 60%)，开区间，**分母为毛收入**（旧实现分母为净收入，
///    等效毛基数 33.3%~37.5% 窗口，与政策口径不符）；
/// ② 净人均 &lt; 低保标准 × 2（维持原状，净基准）；
/// ③ 扣除刚性支出后人均 &gt; 低保标准 × 1.5（边缘标准，**严格大于**）。
///    净 = 毛 − 刚性，"扣减后人均"即净人均本身——仅扣一次，禁止再减（2026-11 前的实现重复扣减）。
///
/// 毛年收入由调用方经 IIncomeCalculationService.CalculateGrossAnnualFamilyIncome 单点取数（§9.4），
/// 本类只接收算好的年值，保持纯函数可直测。
/// </summary>
public static class RigidExpenditureRule
{
    /// <summary>
    /// 求值结果。ComponentsInconsistent = 分项毛收入与（落库净额 + 刚性×12）差异超过 1 分，
    /// 多见于历史导入/变更链档案分项缺失——失效方向为保守拒绝（分母小→占比高→判不过），调用方须记 Warn 留痕。
    /// </summary>
    public sealed record Evaluation(
        decimal GrossPerCapitaMonthly,
        decimal NetPerCapitaMonthly,
        decimal Ratio,
        bool ComponentsInconsistent,
        bool RatioTooLow,
        bool RatioTooHigh,
        bool IncomeCapExceeded,
        bool DeductionNotMet)
    {
        /// <summary>三条件同时满足（占比开区间 + 收入上限 + 扣减后严格大于边缘标准）</summary>
        public bool IsEligible => !RatioTooLow && !RatioTooHigh && !IncomeCapExceeded && !DeductionNotMet;
    }

    /// <summary>
    /// 求值（纯函数，无 IO）。
    /// grossAnnual/netAnnual 为年值权威口径；rigidMonthly 为月值（主表 rigid_expenditure 语义，§9.5）；
    /// 比较基于精确月人均（年÷12÷人数，不经已舍入月值，与 ClassificationService 层间分界同源）。
    /// </summary>
    public static Evaluation Evaluate(
        decimal grossAnnual,
        decimal netAnnual,
        decimal rigidMonthly,
        int familySize,
        decimal monthlyStandard)
    {
        var size = familySize > 0 ? familySize : 1;
        var grossPerCapita = grossAnnual / 12m / size;
        var netPerCapita = netAnnual / 12m / size;
        var rigidPerCapita = rigidMonthly / size;

        // 占比 = 人均刚性 ÷ 人均毛收入；毛为 0 时占比记 0（走 RatioTooLow 拒绝，不除零）
        var ratio = grossPerCapita > 0 ? rigidPerCapita / grossPerCapita : 0m;

        var expectedGross = netAnnual + rigidMonthly * 12m;

        return new Evaluation(
            GrossPerCapitaMonthly: grossPerCapita,
            NetPerCapitaMonthly: netPerCapita,
            Ratio: ratio,
            ComponentsInconsistent: Math.Abs(grossAnnual - expectedGross) > 0.01m,
            RatioTooLow: ratio <= 0.5m,
            RatioTooHigh: ratio >= 0.6m,
            IncomeCapExceeded: netPerCapita >= monthlyStandard * 2m,
            DeductionNotMet: netPerCapita <= monthlyStandard * ClassificationConstants.LowIncomeMultiplier);
    }
}
