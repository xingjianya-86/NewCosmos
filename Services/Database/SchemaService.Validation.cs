using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using NewCosmos.Constants;
using NewCosmos.Models.Options;
using NewCosmos.Models.Results;
using NewCosmos.Models.Schema;
using NewCosmos.Services.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NewCosmos.Services.Database;

public partial class SchemaService
{
    private async Task ValidateTableAsync(
        SchemaValidationResult result,
        DatabaseSchema schema,
        TableSchema table,
        CancellationToken ct)
    {
        var dbColumns = await GetTableColumnsAsync(table.Name, ct);
        if (dbColumns.Count == 0)
        {
            Logger.Warn("表 " + table.Name + " 无列信息");
            return;
        }

        var yamlColumns = new HashSet<string>(table.Columns.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);

        foreach (var column in table.Columns)
        {
            if (!dbColumns.TryGetValue(column.Name, out var dbColumn))
            {
                result.Differences.Add(new SchemaDifference
                {
                    Database = schema.Database,
                    TableName = table.Name,
                    Type = DifferenceType.MissingColumn,
                    Severity = SeverityLevel.Error,
                    ColumnName = column.Name,
                    Expected = column.Type + (column.Nullable ? " nullable" : " not null"),
                    Actual = "该列不存在",
                    Description = "表 " + table.Name + " 缺少列 " + column.Name,
                    Suggestion = "ALTER TABLE " + table.Name + " ADD COLUMN " + column.Name + " " + column.Type + (column.Nullable ? "" : " NOT NULL") + ";"
                });
                result.ErrorCount++;
                continue;
            }

            if (!TypesMatch(column.Type, dbColumn.Type))
            {
                result.Differences.Add(new SchemaDifference
                {
                    Database = schema.Database,
                    TableName = table.Name,
                    Type = DifferenceType.TypeMismatch,
                    Severity = SeverityLevel.Warning,
                    ColumnName = column.Name,
                    Expected = column.Type,
                    Actual = dbColumn.Type,
                    Description = "列 " + column.Name + " 类型不匹配",
                    Suggestion = "ALTER TABLE " + table.Name + " ALTER COLUMN " + column.Name + " TYPE " + column.Type + ";"
                });
                result.WarningCount++;
            }

            var yamlNullable = column.Nullable ? "YES" : "NO";
            if (dbColumn.Nullable != column.Nullable)
            {
                result.Differences.Add(new SchemaDifference
                {
                    Database = schema.Database,
                    TableName = table.Name,
                    Type = DifferenceType.NullableMismatch,
                    Severity = SeverityLevel.Warning,
                    ColumnName = column.Name,
                    Expected = yamlNullable,
                    Actual = dbColumn.Nullable ? "YES" : "NO",
                    Description = "列 " + column.Name + " 可空性不匹配",
                    Suggestion = column.Nullable
                        ? "ALTER TABLE " + table.Name + " ALTER COLUMN " + column.Name + " DROP NOT NULL;"
                        : "ALTER TABLE " + table.Name + " ALTER COLUMN " + column.Name + " SET NOT NULL;"
                });
                result.WarningCount++;
            }
        }

        foreach (var extraColumn in dbColumns.Keys.Except(yamlColumns, StringComparer.OrdinalIgnoreCase))
        {
            result.Differences.Add(new SchemaDifference
            {
                Database = schema.Database,
                TableName = table.Name,
                Type = DifferenceType.ExtraColumn,
                Severity = SeverityLevel.Info,
                ColumnName = extraColumn,
                Expected = "YAML 未定义",
                Actual = dbColumns[extraColumn].Type,
                Description = "表 " + table.Name + " 包含未定义的列 " + extraColumn,
                Suggestion = "该为历史遗留字段可忽略，或将其添加到 YAML 定义"
            });
            result.InfoCount++;
        }

        await ValidateIndexesAsync(result, schema, table, ct);
        await ValidateCommentsAsync(result, schema, table, ct);
    }

