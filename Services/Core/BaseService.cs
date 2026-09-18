using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Core;

/// <summary>
/// 服务基类 - 提供统一的日志记录功能
/// </summary>
public abstract class BaseService
{
    protected readonly ILoggerService Logger;
    protected abstract string ServiceName { get; }

    protected BaseService(ILoggerService logger)
    {
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    protected void LogInfo(string message)
    {
        Logger.Info($"[{ServiceName}] {message}");
    }

    protected void LogDebug(string message)
    {
        Logger.Debug($"[{ServiceName}] {message}");
    }

    protected void LogWarn(string message)
    {
        Logger.Warn($"[{ServiceName}] {message}");
    }

    protected void LogError(string message)
    {
        Logger.Error($"[{ServiceName}] {message}");
    }

    protected void LogError(Exception ex, string operation)
    {
        Logger.LogError(ex, $"[{ServiceName}] 操作失败: {operation}");
    }

    protected void LogException(Exception ex, string operation)
    {
        LogError(ex, operation);
    }

    protected void EnsureNotNull(object param, string paramName)
    {
        if (param == null)
            throw new ArgumentNullException(paramName, $"参数 {paramName} 不能为空");
    }

    protected void EnsureNotEmpty(string param, string paramName)
    {
        if (string.IsNullOrWhiteSpace(param))
            throw new ArgumentException($"{ServiceName}: 参数 {paramName} 不能为空或空", paramName);
    }

    protected void EnsureNotNegative(decimal value, string paramName)
    {
        if (value < 0)
            throw new ArgumentException($"{ServiceName}: 参数 {paramName} 不能为负", paramName);
    }

    protected void ValidateNotNull(object param, string paramName)
    {
        if (param == null)
            throw new ArgumentNullException(paramName, $"参数 {paramName} 不能为空");
    }

    protected void ValidateNotNullOrEmpty(string param, string paramName)
    {
        if (string.IsNullOrWhiteSpace(param))
            throw new ArgumentException($"{ServiceName}: 参数 {paramName} 不能为空或空", paramName);
    }

    /// <summary>
    /// 执行写入 SQL，失败时抛 BusinessException（禁止静默丢弃写入结果）。
    /// 静态方法 + db 参数：BaseService 自身不持有数据库字段，由子类传入。
    /// 典型用法：在已有 try/catch + Result.FromException 的服务方法内调用，
    /// 抛出的 BusinessException 会被转换为携带原错误码的失败 Result。
    /// </summary>
    protected static async Task ExecOrThrowAsync(IDatabaseService db, string sql, CancellationToken ct, params object?[] parameters)
    {
        var r = await db.ExecuteNonQueryAsync(sql, ct, parameters!);
        if (r.IsFailure)
            throw new BusinessException(r.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, r.Message ?? "写入失败");
    }
}
