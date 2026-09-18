using NewCosmos.Models.Exceptions;

namespace NewCosmos.Models.Options;

/// <summary>
/// 性能配置选项
/// </summary>
public class PerformanceOptions
{
    /// <summary>
    /// 最大重试次数    /// </summary>
    public int MaxRetries { get; set; }

    /// <summary>
    /// 重试延迟（毫秒）
    /// </summary>
    public int RetryDelayMilliseconds { get; set; }

    /// <summary>
    /// 是否使用指数退避    /// </summary>
    public bool ExponentialBackoff { get; set; }

    /// <summary>
    /// 慢操作阈值（毫秒）
    /// </summary>
    public int SlowOperationThresholdMs { get; set; }

    /// <summary>
    /// 打印任务超时（分钟）
    /// </summary>
    public int PrintTaskTimeoutMinutes { get; set; }

    /// <summary>
    /// 打印重试基础延迟（毫秒）
    /// </summary>
    public int PrintRetryBaseDelayMs { get; set; }

    /// <summary>
    /// 打印重试增量延迟（毫秒）
    /// </summary>
    public int PrintRetryIncrementMs { get; set; }

    /// <summary>
    /// 权限缓存时长（分钟）
    /// </summary>
    public int PermissionCacheMinutes { get; set; }

    /// <summary>
    /// 权限版本检查间隔（秒）
    /// </summary>
    public int PermissionVersionCheckIntervalSeconds { get; set; }

    /// <summary>
    /// 验证配置是否完整
    /// </summary>
    public void Validate()
    {
        if (MaxRetries <= 0)
            throw new ConfigurationException("PerformanceOptions.MaxRetries", "最大重试次数未配置");

        if (RetryDelayMilliseconds <= 0)
            throw new ConfigurationException("PerformanceOptions.RetryDelayMilliseconds", "重试延迟未配置");

        if (SlowOperationThresholdMs <= 0)
            throw new ConfigurationException("PerformanceOptions.SlowOperationThresholdMs", "慢操作阈值未配置");

        if (PrintTaskTimeoutMinutes <= 0)
            throw new ConfigurationException("PerformanceOptions.PrintTaskTimeoutMinutes", "打印任务超时未配置");

        if (PrintRetryBaseDelayMs <= 0)
            throw new ConfigurationException("PerformanceOptions.PrintRetryBaseDelayMs", "打印重试基础延迟未配置");

        if (PrintRetryIncrementMs <= 0)
            throw new ConfigurationException("PerformanceOptions.PrintRetryIncrementMs", "打印重试增量延迟未配置");

        if (PermissionCacheMinutes <= 0)
            throw new ConfigurationException("PerformanceOptions.PermissionCacheMinutes", "权限缓存时长未配置");

        if (PermissionVersionCheckIntervalSeconds <= 0)
            throw new ConfigurationException("PerformanceOptions.PermissionVersionCheckIntervalSeconds", "权限版本检查间隔未配置");
    }
}