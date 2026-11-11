namespace NewCosmos.Constants;

/// <summary>
/// B 线（业务线/月报）统计周期参数：
/// 周期权威口径在 Services.Utilities.BusinessTimelineService = [上月(结算日+1)日, 本月结算日]
/// （结算日当天仍属本周期；结算日遇法定节假日提前5天）。
/// 结算日默认 15，可由 app.ini 的 BCycleSettleDay 覆盖（上级要求业务截止 20 号 → 配置 20）。
/// </summary>
public static class BusinessCycleConstants
{
    /// <summary>默认结算日：15 号</summary>
    public const int DefaultSettleDay = 15;

    /// <summary>结算日下限</summary>
    public const int MinSettleDay = 1;

    /// <summary>结算日上限（周期终点=结算日+1 需 ≤ 28，避免 2 月越界）</summary>
    public const int MaxSettleDay = 27;

    /// <summary>
    /// 批次顺延提示（工作流列表卡片角标）：{0}=年 {1}=月。
    /// 仅当按业务锚点（入户调查日期优先）判定为顺延时显示；判定唯一入口 BatchDeferralHelper。
    /// </summary>
    public const string BatchDeferredChipFormat = "本批已顺延至 {0}年{1}月";

    /// <summary>
    /// 批次顺延提示（申请表单 Step4 常驻，信息密度更高）：
    /// {0}=年 {1}=月 {2}=会议日 {3}=公示起 {4}=公示止。与文书公示窗口同批次。
    /// </summary>
    public const string BatchDeferredDetailFormat = "本批已顺延至 {0}年{1}月（会议 {2:MM-dd} · 公示 {3:MM-dd} 至 {4:MM-dd}）";
}