    private async Task ValidateCommentsAsync(
        SchemaValidationResult result,
        DatabaseSchema schema,
        TableSchema table,
        CancellationToken ct)
    {
        if (!string.IsNullOrEmpty(table.Comment))
        {
            var dbComment = await GetTableCommentAsync(table.Name, ct);
            if (string.IsNullOrEmpty(dbComment))
            {
                result.Differences.Add(new SchemaDifference
                {
                    Database = schema.Database,
                    TableName = table.Name,
                    Type = DifferenceType.MissingTableComment,
                    Severity = SeverityLevel.Warning,
                    Expected = table.Comment,
                    Actual = "无注释",
                    Description = "表 " + table.Name + " 缺少注释",
                    Suggestion = "COMMENT ON TABLE " + table.Name + " IS '" + table.Comment + "';"
                });
                result.WarningCount++;
            }
        }

        var dbColumnComments = await GetColumnCommentsAsync(table.Name, ct);
        foreach (var column in table.Columns)
        {
            if (!string.IsNullOrEmpty(column.Comment))
            {
                var dbComment = dbColumnComments.GetValueOrDefault(column.Name);
                if (string.IsNullOrEmpty(dbComment))
                {
                    result.Differences.Add(new SchemaDifference
                    {
                        Database = schema.Database,
                        TableName = table.Name,
                        Type = DifferenceType.MissingColumnComment,
                        Severity = SeverityLevel.Warning,
                        ColumnName = column.Name,
                        Expected = column.Comment,
                        Actual = "无注释",
                        Description = "表 " + table.Name + "." + column.Name + " 缺少注释",
                        Suggestion = "COMMENT ON COLUMN " + table.Name + "." + column.Name + " IS '" + column.Comment + "';"
                    });
                    result.WarningCount++;
                }
            }
        }
    }

    private async Task<string?> GetTableCommentAsync(string tableName, CancellationToken ct)
    {
        var sql = @"
            SELECT COALESCE(d.description, '') AS comment
            FROM pg_class c
            LEFT JOIN pg_description d ON d.objoid = c.oid AND d.objsubid = 0
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relname = $1 AND c.relkind = 'r'";

        var result = await _dbService.QuerySingleAsync<string>(sql, ct, tableName);
        return result.IsSuccess ? result.Value : null;
    }

    private async Task<Dictionary<string, string>> GetColumnCommentsAsync(string tableName, CancellationToken ct)
    {
        var sql = @"
            SELECT a.attname AS column_name, 
                   COALESCE(d.description, '') AS comment
            FROM pg_attribute a
            JOIN pg_class c ON c.oid = a.attrelid
            LEFT JOIN pg_description d ON d.objoid = c.oid AND d.objsubid = a.attnum
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' 
              AND c.relname = $1
              AND a.attnum > 0
              AND NOT a.attisdropped;";

        var result = await _dbService.QueryAsync<ColumnCommentInfo>(sql, ct, tableName);

        if (result.IsFailure || result.Value == null)
        {
            return new Dictionary<string, string>();
        }

        return result.Value.ToDictionary(
            c => c.ColumnName,
            c => c.Comment,
            StringComparer.OrdinalIgnoreCase
        );
    }

    private class ColumnCommentInfo
    {
        public string ColumnName { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
    }

    private async Task<Dictionary<string, DbColumnInfo>> GetTableColumnsAsync(string tableName, CancellationToken ct)
    {
        var sql = @"
            SELECT column_name, data_type, is_nullable
            FROM information_schema.columns
            WHERE table_schema = 'public' AND table_name = $1
            ORDER BY ordinal_position;";

        var result = await _dbService.QueryAsync<ColumnInfo>(sql, ct, tableName);

        if (result.IsFailure || result.Value == null)
        {
            return new Dictionary<string, DbColumnInfo>();
        }

        return result.Value.ToDictionary(
            c => c.ColumnName,
            c => new DbColumnInfo { Type = c.DataType, Nullable = c.IsNullable == "YES" },
            StringComparer.OrdinalIgnoreCase
        );
    }

