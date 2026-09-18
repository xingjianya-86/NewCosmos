using NewCosmos.Models.Results;

namespace NewCosmos.Services.Core;

/// <summary>
/// 系统服务接口 - 处理系统级功能（数据库测试、系统检查等    /// </summary>
public interface ISystemService
{
    /// <summary>
    /// 测试数据库连    /// </summary>
    Task<Result<bool>> TestDatabaseConnectionAsync(CancellationToken ct = default);

    /// <summary>
    /// 获取系统状态信    /// </summary>
    Task<Result<SystemStatus>> GetSystemStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// 检查系统是否已初始    /// </summary>
    Task<Result<bool>> CheckSystemInitializedAsync(CancellationToken ct = default);
}

/// <summary>
/// 系统状态信    /// </summary>
public class SystemStatus
{
    public bool IsDatabaseConnected { get; set; }
    public int ActiveUserCount { get; set; }
    public string DatabaseVersion { get; set; } = string.Empty;
    public DateTime ServerTime { get; set; }
    public double ResponseTimeMs { get; set; }
}
