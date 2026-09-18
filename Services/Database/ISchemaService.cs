using NewCosmos.Models.Results;
using NewCosmos.Models.Schema;

namespace NewCosmos.Services.Database;

/// <summary>
/// Schema 服务接口
/// 负责数据库表结构管理
/// </summary>
public interface ISchemaService
{
    /// <summary>
    /// 获取数据库Schema状态（轻量级检查）
    /// </summary>
    Task<Result<SchemaStatus>> GetSchemaStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// 初始化所有表（创建缺失的表）
    /// </summary>
    Task<Result> InitializeAllTablesAsync(string initializedBy, CancellationToken ct = default);

    /// <summary>
    /// 加载数据库 Schema
    /// </summary>
    DatabaseSchema? LoadSchema(string databaseName);

    /// <summary>
    /// 加载所有 Schema
    /// </summary>
    List<DatabaseSchema> LoadAllSchemas();

    /// <summary>
    /// 按表名在全部 YAML Schema（合并后）中查找表定义；未定义时返回 null
    /// </summary>
    TableSchema? GetTableSchema(string tableName);

    /// <summary>
    /// 生成创建表的 SQL 语句
    /// </summary>
    string GenerateCreateTableSql(TableSchema table, bool includeForeignKeys = true);

    /// <summary>
    /// 执行创建表 SQL
    /// </summary>
    Task<Result> ExecuteCreateTableAsync(string sql, CancellationToken ct = default);

    /// <summary>
    /// 检查表是否存在
    /// </summary>
    Task<Result<bool>> TableExistsAsync(string tableName, CancellationToken ct = default);

    /// <summary>
    /// 确保表存在（不存在则创建    /// </summary>
    Task<Result> EnsureTablesExistAsync(string databaseName, CancellationToken ct = default);

    /// <summary>
    /// 执行多条 SQL 语句（用于创建视图等    /// </summary>
    Task<Result> ExecuteMultipleSqlAsync(string[] sqlStatements, CancellationToken ct = default);

    /// <summary>
    /// 验证所有 Schema 与数据库的一致    /// </summary>
    Task<Result<SchemaValidationResult>> ValidateAllSchemasAsync(CancellationToken ct = default);

    /// <summary>
    /// 修复表结构错误（排除多余表）
    /// </summary>
    /// <param name="differences">差异列表</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>修复结果</returns>
    Task<Result<SchemaFixResult>> FixSchemaDifferencesAsync(
        IReadOnlyList<SchemaDifference> differences,
        CancellationToken ct = default);

    /// <summary>
    /// 获取所有YAML Schema中定义的表名列表
    /// </summary>
    /// <returns>表名列表</returns>
    List<string> GetAllYamlTableNames();
}