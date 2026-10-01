namespace NewCosmos.Services.Core;

/// <summary>
/// 数据库备份服务（pg_dump 快照）：破坏性清库操作（清表 TRUNCATE/清理软删）前的统一备份出口。
/// </summary>
public interface IDatabaseBackupService
{
    /// <summary>
    /// 执行 pg_dump 全库快照到指定目录，返回生成的备份文件全路径。
    /// </summary>
    Task<NewCosmos.Models.Results.Result<string>> BackupDatabaseAsync(string backupPath, CancellationToken ct = default);
}
