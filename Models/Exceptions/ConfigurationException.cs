namespace NewCosmos.Models.Exceptions;

/// <summary>
/// 配置异常
/// </summary>
public class ConfigurationException : Exception
{
    public string ConfigKey { get; }
    
    public ConfigurationException(string configKey, string message) 
        : base($"配置错误 [{configKey}]: {message}")
    {
        ConfigKey = configKey;
    }
    
    public ConfigurationException(string configKey, string message, Exception innerException) 
        : base($"配置错误 [{configKey}]: {message}", innerException)
    {
        ConfigKey = configKey;
    }
}