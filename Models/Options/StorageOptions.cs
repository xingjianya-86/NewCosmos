using NewCosmos.Models.Exceptions;

namespace NewCosmos.Models.Options;

/// <summary>
/// 存储配置选项
/// </summary>
public class StorageOptions
{
    /// <summary>
    /// 备份目录名称
    /// </summary>
    public string BackupDirectoryName { get; set; } = string.Empty;

    /// <summary>
    /// 日志目录名称
    /// </summary>
    public string LogDirectoryName { get; set; } = string.Empty;

    /// <summary>
    /// 临时目录名称
    /// </summary>
    public string TempDirectoryName { get; set; } = string.Empty;

    /// <summary>
    /// 是否使用AppData目录
    /// </summary>
    public bool UseAppDataDirectory { get; set; }

    /// <summary>
    /// 应用名称（从AppOptions注入）    /// </summary>
    public string ApplicationName { get; set; } = string.Empty;

    /// <summary>
    /// 获取备份路径（使用用户文档目录）
    /// </summary>
    public string GetBackupPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            ApplicationName,
            BackupDirectoryName);
    }

    /// <summary>
    /// 获取日志路径
    /// </summary>
    public string GetLogPath()
    {
        return UseAppDataDirectory
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ApplicationName, LogDirectoryName)
            : Path.Combine(AppContext.BaseDirectory, LogDirectoryName);
    }

    /// <summary>
    /// 获取导出路径
    /// </summary>
    public string GetExportPath()
    {
        return UseAppDataDirectory
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ApplicationName, "Exports")
            : Path.Combine(AppContext.BaseDirectory, "Exports");
    }

    /// <summary>
    /// 获取临时目录路径
    /// </summary>
    public string GetTempPath(string? subDirectory = null)
    {
        var basePath = Path.Combine(Path.GetTempPath(), ApplicationName);
        return string.IsNullOrEmpty(subDirectory)
            ? basePath
            : Path.Combine(basePath, subDirectory);
    }

    /// <summary>
    /// 验证配置是否完整
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(BackupDirectoryName))
            throw new ConfigurationException("StorageOptions.BackupDirectoryName", "备份目录名称未配置");

        if (string.IsNullOrWhiteSpace(ApplicationName))
            throw new ConfigurationException("StorageOptions.ApplicationName", "应用名称未配置");
    }
}
