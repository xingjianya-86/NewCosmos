using NewCosmos.Constants;
using NewCosmos.Models.Exceptions;

namespace NewCosmos.Models.Options;

/// <summary>
/// 应用配置选项
/// </summary>
public class AppOptions
{
    /// <summary>
    /// 应用版本    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// 应用名称
    /// </summary>
    public string ApplicationName { get; set; } = string.Empty;

    /// <summary>
    /// 窗口标题
    /// </summary>
    public string WindowTitle { get; set; } = string.Empty;

    /// <summary>
    /// B 线（业务线/月报）统计结算日：月报周期 = [上月(结算日+1)日, 本月(结算日+1)日)。
    /// 默认 15（周期 [上月16, 本月16)）；上级要求业务截止 20 号时配置为 20（周期 [上月21, 本月21)）。
    /// 启动时由 App 注入 BusinessCycleHelper。
    /// </summary>
    public int BCycleSettleDay { get; set; } = BusinessCycleConstants.DefaultSettleDay;

    /// <summary>
    /// 验证配置是否完整
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Version))
            throw new ConfigurationException("AppOptions.Version", "应用版本号未配置");

        if (string.IsNullOrWhiteSpace(ApplicationName))
            throw new ConfigurationException("AppOptions.ApplicationName", "应用名称未配置");

        if (string.IsNullOrWhiteSpace(WindowTitle))
            throw new ConfigurationException("AppOptions.WindowTitle", "窗口标题未配置");
    }
}