    private async Task<Result<Dictionary<string, Dictionary<string, DbColumnInfo>>>> GetAllTableColumnsAsync(CancellationToken ct)
    {
        var sql = @"
            SELECT table_name, column_name, data_type, is_nullable
            FROM information_schema.columns
            WHERE table_schema = 'public'
            ORDER BY table_name, ordinal_position;";

        try
        {
            var result = await _dbService.QueryAsync<TableColumnInfoRow>(sql, ct);

            if (result.IsFailure || result.Value == null)
            {
                Logger.Error("获取数据库列信息失败: " + (result.Message ?? "无数据"));
                // 失败必须显式上抛——返回空字典会让上层误判"所有表都缺全部列"
                return Result.Failure<Dictionary<string, Dictionary<string, DbColumnInfo>>>(
                    result.ErrorCode ?? ErrorCodes.UNKNOWN_ERROR, "获取数据库列信息失败: " + (result.Message ?? "无数据"));
            }

            var columns = result.Value
                .GroupBy(c => c.TableName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.ToDictionary(
                        c => c.ColumnName,
                        c => new DbColumnInfo { Type = c.DataType, Nullable = c.IsNullable == "YES" },
                        StringComparer.OrdinalIgnoreCase
                    ),
                    StringComparer.OrdinalIgnoreCase
                );
            return Result.Success(columns);
        }
        catch (Exception ex)
        {
            Logger.Error("获取数据库列信息失败: " + ex.Message);
            return Result.FromException<Dictionary<string, Dictionary<string, DbColumnInfo>>>(ex);
        }
    }

    private class TableColumnInfoRow
    {
        public string TableName { get; set; } = string.Empty;
        public string ColumnName { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public string IsNullable { get; set; } = string.Empty;
    }

    private void InvalidateSchemaStatusCache()
    {
        lock (_schemaStatusCacheLock)
        {
            _schemaStatusCache = null;
            _schemaStatusLastCheckTime = DateTime.MinValue;
        }
        // 同时失效已解析的 YAML 缓存（YAML 变更极少，代价是下次访问重新解析一遍）
        lock (_schemaLoadLock)
        {
            _loadedSchemas = null;
            _schemaCache.Clear();
        }
        Logger.Debug("Schema状态缓存已失效");
    }

    private async Task ValidateIndexesAsync(
        SchemaValidationResult result,
        DatabaseSchema schema,
        TableSchema table,
        CancellationToken ct)
    {
        var dbIndexes = await GetTableIndexesAsync(table.Name, ct);

        foreach (var index in table.Indexes)
        {
            if (!dbIndexes.Contains(index.Name))
            {
                result.Differences.Add(new SchemaDifference
                {
                    Database = schema.Database,
                    TableName = table.Name,
                    Type = DifferenceType.MissingIndex,
                    Severity = SeverityLevel.Warning,
                    ColumnName = string.Join(", ", index.Columns),
                    Expected = index.Name,
                    Actual = "该索引不存在",
                    Description = "表 " + table.Name + " 缺少索引 " + index.Name,
                    Suggestion = "CREATE INDEX " + index.Name + " ON " + table.Name + " (" + string.Join(", ", index.Columns) + ");"
                });
                result.WarningCount++;
            }
        }
    }

