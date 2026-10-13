namespace NewCosmos.Constants;

/// <summary>
/// 保障金变更登记阈值（动态管理停旧建新后，旧/新档同属低保群体时是否登记保障金变更）。
/// 差额 = |新档户月保障金 − 旧档户月保障金|（户月口径，不含分类施保/照料费）。
/// ≥ 阈值 → 写"保障金变更登记"记录（进月报增减发/增减员调整表/告知书）；
/// &lt; 阈值 → 照常停旧建新、新档按新金额，但不登记（不进月报、文书两格 0.00），审计快照保留。
/// 进程内生效值由 app.ini 的 FundChangeThreshold 覆盖（见 Helpers\FundChangeThresholdHelper）。
/// 分类施保为固定档位标准，不适用本阈值——只要有变动一律另行保存记录。
/// </summary>
public static class FundChangeThresholdConstants
{
    /// <summary>默认阈值：10 元/月</summary>
    public const decimal DefaultFundChangeThreshold = 10m;

    /// <summary>阈值下限（含）：0 表示"任何差额都登记"</summary>
    public const decimal MinThreshold = 0m;

    /// <summary>阈值上限（含）：超过 1 万元/月的差额仍应登记，仅防配置误填天文数字</summary>
    public const decimal MaxThreshold = 10000m;
}
