using NewCosmos.Constants;
using NewCosmos.Models.Options;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using System.Diagnostics;
using System.IO;

namespace NewCosmos.Services.System;

public class DatabaseManagementService : BaseService, IDatabaseManagementService
{
    protected override string ServiceName => "DatabaseManagementService";
    private readonly IDatabaseService _dbService;
    private readonly IConfigService _configService;
    private readonly IDatabaseBackupService _backupService;
    private readonly ISchemaService _schemaService;
    private readonly ILoggerService _logger;

    public DatabaseManagementService(
        IDatabaseService dbService,
        IConfigService configService,
        ISchemaService schemaService,
        IDatabaseBackupService backupService,
        ILoggerService logger) : base(logger)
    {
        _dbService = dbService;
        _configService = configService;
        _schemaService = schemaService;
        _backupService = backupService;
        _logger = logger;
    }

    public async Task<Result<DatabaseStatus>> GetDatabaseStatusAsync(CancellationToken ct = default)
    {
        LogInfo("获取数据库连接状态");

        try
        {
            // 单条 SQL 取回全部状态（旧实现是 version/current_database/pg_stat_activity/表大小 4 条串行往返，
            // 跨网络部署时单次往返 0.5-1.2s，进页面要白等 3 轮）
            var yamlTables = _schemaService.GetAllYamlTableNames().ToArray();
            var sql = @"
                SELECT version() AS version,
                       current_database() AS db_name,
                       (SELECT count(*) FROM pg_stat_activity WHERE datname = current_database()) AS active_connections,
                       (SELECT COALESCE(SUM(pg_total_relation_size(c.oid)), 0)::bigint
                          FROM pg_class c
                          JOIN pg_namespace n ON n.oid = c.relnamespace
                         WHERE n.nspname = 'public' AND c.relkind = 'r'
                           AND c.relname = ANY($1)) AS size_bytes";

            var row = await _dbService.QuerySingleAsync<DatabaseStatusRow>(sql, ct, new object[] { yamlTables });
            if (row.IsFailure || row.Value == null)
            {
                return Result.Failure<DatabaseStatus>(row.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    row.Message ?? "获取数据库状态失败");
            }

            var status = new DatabaseStatus
            {
                IsConnected = true,
                Version = row.Value.Version,
                DatabaseName = row.Value.DbName,
                ActiveConnections = Convert.ToInt32(row.Value.ActiveConnections),
                DatabaseSizeBytes = row.Value.SizeBytes,
                DatabaseSizeFormatted = FormatSize(row.Value.SizeBytes),
                CheckedAt = DateTime.Now
            };

            LogInfo($"数据库状态检查完成: {status.DatabaseName}");
            return Result.Success(status);
        }
        catch (Exception ex)
        {
            LogError($"操作失败: {ex.Message}");
            return Result.FromException<DatabaseStatus>(ex);
        }
    }

    /// <summary>状态检查单查询投影（列名 snake_case，映射层自动转 PascalCase 属性）</summary>
    private sealed class DatabaseStatusRow
    {
        public string Version { get; set; } = string.Empty;
        public string DbName { get; set; } = string.Empty;
        public long ActiveConnections { get; set; }
        public long SizeBytes { get; set; }
    }

