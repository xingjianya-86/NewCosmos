namespace NewCosmos.Models.Enums;

/// <summary>
/// 业务时间线类    /// </summary>
public enum TimelineType
{
    /// <summary>A线：经济核查（每月10号结算，上月11日~本月10日）</summary>
    EconomicReview,

    /// <summary>B线：业务线（结算日由 app.ini BCycleSettleDay 配置，默认20号；受理窗口上月(结算日+1)~本月(结算日+1)）</summary>
    BusinessProcess,

    /// <summary>
    /// C线：临时救助验收线（验收日固定每月13号，非工作日顺延至其后最近工作日；
    /// 公示止=验收日前1天、公示起=验收日前3天（均日历日）；入户调查窗口=[验收日前5天, 公示起日]）
    /// </summary>
    TempRelief
}
