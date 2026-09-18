using NewCosmos.Constants;

namespace NewCosmos.Helpers;

/// <summary>
/// B 线业务统计周期（月报/仪表盘统一口径）的唯一事实来源：
/// 周期 = [上月(结算日+1) 日 00:00（含）, 本月(结算日+1) 日 00:00（不含）)。
/// 默认结算日 15（例：2026-08 → [2026-07-16, 2026-08-16)）；
/// 上级业务截止 20 号时由 app.ini 的 BCycleSettleDay=20 覆盖为 [上月21, 本月21)。
/// 所有涉及"B 线周期"的统计必须引用本类，禁止在调用点内联复制日期运算。
/// </summary>
public static class BusinessCycleHelper
{
    /// <summary>当前生效的结算日（进程内固定；启动时由 App 从 app.ini 注入）</summary>
    public static int SettleDay { get; private set; } = BusinessCycleConstants.DefaultSettleDay;

    /// <summary>
    /// 启动时注入结算日：非法值回退默认并告警（不阻断启动）。
    /// </summary>
    public static void Configure(int settleDay)
    {
        if (settleDay is < BusinessCycleConstants.MinSettleDay or > BusinessCycleConstants.MaxSettleDay)
        {
            Serilog.Log.Warning("[BusinessCycleHelper] 非法结算日配置 {Day}，回退默认 {Default}",
                settleDay, BusinessCycleConstants.DefaultSettleDay);
            SettleDay = BusinessCycleConstants.DefaultSettleDay;
            return;
        }

        SettleDay = settleDay;
    }

    /// <summary>周期起点：上月(结算日+1) 日 00:00（含）</summary>
    public static DateTime GetStart(int year, int month) =>
        new DateTime(year, month, 1).AddMonths(-1).AddDays(SettleDay);

    /// <summary>周期终点：本月(结算日+1) 日 00:00（不含）</summary>
    public static DateTime GetEnd(int year, int month) =>
        new DateTime(year, month, 1).AddDays(SettleDay);

    /// <summary>当前时刻所属周期的起点与终点（便捷重载）</summary>
    public static (DateTime Start, DateTime End) GetCurrentCycle()
    {
        var now = DateTime.Now;
        return (GetStart(now.Year, now.Month), GetEnd(now.Year, now.Month));
    }
}
