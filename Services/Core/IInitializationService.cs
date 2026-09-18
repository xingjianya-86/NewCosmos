using NewCosmos.Models.Results;

namespace NewCosmos.Services.Core;

/// <summary>
/// 系统初始化服务接    /// </summary>
public interface IInitializationService
{
    /// <summary>
    /// 检查系统是否已初始化（异步方法，用于启动时    /// </summary>
    Task<bool> IsSystemInitializedAsync();

    /// <summary>
    /// 检查系统是否已初始化（异步方法    /// </summary>
    Task<Result<bool>> CheckInitializationStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 初始化系统，返回生成的默认管理员密码
    /// </summary>
    Task<Result<string>> InitializeSystemAsync(CancellationToken cancellationToken = default);
}