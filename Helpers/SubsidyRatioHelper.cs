using NewCosmos.Models.Entities;

namespace NewCosmos.Helpers;

/// <summary>
/// 农业补贴比例计算说明助手（单点实现，禁止各处重抄）。
/// 金额口径：Amount = 原始金额(面积×单价×数量) × RatioFactor；
/// 汇总 SubsidyTotal 是折算后年值——所有输出此字段处需附条件式说明，
/// 比例 ≠ 100% 时追加「（已按家庭份额XX.XX%折算）」，= 100% 返回空串（老数据零干扰）。
/// </summary>
public static class SubsidyRatioHelper
{
    /// <summary>
    /// 计算有效折算比例（0~1）。
    /// 原始金额优先用行内存量 OriginalAmount（老数据为 0 时回退 面积×单价×Count 计算）；
    /// 无明细或原始金额合计为 0 时返回 1（视为未折算，不产生说明）。
    /// </summary>
    public static decimal GetEffectiveRatio(IEnumerable<Subsidy>? subsidies)
    {
        if (subsidies == null) return 1m;

        decimal originalTotal = 0;
        decimal amountTotal = 0;
        foreach (var item in subsidies)
        {
            var original = item.OriginalAmount > 0
                ? item.OriginalAmount
                : Math.Round(item.Area * item.UnitPrice * item.Count, 2);
            originalTotal += original;
            amountTotal += item.Amount;
        }

        if (originalTotal <= 0) return 1m;
        return amountTotal <= 0 ? 0m : amountTotal / originalTotal;
    }

    /// <summary>
    /// 条件式内联注记：比例 ≈ 100%（±0.01% 容差，规避逐行舍入误差）返回空串，
    /// 否则返回「（已按家庭份额XX.XX%折算）」。
    /// </summary>
    public static string BuildNote(IEnumerable<Subsidy>? subsidies)
        => BuildNote(GetEffectiveRatio(subsidies));

    /// <summary>按已算好的比例（0~1）生成条件式注记，见 <see cref="BuildNote(IEnumerable{Subsidy}?)"/>。</summary>
    public static string BuildNote(decimal ratio)
    {
        var percent = Math.Round(ratio * 100, 2);
        if (percent >= 99.99m) return string.Empty;
        return $"（已按家庭份额{percent:F2}%折算）";
    }
}