    private async Task<HashSet<string>> GetTableIndexesAsync(string tableName, CancellationToken ct)
    {
        var sql = @"
            SELECT indexname 
            FROM pg_indexes 
            WHERE schemaname = 'public' AND tablename = $1;";

        var result = await _dbService.QueryAsync<string>(sql, ct, tableName);
        return result.IsSuccess
            ? new HashSet<string>(result.Value!, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static bool TypesMatch(string yamlType, string dbType)
    {
        var normalizedYaml = NormalizeType(yamlType);
        var normalizedDb = NormalizeType(dbType);

        if (string.Equals(normalizedYaml, normalizedDb, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (TYPE_ALIASES.TryGetValue(normalizedYaml, out var aliases) &&
            aliases.Contains(normalizedDb, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (TYPE_ALIASES.TryGetValue(normalizedDb, out var dbAliases) &&
            dbAliases.Contains(normalizedYaml, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalizedYaml.StartsWith("varchar", StringComparison.OrdinalIgnoreCase) &&
            normalizedDb.Contains("character varying", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalizedYaml.Equals("character varying", StringComparison.OrdinalIgnoreCase) &&
            normalizedDb.StartsWith("varchar", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalizedYaml.StartsWith("decimal", StringComparison.OrdinalIgnoreCase) &&
            normalizedDb.Equals("numeric", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalizedYaml.Equals("numeric", StringComparison.OrdinalIgnoreCase) &&
            normalizedDb.StartsWith("decimal", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalizedYaml.StartsWith("timestamp", StringComparison.OrdinalIgnoreCase) &&
            normalizedDb.StartsWith("timestamp", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (normalizedYaml.StartsWith("time", StringComparison.OrdinalIgnoreCase) &&
            normalizedDb.StartsWith("time", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string NormalizeType(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return string.Empty;
        }

        var normalized = type.Trim().ToLowerInvariant();

        var parenIndex = normalized.IndexOf('(');
        if (parenIndex > 0)
        {
            normalized = normalized[..parenIndex].Trim();
        }

        var spaceIndex = normalized.IndexOf(' ');
        if (spaceIndex > 0)
        {
            normalized = normalized[..spaceIndex].Trim();
        }

        return normalized;
    }

    private static readonly Dictionary<string, string[]> TYPE_ALIASES = new(StringComparer.OrdinalIgnoreCase)
    {
        ["integer"] = new[] { "int", "int4", "serial" },
        ["bigint"] = new[] { "int8", "bigserial" },
        ["smallint"] = new[] { "int2" },
        ["boolean"] = new[] { "bool" },
        ["character varying"] = new[] { "varchar" },
        ["timestamp without time zone"] = new[] { "timestamp" },
        ["timestamp with time zone"] = new[] { "timestamptz" },
        ["double precision"] = new[] { "float8", "float" },
        ["real"] = new[] { "float4" },
        ["numeric"] = new[] { "decimal" },
    };

    private class DbColumnInfo
    {
        public string Type { get; set; } = string.Empty;
        public bool Nullable { get; set; }
    }

    private class ColumnInfo
    {
        public string ColumnName { get; set; } = string.Empty;
        public string DataType { get; set; } = string.Empty;
        public string IsNullable { get; set; } = "YES";
    }

    public async Task<Result<SchemaFixResult>> FixSchemaDifferencesAsync(
        IReadOnlyList<SchemaDifference> differences,
        CancellationToken ct = default)
    {
        Logger.Info("开始修复结构错误，共 " + differences.Count + " 个差异");

        var result = new SchemaFixResult();

        var schemas = LoadAllSchemas();
        var schemaDict = new Dictionary<string, DatabaseSchema>(StringComparer.OrdinalIgnoreCase);
        foreach (var schema in schemas)
        {
            schemaDict[schema.Database] = schema;
        }

        foreach (var diff in differences)
        {
            if (diff.Severity != SeverityLevel.Error && diff.Severity != SeverityLevel.Warning)
            {
                result.SkippedCount++;
                result.SkippedItems.Add("[跳过-非错误] " + diff.Description);
                continue;
            }

            if (diff.Type == DifferenceType.ExtraTable)
            {
                result.SkippedCount++;
                result.SkippedItems.Add("[跳过-多余表] " + diff.TableName);
                Logger.Info("跳过多余表: " + diff.TableName);
                continue;
            }

            if (diff.Type == DifferenceType.ExtraColumn)
            {
                result.SkippedCount++;
                result.SkippedItems.Add("[跳过-多余列] " + diff.TableName + "." + diff.ColumnName);
                Logger.Info("跳过多余列: " + diff.TableName + "." + diff.ColumnName);
                continue;
            }

            bool fixSuccess = false;

            if (diff.Type == DifferenceType.MissingTable)
            {
                fixSuccess = await FixMissingTableAsync(schemaDict, diff, result, ct);
            }
            else if (diff.Type == DifferenceType.MissingColumn)
            {
                fixSuccess = await FixMissingColumnAsync(schemaDict, diff, result, ct);
            }
            else if (diff.Type == DifferenceType.MissingIndex)
            {
                fixSuccess = await FixMissingIndexAsync(schemaDict, diff, result, ct);
            }
            else if (diff.Type == DifferenceType.MissingConstraint)
            {
                fixSuccess = await FixMissingConstraintAsync(schemaDict, diff, result, ct);
            }
            else if (diff.Type == DifferenceType.TypeMismatch)
            {
                fixSuccess = await FixTypeMismatchAsync(schemaDict, diff, result, ct);
            }
            else if (diff.Type == DifferenceType.NullableMismatch)
            {
                fixSuccess = await FixNullableMismatchAsync(schemaDict, diff, result, ct);
            }
            else if (diff.Type == DifferenceType.MissingTableComment)
            {
                fixSuccess = await FixMissingTableCommentAsync(diff, result, ct);
            }
            else if (diff.Type == DifferenceType.MissingColumnComment)
            {
                fixSuccess = await FixMissingColumnCommentAsync(diff, result, ct);
            }

            if (!fixSuccess)
            {
                result.FailedCount++;
                result.FailedItems.Add("[失败] " + diff.Description);
            }
        }

        Logger.Info("结构修复完成，成功 " + result.TotalFixed + " 个");

        InvalidateSchemaStatusCache();

        return Result<SchemaFixResult>.Success(result);
    }

    private async Task<bool> FixMissingTableAsync(
        Dictionary<string, DatabaseSchema> schemaDict,
        SchemaDifference diff,
        SchemaFixResult result,
        CancellationToken ct)
    {
        try
        {
            if (!schemaDict.TryGetValue(diff.Database, out var schema))
            {
                Logger.Warn("Schema未找到: " + diff.Database);
                return false;
            }

            var table = schema.Tables.FirstOrDefault(t => t.Name.Equals(diff.TableName, StringComparison.OrdinalIgnoreCase));
            if (table == null)
            {
                Logger.Warn("表定义未找到: " + diff.TableName);
                return false;
            }

            var sql = GenerateCreateTableSql(table, includeForeignKeys: true);
            var execResult = await ExecuteCreateTableAsync(sql, ct);

            if (execResult.IsSuccess)
            {
                result.TotalFixed++;
                result.FixedItems.Add("[修复-缺失表] " + diff.TableName);
                Logger.Info("创建表成功: " + diff.TableName);
                return true;
            }
            else
            {
                Logger.Error("创建表失败: " + diff.TableName);
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("修复缺失表失败: " + ex.Message);
            return false;
        }
    }

    private async Task<bool> FixMissingColumnAsync(
        Dictionary<string, DatabaseSchema> schemaDict,
        SchemaDifference diff,
        SchemaFixResult result,
        CancellationToken ct)
    {
        try
        {
            if (!schemaDict.TryGetValue(diff.Database, out var schema))
            {
                Logger.Warn("Schema未找到: " + diff.Database);
                return false;
            }

            ColumnSchema? column = null;

            // 先在 Tables 中查找
            var table = schema.Tables.FirstOrDefault(t => t.Name.Equals(diff.TableName, StringComparison.OrdinalIgnoreCase));
            if (table != null)
            {
                column = table.Columns.FirstOrDefault(c => c.Name.Equals(diff.ColumnName, StringComparison.OrdinalIgnoreCase));
            }

            // Tables 中找不到，在 AlterTables 中查找
            if (column == null)
            {
                var alterTable = schema.AlterTables.FirstOrDefault(t => t.Name.Equals(diff.TableName, StringComparison.OrdinalIgnoreCase));
                if (alterTable != null)
                {
                    column = alterTable.AddColumns.FirstOrDefault(c => c.Name.Equals(diff.ColumnName, StringComparison.OrdinalIgnoreCase));
                }
            }

            if (column == null)
            {
                Logger.Warn("列定义未找到: " + diff.TableName + "." + diff.ColumnName);
                return false;
            }

            var nullableClause = column.Nullable ? "" : " NOT NULL";
            var defaultClause = string.IsNullOrEmpty(column.Default) ? "" : " DEFAULT " + column.Default;
            var sql = "ALTER TABLE " + diff.TableName + " ADD COLUMN " + column.Name + " " + column.Type + nullableClause + defaultClause;

            var execResult = await _dbService.ExecuteNonQueryAsync(sql, ct);

            if (execResult.IsSuccess)
            {
                result.TotalFixed++;
                result.FixedItems.Add("[修复-缺失列] " + diff.TableName + "." + diff.ColumnName);
                Logger.Info("添加列成功: " + diff.TableName + "." + diff.ColumnName);
                return true;
            }
            else
            {
                Logger.Error("添加列失败: " + diff.TableName + "." + diff.ColumnName);
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("修复缺失列失败: " + ex.Message);
            return false;
        }
    }

    private async Task<bool> FixMissingIndexAsync(
        Dictionary<string, DatabaseSchema> schemaDict,
        SchemaDifference diff,
        SchemaFixResult result,
        CancellationToken ct)
    {
        try
        {
            if (!schemaDict.TryGetValue(diff.Database, out var schema))
            {
                Logger.Warn("Schema未找到: " + diff.Database);
                return false;
            }

            var table = schema.Tables.FirstOrDefault(t => t.Name.Equals(diff.TableName, StringComparison.OrdinalIgnoreCase));
            if (table == null)
            {
                Logger.Warn("表定义未找到: " + diff.TableName);
                return false;
            }

            var indexName = diff.Expected;
            var index = table.Indexes.FirstOrDefault(i => i.Name.Equals(indexName, StringComparison.OrdinalIgnoreCase));
            if (index == null)
            {
                Logger.Warn("索引定义未找到: " + indexName);
                return false;
            }

            var uniqueClause = index.Unique ? "UNIQUE " : "";
            var sql = "CREATE " + uniqueClause + "INDEX IF NOT EXISTS " + index.Name + " ON " + diff.TableName + " (" + string.Join(", ", index.Columns) + ")";

            var execResult = await _dbService.ExecuteNonQueryAsync(sql, ct);

            if (execResult.IsSuccess)
            {
                result.TotalFixed++;
                result.FixedItems.Add("[修复-缺失索引] " + indexName);
                Logger.Info("创建索引成功: " + indexName);
                return true;
            }
            else
            {
                Logger.Error("创建索引失败: " + indexName);
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("修复缺失索引失败: " + ex.Message);
            return false;
        }
    }

    private async Task<bool> FixMissingConstraintAsync(
        Dictionary<string, DatabaseSchema> schemaDict,
        SchemaDifference diff,
        SchemaFixResult result,
        CancellationToken ct)
    {
        try
        {
            if (!schemaDict.TryGetValue(diff.Database, out var schema))
            {
                Logger.Warn("Schema未找到: " + diff.Database);
                return false;
            }

            var table = schema.Tables.FirstOrDefault(t => t.Name.Equals(diff.TableName, StringComparison.OrdinalIgnoreCase));
            if (table == null)
            {
                Logger.Warn("表定义未找到: " + diff.TableName);
                return false;
            }

            var constraintName = diff.Expected;
            var constraint = table.Constraints.FirstOrDefault(c => c.Name.Equals(constraintName, StringComparison.OrdinalIgnoreCase));
            if (constraint == null)
            {
                Logger.Warn("约束定义未找到: " + constraintName);
                return false;
            }

            var constraintDef = GenerateConstraintDefinition(constraint);
            if (string.IsNullOrEmpty(constraintDef))
            {
                Logger.Warn("约束定义为空: " + constraintName);
                return false;
            }

            var sql = "ALTER TABLE " + diff.TableName + " ADD " + constraintDef;

            var execResult = await _dbService.ExecuteNonQueryAsync(sql, ct);

            if (execResult.IsSuccess)
            {
                result.TotalFixed++;
                result.FixedItems.Add("[修复-缺失约束] " + constraintName);
                Logger.Info("添加约束成功: " + constraintName);
                return true;
            }
            else
            {
                Logger.Error("添加约束失败: " + constraintName);
                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.Error("修复缺失约束失败: " + ex.Message);
            return false;
        }
    }

    private async Task<bool> FixTypeMismatchAsync(
        Dictionary<string, DatabaseSchema> schemaDict,
        SchemaDifference diff,
        SchemaFixResult result,
        CancellationToken ct)
    {
        try
        {
            var targetType = NormalizeTypeForFix(diff.Expected);
            var sql = "ALTER TABLE " + diff.TableName + " ALTER COLUMN " + diff.ColumnName + " TYPE " + targetType + " USING " + diff.ColumnName + "::" + targetType;

            var execResult = await _dbService.ExecuteNonQueryAsync(sql, ct);

            if (execResult.IsSuccess)
            {
                result.TotalFixed++;
                result.FixedItems.Add("[修复-类型不匹配] " + diff.TableName + "." + diff.ColumnName);
                Logger.Info("类型修复成功: " + diff.TableName + "." + diff.ColumnName);
                return true;
            }
            else
            {
                if (execResult.Message.Contains("0A000") == true ||
                    execResult.Message.Contains("view or rule") == true)
                {
                    result.SkippedCount++;
                    result.SkippedItems.Add("[跳过-视图或规则] " + diff.TableName + "." + diff.ColumnName + "，该字段跳过");
                    Logger.Warn("类型修复跳过（视图或规则）: " + diff.TableName + "." + diff.ColumnName);
                    return true;
                }
                Logger.Error("类型修复失败: " + diff.TableName + "." + diff.ColumnName);
                return false;
            }
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("0A000") || ex.Message.Contains("view or rule"))
            {
                result.SkippedCount++;
                result.SkippedItems.Add("[跳过-视图或规则] " + diff.TableName + "." + diff.ColumnName + "，该字段跳过");
                Logger.Warn("类型修复跳过（视图或规则）: " + diff.TableName + "." + diff.ColumnName);
                return true;
            }
            Logger.Error("类型修复失败: " + ex.Message);
            return false;
        }
    }

    private static string NormalizeTypeForFix(string type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return type;
        }

        var trimmed = type.Trim();

        if (trimmed.StartsWith("varchar", StringComparison.OrdinalIgnoreCase))
        {
            var parenIndex = trimmed.IndexOf('(');
            if (parenIndex > 0)
            {
                return trimmed;
            }
            return "varchar(255)";
        }

        if (trimmed.StartsWith("character varying", StringComparison.OrdinalIgnoreCase))
        {
            var parenIndex = trimmed.IndexOf('(');
            if (parenIndex > 0)
            {
                return "varchar" + trimmed.Substring(parenIndex);
            }
            return "varchar(255)";
        }

        if (trimmed.StartsWith("decimal", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("numeric", StringComparison.OrdinalIgnoreCase))
        {
            var parenIndex = trimmed.IndexOf('(');
            if (parenIndex > 0)
            {
                return trimmed;
            }
            return "numeric(18,2)";
        }

        return trimmed;
    }

    private async Task<bool> FixNullableMismatchAsync(
        Dictionary<string, DatabaseSchema> schemaDict,
        SchemaDifference diff,
        SchemaFixResult result,
        CancellationToken ct)
    {
        try
        {
            var yamlNullable = diff.Expected == "YES";
            var sql = yamlNullable
                ? "ALTER TABLE " + diff.TableName + " ALTER COLUMN " + diff.ColumnName + " DROP NOT NULL"
                : "ALTER TABLE " + diff.TableName + " ALTER COLUMN " + diff.ColumnName + " SET NOT NULL";

            var execResult = await _dbService.ExecuteNonQueryAsync(sql, ct);

            if (execResult.IsSuccess)
            {
                result.TotalFixed++;
                result.FixedItems.Add("[修复-可空性] " + diff.TableName + "." + diff.ColumnName + " -> " + (yamlNullable ? "NULL" : "NOT NULL"));
                Logger.Info("修复可空性成功: " + diff.TableName + "." + diff.ColumnName + " -> " + (yamlNullable ? "NULL" : "NOT NULL"));
                return true;
            }
            else
            {
                if (execResult.Message.Contains("42P16") == true ||
                    execResult.Message.Contains("primary key") == true)
                {
                    result.SkippedCount++;
                    result.SkippedItems.Add("[跳过-主键约束] " + diff.TableName + "." + diff.ColumnName);
                    Logger.Warn("可空性修改跳过（主键约束）: " + diff.TableName + "." + diff.ColumnName);
                    return true;
                }
                if (execResult.Message.Contains("0A000") == true ||
                    execResult.Message.Contains("view or rule") == true)
                {
                    result.SkippedCount++;
                    result.SkippedItems.Add("[跳过-视图或规则] " + diff.TableName + "." + diff.ColumnName + "，该字段跳过");
                    Logger.Warn("可空性修改跳过（视图或规则）: " + diff.TableName + "." + diff.ColumnName);
                    return true;
                }
                Logger.Error("可空性修复失败: " + diff.TableName + "." + diff.ColumnName);
                return false;
            }
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("42P16") || ex.Message.Contains("primary key"))
            {
                result.SkippedCount++;
                result.SkippedItems.Add("[跳过-主键约束] " + diff.TableName + "." + diff.ColumnName);
                Logger.Warn("可空性修改跳过（主键约束）: " + diff.TableName + "." + diff.ColumnName);
                return true;
            }
            if (ex.Message.Contains("0A000") || ex.Message.Contains("view or rule"))
            {
                result.SkippedCount++;
                result.SkippedItems.Add("[跳过-视图或规则] " + diff.TableName + "." + diff.ColumnName + "，该字段跳过");
                Logger.Warn("可空性修改跳过（视图或规则）: " + diff.TableName + "." + diff.ColumnName);
                return true;
            }
            Logger.Error("可空性修复失败: " + ex.Message);
            return false;
        }
    }

    private async Task<bool> FixMissingTableCommentAsync(
        SchemaDifference diff,
        SchemaFixResult result,
        CancellationToken ct)
    {
        try
        {
            var comment = diff.Expected.Replace("'", "''");
            var sql = "COMMENT ON TABLE " + diff.TableName + " IS '" + comment + "'";

            var execResult = await _dbService.ExecuteNonQueryAsync(sql, ct);

            if (execResult.IsSuccess)
            {
                result.TotalFixed++;
                result.FixedItems.Add("[修复-注释] " + diff.TableName);
                Logger.Info("添加表注释成功: " + diff.TableName);
                return true;
            }

            Logger.Error("添加表注释失败: " + diff.TableName);
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("添加表注释失败: " + ex.Message);
            return false;
        }
    }

    private async Task<bool> FixMissingColumnCommentAsync(
        SchemaDifference diff,
        SchemaFixResult result,
        CancellationToken ct)
    {
        try
        {
            var comment = diff.Expected.Replace("'", "''");
            var sql = "COMMENT ON COLUMN " + diff.TableName + "." + diff.ColumnName + " IS '" + comment + "'";

            var execResult = await _dbService.ExecuteNonQueryAsync(sql, ct);

            if (execResult.IsSuccess)
            {
                result.TotalFixed++;
                result.FixedItems.Add("[修复-注释] " + diff.TableName + "." + diff.ColumnName);
                Logger.Info("添加表注释成功: " + diff.TableName + "." + diff.ColumnName);
                return true;
            }

            Logger.Error("添加表注释失败: " + diff.TableName + "." + diff.ColumnName);
            return false;
        }
        catch (Exception ex)
        {
            Logger.Error("添加表注释失败: " + ex.Message);
            return false;
        }
    }
}
