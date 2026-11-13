namespace NewCosmos.Models.Enums;

/// <summary>
/// 业务时间线类
/// </summary>
public enum TimelineType
{
    /// <summary>A线：经济核查（每月10号结算，上月11日~本月10日）</summary>
    EconomicReview,

    /// <summary>B线：业务线（结算日由 app.ini BCycleSettleDay 配置，默认20号；受理窗口上月(结算日+1)~本月(结算日+1)）</summary>
    BusinessProcess,

    /// <summary>
    /// C线：临时救助逐笔 5 天时间曲线（2026-11-13 起，不再锚定每月 13 号）——
    /// 以申请日 D 为锚：公示起 = D+1、公示止 = D+3（恒 3 天）、验收日 = D+4（= 公示止 + 1 日历日，均日历日、不规避休息日）；
    /// 入户调查窗口 = [申请日 D, 公示起]。
    /// 月度批次仅作标签：申请日 ≤ 当月 13 号归本月（13 号非工作日顺延），补打窗口 (上月A, 本月A]，与公示/验收脱钩。
    /// </summary>
    TempRelief
}
