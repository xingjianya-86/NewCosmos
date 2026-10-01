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
    /// C线：临时救助验收线（公示固定每月10日~12日；入户调查核实倒推公示开始前 10/5 个工作日；验收=公示结束次一个工作日）
    /// </summary>
    TempRelief
}
