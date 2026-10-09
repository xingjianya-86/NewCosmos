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

public partial class SchemaService : BaseService, ISchemaService
{
    protected override string ServiceName => "SchemaService";

    private readonly IDatabaseService _dbService;
    private readonly IConfigService _configService;
    private readonly string _schemaDirectory;
    private readonly IDeserializer _deserializer;
    private readonly ConcurrentDictionary<string, DatabaseSchema> _schemaCache;

    private SchemaStatus _schemaStatusCache = null!;
    private DateTime _schemaStatusLastCheckTime = DateTime.MinValue;
    private static readonly TimeSpan SCHEMA_STATUS_CACHE_DURATION = TimeSpan.FromSeconds(30);
    private readonly object _schemaStatusCacheLock = new();

    public SchemaService(IDatabaseService dbService, IConfigService configService, ILoggerService logger)
        : base(logger)
    {
        _dbService = dbService;
        _configService = configService;
        _schemaCache = new ConcurrentDictionary<string, DatabaseSchema>(StringComparer.OrdinalIgnoreCase);
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        _schemaDirectory = FindSchemaDirectory();
        InvalidateSchemaStatusCache();
        Logger.Info("SchemaService initialized");
    }

    private static string FindSchemaDirectory()
    {
        string[] possiblePaths =
        {
            Path.Combine(AppContext.BaseDirectory, "Resources", "Schema"),
            Path.Combine(AppContext.BaseDirectory, "..", "Resources", "Schema"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "Resources", "Schema"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Resources", "Schema"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Resources", "Schema"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "Resources", "Schema"),
        };

        foreach (var path in possiblePaths)
        {
            var fullPath = Path.GetFullPath(path);
            if (Directory.Exists(fullPath))
                return fullPath;
        }

        return Path.GetFullPath(possiblePaths[0]);
    }

    public string GetSchemaDirectory() => _schemaDirectory;

    /// <summary>已解析 Schema 的读透缓存。YAML 在运行期不会变化，进程内只需解析一次；
    /// 旧实现每次调用都重读 55 个文件（400KB）并反序列化，且发生在 UI 线程上，是首屏延迟的主因之一。</summary>
    private List<DatabaseSchema>? _loadedSchemas;
    private readonly object _schemaLoadLock = new();

    public List<DatabaseSchema> LoadAllSchemas()
    {
        var cached = _loadedSchemas;
        if (cached != null)
            return cached;

        lock (_schemaLoadLock)
        {
            if (_loadedSchemas != null)
                return _loadedSchemas;

            _loadedSchemas = LoadAllSchemasCore();
            return _loadedSchemas;
        }
    }

