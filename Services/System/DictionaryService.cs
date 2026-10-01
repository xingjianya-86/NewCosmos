using System.Reflection;
using System.Text;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Models.Schema;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NewCosmos.Services.System;

/// <summary>
/// 字典服务实现
/// 支持双表架构：种子数据表（nc_dict_*）和更新数据表（nc_dict_*_updates）
/// </summary>
public class DictionaryService : BaseService, IDictionaryService
{
    protected override string ServiceName => "DictionaryService";

    private const int IN_PROGRESS_DELAY_MS = 200;
    private const int COMPLETED_DELAY_MS = 100;

    private readonly IDatabaseService _dbService;
    private readonly ISchemaSyncService _schemaSync;
    private readonly ISeedMergeService _seedMerge;
    private readonly ILoggerService _logger;
    private bool _dataInitialized;
    private bool _tablesReady;

    public DictionaryService(
        IDatabaseService dbService,
        ISchemaSyncService schemaSync,
        ISeedMergeService seedMerge,
        ILoggerService logger) : base(logger)
    {
        _dbService = dbService;
        _schemaSync = schemaSync;
        _seedMerge = seedMerge;
        _logger = logger;
    }

    private async Task ReportProgressWithDelayAsync(
        IProgress<ProgressContext> progress,
        int currentStep,
        int totalSteps,
        string stepName)
    {
        progress?.Report(new ProgressContext
        {
            CurrentStep = currentStep,
            TotalSteps = totalSteps,
            CurrentStepName = stepName
        });

        await Task.Delay(IN_PROGRESS_DELAY_MS);
    }

    private async Task DelayAfterCompletionAsync()
    {
        await Task.Delay(COMPLETED_DELAY_MS);
    }

    #region Schema 管理

    public async Task<Result> EnsureTablesExistAsync(CancellationToken ct = default)
    {
        // 首次确认后短路：字典表是 schema 稳定存在，每次查询前检查是纯浪费（DD 查询路径高频调用）
        if (_tablesReady) return Result.Success();

        LogInfo("执行字典表检查");

        var checkSql = @"
            SELECT COUNT(*) FROM information_schema.tables 
            WHERE table_schema = 'public' 
            AND table_name IN ('nc_dict_categories', 'nc_dict_items', 
                               'nc_dict_categories_updates', 'nc_dict_items_updates')";
        
        var result = await _dbService.ExecuteScalarAsync(checkSql, ct);
        if (result.IsSuccess && Convert.ToInt64(result.Value) >= 4)
        {
            _tablesReady = true;
            LogInfo("字典表已存在");
            return Result.Success();
        }

        LogInfo("字典表不完整，开始创建");
        var syncResult = await SyncSchemaAsync(null, ct);
        if (syncResult.IsSuccess)
            _tablesReady = true;
        return syncResult;
    }

    private async Task<Result> EnsureDataInitializedAsync(CancellationToken ct = default)
    {
        if (_dataInitialized) return Result.Success();

        var checkSql = "SELECT COUNT(*) FROM nc_dict_items";
        var countResult = await _dbService.ExecuteScalarAsync(checkSql, ct);
        if (countResult.IsSuccess && Convert.ToInt64(countResult.Value) > 0)
        {
            _dataInitialized = true;
            return Result.Success();
        }

        LogInfo("执行字典数据初始化");
        return await InitializeFromSeedAsync(null, ct);
    }

