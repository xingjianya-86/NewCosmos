using NewCosmos.Constants;

namespace NewCosmos.Helpers;

/// <summary>
/// 保障金变更登记阈值（户月保障金差额 ≥ 阈值才登记"保障金变更"）的进程内唯一事实来源。
/// 启动时由 App 从 app.ini 的 FundChangeThreshold 注入（默认 10 元/月）。
/// 所有涉及"是否登记保障金变更/是否进月报增减发/文书增发减发格"的判定必须引用本类，
/// 禁止在调用点内联复制阈值数字（分类施保变动不适用本阈值——固定档位标准，一档就是真钱）。
/// </summary>
public static class FundChangeThresholdHelper
{
    /// <summary>当前生效阈值（元/月；进程内固定，启动时注入）</summary>
    public static decimal Threshold { get; private set; } = FundChangeThresholdConstants.DefaultFundChangeThreshold;

    /// <summary>
    /// 启动时注入阈值：非法值回退默认并告警（不阻断启动）。
    /// </summary>
    public static void Configure(decimal threshold)
    {
        if (threshold < FundChangeThresholdConstants.MinThreshold
            || threshold > FundChangeThresholdConstants.MaxThreshold)
        {
            Serilog.Log.Warning("[FundChangeThresholdHelper] 非法阈值配置 {Threshold}，回退默认 {Default}",
                threshold, FundChangeThresholdConstants.DefaultFundChangeThreshold);
            Threshold = FundChangeThresholdConstants.DefaultFundChangeThreshold;
            return;
        }

        Threshold = threshold;
    }

    /// <summary>差额是否达到登记门槛（|diff| ≥ 阈值）。分类施保变动不要用本方法判断。</summary>
    public static bool MeetsThreshold(decimal amountDiff) => System.Math.Abs(amountDiff) >= Threshold;
}
