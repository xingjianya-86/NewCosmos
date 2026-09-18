using System.Text;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Models.Schema;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Core;

public class SchemaSyncService : BaseService, ISchemaSyncService
{
    protected override string ServiceName => "SchemaSyncService";
    private readonly IDatabaseService _db;
    private readonly ISchemaService _schemaService;

    public SchemaSyncService(IDatabaseService db, ISchemaService schemaService, ILoggerService logger) : base(logger)
    {
        _db = db;
        _schemaService = schemaService;
    }

    /// <summary>
    /// 以 Resources/Schema YAML 为唯一权威来源重建表结构：
    /// 按表名从 ISchemaService 合并后的 Schema 中取列/索引定义，再走既有的 DROP + CREATE 流程。
    /// </summary>
    public async Task<Result> SyncTableSchemaAsync(
        string tableName,
        bool allowDrop = false,
        IProgress<ProgressContext> progress = null,
        CancellationToken ct = default)
    {
        var table = _schemaService.GetTableSchema(tableName);
        if (table == null)
        {
            LogError($"YAML Schema 中未找到表定义: {tableName}");
            return Result.Failure(ErrorCodes.FILE_NOT_FOUND,
                $"YAML Schema 中未找到表 {tableName} 的定义，无法重建表结构");
        }

        return await SyncTableSchemaCoreAsync(tableName, table.Columns, table.Indexes, allowDrop, ct);
    }

    private async Task<Result> SyncTableSchemaCoreAsync(
        string tableName,
        List<ColumnSchema> canonicalColumns,
        List<IndexSchema> canonicalIndexes,
        bool allowDrop,
        CancellationToken ct)
    {
        LogInfo("同步表结构");
        TableNameValidator.ValidateOrThrow(tableName);

        var existsResult = await _db.ExecuteScalarAsync(
            "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = $1",
            ct, tableName);
        if (existsResult.IsFailure)
        {
            LogError($"检查表是否存在失败: {tableName}");
            return Result.Failure(existsResult.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, existsResult.Message);
        }
        var tableExists = Convert.ToInt64(existsResult.Value) > 0;

        // 破坏性重建必须由调用方显式选择：表已存在且未允许删除时拒绝，防止误删数据
        if (tableExists && !allowDrop)
        {
            LogWarn($"表 {tableName} 已存在且 allowDrop=false，跳过重建以防数据丢失");
            return Result.Failure(ErrorCodes.VALIDATION_FAILED,
                $"表 {tableName} 已存在，重建将删除其全部数据；如确需重建请在调用处显式传入 allowDrop: true");
        }

        // DROP + CREATE 在同一事务内执行：任一步失败整体回滚，避免"旧表已删、新表未建"的半毁状态。
        // 若调用方已开启环境事务则直接加入，不重复开启。
        var ownsTransaction = !_db.HasTransaction;
        if (ownsTransaction)
            await _db.BeginTransactionAsync(ct);

        try
        {
            if (tableExists)
            {
                Logger.LogSecurity("重建表(DROP TABLE CASCADE)", ("Table", tableName), ("AllowDrop", allowDrop));
                var dropResult = await _db.ExecuteNonQueryAsync($"DROP TABLE IF EXISTS {tableName} CASCADE", ct);
                if (dropResult.IsFailure)
                {
                    LogError("删除旧表失败");
                    if (ownsTransaction)
                        await _db.RollbackTransactionAsync();
                    return dropResult;
                }
            }

            // 整批 DDL 作为单条多语句命令执行（PostgreSQL 支持，项目内 TemplateService 已有同类用法），
            // 避免按 ';' 切分在字符串字面量含分号时错误切割语句
            var createSql = BuildCreateTableSql(tableName, canonicalColumns, canonicalIndexes);
            var createResult = await _db.ExecuteNonQueryAsync(createSql, ct);
            if (createResult.IsFailure)
            {
                LogError($"创建新表失败: {tableName}");
                if (ownsTransaction)
                    await _db.RollbackTransactionAsync();
                return createResult;
            }

            if (ownsTransaction)
                await _db.CommitTransactionAsync(ct);

            LogInfo("结构同步完成");
            return Result.Success();
        }
        catch
        {
            if (ownsTransaction)
                await _db.RollbackTransactionAsync();
            throw;
        }
    }

    private static string BuildCreateTableSql(
        string tableName,
        List<ColumnSchema> columns,
        List<IndexSchema> indexes)
    {
        var pkColumns = columns.Where(c => c.PrimaryKey).Select(c => c.Name).ToList();
        var hasCompositePk = pkColumns.Count > 1;

        var colDefs = columns.Select(c =>
        {
            var parts = new List<string> { $"{c.Name} {c.Type}" };
            if (c.PrimaryKey && !hasCompositePk) parts.Add("PRIMARY KEY");
            if (!c.Nullable) parts.Add("NOT NULL");
            if (!string.IsNullOrEmpty(c.Default))
                parts.Add($"DEFAULT {c.Default}");
            if (c.Unique) parts.Add("UNIQUE");
            return $"    {string.Join(" ", parts)}";
        }).ToList();

        var sql = new StringBuilder();
        sql.AppendLine($"CREATE TABLE IF NOT EXISTS {tableName} (");
        sql.AppendLine(string.Join(",\n", colDefs));

        if (hasCompositePk)
        {
            sql.AppendLine($",\n    PRIMARY KEY ({string.Join(", ", pkColumns)})");
        }

        sql.AppendLine(");");

        foreach (var idx in indexes)
        {
            var unique = idx.Unique ? "UNIQUE " : "";
            var cols = string.Join(", ", idx.Columns);
            sql.AppendLine($"CREATE {unique}INDEX IF NOT EXISTS \"{idx.Name}\" ON {tableName}({cols});");
        }

        return sql.ToString();
    }
}
