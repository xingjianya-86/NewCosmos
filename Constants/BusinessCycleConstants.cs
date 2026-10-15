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
}
