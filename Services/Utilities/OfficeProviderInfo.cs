#if WINDOWS
namespace NewCosmos.Services.Utilities;

/// <summary>
/// 办公软件提供    /// </summary>
public enum OfficeProvider
{
    None,
    WPS,
    Office,
    Both
}

/// <summary>
/// 办公软件提供商信    /// </summary>
public class OfficeProviderInfo
{
    public OfficeProvider Provider { get; set; }
    public string? WordProgId { get; set; } = string.Empty;
    public string? ExcelProgId { get; set; } = string.Empty;
    public string? InstallPath { get; set; } = string.Empty;
    public string? Version { get; set; } = string.Empty;
    public bool HasWord => !string.IsNullOrEmpty(WordProgId);
    public bool HasExcel => !string.IsNullOrEmpty(ExcelProgId);
}
#endif
