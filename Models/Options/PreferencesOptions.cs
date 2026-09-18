using NewCosmos.Models.Exceptions;

namespace NewCosmos.Models.Options;

/// <summary>
/// Preferences键名配置选项
/// </summary>
public class PreferencesOptions
{
    public string RememberMeKey { get; set; } = string.Empty;
    public string RememberedUsernameKey { get; set; } = string.Empty;
    public string RememberedPasswordKey { get; set; } = string.Empty;
    public string RememberPasswordKey { get; set; } = string.Empty;
    public string LastBackgroundKey { get; set; } = string.Empty;

    /// <summary>
    /// 验证配置是否完整
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(RememberMeKey))
            throw new ConfigurationException("PreferencesOptions.RememberMeKey", "RememberMe键名未配置");

        if (string.IsNullOrWhiteSpace(RememberedUsernameKey))
            throw new ConfigurationException("PreferencesOptions.RememberedUsernameKey", "RememberedUsername键名未配置");

        if (string.IsNullOrWhiteSpace(RememberedPasswordKey))
            throw new ConfigurationException("PreferencesOptions.RememberedPasswordKey", "RememberedPassword键名未配置");

        if (string.IsNullOrWhiteSpace(RememberPasswordKey))
            throw new ConfigurationException("PreferencesOptions.RememberPasswordKey", "RememberPassword键名未配置");
    }
}