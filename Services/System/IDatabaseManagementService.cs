using NewCosmos.Models.Results;

namespace NewCosmos.Services.System;

/// <summary>
/// 数据库管理服务接    /// 使用现有的数据库表结构（system_logs, standards, app_versions等）
/// </summary>
public interface IDatabaseManagementService
{
    /// <summary>
    /// 获取数据库连接状    /// </summary>
    Task<Result<DatabaseStatus>> GetDatabaseStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// 获取数据库统计信    /// </summary>
    Task<Result<DatabaseStatistics>> GetDatabaseStatisticsAsync(CancellationToken ct = default);

    /// <summary>
    /// 获取所有表的大小信    /// </summary>
    Task<Result<List<TableSizeInfo>>> GetTableSizesAsync(CancellationToken ct = default);

    /// <summary>
    /// 执行数据库备    /// </summary>
    Task<Result<string>> BackupDatabaseAsync(string backupPath, CancellationToken ct = default);

    /// <summary>
    /// 获取备份文件列表
    /// </summary>
    Task<Result<List<BackupFileInfo>>> GetBackupFilesAsync(CancellationToken ct = default);

    /// <summary>
    /// 清理过期日志
    /// </summary>
    Task<Result<int>> CleanupOldLogsAsync(int retentionDays = 90, CancellationToken ct = default);

    /// <summary>
    /// 检测全库软删除行（只读）：逐表统计软删数/保留数/待删数。
    /// </summary>
    Task<Result<SoftDeleteCensus>> DetectSoftDeleteAsync(CancellationToken ct = default);

    /// <summary>
    /// 清理软删除行（先本地备份，再物理删除；保留停保档案与其链接、恢复停保档案成员）。
    /// </summary>
    Task<Result<SoftDeleteCleanupResult>> CleanupSoftDeletedAsync(CancellationToken ct = default);
}

/// <summary>单表软删除统计</summary>
public class SoftDeleteTableStat
{
    public string TableName { get; set; } = string.Empty;
    public int SoftDeleted { get; set; }
    public int Kept { get; set; }
    public int ToDelete { get; set; }
}

/// <summary>全库软删除检测结果</summary>
public class SoftDeleteCensus
{
    public List<SoftDeleteTableStat> Tables { get; set; } = new();
    public int TotalSoftDeleted => Tables.Sum(t => t.SoftDeleted);
    public int TotalKept => Tables.Sum(t => t.Kept);
    public int TotalToDelete => Tables.Sum(t => t.ToDelete);
    public DateTime DetectedAt { get; set; }

    /// <summary>档案链一致性异常（应停未停的旧档案；单人保/导入建档豁免）</summary>
    public List<ChainInconsistency> ChainIssues { get; set; } = new();
}

/// <summary>档案链一致性异常：旧档案应停止（停旧建新）但状态非 Stopped</summary>
public class ChainInconsistency
{
    public long OldApplicationId { get; set; }
    public string OldName { get; set; } = string.Empty;
    public string OldStatus { get; set; } = string.Empty;
    public long NewApplicationId { get; set; }
    public string ChainType { get; set; } = string.Empty;
}

/// <summary>软删除清理结果</summary>
public class SoftDeleteCleanupResult
{
    /// <summary>清理前本地备份文件路径</summary>
    public string BackupFile { get; set; } = string.Empty;
    /// <summary>物理删除行数（含从表）</summary>
    public int DeletedRows { get; set; }
    /// <summary>恢复行数（停保档案成员）</summary>
    public int RestoredRows { get; set; }
    /// <summary>删除的档案数（救助/高龄/临时救助）</summary>
    public int DeletedArchives { get; set; }
}

/// <summary>
/// 数据库状    /// </summary>
public class DatabaseStatus
{
    public bool IsConnected { get; set; }
    public string DatabaseName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public int ActiveConnections { get; set; }
    public long DatabaseSizeBytes { get; set; }
    public string DatabaseSizeFormatted { get; set; } = string.Empty;
    public DateTime CheckedAt { get; set; }
}

/// <summary>
/// 数据库统计信    /// </summary>
public class DatabaseStatistics
{
    public int TotalTables { get; set; }
    public int TotalRecords { get; set; }
    public long TotalSizeBytes { get; set; }
    public string TotalSizeFormatted { get; set; } = string.Empty;
    public int ActiveUsers { get; set; }
    public DateTime LastBackupAt { get; set; }
}

/// <summary>
/// 表大小信    /// </summary>
public class TableSizeInfo
{
    public string TableName { get; set; } = string.Empty;
    public string TableComment { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string SizeFormatted { get; set; } = string.Empty;
    public long RowCount { get; set; }
}

/// <summary>
/// 备份文件信息
/// </summary>
public class BackupFileInfo
{
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string SizeFormatted { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
