using NewCosmos.Models.Exceptions;

namespace NewCosmos.Models.Options;

public class SchemaOptions
{
    public string CurrentVersion { get; set; } = string.Empty;
    public string InitializedAt { get; set; } = string.Empty;
    public string InitializedBy { get; set; } = string.Empty;
    public string TablePrefixFilter { get; set; } = string.Empty;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(CurrentVersion))
            throw new ConfigurationException("Schema.CurrentVersion", "当前版本号未配置");
    }

    public bool IsInitialized => !string.IsNullOrWhiteSpace(InitializedAt);
}