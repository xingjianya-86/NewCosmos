using System.Globalization;

namespace NewCosmos.Services.Domain.SevereDisease;

/// <summary>
/// 重病费用口径规则（单点实现，AGENTS §14 重病判定权威口径的一部分）。
/// 依据《黑龙江省低收入人口认定办法（暂行）》第六十八条：救助申请之日前 12 个月内，
/// 个人累计负担的住院、门诊慢特病及高值药品等费用超过上年度本地区居民人均可支配收入的，
/// 认定为重病患者。
///
/// - 阈值存 nc_config_standards（standard_type=SevereDiseaseCostThreshold，
///   hukou_type=Rural/Urban 两档），经 IStandardConfigService.GetStandardValueAsync 单点读取；
///   配置缺失/为0 → 不判定（降级人工，禁止硬编码回退值，同 §11/§14 低保标准读取纪律）。
/// - 费用数据为经办人手工录入的"前12个月个人累计自付"（元），App 无发票级明细。
/// - 判定是"超阈值才成立"的确定性规则：只产生勾选建议，不自动取消（人工优先）。
/// </summary>
public static class SevereDiseaseByCostRule
{
    /// <summary>nc_config_standards.standard_type（城乡两档用 hukou_type 区分）</summary>
    public const string CostThresholdStandardType = "SevereDiseaseCostThreshold";

    /// <summary>
    /// 费用口径重病判定：自付 &gt; 阈值 即成立（"超过"=严格大于，等于不算）。
    /// 阈值 ≤0 视为未配置 → 不判定；自付缺失/负数不成立。
    /// </summary>
    public static bool IsSevereByCost(decimal? selfPaid12m, decimal threshold)
    {
        if (threshold <= 0) return false;          // 未配置/配置无效：降级人工
        if (!selfPaid12m.HasValue) return false;
        if (selfPaid12m.Value <= 0) return false;  // 负数/零视为无效录入
        return selfPaid12m.Value > threshold;
    }

    /// <summary>
    /// 建议条文案唯一拼装点（费用口径）。autoChecked=已过闸门自动勾选。
    /// </summary>
    public static string FormatSuggestion(decimal selfPaid12m, decimal threshold, bool isRural, bool autoChecked)
    {
        var paid = selfPaid12m.ToString("N2", CultureInfo.InvariantCulture);
        var cap = threshold.ToString("N2", CultureInfo.InvariantCulture);
        var tier = isRural ? "农村" : "城镇";
        return autoChecked
            ? $"费用口径重病：前12个月个人自付 {paid} 元 > 上年度{tier}居民人均可支配收入 {cap} 元，已自动勾选，可人工取消"
            : $"费用口径重病：前12个月个人自付 {paid} 元 > 上年度{tier}居民人均可支配收入 {cap} 元，请人工确认";
    }
}
