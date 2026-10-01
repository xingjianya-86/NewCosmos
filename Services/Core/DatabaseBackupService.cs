using System.Diagnostics;
using NewCosmos.Constants;
using NewCosmos.Models.Options;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Core;

/// <summary>
/// pg_dump 备份实现（自 DatabaseManagementService 提取，供清表/清理前统一调用）。
/// 凭据经临时 PGPASSFILE 传递（用后即删，不落日志/产物）。
/// </summary>
public class DatabaseBackupService : BaseService, IDatabaseBackupService
{
    protected override string ServiceName => "DatabaseBackupService";

    private readonly IConfigService _configService;

    public DatabaseBackupService(IConfigService configService, ILoggerService logger) : base(logger)
    {
        _configService = configService;
    }

    private string ResolvePgDumpPath()
    {
        var dbOptions = _configService.GetDatabaseOptions();

        // 1. 配置文件指定路径
        if (!string.IsNullOrWhiteSpace(dbOptions.PgDumpPath) && File.Exists(dbOptions.PgDumpPath))
            return dbOptions.PgDumpPath;

        // 2. 应用内置 pg_dump（Resources/PostgreSQL/pg_dump.exe）
        var bundledPath = Path.Combine(AppContext.BaseDirectory, "Resources", "PostgreSQL", "pg_dump.exe");
        if (File.Exists(bundledPath))
            return bundledPath;

        // 3. 兜底：走系统 PATH
        return "pg_dump";
    }

    public async Task<Result<string>> BackupDatabaseAsync(string backupPath, CancellationToken ct = default)
    {
        LogInfo("备份数据库");

        try
        {
            var dbOptions = _configService.GetDatabaseOptions();
            var appOptions = _configService.GetAppOptions();

            var backupFileName = $"{appOptions.ApplicationName}_backup_{DateTime.Now:yyyyMMdd_HHmmss}.sql";
            var fullPath = Path.Combine(backupPath, backupFileName);

            if (!Directory.Exists(backupPath))
            {
                Directory.CreateDirectory(backupPath);
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = ResolvePgDumpPath(),
                Arguments = $"-h {dbOptions.Host} -p {dbOptions.Port} -U {dbOptions.Username} -d {dbOptions.DatabaseName} -F p -f \"{fullPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            var pgpassPath = Path.Combine(Path.GetTempPath(), $".pgpass_{Guid.NewGuid():N}");
            try
            {
                var pgpassContent = $"{dbOptions.Host}:{dbOptions.Port}:{dbOptions.DatabaseName}:{dbOptions.Username}:{dbOptions.Password}";
                await File.WriteAllTextAsync(pgpassPath, pgpassContent, ct);
                File.SetAttributes(pgpassPath, FileAttributes.Hidden);
                processStartInfo.Environment["PGPASSFILE"] = pgpassPath;

                using var process = new Process { StartInfo = processStartInfo };
                process.Start();

                var stderr = await process.StandardError.ReadToEndAsync(ct);
                await process.WaitForExitAsync(ct);

                if (process.ExitCode != 0)
                {
                    LogError($"备份数据库失败: {stderr}");
                    return Result.Failure<string>(ErrorCodes.DB_QUERY_ERROR, $"pg_dump执行失败: {stderr}");
                }
            }
            finally
            {
                if (File.Exists(pgpassPath))
                    File.Delete(pgpassPath);
            }

            if (!File.Exists(fullPath))
            {
                return Result.Failure<string>(ErrorCodes.FILE_NOT_FOUND, "备份文件未生成");
            }

            LogInfo("备份数据库完成");
            Logger.LogBusiness("数据库备份完成", ("BackupFile", backupFileName), ("Size", FormatSize(new FileInfo(fullPath).Length)));

            return Result.Success(fullPath);
        }
        catch (Exception ex)
        {
            LogError($"操作失败: {ex.Message}");
            return Result.FromException<string>(ex);
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:F2} {units[unit]}";
    }
}