    public async Task<Result<DatabaseStatistics>> GetDatabaseStatisticsAsync(CancellationToken ct = default)
    {
        LogInfo("获取数据库统计信息");

        try
        {
            var yamlTables = _schemaService.GetAllYamlTableNames().ToArray();

            // 单条 SQL 取回全部统计（旧实现 reltuples/表大小/活跃用户 3 条串行往返）
            var sql = @"
                SELECT (SELECT COALESCE(SUM(c.reltuples), 0)::bigint
                          FROM pg_class c
                          JOIN pg_namespace n ON n.oid = c.relnamespace
                         WHERE n.nspname = 'public' AND c.relkind = 'r'
                           AND c.relname = ANY($1)) AS total_records,
                       (SELECT COALESCE(SUM(pg_total_relation_size(c.oid)), 0)::bigint
                          FROM pg_class c
                          JOIN pg_namespace n ON n.oid = c.relnamespace
                         WHERE n.nspname = 'public' AND c.relkind = 'r'
                           AND c.relname = ANY($1)) AS size_bytes,
                       (SELECT COUNT(*) FROM nc_sys_users WHERE is_active = true) AS active_users";

            var row = await _dbService.QuerySingleAsync<DatabaseStatisticsRow>(sql, ct, new object[] { yamlTables });
            if (row.IsFailure || row.Value == null)
            {
                return Result.Failure<DatabaseStatistics>(row.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                    row.Message ?? "获取数据库统计失败");
            }

            var stats = new DatabaseStatistics
            {
                TotalTables = yamlTables.Length,
                TotalRecords = Convert.ToInt32(row.Value.TotalRecords),
                TotalSizeBytes = row.Value.SizeBytes,
                TotalSizeFormatted = FormatSize(row.Value.SizeBytes),
                ActiveUsers = Convert.ToInt32(row.Value.ActiveUsers)
            };

            LogInfo($"数据库统计完成: {stats.TotalTables}张表, {stats.TotalRecords}条记录");
            return Result.Success(stats);
        }
        catch (Exception ex)
        {
            LogError($"操作失败: {ex.Message}");
            return Result.FromException<DatabaseStatistics>(ex);
        }
    }

    /// <summary>统计单查询投影（列名 snake_case，映射层自动转 PascalCase 属性）</summary>
    private sealed class DatabaseStatisticsRow
    {
        public long TotalRecords { get; set; }
        public long SizeBytes { get; set; }
        public long ActiveUsers { get; set; }
    }

    public async Task<Result<List<TableSizeInfo>>> GetTableSizesAsync(CancellationToken ct = default)
    {
        LogInfo("获取表大小信息");

        try
        {
            var yamlTables = _schemaService.GetAllYamlTableNames();
            if (yamlTables.Count == 0)
            {
                return Result.Success(new List<TableSizeInfo>());
            }

            var sql = @"
                SELECT 
                    c.relname AS table_name,
                    pg_total_relation_size(c.oid) AS size_bytes,
                    c.reltuples::bigint AS row_count
                FROM pg_class c
                INNER JOIN pg_namespace n ON n.oid = c.relnamespace
                WHERE n.nspname = 'public' AND c.relkind = 'r'
                  AND c.relname = ANY($1)
                ORDER BY pg_total_relation_size(c.oid) DESC";

            var result = await _dbService.QueryAsync<TableSizeRaw>(sql, ct, new object[] { yamlTables.ToArray() });

            if (result.IsSuccess && result.Value is not null)
            {
                var tables = result.Value.Select(t => new TableSizeInfo
                {
                    TableName = t.table_name,
                    TableComment = GetTableComment(t.table_name),
                    SizeBytes = t.size_bytes,
                    SizeFormatted = FormatSize(t.size_bytes),
                    RowCount = t.row_count
                }).ToList();

                LogInfo($"获取{tables.Count}张表的大小信息");
                return Result.Success(tables);
            }

            return Result.Failure<List<TableSizeInfo>>(ErrorCodes.DB_QUERY_ERROR, "获取表大小失败");
        }
        catch (Exception ex)
        {
            LogError($"操作失败: {ex.Message}");
            return Result.FromException<List<TableSizeInfo>>(ex);
        }
    }

    /// <summary>pg_dump 备份（委托共享实现 IDatabaseBackupService，与导入清表前备份同源）。</summary>
    public Task<Result<string>> BackupDatabaseAsync(string backupPath, CancellationToken ct = default)
    {
        return _backupService.BackupDatabaseAsync(backupPath, ct);
    }

