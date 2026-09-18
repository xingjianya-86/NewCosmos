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
    /// 到期预警窗口天数：渐退期将在该天数内到期（含已到期未处理）时触发首页横幅提醒。
    /// 调用点：GetExpiringCountAsync(ExpiringWarningDays)，禁止在调用处写死 30。
    /// </summary>
    public const int EXPIRING_WARNING_DAYS = 30;

    /// <summary>
    /// 验证月数是否在有效范围内
    /// </summary>
    public static bool IsValidMonths(int months)
    {
        return months >= MIN_MONTHS && months <= MAX_MONTHS;
    }
}
