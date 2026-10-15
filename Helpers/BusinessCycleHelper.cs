using NewCosmos.Constants;

namespace NewCosmos.Helpers;

/// <summary>
/// B 线结算日参数（app.ini 的 BCycleSettleDay，启动时注入）的唯一事实来源。
/// ⚠️ 周期权威口径在 <c>Services.Utilities.BusinessTimelineService</c>（含结算日遇法定节假日提前的惩罚）：
/// 周期 = [上月(结算日+1)日, 本月结算日]（结算日当天仍属本周期）。
/// 本类 GetStart/GetEnd 只是"无节假日修正"的纯日期运算；统计/月报/档案一律走 BusinessTimelineService，
/// 禁止再引用 GetStart/GetEnd 计算统计区间（曾导致统计周期与档案公示窗口两套口径分裂）。
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
