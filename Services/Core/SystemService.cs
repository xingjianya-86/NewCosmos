using NewCosmos.Models.Results;
using NewCosmos.Services.Database;
using System.Diagnostics;

namespace NewCosmos.Services.Core;

/// <summary>
/// 系统服务实现 - 处理系统级功能
/// </summary>
public class SystemService : BaseService, ISystemService
{
    protected override string ServiceName => "SystemService";
    private readonly IDatabaseService _db;

    public SystemService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
    }

    /// <summary>
    /// 测试数据库连接
    /// </summary>
    public async Task<Result<bool>> TestDatabaseConnectionAsync(CancellationToken ct = default)
    {
        LogInfo("执行数据库连接测试");
        var timerId = Logger.StartPerfTimer("数据库连接测试");

        try
        {
            var result = await _db.TestConnectionAsync(ct);

            if (result.IsSuccess)
            {
                LogInfo("数据库连接测试成功");
            }
            else
            {
                LogError($"操作失败：{result.Message}");
            }

            return result;
        }
        catch (Exception ex)
        {
            LogError(ex, "数据库连接测试失败");
            return Result.FromException<bool>(ex);
        }
        finally
        {
            Logger.StopPerfTimer(timerId, "数据库连接测试");
        }
    }

    /// <summary>
    /// 获取系统状态信息
    /// </summary>
    public async Task<Result<SystemStatus>> GetSystemStatusAsync(CancellationToken ct = default)
    {
        LogInfo("获取系统状态");
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var status = new SystemStatus();

            // 测试数据库连接
            var connectionResult = await _db.TestConnectionAsync(ct);
            status.IsDatabaseConnected = connectionResult.IsSuccess;

            if (status.IsDatabaseConnected)
            {
                // 获取活跃用户数量
                var userCountSql = "SELECT COUNT(*) FROM nc_sys_users WHERE is_active = true";
                var countResult = await _db.ExecuteScalarAsync(userCountSql, ct);
                status.ActiveUserCount = countResult.IsSuccess ? Convert.ToInt32(countResult.Value) : 0;

                // 获取数据库版本
                var versionSql = "SELECT version()";
                var versionResult = await _db.QuerySingleAsync<string>(versionSql, ct);
                status.DatabaseVersion = versionResult.IsSuccess && versionResult.Value != null
                    ? versionResult.Value.Split(',')[0]
                    : "未知";

                // 获取服务器时间
                var timeSql = "SELECT NOW()";
                var timeResult = await _db.QuerySingleAsync<DateTime>(timeSql, ct);
                status.ServerTime = timeResult.IsSuccess ? timeResult.Value : DateTime.Now;
            }

            stopwatch.Stop();
            status.ResponseTimeMs = stopwatch.Elapsed.TotalMilliseconds;

            LogInfo($"系统状态获取成功 数据库连接:{status.IsDatabaseConnected}");
            return Result.Success(status);
        }
        catch (Exception ex)
        {
            LogError(ex, "获取系统状态失败");
            return Result.FromException<SystemStatus>(ex);
        }
    }

    /// <summary>
    /// 检查系统是否已初始化
    /// </summary>
    public async Task<Result<bool>> CheckSystemInitializedAsync(CancellationToken ct = default)
    {
        LogInfo("检查系统初始化状态");

        try
        {
            // 检查是否有用户存在
            var sql = "SELECT COUNT(*) FROM nc_sys_users WHERE is_active = true";
            var countResult = await _db.ExecuteScalarAsync(sql, ct);

            if (countResult.IsFailure)
            {
                return Result.Failure<bool>(countResult.ErrorCode!, countResult.Message!);
            }

            var isInitialized = Convert.ToInt32(countResult.Value) > 0;
            LogInfo($"系统初始化状态: {(isInitialized ? "已初始化" : "未初始化")}");

            return Result.Success(isInitialized);
        }
        catch (Exception ex)
        {
            LogError(ex, "检查系统初始化状态失败");
            return Result.FromException<bool>(ex);
        }
    }
}