    public Task<Result<List<BackupFileInfo>>> GetBackupFilesAsync(CancellationToken ct = default)
    {
        LogInfo("获取备份文件列表");

        // 目录扫描 + 逐文件 FileInfo/GetCreationTime 是同步 IO：旧实现在调用线程执行，
        // 而本方法在页面进入路径上被调用（= UI 线程），备份目录文件多时会直接卡住页面（§6）
        return Task.Run(() =>
        {
            try
            {
                var storageOptions = _configService.GetStorageOptions();
                var backupPath = storageOptions.GetBackupPath();

                if (!Directory.Exists(backupPath))
                {
                    return Result.Success(new List<BackupFileInfo>());
                }

                var files = Directory.GetFiles(backupPath, "*.sql")
                    .Select(f => new BackupFileInfo
                    {
                        FileName = Path.GetFileName(f),
                        FilePath = f,
                        SizeBytes = new FileInfo(f).Length,
                        SizeFormatted = FormatSize(new FileInfo(f).Length),
                        CreatedAt = File.GetCreationTime(f)
                    })
                    .OrderByDescending(f => f.CreatedAt)
                    .ToList();

                LogInfo($"找到{files.Count}个备份文件");
                return Result.Success(files);
            }
            catch (Exception ex)
            {
                LogError($"操作失败: {ex.Message}");
                return Result.FromException<List<BackupFileInfo>>(ex);
            }
        }, ct);
    }

