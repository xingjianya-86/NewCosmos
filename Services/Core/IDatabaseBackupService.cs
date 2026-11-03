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

    /// <summary>
    /// 从 pg_dump 纯文本备份还原数据库（危险操作）。
    /// 三重防护：调用方二次确认 → 还原前自动再备份 → psql 单事务执行（失败整体回滚，不留半还原状态）。
    /// 还原逻辑：DROP SCHEMA public CASCADE + 重建 + 回放备份，全部在同一事务内。
    /// 返回还原所用的备份文件路径。
    /// </summary>
    Task<NewCosmos.Models.Results.Result<string>> RestoreDatabaseAsync(string backupFilePath, CancellationToken ct = default);
}
