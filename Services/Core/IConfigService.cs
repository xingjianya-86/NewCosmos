using NewCosmos.Models.Options;

namespace NewCosmos.Services.Core;

/// <summary>
/// 配置服务接口
/// </summary>
public interface IConfigService
{
    /// <summary>
    /// 获取应用配置
    /// </summary>
    AppOptions GetAppOptions();

    /// <summary>
    /// 获取数据库配    /// </summary>
    DatabaseOptions GetDatabaseOptions();

    /// <summary>
    /// 获取存储配置
    /// </summary>
    StorageOptions GetStorageOptions();

    /// <summary>
    /// 获取UI配置
    /// </summary>
    UIOptions GetUIOptions();

    /// <summary>
    /// 获取性能配置
    /// </summary>
    PerformanceOptions GetPerformanceOptions();

    /// <summary>
    /// 获取Preferences配置
    /// </summary>
    PreferencesOptions GetPreferencesOptions();

    /// <summary>
    /// 获取Schema配置
    /// </summary>
    SchemaOptions GetSchemaOptions();

    /// <summary>
    /// 获取网络接入配置（ZeroTier）；文件缺失时返回默认值
    /// </summary>
    NetworkOptions GetNetworkOptions();

    /// <summary>
    /// 获取在线更新配置（config/update.ini）；文件缺失时 Enabled=false（功能关闭）
    /// </summary>
    UpdateOptions GetUpdateOptions();

    /// <summary>
    /// 保存网络接入配置（写回 network.ini）
    /// </summary>
    void SaveNetworkOptions(NetworkOptions options);

    /// <summary>
    /// 更新数据库连接地址（写回 database.ini 的 Host/Port，保持其余行与密码密文不动）。
    /// 连接串已在运行期固化，改动需重启应用生效。
    /// </summary>
    void UpdateDatabaseEndpoint(string host, int port);

    /// <summary>
    /// 保存Schema配置
    /// </summary>
    void SaveSchemaOptions(SchemaOptions options);

    /// <summary>
    /// 确保配置目录存在，不存在则创    /// </summary>
    void EnsureConfigDirectoryExists();

    /// <summary>
    /// 检查所有配置文件是否存    /// </summary>
    bool CheckConfigFilesExist();

    /// <summary>
    /// 获取缺失的配置文件列    /// </summary>
    List<string> GetMissingConfigFiles();

    /// <summary>
    /// 按默认模板生成缺失的配置文件（配置向导"一键使用默认配置"），
    /// 已存在的文件不覆盖；返回本次实际生成的文件名列表
    /// </summary>
    List<string> GenerateDefaultConfigFiles();
}