    public async Task<Result> SyncSchemaAsync(IProgress<ProgressContext> progress = null, CancellationToken ct = default)
    {
        LogInfo("执行字典表结构同步");

        // 表结构以 Resources/Schema/sys_dictionary/database.yaml 为唯一权威来源
        var tables = new[]
        {
            "nc_dict_categories",
            "nc_dict_categories_updates",
            "nc_dict_items",
            "nc_dict_items_updates",
        };

        var stepIndex = 0;
        foreach (var name in tables)
        {
            stepIndex++;
            await ReportProgressWithDelayAsync(progress, stepIndex, 6, $"同步表: {name}");
            // 字典表同步流程有意整表重建（种子数据随后回填），显式允许删除
            var result = await _schemaSync.SyncTableSchemaAsync(name, allowDrop: true, null, ct);
            if (result.IsFailure)
            {
                LogError($"同步表结构失败: {name}");
                return result;
            }
            await DelayAfterCompletionAsync();
        }

        stepIndex++;
        await ReportProgressWithDelayAsync(progress, stepIndex, 6, "重建分类视图");
        var dropCatViewResult = await _dbService.ExecuteNonQueryAsync("DROP VIEW IF EXISTS nc_view_dict_categories CASCADE", ct);
        if (dropCatViewResult.IsFailure)
        {
            LogError($"删除分类视图失败: {dropCatViewResult.Message}");
            return dropCatViewResult;
        }
        var createCatViewResult = await _dbService.ExecuteNonQueryAsync(@"
            CREATE VIEW nc_view_dict_categories AS
            SELECT s.id, COALESCE(u.category, s.category) as category,
                   COALESCE(u.display_name, s.display_name) as display_name,
                   COALESCE(u.description, s.description) as description,
                   COALESCE(u.sort_order, s.sort_order) as sort_order,
                   CASE WHEN u.id IS NOT NULL THEN u.is_active ELSE s.is_active END as is_active,
                   CASE WHEN u.id IS NOT NULL THEN 'updated' ELSE 'seed' END as status,
                   u.source, u.action, u.created_at as updated_at, u.created_by as updated_by
            FROM nc_dict_categories s
            LEFT JOIN nc_dict_categories_updates u ON s.id = u.id
            WHERE COALESCE(u.action, 'none') != 'delete'
            UNION ALL
            SELECT u.id, u.category, u.display_name, u.description, u.sort_order,
                   u.is_active, 'updated' as status, u.source, u.action, u.created_at, u.created_by
            FROM nc_dict_categories_updates u
            WHERE u.action = 'add' AND NOT EXISTS (SELECT 1 FROM nc_dict_categories s WHERE s.id = u.id)
            ORDER BY category, sort_order, id", ct);
        if (createCatViewResult.IsFailure)
        {
            LogError($"创建分类视图失败: {createCatViewResult.Message}");
            return createCatViewResult;
        }
        await DelayAfterCompletionAsync();

        stepIndex++;
        await ReportProgressWithDelayAsync(progress, stepIndex, 6, "重建字典项视图");
        var dropItemViewResult = await _dbService.ExecuteNonQueryAsync("DROP VIEW IF EXISTS nc_view_dict_items CASCADE", ct);
        if (dropItemViewResult.IsFailure)
        {
            LogError($"删除字典项视图失败: {dropItemViewResult.Message}");
            return dropItemViewResult;
        }
        var createItemViewResult = await _dbService.ExecuteNonQueryAsync(@"
            CREATE VIEW nc_view_dict_items AS
            SELECT s.id, COALESCE(u.category, s.category) as category,
                   COALESCE(u.item_key, s.item_key) as item_key,
                   COALESCE(u.item_value, s.item_value) as item_value,
                   COALESCE(u.sort_order, s.sort_order) as sort_order,
                   CASE WHEN u.id IS NOT NULL THEN u.is_active ELSE s.is_active END as is_active,
                   COALESCE(u.description, s.description) as description,
                   CASE WHEN u.id IS NOT NULL THEN 'updated' ELSE 'seed' END as status,
                   u.source, u.action, u.created_at as updated_at, u.created_by as updated_by
            FROM nc_dict_items s
            LEFT JOIN nc_dict_items_updates u ON s.id = u.id
            WHERE COALESCE(u.action, 'none') != 'delete'
            UNION ALL
            SELECT u.id, u.category, u.item_key, u.item_value, u.sort_order,
                   u.is_active, u.description, 'updated' as status, u.source, u.action, u.created_at, u.created_by
            FROM nc_dict_items_updates u
            WHERE u.action = 'add' AND NOT EXISTS (SELECT 1 FROM nc_dict_items s WHERE s.id = u.id)
            ORDER BY category, sort_order, id", ct);
        if (createItemViewResult.IsFailure)
        {
            LogError($"创建字典项视图失败: {createItemViewResult.Message}");
            return createItemViewResult;
        }
        await DelayAfterCompletionAsync();

        Logger.LogBusiness("字典视图重建完成");
        return Result.Success();
    }

    public async Task<Result> ClearSeedTablesAsync(CancellationToken ct = default)
    {
        LogInfo("执行清空种子表操作");

        await using var tx = await _dbService.BeginTransactionScopeAsync(ct);
        try
        {
            await _dbService.ExecuteNonQueryAsync("TRUNCATE TABLE nc_dict_categories", ct);
            await _dbService.ExecuteNonQueryAsync("TRUNCATE TABLE nc_dict_items", ct);
            await tx.CommitAsync(ct);

            Logger.LogBusiness("种子表清空完成");
            return Result.Success();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogError($"清空种子表失败: {ex.Message}");
            return Result.FromException(ex);
        }
    }

    #endregion

    #region 查询方法（使用视图）

    public async Task<Result<List<DictCategoryView>>> GetCategoriesAsync(CancellationToken ct = default)
    {
        await EnsureTablesExistAsync(ct);
        await EnsureDataInitializedAsync(ct);

        LogInfo("获取所有字典分类");

        var sql = "SELECT * FROM nc_view_dict_categories WHERE is_active = TRUE ORDER BY sort_order, id";
        var result = await _dbService.QueryAsync<DictCategoryView>(sql, ct);

        return result.IsSuccess && result.Value is not null
            ? Result.Success(result.Value)
            : Result.Success(new List<DictCategoryView>());
    }

    public async Task<Result<List<DictItemView>>> GetItemsByCategoryAsync(string category, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return Result.Failure<List<DictItemView>>(ErrorCodes.VALIDATION_FAILED, "分类参数不能为空");
        }

        await EnsureTablesExistAsync(ct);
        await EnsureDataInitializedAsync(ct);

        LogInfo($"获取分类 {category} 的字典项");

        var sql = "SELECT * FROM nc_view_dict_items WHERE category = $1 AND is_active = TRUE ORDER BY sort_order, id";
        var result = await _dbService.QueryAsync<DictItemView>(sql, ct, category);

        return result.IsSuccess && result.Value is not null
            ? Result.Success(result.Value)
            : Result.Success(new List<DictItemView>());
    }

    public async Task<Result<List<DictItemView>>> GetAllItemsAsync(CancellationToken ct = default)
    {
        await EnsureTablesExistAsync(ct);
        await EnsureDataInitializedAsync(ct);

        LogInfo("获取全部字典项（一次查询，含分类列）");

        var sql = "SELECT * FROM nc_view_dict_items WHERE is_active = TRUE ORDER BY category, sort_order, id";
        var result = await _dbService.QueryAsync<DictItemView>(sql, ct);

        return result.IsSuccess && result.Value is not null
            ? Result.Success(result.Value)
            : Result.Success(new List<DictItemView>());
    }

    public async Task<Result<DictItemView>> GetItemByKeyAsync(string category, string itemKey, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(category) || string.IsNullOrWhiteSpace(itemKey))
        {
            return Result.Failure<DictItemView>(ErrorCodes.VALIDATION_FAILED, "分类或字典项Key不能为空");
        }

        await EnsureTablesExistAsync(ct);
        await EnsureDataInitializedAsync(ct);

        var sql = "SELECT * FROM nc_view_dict_items WHERE category = $1 AND item_key = $2 LIMIT 1";
        var result = await _dbService.QuerySingleAsync<DictItemView>(sql, ct, category, itemKey);

        return result.IsSuccess ? Result.Success(result.Value) : Result.Success<DictItemView>(null);
    }

