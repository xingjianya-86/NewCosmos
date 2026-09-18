using NewCosmos.Models.Exceptions;

namespace NewCosmos.Models.Options;

/// <summary>
/// 数据库配置选项
/// </summary>
public class DatabaseOptions
{
    /// <summary>
    /// 数据库类型    /// </summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// 主机地址
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// 端口
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// 数据库名称    /// </summary>
    public string DatabaseName { get; set; } = string.Empty;

    /// <summary>
    /// 用户名    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// 密码
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// pg_dump 工具路径（为空时自动探测：应用内置 → 系统 PATH）
    /// </summary>
    public string PgDumpPath { get; set; } = string.Empty;

    /// <summary>
    /// 连接超时时间（秒）    /// </summary>
    public int ConnectionTimeout { get; set; }

    /// <summary>
    /// 命令超时时间（秒）    /// </summary>
    public int CommandTimeout { get; set; }

    /// <summary>
    /// 最大连接池大小
    /// </summary>
    public int MaxPoolSize { get; set; }

    /// <summary>
    /// 最小连接池大小
    /// </summary>
    public int MinPoolSize { get; set; }

    /// <summary>
    /// 是否为生产环境    /// </summary>
    public bool IsProduction { get; set; }

    /// <summary>
    /// 应用程序名称
    /// </summary>
    public string ApplicationName { get; set; } = string.Empty;

    /// <summary>
    /// SSL模式
    /// </summary>
    public string SslMode { get; set; } = string.Empty;

    /// <summary>
    /// 是否信任服务器证书    /// </summary>
    public bool TrustServerCertificate { get; set; }

    /// <summary>
    /// 是否包含错误详情
    /// </summary>
    public bool IncludeErrorDetail { get; set; }

    /// <summary>
    /// 验证配置是否完整
    /// </summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
            throw new ConfigurationException("DatabaseOptions.Host", "数据库主机地址未配置");

        if (Port <= 0)
            throw new ConfigurationException("DatabaseOptions.Port", "数据库端口未配置或无效");

        if (string.IsNullOrWhiteSpace(DatabaseName))
            throw new ConfigurationException("DatabaseOptions.DatabaseName", "数据库名称未配置");

        if (string.IsNullOrWhiteSpace(Username))
            throw new ConfigurationException("DatabaseOptions.Username", "数据库用户名未配置");

        if (string.IsNullOrWhiteSpace(Password))
            throw new ConfigurationException("DatabaseOptions.Password", "数据库密码未配置");
    }
}