namespace NewCosmos.Constants;

/// <summary>
/// 渐退期常量
/// </summary>
public static class GracePeriodConstants
{
    /// <summary>
    /// 默认渐退期月数
    /// </summary>
    public const int DEFAULT_MONTHS = 6;

    /// <summary>
    /// 最小渐退期月数
    /// </summary>
    public const int MIN_MONTHS = 1;

    /// <summary>
    /// 最大渐退期月数
    /// </summary>
    public const int MAX_MONTHS = 12;

    /// <summary>
    /// 到期预警窗口天数（首页横幅口径：N 天内到期 + 已到期才提醒）。
    /// 调用点：MainViewModel → GracePeriodService.GetWarningCountAsync(ExpiringWarningDays)，
    /// 禁止在调用处写死 30。
    /// 注意：渐退期管理页/两处胶囊走 GetExpiringCountAsync（进行中+已到期全量），不适用本窗口。
    /// </summary>
    public const int EXPIRING_WARNING_DAYS = 30;

    /// <summary>
    /// 验证月数是否在有效范围内
    /// </summary>
    public static bool IsValidMonths(int months)
    {
        return months >= MIN_MONTHS && months <= MAX_MONTHS;
    }

    /// <summary>
    /// 渐退期区间计算（唯一实现，禁止调用点另抄公式）：
    /// 起算日 = 业务所属年月（B线归属月）的次月 1 日；结束日 = 起算日 + months 个月 − 1 天。
    /// 业务归属月须由 BusinessTimelineService.GetTimelineForDate(date, BusinessProcess) 的 Year/Month 传入
    /// （周期含结算日提前惩罚，与月报统计同源），禁止直接用日历月——曾因"按点判定当天的日历月起算"
    /// 导致跨结算日登记的业务渐退期错位（档案80 卢永华：9-25 登记、10-09 办结属10月业务，
    /// 却按 9 月日历月起算成 2026-10-01，正确应为 2026-11-01）。见 docs\20261016_渐退期起算口径改按业务月.md。
    /// </summary>
    public static (DateTime StartDate, DateTime EndDate) CalculatePeriod(int businessYear, int businessMonth, int months)
    {
        months = Math.Clamp(months, MIN_MONTHS, MAX_MONTHS);
        var start = new DateTime(businessYear, businessMonth, 1).AddMonths(1);
        return (start, start.AddMonths(months).AddDays(-1));
    }
}
