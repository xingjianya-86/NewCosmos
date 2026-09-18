namespace NewCosmos.Constants;

/// <summary>
/// B 线（业务线/月报）统计周期参数：
/// 周期 = [上月(结算日+1)日 00:00, 本月(结算日+1)日 00:00)；
/// 默认结算日 15（周期 [上月16, 本月16)），可由 app.ini 的 BCycleSettleDay 覆盖
/// （上级要求业务截止 20 号 → 配置 20，周期 [上月21, 本月21)）。
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