    private List<DatabaseSchema> LoadAllSchemasCore()
    {
        var schemas = new List<DatabaseSchema>();
        var loadedDatabases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var failedFiles = new List<string>();

        if (!Directory.Exists(_schemaDirectory))
        {
            Logger.Warn("Schema目录不存在: " + _schemaDirectory);
            return schemas;
        }

        var schemaDirectories = Directory.GetDirectories(_schemaDirectory);
        foreach (var dir in schemaDirectories)
        {
            var yamlFiles = Directory.GetFiles(dir, "*.yaml")
                .Where(f => !f.EndsWith(".back", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (yamlFiles.Count == 0)
            {
                Logger.Debug("Schema目录无YAML文件: " + Path.GetFileName(dir));
                continue;
            }

            foreach (var yamlPath in yamlFiles)
            {
                try
                {
                    var yamlContent = File.ReadAllText(yamlPath);
                    var schema = _deserializer.Deserialize<DatabaseSchema>(yamlContent);
                    if (schema != null && !string.IsNullOrEmpty(schema.Database) && (schema.Tables.Count > 0 || schema.AlterTables.Count > 0))
                    {
                        // 把表级 foreignKeys: 段归一化为 FOREIGN KEY 约束（走既有约束管线）
                        foreach (var table in schema.Tables)
                        {
                            NormalizeForeignKeys(table);
                        }

                        if (loadedDatabases.Contains(schema.Database))
                        {
                            // 同名 database 的后续文件：合并表定义。
                            // 历史实现在这里只合并 alterTables、把 Tables 整体丢弃——
                            // social_assistance/ 下 17 个文件（含全部外键与大量索引声明）因此从未生效。
                            if (_schemaCache.TryGetValue(schema.Database, out var existing))
                            {
                                MergeSchemaTables(existing, schema, Path.GetFileName(yamlPath));

                                if (schema.AlterTables.Count > 0)
                                {
                                    existing.AlterTables.AddRange(schema.AlterTables);
                                    Logger.Info("合并 alterTables: " + schema.Database + " (" + Path.GetFileName(yamlPath) + ") 共 " + schema.AlterTables.Count + " 个扩展表");
                                }
                            }
                            continue;
                        }

                        _schemaCache[schema.Database] = schema;
                        schemas.Add(schema);
                        loadedDatabases.Add(schema.Database);
                        Logger.Debug("加载Schema: " + schema.Database + " (" + Path.GetFileName(yamlPath) + ") 包含 " + schema.Tables.Count + " 张表");
                    }
                    else
                    {
                        var reason = schema == null ? "反序列化返回null" :
                            string.IsNullOrEmpty(schema.Database) ? "Database字段为空" :
                            schema.Tables.Count == 0 ? "Tables列表为空" : "未知原因";
                        Logger.Warn("Schema文件内容无效: " + yamlPath + " - " + reason);
                        failedFiles.Add(Path.GetFileName(yamlPath));
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error("加载Schema文件失败: " + yamlPath + " - " + ex.Message);
                    failedFiles.Add(Path.GetFileName(yamlPath));
                }
            }
        }

        if (failedFiles.Count > 0)
        {
            Logger.Warn("Schema加载汇总: 共 " + schemas.Count + " 个成功, " + failedFiles.Count + " 个失败 [" + string.Join(", ", failedFiles) + "]");
        }
        else
        {
            Logger.Info("Schema加载汇总: 共 " + schemas.Count + " 个Schema文件加载成功");
        }

        return schemas;
    }

    /// <summary>
    /// 把表级 foreignKeys: 段转换为 FOREIGN KEY 约束（去重），使 DDL 生成/校验/补建都能看到外键。
    /// </summary>
    private static void NormalizeForeignKeys(TableSchema table)
    {
        if (table.ForeignKeys.Count == 0)
            return;

        foreach (var fk in table.ForeignKeys)
        {
            if (string.IsNullOrEmpty(fk.Name) || fk.Columns.Count == 0 ||
                string.IsNullOrEmpty(fk.References.Table) || fk.References.Columns.Count == 0)
                continue;

            if (table.Constraints.Any(c => c.Name.Equals(fk.Name, StringComparison.OrdinalIgnoreCase)))
                continue;

            table.Constraints.Add(new ConstraintSchema
            {
                Name = fk.Name,
                Type = "FOREIGN KEY",
                Columns = fk.Columns,
                References = new ForeignKeyReference
                {
                    Table = fk.References.Table,
                    Column = fk.References.Columns[0],
                    OnDelete = fk.OnDelete
                }
            });
        }
    }

    /// <summary>
    /// 合并同名 database 的表定义：
    /// - 新表名 → 直接加入；
    /// - 重复表名 → 保留先加载的列定义（列数不一致时告警），索引与约束按名字取并集。
    ///   典型场景：family_economy/database.yaml（无索引版）先加载，
    ///   social_assistance/*.yaml（带索引+外键版）后加载——合并后索引与外键得以生效。
    /// </summary>
    private void MergeSchemaTables(DatabaseSchema existing, DatabaseSchema incoming, string sourceFile)
    {
        foreach (var table in incoming.Tables)
        {
            var current = existing.Tables.FirstOrDefault(t =>
                t.Name.Equals(table.Name, StringComparison.OrdinalIgnoreCase));

            if (current == null)
            {
                existing.Tables.Add(table);
                Logger.Info("合并新表: " + table.Name + " (来自 " + sourceFile + ")");
                continue;
            }

            if (current.Columns.Count != table.Columns.Count)
            {
                Logger.Warn("表 " + table.Name + " 在多个 YAML 中列数不一致 (" +
                    current.Columns.Count + " vs " + table.Columns.Count + ", 来自 " + sourceFile +
                    ")，保留先加载的列定义");
            }

            var mergedIndexes = 0;
            foreach (var index in table.Indexes)
            {
                if (!current.Indexes.Any(i => i.Name.Equals(index.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    current.Indexes.Add(index);
                    mergedIndexes++;
                }
            }

            var mergedConstraints = 0;
            foreach (var constraint in table.Constraints)
            {
                if (!current.Constraints.Any(c => c.Name.Equals(constraint.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    current.Constraints.Add(constraint);
                    mergedConstraints++;
                }
            }

            if (mergedIndexes > 0 || mergedConstraints > 0)
            {
                Logger.Info("合并表 " + table.Name + ": +" + mergedIndexes + " 索引, +" +
                    mergedConstraints + " 约束 (来自 " + sourceFile + ")");
            }
        }
    }

    /// <summary>
    /// 按表名在全部 YAML Schema（合并后）中查找表定义。
    /// 供 SchemaSyncService 等以 YAML 为唯一权威来源重建表结构时使用。
    /// </summary>
    public TableSchema? GetTableSchema(string tableName)
    {
        if (string.IsNullOrWhiteSpace(tableName))
            return null;

        foreach (var schema in LoadAllSchemas())
        {
            var table = schema.Tables.FirstOrDefault(t =>
                t.Name.Equals(tableName, StringComparison.OrdinalIgnoreCase));
            if (table != null)
                return table;
        }

        return null;
    }

    public List<string> GetAllYamlTableNames()
    {
        var schemas = LoadAllSchemas();
        var schemaOptions = _configService.GetSchemaOptions();
        var tableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var schema in schemas)
        {
            foreach (var table in schema.Tables)
            {
                if (!string.IsNullOrEmpty(schemaOptions.TablePrefixFilter))
                {
                    if (!table.Name.StartsWith(schemaOptions.TablePrefixFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                tableNames.Add(table.Name);
            }
        }

        return tableNames.ToList();
    }

    private string GetSchemaFilePath(string databaseName)
    {
        string subDirectory = databaseName switch
        {
            "sys_dictionary" or "系统字典管理" => "sys_dictionary",
            "new_permission_system" => "new_permission_system",
            "config_standards" => "config_standards",
            "standards" => "standards",
            _ => databaseName
        };

        return Path.Combine(_schemaDirectory, subDirectory, "database.yaml");
    }

    public async Task<Result<SchemaStatus>> GetSchemaStatusAsync(CancellationToken ct = default)
    {
        lock (_schemaStatusCacheLock)
        {
            if (_schemaStatusCache != null && DateTime.Now - _schemaStatusLastCheckTime < SCHEMA_STATUS_CACHE_DURATION)
            {
                Logger.Debug("Schema状态使用缓存（手动刷新实时更新）");
                var cached = _schemaStatusCache;
                var freshOptions = _configService.GetSchemaOptions();
                var merged = new SchemaStatus
                {
                    CurrentVersion = freshOptions.CurrentVersion,
                    InitializedAt = freshOptions.InitializedAt,
                    InitializedBy = freshOptions.InitializedBy,
                    IsInitialized = freshOptions.IsInitialized,
                    TotalRequiredTables = cached.TotalRequiredTables,
                    ExistingTables = cached.ExistingTables,
                    MissingTables = cached.MissingTables,
                    MissingColumns = cached.MissingColumns
                };
                return Result.Success(merged);
            }
        }

        Logger.Info("检查数据库Schema状态");

        var connectionTest = await _dbService.TestConnectionAsync(ct);
        if (!connectionTest.IsSuccess)
        {
            Logger.Error("数据库连接失败，无法检查Schema状态");
            return Result.Failure<SchemaStatus>(connectionTest.ErrorCode, connectionTest.Message);
        }
        Logger.Info("数据库连接测试成功");

        var schemaOptions = _configService.GetSchemaOptions();
        var status = new SchemaStatus
        {
            CurrentVersion = schemaOptions.CurrentVersion,
            InitializedAt = schemaOptions.InitializedAt,
            InitializedBy = schemaOptions.InitializedBy,
            IsInitialized = schemaOptions.IsInitialized
        };

        // 首次加载在线程池解析 55 个 YAML（约 400KB），避免阻塞 UI 线程；此后走读透缓存
        var schemas = await Task.Run(() => LoadAllSchemas(), ct);
        var allRequiredTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var schema in schemas)
        {
            foreach (var table in schema.Tables)
            {
                if (!string.IsNullOrEmpty(schemaOptions.TablePrefixFilter))
                {
                    if (!table.Name.StartsWith(schemaOptions.TablePrefixFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                }
                allRequiredTables.Add(table.Name);
            }
        }
        status.TotalRequiredTables = allRequiredTables.Count;
        Logger.Info("YAML定义的表数量: " + allRequiredTables.Count + " (Schema文件: " + schemas.Count + " 个)");

        var dbTablesResult = await GetDatabaseTablesAsync(ct);
        if (dbTablesResult.IsFailure)
        {
            return Result.Failure<SchemaStatus>(dbTablesResult.ErrorCode, dbTablesResult.Message);
        }
        var dbTables = dbTablesResult.Value!;
        Logger.Info("数据库实际表数量: " + dbTables.Count);
        foreach (var table in allRequiredTables)
        {
            if (dbTables.Contains(table))
            {
                status.ExistingTables.Add(table);
            }
            else
            {
                status.MissingTables.Add(table);
            }
        }

        var allDbColumnsResult = await GetAllTableColumnsAsync(ct);
        if (allDbColumnsResult.IsFailure)
        {
            return Result.Failure<SchemaStatus>(allDbColumnsResult.ErrorCode, allDbColumnsResult.Message);
        }
        var allDbColumns = allDbColumnsResult.Value!;
        foreach (var schema in schemas)
        {
            foreach (var table in schema.Tables)
            {
                if (!status.ExistingTables.Contains(table.Name)) continue;

                if (!allDbColumns.TryGetValue(table.Name, out var dbColumns))
                    continue;

                var missingCols = table.Columns
                    .Where(c => !dbColumns.ContainsKey(c.Name))
                    .Select(c => c.Name)
                    .ToList();

                if (missingCols.Count > 0)
                {
                    status.MissingColumns[table.Name] = missingCols;
                    Logger.Warn("表 " + table.Name + " 缺少列: " + string.Join(", ", missingCols));
                }
            }

            // 检查 alterTables 扩展列
            foreach (var alterTable in schema.AlterTables)
            {
                if (!allDbColumns.TryGetValue(alterTable.Name, out var dbColumns))
                    continue;

                var missingCols = alterTable.AddColumns
                    .Where(c => !dbColumns.ContainsKey(c.Name))
                    .Select(c => c.Name)
                    .ToList();

                if (missingCols.Count > 0)
                {
                    if (!status.MissingColumns.ContainsKey(alterTable.Name))
                        status.MissingColumns[alterTable.Name] = new List<string>();
                    status.MissingColumns[alterTable.Name].AddRange(missingCols);
                    Logger.Warn("表 " + alterTable.Name + " 缺少扩展列: " + string.Join(", ", missingCols));
                }
            }
        }

        if (status.MissingTables.Count == 0)
        {
            if (!schemaOptions.IsInitialized)
            {
                Logger.Info("检测到所有表已存在但未初始化，自动同步初始化状态");

                schemaOptions.InitializedAt = DateTime.Now.ToString("O");
                schemaOptions.InitializedBy = "auto-detect";
                schemaOptions.CurrentVersion = "1.0";
                _configService.SaveSchemaOptions(schemaOptions);

                status.IsInitialized = true;
                status.CurrentVersion = "1.0";
                Logger.Info("Schema自动同步完成");
            }
            else
            {
                status.IsInitialized = true;
                Logger.Info("Schema状态: 已初始化");
            }
        }
        else if (status.ExistingTables.Count > 0)
        {
            Logger.Info("Schema状态: 部分就绪, 已存在 " + status.ExistingTables.Count + " 个表, 缺失 " + status.MissingTables.Count + " 个表: [" + string.Join(", ", status.MissingTables) + "]");
        }
        else
        {
            status.IsInitialized = false;
            Logger.Info("Schema状态: 未初始化, 需要创建 " + status.TotalRequiredTables + " 个表");
        }

        Logger.Info("Schema检查结果: 已初始化=" + status.IsInitialized
            + " 缺失表=" + status.MissingTables.Count
            + " 缺失列=" + status.TotalMissingColumns);

        lock (_schemaStatusCacheLock)
        {
            _schemaStatusCache = status;
            _schemaStatusLastCheckTime = DateTime.Now;
        }

        return Result<SchemaStatus>.Success(status);
    }

    public async Task<Result> InitializeAllTablesAsync(string initializedBy, CancellationToken ct = default)
    {
        Logger.Info("开始初始化所有表");

        var schemaOptions = _configService.GetSchemaOptions();
        var schemas = LoadAllSchemas();
        var createdCount = 0;
        var failedTables = new List<string>();

        foreach (var schema in schemas)
        {
            foreach (var table in schema.Tables)
            {
                if (!string.IsNullOrEmpty(schemaOptions.TablePrefixFilter))
                {
                    if (!table.Name.StartsWith(schemaOptions.TablePrefixFilter, StringComparison.OrdinalIgnoreCase))
                        continue;
                }

                var existsResult = await TableExistsAsync(table.Name, ct);
                if (existsResult.IsSuccess && existsResult.Value)
                {
                    // 表已存在也要补建缺失的索引与约束——
                    // 历史实现直接 continue，导致 YAML 里后续新增的索引永远不会应用到已有库
                    await EnsureIndexesAndConstraintsAsync(table, ct);
                    continue;
                }

                var sql = GenerateCreateTableSql(table);
                var result = await ExecuteCreateTableAsync(sql, ct);
                if (result.IsSuccess)
                {
                    createdCount++;
                    Logger.Info("创建表成功: " + table.Name);
                }
                else
                {
                    failedTables.Add(table.Name);
                    Logger.Error("创建表失败: " + table.Name);
                }
            }
        }

        if (failedTables.Count > 0)
        {
            return Result.Failure(ErrorCodes.DB_QUERY_ERROR, "以下表创建失败: " + string.Join(", ", failedTables));
        }

        schemaOptions.InitializedAt = DateTime.Now.ToString("O");
        schemaOptions.InitializedBy = initializedBy;
        _configService.SaveSchemaOptions(schemaOptions);

        Logger.Info("所有表初始化完成，共创建 " + createdCount + " 张表");

        InvalidateSchemaStatusCache();

        return Result.Success();
    }

    /// <summary>
    /// 对已存在的表幂等补建缺失的索引与约束。
    /// 索引走 CREATE INDEX IF NOT EXISTS；约束先查 pg_constraint 再 ALTER TABLE ADD。
    /// 补建失败只告警不中断（外键可能因存量脏数据无法建立，需人工清理后重试）。
    /// [N+1 豁免] DDL 管理域：CREATE INDEX/ADD CONSTRAINT 语句内容各不相同、无法合并为多行 VALUES，
    /// 且属一次性幂等低频操作（仅建库/升级时执行）。
    /// </summary>
    private async Task EnsureIndexesAndConstraintsAsync(TableSchema table, CancellationToken ct)
    {
        foreach (var index in table.Indexes)
        {
            var indexSql = GenerateIndexSql(table.Name, index).TrimEnd(';', '\r', '\n');
            var result = await _dbService.ExecuteNonQueryAsync(indexSql, ct);
            if (result.IsFailure)
            {
                Logger.Warn("补建索引失败: " + index.Name + " on " + table.Name + " - " + result.Message);
            }
        }

        foreach (var constraint in table.Constraints)
        {
            if (string.IsNullOrEmpty(constraint.Name))
                continue;

            var existsResult = await _dbService.ExecuteScalarAsync(
                "SELECT COUNT(*) FROM pg_constraint WHERE conname = $1", ct, constraint.Name);
            if (existsResult.IsFailure || existsResult.Value > 0)
                continue;

            var constraintDef = GenerateConstraintDefinition(constraint);
            if (string.IsNullOrEmpty(constraintDef))
                continue;

            var alterResult = await _dbService.ExecuteNonQueryAsync(
                "ALTER TABLE " + table.Name + " ADD " + constraintDef, ct);
            if (alterResult.IsSuccess)
            {
                Logger.Info("补建约束成功: " + constraint.Name + " on " + table.Name);
            }
            else
            {
                Logger.Warn("补建约束失败: " + constraint.Name + " on " + table.Name + " - " + alterResult.Message +
                    "（外键可能因存量数据不满足引用完整性而失败，需人工清理后重试）");
            }
        }
    }

    public string GenerateCreateTableSql(TableSchema table, bool includeForeignKeys = true)
    {
        var sb = new StringBuilder();

        sb.AppendLine("CREATE TABLE IF NOT EXISTS " + table.Name + " (");

        var columnDefs = new List<string>();
        var primaryKeys = new List<string>();

        foreach (var column in table.Columns)
        {
            string columnDef = GenerateColumnDefinition(column);
            columnDefs.Add("    " + columnDef);

            if (column.PrimaryKey)
            {
                primaryKeys.Add(column.Name);
            }
        }

        if (primaryKeys.Count > 0)
        {
            columnDefs.Add("    PRIMARY KEY (" + string.Join(", ", primaryKeys) + ")");
        }

        foreach (var constraint in table.Constraints)
        {
            if (!includeForeignKeys && constraint.Type.Equals("FOREIGN KEY", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string constraintDef = GenerateConstraintDefinition(constraint);
            if (!string.IsNullOrEmpty(constraintDef))
            {
                columnDefs.Add("    " + constraintDef);
            }
        }

        sb.AppendLine(string.Join(",\n", columnDefs));
        sb.AppendLine(");");

        foreach (var index in table.Indexes)
        {
            sb.AppendLine(GenerateIndexSql(table.Name, index));
        }

        if (!string.IsNullOrEmpty(table.Comment))
        {
            var modulePrefix = GetModulePrefix(table.Name);
            var safeComment = table.Comment.Replace("'", "''");
            sb.AppendLine("COMMENT ON TABLE " + table.Name + " IS '[" + modulePrefix + "] " + safeComment + "';");
        }

        foreach (var column in table.Columns)
        {
            if (!string.IsNullOrEmpty(column.Comment))
            {
                var safeComment = column.Comment.Replace("'", "''");
                sb.AppendLine("COMMENT ON COLUMN " + table.Name + "." + column.Name + " IS '" + safeComment + "';");
            }
        }

        return sb.ToString();
    }

    private static string GetModulePrefix(string tableName)
    {
        return tableName switch
        {
            var name when name.StartsWith("nc_dict") => "字典",
            var name when name.StartsWith("nc_biz") => "业务",
            var name when name.StartsWith("nc_biz_household") || name.StartsWith("nc_biz_survey") => "入户调查",
            var name when name.StartsWith("nc_biz_labor") || name.StartsWith("nc_biz_business") || name.StartsWith("nc_biz_property_incomes") || name.StartsWith("nc_biz_transfer") || name.StartsWith("nc_biz_other_incomes") || name.StartsWith("nc_biz_alimony") || name.StartsWith("nc_biz_land_incomes") || name.StartsWith("nc_biz_subsidy_incomes") => "家庭收入",
            var name when name.StartsWith("nc_biz_properties") || name.StartsWith("nc_biz_vehicles") || name.StartsWith("nc_biz_machineries") || name.StartsWith("nc_biz_financial") => "家庭财产",
            var name when name.StartsWith("nc_biz_rigid") => "刚性支出",
            var name when name.StartsWith("nc_config") => "配置",
            var name when name.StartsWith("nc_perm") => "权限",
            var name when name.StartsWith("nc_view") => "视图",
            _ => "系统"
        };
    }

    private string GenerateColumnDefinition(ColumnSchema column)
    {
        var parts = new List<string> { column.Name, column.Type };

        if (!column.Nullable)
        {
            parts.Add("NOT NULL");
        }

        if (!string.IsNullOrEmpty(column.Default))
        {
            parts.Add("DEFAULT " + column.Default);
        }

        if (column.Unique && !column.PrimaryKey)
        {
            parts.Add("UNIQUE");
        }

        return string.Join(" ", parts);
    }

    private string GenerateConstraintDefinition(ConstraintSchema constraint)
    {
        return constraint.Type.ToUpperInvariant() switch
        {
            "UNIQUE" when constraint.Columns.Count > 0 =>
                "CONSTRAINT " + constraint.Name + " UNIQUE (" + string.Join(", ", constraint.Columns) + ")",
            "FOREIGN KEY" when constraint.References != null =>
                "CONSTRAINT " + constraint.Name + " FOREIGN KEY (" + string.Join(", ", constraint.Columns) + ") REFERENCES " + constraint.References.Table + "(" + constraint.References.Column + ")" +
                (constraint.References.OnDelete != null ? " ON DELETE " + constraint.References.OnDelete : ""),
            "CHECK" when !string.IsNullOrEmpty(constraint.Expression) =>
                "CONSTRAINT " + constraint.Name + " CHECK (" + constraint.Expression + ")",
            _ => string.Empty
        };
    }

    private string GenerateIndexSql(string tableName, IndexSchema index)
    {
        var unique = index.Unique ? "UNIQUE " : string.Empty;
        var whereClause = string.IsNullOrWhiteSpace(index.Where) ? string.Empty : " WHERE " + index.Where;
        return "CREATE " + unique + "INDEX IF NOT EXISTS " + index.Name + " ON " + tableName + " (" + string.Join(", ", index.Columns) + ")" + whereClause + ";";
    }

    public async Task<Result> ExecuteCreateTableAsync(string sql, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sql))
        {
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "SQL语句不能为空");
        }

        try
        {
            var statements = sql.Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToArray();

            // [N+1 豁免] DDL：按分号拆分出的独立语句序列，语句内容互异不可合并；建表属一次性低频操作
            foreach (var statement in statements)
            {
                var result = await _dbService.ExecuteNonQueryAsync(statement + ";", ct);
                if (result.IsFailure)
                {
                    Logger.Error("执行SQL失败: " + statement);
                    return result;
                }
            }

            return Result.Success();
        }
        catch (Exception ex)
        {
            Logger.Error("执行SQL失败: " + ex.Message);
            return Result.FromException(ex);
        }
    }

    public async Task<Result<bool>> TableExistsAsync(string tableName, CancellationToken ct = default)
    {
        var sql = "SELECT COUNT(*) FROM information_schema.tables WHERE table_schema = 'public' AND table_name = $1";
        var result = await _dbService.ExecuteScalarAsync(sql, ct, tableName);

        if (result.IsSuccess)
        {
            var count = Convert.ToInt64(result.Value);
            return Result<bool>.Success(count > 0);
        }

        return Result<bool>.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, result.Message);
    }

    public async Task<Result> EnsureTablesExistAsync(string databaseName, CancellationToken ct = default)
    {
        Logger.Info("检查表是否存在: " + databaseName);

        var schema = LoadSchema(databaseName);
        if (schema == null)
        {
            return Result.Failure(ErrorCodes.FILE_NOT_FOUND, "Schema文件未找到: " + databaseName);
        }

        foreach (var table in schema.Tables)
        {
            var existsResult = await TableExistsAsync(table.Name, ct);
            if (existsResult.IsSuccess && existsResult.Value)
            {
                Logger.Info("表已存在: " + table.Name);
                continue;
            }

            var sql = GenerateCreateTableSql(table);
            var result = await ExecuteCreateTableAsync(sql, ct);
            if (result.IsFailure)
            {
                return result;
            }

            Logger.Info("创建表成功: " + table.Name);
        }

        foreach (var view in schema.Views)
        {
            var result = await ExecuteCreateTableAsync(view.Definition, ct);
            if (result.IsFailure)
            {
                Logger.Warn("视图创建失败: " + (view.Name ?? ""));
            }
            else
            {
                Logger.Info("视图创建成功: " + (view.Name ?? ""));
            }
        }

        return Result.Success();
    }

    public async Task<Result> ExecuteMultipleSqlAsync(string[] sqlStatements, CancellationToken ct = default)
    {
        // [N+1 豁免] DDL/管理域：调用方传入的独立语句序列，内容互异不可合并；低频一次性执行
        foreach (var sql in sqlStatements)
        {
            if (string.IsNullOrWhiteSpace(sql)) continue;

            var result = await _dbService.ExecuteNonQueryAsync(sql, ct);
            if (result.IsFailure)
            {
                Logger.Error("执行多条SQL失败");
                return result;
            }
        }

        return Result.Success();
    }

    public async Task<Result<SchemaValidationResult>> ValidateAllSchemasAsync(CancellationToken ct = default)
    {
        Logger.Info("开始验证所有Schema");

        var result = new SchemaValidationResult
        {
            CheckedAt = DateTime.Now
        };

        try
        {
            var schemas = LoadAllSchemas();
            result.TotalSchemas = schemas.Count;

            var yamlTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var schema in schemas)
            {
                foreach (var table in schema.Tables)
                {
                    yamlTables.Add(table.Name);
                }
            }
            result.TotalTablesInYaml = yamlTables.Count;

            var dbTablesResult = await GetDatabaseTablesAsync(ct);
            if (dbTablesResult.IsFailure)
            {
                return Result.Failure<SchemaValidationResult>(dbTablesResult.ErrorCode, dbTablesResult.Message);
            }
            var dbTables = dbTablesResult.Value!;
            result.TotalTablesInDatabase = dbTables.Count;

            foreach (var schema in schemas)
            {
                foreach (var table in schema.Tables)
                {
                    if (!dbTables.Contains(table.Name))
                    {
                        result.Differences.Add(new SchemaDifference
                        {
                            Database = schema.Database,
                            TableName = table.Name,
                            Type = DifferenceType.MissingTable,
                            Severity = SeverityLevel.Error,
                            Expected = "不存在",
                            Actual = "不存在",
                            Description = "YAML 定义了表 " + table.Name + " 但在数据库中不存在",
                            Suggestion = "执行 EnsureTablesExistAsync('" + schema.Database + "') 或手动创建"
                        });
                        result.ErrorCount++;
                        continue;
                    }

                    await ValidateTableAsync(result, schema, table, ct);
                }

                // 验证 alterTables（扩展字段）
                foreach (var alterTable in schema.AlterTables)
                {
                    if (!dbTables.Contains(alterTable.Name))
                    {
                        result.Differences.Add(new SchemaDifference
                        {
                            Database = schema.Database,
                            TableName = alterTable.Name,
                            Type = DifferenceType.MissingTable,
                            Severity = SeverityLevel.Error,
                            Expected = "不存在",
                            Actual = "不存在",
                            Description = "YAML alterTables 目标表 " + alterTable.Name + " 在数据库中不存在",
                            Suggestion = "执行 EnsureTablesExistAsync('" + schema.Database + "') 或手动创建"
                        });
                        result.ErrorCount++;
                        continue;
                    }

                    var dbColumns = await GetTableColumnsAsync(alterTable.Name, ct);
                    foreach (var column in alterTable.AddColumns)
                    {
                        if (!dbColumns.TryGetValue(column.Name, out var dbColumn))
                        {
                            result.Differences.Add(new SchemaDifference
                            {
                                Database = schema.Database,
                                TableName = alterTable.Name,
                                Type = DifferenceType.MissingColumn,
                                Severity = SeverityLevel.Error,
                                ColumnName = column.Name,
                                Expected = column.Type + (column.Nullable ? " nullable" : " not null"),
                                Actual = "该列不存在",
                                Description = "表 " + alterTable.Name + " 缺少扩展列 " + column.Name,
                                Suggestion = "ALTER TABLE " + alterTable.Name + " ADD COLUMN " + column.Name + " " + column.Type + (column.Nullable ? "" : " NOT NULL") + ";"
                            });
                            result.ErrorCount++;
                            continue;
                        }

                        if (!TypesMatch(column.Type, dbColumn.Type))
                        {
                            result.Differences.Add(new SchemaDifference
                            {
                                Database = schema.Database,
                                TableName = alterTable.Name,
                                Type = DifferenceType.TypeMismatch,
                                Severity = SeverityLevel.Warning,
                                ColumnName = column.Name,
                                Expected = column.Type,
                                Actual = dbColumn.Type,
                                Description = "扩展列 " + column.Name + " 类型不匹配",
                                Suggestion = "ALTER TABLE " + alterTable.Name + " ALTER COLUMN " + column.Name + " TYPE " + column.Type + ";"
                            });
                            result.WarningCount++;
                        }
                    }
                }
            }

            var extraTables = dbTables.Except(yamlTables, StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var extraTable in extraTables)
            {
                result.Differences.Add(new SchemaDifference
                {
                    Database = "数据库",
                    TableName = extraTable,
                    Type = DifferenceType.ExtraTable,
                    Severity = SeverityLevel.Info,
                    Expected = "YAML 未定义",
                    Actual = "存在",
                    Description = "数据库中存在表 " + extraTable + " 但 YAML 未定义",
                    Suggestion = "该为历史遗留可忽略，或将其添加到 YAML 定义"
                });
                result.InfoCount++;
            }

            Logger.Info("Schema 验证完成: " + result.ErrorCount + " 个错误");
            return Result<SchemaValidationResult>.Success(result);
        }
        catch (Exception ex)
        {
            Logger.Error("Schema验证失败: " + ex.Message);
            return Result<SchemaValidationResult>.Failure(ErrorCodes.UNKNOWN_ERROR, ex.Message);
        }
    }

    private async Task<Result<HashSet<string>>> GetDatabaseTablesAsync(CancellationToken ct)
    {
        Logger.Info("获取数据库表列表");

        var sql = @"
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_type = 'BASE TABLE'
              AND table_name NOT LIKE 'pg_%'
            ORDER BY table_name;";

        try
        {
            var result = await _dbService.QueryAsync<string>(sql, ct);

            if (!result.IsSuccess)
            {
                Logger.Error("获取数据库表列表失败: " + result.Message);
                // 失败必须显式上抛——返回空集合会让上层误判"库里一个表都没有"并尝试全量建表
                return Result.Failure<HashSet<string>>(result.ErrorCode, "获取数据库表列表失败: " + result.Message);
            }

            var tables = result.Value!;
            Logger.Info("数据库表列表获取成功");

            if (tables.Count > 0)
            {
                var sampleTables = tables.Take(10).ToList();
                Logger.Debug("示例表名: " + string.Join(", ", sampleTables));
            }

            return Result.Success(new HashSet<string>(tables, StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex)
        {
            Logger.Error("获取数据库表列表失败: " + ex.Message);
            return Result.FromException<HashSet<string>>(ex);
        }
    }

    public DatabaseSchema? LoadSchema(string databaseName)
    {
        if (_schemaCache.TryGetValue(databaseName, out var cached))
            return cached;

        var filePath = GetSchemaFilePath(databaseName);
        if (!File.Exists(filePath))
        {
            Logger.Warn("Schema文件不存在: " + filePath);
            return null;
        }

        try
        {
            var yamlContent = File.ReadAllText(filePath);
            var schema = _deserializer.Deserialize<DatabaseSchema>(yamlContent);
            if (schema != null)
            {
                _schemaCache[databaseName] = schema;
            }
            return schema;
        }
        catch (Exception ex)
        {
            // [吞异常豁免] 解析失败与文件缺失统一返回 null；唯一调用方 EnsureTablesExistAsync 将 null
            // 转为显式 Result.Failure(FILE_NOT_FOUND)（见 :773-776），失败链路完整非静默
            Logger.Error("加载Schema文件失败: " + ex.Message);
            return null;
        }
    }

}
