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

    /// <summary>
    /// 解析 psql 路径（还原用）：与 pg_dump 同目录优先（同一 PostgreSQL 安装套件），其次内置目录，最后 PATH。
    /// </summary>
    private string ResolvePsqlPath()
    {
        // 1. 配置指定 pg_dump 的同目录 psql.exe（部署机常为 C:\pgsql\bin\pg_dump.exe）
        var dbOptions = _configService.GetDatabaseOptions();
        if (!string.IsNullOrWhiteSpace(dbOptions.PgDumpPath) && File.Exists(dbOptions.PgDumpPath))
        {
            var dir = Path.GetDirectoryName(dbOptions.PgDumpPath);
            if (!string.IsNullOrEmpty(dir))
            {
                var sibling = Path.Combine(dir, "psql.exe");
                if (File.Exists(sibling)) return sibling;
            }
        }

        // 2. 内置目录
        var bundledPath = Path.Combine(AppContext.BaseDirectory, "Resources", "PostgreSQL", "psql.exe");
        if (File.Exists(bundledPath))
            return bundledPath;

        // 3. PATH 兜底
        return "psql";
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

    /// <summary>字节数人性化（B/KB/MB/GB/TB，单位单点实现——原 DatabaseManagementService 有一份格式不同的副本，已收敛）。</summary>
    public static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size:0.##} {units[unit]}";
    }

    /// <summary>
    /// 从 pg_dump 纯文本备份还原数据库（危险操作，调用方必须完成二次确认与"还原前自动备份"）。
    /// 实现要点：
    /// 1) 备份文件头校验：必须含 pg_dump 标识 "-- PostgreSQL database dump"，防止误回放任意文件；
    /// 2) 还原脚本 = 终止其他连接 + DROP SCHEMA public CASCADE + 重建 + 回放备份；
    /// 3) psql --single-transaction + ON_ERROR_STOP：任一语句失败整体回滚，不留"半还原"状态；
    /// 4) 凭据经临时 PGPASSFILE 传递（用后即删）。
    /// </summary>
    public async Task<Result<string>> RestoreDatabaseAsync(string backupFilePath, CancellationToken ct = default)
    {
        LogInfo($"还原数据库: {Path.GetFileName(backupFilePath)}");

        try
        {
            if (string.IsNullOrWhiteSpace(backupFilePath) || !File.Exists(backupFilePath))
                return Result.Failure<string>(ErrorCodes.FILE_NOT_FOUND, $"备份文件不存在: {backupFilePath}");

            // ① 文件头校验：拒绝非 pg_dump 产物（防误选任意 .sql 回放毁库）
            var header = await ReadFileHeadAsync(backupFilePath, 2048, ct);
            if (header.IndexOf("-- PostgreSQL database dump", StringComparison.OrdinalIgnoreCase) < 0)
                return Result.Failure<string>(ErrorCodes.VALIDATION_FAILED,
                    "所选文件不是 pg_dump 生成的数据库备份，已拒绝还原");

            var dbOptions = _configService.GetDatabaseOptions();
            var appOptions = _configService.GetAppOptions();

            // ② 前置脚本（临时文件，psql -f 回放，作为第一个 -f 与备份文件串联、同一事务）：
            //    - 尽力切断同库其他连接（非超管无权时 DO 块吞掉权限异常继续，由 lock_timeout 兜底防阻塞）；
            //    - DROP + 重建 public schema（pg_dump 不输出 CREATE SCHEMA public——"not creating schema,
            //      since initdb creates it"——直接回放会撞"表已存在"，必须先清空 public）；
            //    - lock_timeout：拿不到锁时快速失败整体回滚，而不是无限挂起。
            var scriptPath = Path.Combine(Path.GetTempPath(), $"nc_restore_preamble_{Guid.NewGuid():N}.sql");
            await using (var writer = new StreamWriter(scriptPath, false, new global::System.Text.UTF8Encoding(false)))
            {
                await writer.WriteLineAsync("-- NewCosmos 恢复前置脚本（自动生成，与备份文件同一事务回放）");
                await writer.WriteLineAsync("SET lock_timeout = '15s';");
                await writer.WriteLineAsync("DO $nc$ BEGIN");
                await writer.WriteLineAsync("  PERFORM pg_terminate_backend(pid) FROM pg_stat_activity");
                await writer.WriteLineAsync($"    WHERE datname = '{EscapeSqlLiteral(dbOptions.DatabaseName)}' AND pid <> pg_backend_pid();");
                await writer.WriteLineAsync("EXCEPTION WHEN OTHERS THEN NULL; -- 非超管无终止权限时忽略，由 lock_timeout 兜底");
                await writer.WriteLineAsync("END $nc$;");
                await writer.WriteLineAsync("DROP SCHEMA IF EXISTS public CASCADE;");
                await writer.WriteLineAsync("CREATE SCHEMA public;");
                await writer.FlushAsync(ct);
            }

            try
            {
                var processStartInfo = new ProcessStartInfo
                {
                    FileName = ResolvePsqlPath(),
                    // --single-transaction：前置脚本+备份文件全部语句一个事务；ON_ERROR_STOP：出错即停不带病回放。
                    // psql 报错自带 "文件:行号: ERROR: ..."，失败时 stderr 足以定位。
                    Arguments = $"-h {dbOptions.Host} -p {dbOptions.Port} -U {dbOptions.Username} -d {dbOptions.DatabaseName} " +
                                $"--single-transaction --set ON_ERROR_STOP=1 --no-psqlrc " +
                                $"-f \"{scriptPath}\" -f \"{backupFilePath}\"",
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

                    // stdout/stderr 必须同时排空，否则管道写满会死锁
                    var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
                    var stderrTask = process.StandardError.ReadToEndAsync(ct);
                    await process.WaitForExitAsync(ct);
                    var stdout = await stdoutTask;
                    var stderr = await stderrTask;

                    if (process.ExitCode != 0)
                    {
                        var detail = FirstNonEmpty(stderr, stdout);
                        LogError($"还原数据库失败(exit={process.ExitCode}): {detail}");
                        Logger.LogSecurity("数据库还原失败", ("BackupFile", Path.GetFileName(backupFilePath)), ("ExitCode", process.ExitCode));
                        return Result.Failure<string>(ErrorCodes.DB_QUERY_ERROR,
                            $"恢复失败（已整体回滚，数据库保持原状）: {Truncate(detail, 800)}");
                    }
                }
                finally
                {
                    if (File.Exists(pgpassPath))
                        File.Delete(pgpassPath);
                }
            }
            finally
            {
                if (File.Exists(scriptPath))
                    File.Delete(scriptPath);
            }

            LogInfo("还原数据库完成");
            Logger.LogSecurity("数据库还原完成",
                ("BackupFile", Path.GetFileName(backupFilePath)),
                ("BackupSize", FormatSize(new FileInfo(backupFilePath).Length)),
                ("Database", dbOptions.DatabaseName),
                ("Operator", appOptions.ApplicationName));

            return Result.Success(backupFilePath);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            LogError($"还原数据库异常: {ex.Message}");
            return Result.FromException<string>(ex);
        }
    }

    /// <summary>读取文件头部若干字节转字符串（校验 pg_dump 标识用，不载入全文件）。</summary>
    private static async Task<string> ReadFileHeadAsync(string path, int maxBytes, CancellationToken ct)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        var buffer = new byte[maxBytes];
        var read = await fs.ReadAsync(buffer.AsMemory(0, maxBytes), ct);
        return global::System.Text.Encoding.UTF8.GetString(buffer, 0, read);
    }

    private static string EscapeSqlLiteral(string value) => value.Replace("'", "''");

    private static string FirstNonEmpty(string first, string second) =>
        !string.IsNullOrWhiteSpace(first) ? first : second ?? string.Empty;

    private static string Truncate(string value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength] + "…";
}