    #endregion

    #region 数据初始化
    public async Task<Result> InitializeFromSeedAsync(IProgress<ProgressContext> progress = null, CancellationToken ct = default)
    {
        LogInfo("开始从 Seed 初始化字典数据");

        const int totalSteps = 6;

        await ReportProgressWithDelayAsync(progress, 1, totalSteps, "加载分类数据");
        var categories = LoadCategoriesFromYaml();
        if (categories == null || categories.Count == 0)
        {
            LogError("加载字典分类YAML失败");
            return Result.Failure(ErrorCodes.FILE_NOT_FOUND, "无法加载字典分类数据");
        }
        await DelayAfterCompletionAsync();

        await ReportProgressWithDelayAsync(progress, 2, totalSteps, "加载字典项数据");
        var items = LoadItemsFromYaml();
        if (items == null || items.Count == 0)
        {
            LogError("加载字典项数据失败");
            return Result.Failure(ErrorCodes.FILE_NOT_FOUND, "无法加载字典项数据");
        }
        await DelayAfterCompletionAsync();

        await ReportProgressWithDelayAsync(progress, 3, totalSteps, "验证数据完整性");
        var invalidCategories = categories.Where(c => string.IsNullOrWhiteSpace(c.Category)).ToList();
        var invalidItems = items.Where(i => string.IsNullOrWhiteSpace(i.Category) || string.IsNullOrWhiteSpace(i.ItemKey)).ToList();

        if (invalidCategories.Count > 0)
        {
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, $"存在 {invalidCategories.Count} 个无效分类");
        }
        if (invalidItems.Count > 0)
        {
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, $"存在 {invalidItems.Count} 个无效字典项");
        }
        await DelayAfterCompletionAsync();

        await ReportProgressWithDelayAsync(progress, 4, totalSteps, "确保表结构存在");
        var ensureResult = await SyncSchemaAsync(null, ct);
        if (ensureResult.IsFailure)
            return ensureResult;
        await DelayAfterCompletionAsync();

        await ReportProgressWithDelayAsync(progress, 5, totalSteps, "合并分类数据");
        var catRows = categories.Select(c => new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = c.Id,
            ["category"] = c.Category,
            ["display_name"] = c.DisplayName,
            ["description"] = c.Description ?? string.Empty,
            ["sort_order"] = c.SortOrder,
            ["is_active"] = c.IsActive,
        }).ToList();

        var catDef = new SeedMergeDefinition
        {
            TableName = "nc_dict_categories",
            BusinessKeyColumn = "id",
            Columns = new List<string> { "id", "category", "display_name", "description", "sort_order", "is_active" }
        };

        var catResult = await _seedMerge.MergeSeedDataAsync(catDef, catRows, ct);
        if (catResult.IsFailure || catResult.Value is null)
        {
            LogError("合并分类数据失败");
            return Result.Failure(ErrorCodes.DB_QUERY_ERROR, "合并分类数据失败");
        }
        await DelayAfterCompletionAsync();

        await ReportProgressWithDelayAsync(progress, 6, totalSteps, "合并字典项数据");
        var itemRows = items.Select(i => new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        {
            ["id"] = i.Id,
            ["category"] = i.Category,
            ["item_key"] = i.ItemKey,
            ["item_value"] = i.ItemValue ?? string.Empty,
            ["sort_order"] = i.SortOrder,
            ["is_active"] = i.IsActive,
            ["description"] = i.Description ?? string.Empty,
        }).ToList();

        var itemDef = new SeedMergeDefinition
        {
            TableName = "nc_dict_items",
            BusinessKeyColumn = "id",
            Columns = new List<string> { "id", "category", "item_key", "item_value", "sort_order", "is_active", "description" }
        };

        var itemResult = await _seedMerge.MergeSeedDataAsync(itemDef, itemRows, ct);
        if (itemResult.IsFailure || itemResult.Value is null)
        {
            LogError("合并字典项数据失败");
            return Result.Failure(ErrorCodes.DB_QUERY_ERROR, "合并字典项数据失败");
        }
        await DelayAfterCompletionAsync();

        Logger.LogBusiness("字典数据初始化完成",
            ("Categories", catResult.Value.Inserted + catResult.Value.Updated),
            ("Items", itemResult.Value.Inserted + itemResult.Value.Updated));

        return Result.Success();
    }

    #endregion

    #region 写入操作（更新表）
    public async Task<Result> CreateCategoryAsync(DictCategoryUpdate category, CancellationToken ct = default)
    {
        if (category == null || string.IsNullOrWhiteSpace(category.Category))
        {
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "字典分类信息无效");
        }

        await EnsureTablesExistAsync(ct);

        LogInfo($"创建字典分类: {category.Category}");

        var sql = @"
            INSERT INTO nc_dict_categories_updates (id, category, display_name, description, sort_order, is_active, source, action, created_at, created_by)
            VALUES ($1, $2, $3, $4, $5, $6, $7, 'add', NOW(), $8)
            ON CONFLICT (id) DO UPDATE SET
                category = EXCLUDED.category,
                display_name = EXCLUDED.display_name,
                description = EXCLUDED.description,
                sort_order = EXCLUDED.sort_order,
                is_active = EXCLUDED.is_active,
                action = 'add',
                updated_at = NOW()";

        var result = await _dbService.ExecuteNonQueryAsync(sql, ct,
            category.Id,
            category.Category,
            category.DisplayName,
            category.Description ?? string.Empty,
            category.SortOrder,
            category.IsActive,
            category.Source,
            category.CreatedBy);

        if (result.IsSuccess)
        {
            Logger.LogBusiness("创建字典分类", ("Category", category.Category));
        }

        return result.IsSuccess ? Result.Success() : Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, result.Message);
    }

    public async Task<Result> CreateItemAsync(DictItemUpdate item, CancellationToken ct = default)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.Category) || string.IsNullOrWhiteSpace(item.ItemKey))
        {
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "字典项信息无效");
        }

        await EnsureTablesExistAsync(ct);

        LogInfo($"创建字典项: {item.ItemKey}");

        var sql = @"
            INSERT INTO nc_dict_items_updates (id, category, item_key, item_value, sort_order, is_active, description, source, action, created_at, created_by)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, 'add', NOW(), $9)
            ON CONFLICT (id) DO UPDATE SET
                category = EXCLUDED.category,
                item_key = EXCLUDED.item_key,
                item_value = EXCLUDED.item_value,
                sort_order = EXCLUDED.sort_order,
                is_active = EXCLUDED.is_active,
                description = EXCLUDED.description,
                action = 'add',
                updated_at = NOW()";

        var result = await _dbService.ExecuteNonQueryAsync(sql, ct,
            item.Id,
            item.Category,
            item.ItemKey,
            item.ItemValue ?? string.Empty,
            item.SortOrder,
            item.IsActive,
            item.Description ?? string.Empty,
            item.Source,
            item.CreatedBy);

        if (result.IsSuccess)
        {
            Logger.LogBusiness("创建字典项", ("Category", item.Category), ("Key", item.ItemKey));
        }

        return result.IsSuccess ? Result.Success() : Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, result.Message);
    }

    public async Task<Result> UpdateCategoryAsync(DictCategoryUpdate category, CancellationToken ct = default)
    {
        if (category == null || category.Id <= 0)
        {
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "字典分类信息无效");
        }

        await EnsureTablesExistAsync(ct);

        LogInfo($"更新字典分类: {category.Id}");

        var sql = @"
            INSERT INTO nc_dict_categories_updates (id, category, display_name, description, sort_order, is_active, source, action, created_at, created_by, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, 'update', NOW(), $8, NOW())
            ON CONFLICT (id) DO UPDATE SET
                category = EXCLUDED.category,
                display_name = EXCLUDED.display_name,
                description = EXCLUDED.description,
                sort_order = EXCLUDED.sort_order,
                is_active = EXCLUDED.is_active,
                action = 'update',
                updated_at = NOW()";

        var result = await _dbService.ExecuteNonQueryAsync(sql, ct,
            category.Id,
            category.Category,
            category.DisplayName,
            category.Description ?? string.Empty,
            category.SortOrder,
            category.IsActive,
            category.Source,
            category.CreatedBy);

        if (result.IsSuccess)
        {
            Logger.LogBusiness("更新字典分类", ("Id", category.Id));
        }

        return result.IsSuccess ? Result.Success() : Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, result.Message);
    }

    public async Task<Result> UpdateItemAsync(DictItemUpdate item, CancellationToken ct = default)
    {
        if (item == null || item.Id <= 0)
        {
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "字典项信息无效");
        }

        await EnsureTablesExistAsync(ct);

        LogInfo($"更新字典项: {item.Id}");

        var sql = @"
            INSERT INTO nc_dict_items_updates (id, category, item_key, item_value, sort_order, is_active, description, source, action, created_at, created_by, updated_at)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, 'update', NOW(), $9, NOW())
            ON CONFLICT (id) DO UPDATE SET
                category = EXCLUDED.category,
                item_key = EXCLUDED.item_key,
                item_value = EXCLUDED.item_value,
                sort_order = EXCLUDED.sort_order,
                is_active = EXCLUDED.is_active,
                description = EXCLUDED.description,
                action = 'update',
                updated_at = NOW()";

        var result = await _dbService.ExecuteNonQueryAsync(sql, ct,
            item.Id,
            item.Category,
            item.ItemKey,
            item.ItemValue ?? string.Empty,
            item.SortOrder,
            item.IsActive,
            item.Description ?? string.Empty,
            item.Source,
            item.CreatedBy);

        if (result.IsSuccess)
        {
            Logger.LogBusiness("更新字典项", ("Id", item.Id));
        }

        return result.IsSuccess ? Result.Success() : Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, result.Message);
    }

    public async Task<Result> DeleteCategoryAsync(int categoryId, CancellationToken ct = default)
    {
        if (categoryId <= 0)
        {
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "字典分类ID无效");
        }

        await EnsureTablesExistAsync(ct);

        LogInfo($"删除字典分类: {categoryId}");

        var sql = @"
            INSERT INTO nc_dict_categories_updates (id, source, action, created_at)
            VALUES ($1, 'user', 'delete', NOW())
            ON CONFLICT (id) DO UPDATE SET
                action = 'delete',
                is_active = FALSE,
                updated_at = NOW()";

        var result = await _dbService.ExecuteNonQueryAsync(sql, ct, categoryId);

        if (result.IsSuccess)
        {
            Logger.LogBusiness("删除字典分类", ("Id", categoryId));
        }

        return result.IsSuccess ? Result.Success() : Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, result.Message);
    }

    public async Task<Result> DeleteItemAsync(int itemId, CancellationToken ct = default)
    {
        if (itemId <= 0)
        {
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "字典项ID无效");
        }

        await EnsureTablesExistAsync(ct);

        LogInfo($"删除字典项: {itemId}");

        var sql = @"
            INSERT INTO nc_dict_items_updates (id, source, action, created_at)
            VALUES ($1, 'user', 'delete', NOW())
            ON CONFLICT (id) DO UPDATE SET
                action = 'delete',
                is_active = FALSE,
                updated_at = NOW()";

        var result = await _dbService.ExecuteNonQueryAsync(sql, ct, itemId);

        if (result.IsSuccess)
        {
            Logger.LogBusiness("删除字典项", ("Id", itemId));
        }

        return result.IsSuccess ? Result.Success() : Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, result.Message);
    }

    #endregion

    #region YAML 加载

    private List<DictCategoryView>? LoadCategoriesFromYaml()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "NewCosmos.Resources.Seed.sys_dictionary.categories._index.yaml";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                _logger.Warn($"资源文件未找到: {resourceName}");
                return null;
            }

            using var reader = new StreamReader(stream);
            var yaml = reader.ReadToEnd();

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            var data = deserializer.Deserialize<DictionaryCategoryYaml>(yaml);
            if (data.Tables == null || data.Tables.Count == 0 || data.Tables[0].Data == null)
            {
                return null;
            }

            return data.Tables[0].Data.Select(c => new DictCategoryView
            {
                Id = c.Id,
                Category = c.Category ?? string.Empty,
                DisplayName = c.DisplayName ?? c.Category ?? string.Empty,
                Description = c.Description,
                SortOrder = c.SortOrder,
                IsActive = c.IsActive
            }).ToList();
        }
        catch (Exception ex)
        {
            LogError($"加载字典分类YAML失败: {ex.Message}");
            return null;
        }
    }

    private List<DictItemView>? LoadItemsFromYaml()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var items = new List<DictItemView>();

            var resourceNames = assembly.GetManifestResourceNames()
                .Where(n => n.StartsWith("NewCosmos.Resources.Seed.sys_dictionary.items.") && n.EndsWith(".yaml"))
                .ToList();

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            foreach (var resourceName in resourceNames)
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) continue;

                using var reader = new StreamReader(stream);
                var yaml = reader.ReadToEnd();

                var data = deserializer.Deserialize<DictionaryItemYaml>(yaml);
                if (data.Tables == null || data.Tables.Count == 0 || data.Tables[0].Data == null)
                {
                    continue;
                }

                var category = data.Tables[0].Data.FirstOrDefault()?.Category ?? "Unknown";

                foreach (var item in data.Tables[0].Data)
                {
                    items.Add(new DictItemView
                    {
                        Id = item.Id,
                        Category = item.Category ?? category,
                        ItemKey = item.ItemKey ?? string.Empty,
                        ItemValue = item.ItemValue,
                        SortOrder = item.SortOrder,
                        IsActive = item.IsActive,
                        Description = item.Description
                    });
                }
            }

            return items;
        }
        catch (Exception ex)
        {
            LogError($"加载字典项YAML失败: {ex.Message}");
            return null;
        }
    }

    private class DictionaryCategoryYaml
    {
        public string Database { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public List<CategoryTableYaml> Tables { get; set; } = new();
    }

    private class CategoryTableYaml
    {
        public string Name { get; set; } = string.Empty;
        public List<CategoryDataYaml> Data { get; set; } = new();
    }

    private class CategoryDataYaml
    {
        public int Id { get; set; }
        public string Category { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }

    private class DictionaryItemYaml
    {
        public string Database { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public List<ItemTableYaml> Tables { get; set; } = new();
    }

    private class ItemTableYaml
    {
        public string Name { get; set; } = string.Empty;
        public List<ItemDataYaml> Data { get; set; } = new();
    }

    private class ItemDataYaml
    {
        public int Id { get; set; }
        public string Category { get; set; } = string.Empty;
        public string ItemKey { get; set; } = string.Empty;
        public string ItemValue { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    #endregion
}