    public Task<Result<int>> CleanupOldLogsAsync(int retentionDays = 90, CancellationToken ct = default)
    {
        LogInfo("清理旧日志文件");

        try
        {
            var storageOptions = _configService.GetStorageOptions();
            var logRootPath = storageOptions.GetLogPath();

            if (!Directory.Exists(logRootPath))
            {
                LogInfo("日志目录不存在，跳过清理");
                return Task.FromResult(Result.Success(0));
            }

            var retentionMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["app"] = 30,
                ["biz"] = 180,
                ["sec"] = 365,
                ["err"] = 90,
                ["perf"] = 30
            };

            var deletedCount = 0;
            var deletedSize = 0L;
            var now = DateTime.Now;

            foreach (var subDir in Directory.GetDirectories(logRootPath))
            {
                ct.ThrowIfCancellationRequested();

                var dirName = Path.GetFileName(subDir);
                if (!retentionMap.TryGetValue(dirName, out var retentionDaysForDir))
                    continue;

                var cutoffDate = now.AddDays(-retentionDaysForDir);

                foreach (var file in Directory.GetFiles(subDir, "*.log"))
                {
                    ct.ThrowIfCancellationRequested();

                    var fileInfo = new FileInfo(file);
                    if (fileInfo.CreationTime < cutoffDate)
                    {
                        try
                        {
                            deletedSize += fileInfo.Length;
                            File.Delete(file);
                            deletedCount++;
                            LogInfo($"已删除过期日志: {file}");
                        }
                        catch (Exception ex)
                        {
                            LogError($"删除日志文件失败: {file}: {ex.Message}");
                        }
                    }
                }
            }

            var summary = $"已清理{deletedCount}个日志文件，释放{FormatSize(deletedSize)}";
            Logger.LogBusiness("本地日志清理完成", ("DeletedCount", deletedCount), ("DeletedSize", FormatSize(deletedSize)));

            return Task.FromResult(Result.Success(deletedCount));
        }
        catch (OperationCanceledException)
        {
            return Task.FromResult(Result.Failure<int>(ErrorCodes.CANCELLED, "操作已取消"));
        }
        catch (Exception ex)
        {
            LogError($"操作失败: {ex.Message}");
            return Task.FromResult(Result.FromException<int>(ex));
        }
    }

    #region 软删除清理

    private static readonly string[] ApplicationDependentTables =
    {
        "nc_biz_alimony_incomes","nc_biz_application_logs","nc_biz_archives","nc_biz_breeding_incomes",
        "nc_biz_business_incomes","nc_biz_capability_assessments","nc_biz_caregivers","nc_biz_change_records",
        "nc_biz_employment_costs","nc_biz_family_members","nc_biz_financial_assets","nc_biz_forest_lands",
        "nc_biz_grace_periods","nc_biz_guardians","nc_biz_household_surveys","nc_biz_kinship_filings",
        "nc_biz_labor_incomes","nc_biz_land_confirmations","nc_biz_land_incomes","nc_biz_land_registrations",
        "nc_biz_machineries","nc_biz_near_relative_links","nc_biz_other_incomes","nc_biz_properties",
        "nc_biz_property_incomes","nc_biz_rigid_expenditures","nc_biz_self_care_assessments","nc_biz_subsidies",
        "nc_biz_subsidy_incomes","nc_biz_supporters","nc_biz_transfer_incomes","nc_biz_vehicles"
    };

    private static readonly string[] ElderlyDependentTables = { "nc_biz_elderly_payback_segments" };

    private static readonly string[] TempReliefDependentTables =
    {
        "nc_biz_temp_relief_members","nc_biz_temp_relief_diseases","nc_biz_temp_relief_accidents","nc_biz_temp_relief_educations"
    };

    private static readonly string[] SpecialSoftDeleteTables =
    {
        "nc_biz_applications","nc_biz_family_members","nc_biz_elderly_applications",
        "nc_biz_temp_relief_applications","nc_biz_change_records"
    };

    /// <summary>
    /// 动态表名形状守卫（information_schema 发现的表名先过形状再进参数化 SQL）。
    /// 注意与 Helpers.TableNameValidator 白名单用途不同：软删扫描/清理需覆盖全部 nc_ 业务表，
    /// 无法用固定白名单枚举，故保留前缀形状校验（白名单场景一律走 TableNameValidator）。
    /// </summary>
    private static bool IsSafeTableName(string name) =>
        !string.IsNullOrEmpty(name) && name.Length <= 64 &&
        global::System.Text.RegularExpressions.Regex.IsMatch(name, "^nc_[a-z0-9_]+$");

    private async Task<List<string>> GetSoftDeleteTablesAsync(CancellationToken ct)
    {
        var r = await _dbService.QueryAsync<string>(
            "SELECT table_name FROM information_schema.columns WHERE column_name='deleted_at' AND table_schema='public' ORDER BY table_name", ct);
        return (r.IsSuccess ? r.Value : null)?
            .Where(t => IsSafeTableName(t) && !t.Contains("_bak_")).ToList() ?? new List<string>();
    }

    /// <summary>检测全库软删除行（只读）。</summary>
    public async Task<Result<SoftDeleteCensus>> DetectSoftDeleteAsync(CancellationToken ct = default)
    {
        try
        {
            LogInfo("检测全库软删除行");
            var census = new SoftDeleteCensus { DetectedAt = DateTime.Now };

            foreach (var t in await GetSoftDeleteTablesAsync(ct))
            {
                var n = await _dbService.ExecuteScalarAsync<int?>($"SELECT count(*) FROM {t} WHERE deleted_at IS NOT NULL", ct);
                var softDeleted = n.Value ?? 0;
                if (softDeleted == 0) continue;

                var toDelete = t switch
                {
                    "nc_biz_applications" => (await _dbService.ExecuteScalarAsync<int?>(@"
                        SELECT count(*) FROM nc_biz_applications a
                        WHERE a.deleted_at IS NOT NULL AND a.status <> $1
                          AND NOT EXISTS (SELECT 1 FROM nc_biz_change_records cr WHERE cr.application_id=a.id OR cr.original_application_id=a.id OR cr.new_application_id=a.id)
                          AND NOT EXISTS (SELECT 1 FROM nc_biz_applications c WHERE c.original_application_id=a.id AND c.deleted_at IS NULL)", ct, ApplicationStatusCodes.STOPPED)).Value ?? 0,
                    "nc_biz_elderly_applications" or "nc_biz_temp_relief_applications" =>
                        (await _dbService.ExecuteScalarAsync<int?>($"SELECT count(*) FROM {t} WHERE deleted_at IS NOT NULL AND status <> $1", ct, ApplicationStatusCodes.STOPPED)).Value ?? 0,
                    "nc_biz_family_members" => (await _dbService.ExecuteScalarAsync<int?>(
                        "SELECT count(*) FROM nc_biz_family_members fm JOIN nc_biz_applications a ON a.id=fm.application_id WHERE fm.deleted_at IS NOT NULL AND NOT(a.deleted_at IS NULL AND a.status=$1)", ct, ApplicationStatusCodes.STOPPED)).Value ?? 0,
                    _ => softDeleted
                };

                census.Tables.Add(new SoftDeleteTableStat
                {
                    TableName = t,
                    SoftDeleted = softDeleted,
                    ToDelete = toDelete,
                    Kept = softDeleted - toDelete
                });
            }

            // 档案链一致性：停旧建新的旧档案应 Stopped（单人保/导入建档豁免）
            var chainRows = await _dbService.QueryAsync<ChainRow>(@"
                SELECT p.id AS old_application_id,
                       p.applicant_name AS old_name,
                       p.status AS old_status,
                       c.id AS new_application_id,
                       c.chain_type AS chain_type
                FROM nc_biz_applications p
                JOIN nc_biz_applications c ON c.original_application_id = p.id AND c.deleted_at IS NULL
                WHERE p.deleted_at IS NULL AND p.status <> $1
                  AND c.chain_type IN ('CategoryRebuild', 'HeadChange', 'HouseholdDeath')
                ORDER BY p.id", ct, ApplicationStatusCodes.STOPPED);
            if (chainRows.IsSuccess && chainRows.Value != null)
            {
                census.ChainIssues = chainRows.Value.Select(r => new ChainInconsistency
                {
                    OldApplicationId = r.OldApplicationId,
                    OldName = r.OldName ?? "",
                    OldStatus = r.OldStatus ?? "",
                    NewApplicationId = r.NewApplicationId,
                    ChainType = r.ChainType ?? ""
                }).ToList();
            }

            LogInfo($"检测完成: 待删 {census.TotalToDelete} 行，保留 {census.TotalKept} 行，档案链异常 {census.ChainIssues.Count} 条");
            return Result.Success(census);
        }
        catch (Exception ex)
        {
            LogError($"检测软删除失败: {ex.Message}");
            return Result.FromException<SoftDeleteCensus>(ex);
        }
    }

    private class ChainRow
    {
        public long OldApplicationId { get; set; }
        public string? OldName { get; set; }
        public string? OldStatus { get; set; }
        public long NewApplicationId { get; set; }
        public string? ChainType { get; set; }
    }

    /// <summary>清理软删除行（先本地备份，再物理删除）。</summary>
    public async Task<Result<SoftDeleteCleanupResult>> CleanupSoftDeletedAsync(CancellationToken ct = default)
    {
        try
        {
            LogInfo("开始软删除清理");
            var result = new SoftDeleteCleanupResult();

            // 1. 本地备份（pg_dump 到本地文件，不使用影子表）
            var storageOptions = _configService.GetStorageOptions();
            var backup = await BackupDatabaseAsync(storageOptions.GetBackupPath(), ct);
            if (backup.IsFailure)
            {
                LogError($"清理前备份失败，已中止: {backup.Message}");
                return Result.Failure<SoftDeleteCleanupResult>(backup.ErrorCode!, $"清理前本地备份失败，已中止：{backup.Message}");
            }
            result.BackupFile = backup.Value;
            LogInfo($"清理前备份完成: {backup.Value}");

            // 2. 事务内清理
            await using var tx = await _dbService.BeginTransactionScopeAsync(ct);

            // 2.1 恢复停保档案成员
            result.RestoredRows += (await _dbService.ExecuteNonQueryAsync(
                @"UPDATE nc_biz_family_members fm SET deleted_at=NULL
                  FROM nc_biz_applications a
                  WHERE fm.application_id=a.id AND fm.deleted_at IS NOT NULL AND a.deleted_at IS NULL AND a.status=$1", ct, ApplicationStatusCodes.STOPPED)).Value;

            // 2.2 删除活档案(非停保)下冗余软删成员
            result.DeletedRows += (await _dbService.ExecuteNonQueryAsync(
                @"DELETE FROM nc_biz_family_members fm USING nc_biz_applications a
                  WHERE fm.application_id=a.id AND fm.deleted_at IS NOT NULL AND a.deleted_at IS NULL AND a.status<>$1", ct, ApplicationStatusCodes.STOPPED)).Value;

            // 2.3 待删救助档案（非停保、无变更链接、非存活档案之父）+ 从表
            var appIds = (await _dbService.QueryAsync<long>(@"
                SELECT a.id FROM nc_biz_applications a
                WHERE a.deleted_at IS NOT NULL AND a.status <> $1
                  AND NOT EXISTS (SELECT 1 FROM nc_biz_change_records cr WHERE cr.application_id=a.id OR cr.original_application_id=a.id OR cr.new_application_id=a.id)
                  AND NOT EXISTS (SELECT 1 FROM nc_biz_applications c WHERE c.original_application_id=a.id AND c.deleted_at IS NULL)", ct, ApplicationStatusCodes.STOPPED)).Value ?? new List<long>();
            if (appIds.Count > 0)
            {
                var arr = appIds.ToArray();
                foreach (var t in ApplicationDependentTables)
                    result.DeletedRows += (await _dbService.ExecuteNonQueryAsync($"DELETE FROM {t} WHERE application_id = ANY($1)", ct, arr)).Value;
                var delApps = await _dbService.ExecuteNonQueryAsync("DELETE FROM nc_biz_applications WHERE id = ANY($1)", ct, arr);
                result.DeletedRows += delApps.Value;
                result.DeletedArchives += delApps.Value;
            }

            // 2.4 高龄（非停止）
            var elderlyIds = (await _dbService.QueryAsync<long>(
                "SELECT id FROM nc_biz_elderly_applications WHERE deleted_at IS NOT NULL AND status <> $1", ct, ApplicationStatusCodes.STOPPED)).Value ?? new List<long>();
            if (elderlyIds.Count > 0)
            {
                var arr = elderlyIds.ToArray();
                foreach (var t in ElderlyDependentTables)
                    result.DeletedRows += (await _dbService.ExecuteNonQueryAsync($"DELETE FROM {t} WHERE application_id = ANY($1)", ct, arr)).Value;
                var d = await _dbService.ExecuteNonQueryAsync("DELETE FROM nc_biz_elderly_applications WHERE id = ANY($1)", ct, arr);
                result.DeletedRows += d.Value;
                result.DeletedArchives += d.Value;
            }

            // 2.5 临时救助（非停止）
            var tempIds = (await _dbService.QueryAsync<long>(
                "SELECT id FROM nc_biz_temp_relief_applications WHERE deleted_at IS NOT NULL AND status <> $1", ct, ApplicationStatusCodes.STOPPED)).Value ?? new List<long>();
            if (tempIds.Count > 0)
            {
                var arr = tempIds.ToArray();
                foreach (var t in TempReliefDependentTables)
                    result.DeletedRows += (await _dbService.ExecuteNonQueryAsync($"DELETE FROM {t} WHERE application_id = ANY($1)", ct, arr)).Value;
                var d = await _dbService.ExecuteNonQueryAsync("DELETE FROM nc_biz_temp_relief_applications WHERE id = ANY($1)", ct, arr);
                result.DeletedRows += d.Value;
                result.DeletedArchives += d.Value;
            }

            // 2.6 变更记录（连带快照/明细）
            await _dbService.ExecuteNonQueryAsync("DELETE FROM nc_biz_change_details WHERE change_id IN (SELECT id FROM nc_biz_change_records WHERE deleted_at IS NOT NULL)", ct);
            await _dbService.ExecuteNonQueryAsync("DELETE FROM nc_biz_change_snapshots WHERE change_id IN (SELECT id FROM nc_biz_change_records WHERE deleted_at IS NOT NULL)", ct);
            result.DeletedRows += (await _dbService.ExecuteNonQueryAsync("DELETE FROM nc_biz_change_records WHERE deleted_at IS NOT NULL", ct)).Value;

            // 2.7 其它含 deleted_at 的表：删除软删行
            foreach (var t in await GetSoftDeleteTablesAsync(ct))
            {
                if (SpecialSoftDeleteTables.Contains(t)) continue;
                result.DeletedRows += (await _dbService.ExecuteNonQueryAsync($"DELETE FROM {t} WHERE deleted_at IS NOT NULL", ct)).Value;
            }

            await tx.CommitAsync(ct);

            Logger.LogBusiness("软删除清理完成",
                ("DeletedRows", result.DeletedRows), ("RestoredRows", result.RestoredRows),
                ("DeletedArchives", result.DeletedArchives), ("Backup", result.BackupFile));
            LogInfo($"软删除清理完成: 删除 {result.DeletedRows} 行，恢复 {result.RestoredRows} 行，删除档案 {result.DeletedArchives}");

            return Result.Success(result);
        }
        catch (OperationCanceledException)
        {
            return Result.Failure<SoftDeleteCleanupResult>(ErrorCodes.CANCELLED, "操作已取消");
        }
        catch (Exception ex)
        {
            LogError($"软删除清理失败: {ex.Message}");
            return Result.FromException<SoftDeleteCleanupResult>(ex);
        }
    }

    #endregion

    private string FormatSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }

    private string GetTableComment(string tableName)
    {
        return tableName switch
        {
            "nc_sys_users" => "系统用户表",
            "nc_sys_organizations" => "组织机构表",
            "nc_sys_roles" => "角色表",
            "nc_sys_permissions" => "权限表",
            "nc_sys_user_roles" => "用户角色关联表",
            "nc_perm_permissions" => "权限表",
            "nc_sys_user_activity_logs" => "用户活动日志",
            "nc_sys_permission_change_logs" => "权限变更日志",
            "nc_sys_role_change_logs" => "角色变更日志",
            "nc_config_income_standards" => "收入标准表",
            "nc_config_subsidy_standards" => "分类补贴标准表",
            "nc_config_destitute_standards" => "特困供养标准表",
            "nc_config_system_standards" => "系统配置标准表",
            "nc_config_standards" => "统一标准配置表",
            "nc_config_subsidy_prices" => "补贴价格表",
            "nc_sys_app_versions" => "应用版本表",
            "nc_dict_categories" => "字典分类表",
            "nc_dict_categories_updates" => "字典分类变更记录",
            "nc_dict_items" => "字典项表",
            "nc_dict_items_updates" => "字典项变更记录",
            "nc_perm_role_permissions" => "角色权限关联表",
            "nc_perm_user_permissions" => "用户权限关联表",
            "nc_perm_cache_version" => "权限缓存版本表",
            "nc_biz_applications" => "申请表",
            "nc_biz_archive_files" => "档案文件表",
            "nc_biz_family_members" => "家庭成员表",
            "nc_biz_application_logs" => "申请日志表",
            "nc_biz_archives" => "档案表",
            "nc_biz_print_records" => "打印记录表",
            "nc_biz_change_records" => "变更记录表",
            "nc_biz_change_details" => "变更明细表",
            "nc_biz_change_snapshots" => "变更快照表",
            "nc_biz_monthly_reports" => "月报表",
            "nc_biz_monthly_report_items" => "月报明细表",
            "nc_biz_asset_verifications" => "资产核查表",
            "nc_biz_asset_checks" => "资产核查任务表",
            "nc_biz_asset_check_agents" => "资产核查代理人表",
            "nc_biz_asset_check_operators" => "资产核查经办人表",
            "nc_biz_asset_check_reports" => "资产核查报告表",
            "nc_biz_low_income_families" => "低收入家庭表",
            "nc_biz_low_income_persons" => "低收入人员表",
            "nc_biz_low_income_archives" => "低收入档案表",
            "nc_biz_low_income_edge_families" => "低收入边缘家庭表",
            "nc_biz_low_income_edge_persons" => "低收入边缘人员表",
            "nc_biz_rural_subsistence_families" => "最低生活保障家庭表",
            "nc_biz_rural_subsistence_persons" => "最低生活保障人员表",
            "nc_biz_urban_subsistence_families" => "最低生活保障家庭表",
            "nc_biz_urban_subsistence_persons" => "最低生活保障人员表",
            "nc_biz_destitute_families" => "特困供养家庭表",
            "nc_biz_destitute_persons" => "特困供养人员表",
            "nc_biz_elderly_subsidy_history" => "普惠高龄补贴历史表",
            "nc_biz_special_approvals" => "一事一议豁免表",
            "nc_biz_templates" => "模板表",
            "nc_biz_field_groups" => "字段分组表",
            "nc_biz_household_surveys" => "入户调查表",
            "nc_biz_labor_incomes" => "务工收入表",
            "nc_biz_transfer_incomes" => "转移收入表",
            "nc_biz_other_incomes" => "其他收入表",
            "nc_biz_alimony_incomes" => "赡养费收入表",
            "nc_biz_land_incomes" => "土地收入表",
            "nc_biz_subsidy_incomes" => "补贴收入表",
            "nc_biz_property_incomes" => "财产性收入表",
            "nc_biz_business_incomes" => "经营收入表",
            "nc_biz_rigid_expenditures" => "刚性支出表",
            "nc_biz_properties" => "财产表",
            "nc_biz_vehicles" => "车辆表",
            "nc_biz_machineries" => "农机具表",
            "nc_biz_financial_assets" => "金融资产表",
            "nc_biz_land_confirmations" => "土地确权表",
            "nc_biz_land_confirmation_records" => "土地确权记录表",
            "nc_biz_land_contract_plot" => "土地承包地块表",
            "nc_biz_land_contract_contractor" => "土地承包承包方表",
            "nc_biz_forest_lands" => "林地表",
            "nc_biz_breeding_incomes" => "养殖收入表",
            "nc_biz_employment_costs" => "就业成本表",
            "nc_biz_caregivers" => "照料护理人表",
            "nc_biz_guardians" => "监护人表",
            "nc_biz_self_care_assessments" => "生活自理能力评估表",
            "nc_biz_kinship_filings" => "亲属关系备案表",
            "nc_biz_land_registrations" => "土地登记表",
            "nc_biz_subsidies" => "农业补贴表",
            "nc_biz_high_oil_soybean_detail" => "高油大豆明细表",
            "nc_biz_high_oil_soybean_person" => "高油大豆人员表",
            "nc_biz_high_protein_soybean_detail" => "高蛋白大豆明细表",
            "nc_biz_high_protein_soybean_person" => "高蛋白大豆人员表",
            "nc_biz_planting_subsidy" => "种植补贴表",
            "nc_biz_rotation_subsidy" => "轮作补贴表",
            "nc_biz_soil_subsidy" => "地力补贴表",
            "nc_regions_cities" => "市级区划表",
            "nc_regions_counties" => "县级区划表",
            "nc_regions_towns" => "乡镇区划表",
            "nc_regions_villages" => "村级区划表",
            _ => tableName
        };
    }

    private class TableSizeRaw
    {
        public string table_name { get; set; } = string.Empty;
        public long size_bytes { get; set; }
        public long row_count { get; set; }
    }
}
