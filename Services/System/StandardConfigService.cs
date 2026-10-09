using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using System.Collections.Concurrent;
using System.Reflection;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NewCosmos.Services.System;

public class StandardConfigService : BaseService, IStandardConfigService
{
    protected override string ServiceName => "StandardConfigService";
    private readonly IDatabaseService _dbService;
    private readonly ISchemaSyncService _schemaSync;
    private readonly ISeedMergeService _seedMerge;

    private const int IN_PROGRESS_DELAY_MS = 200;
    private const int COMPLETED_DELAY_MS = 100;

    // 标准配置 TTL 缓存（参考 NewPermissionService 模式）：
    // 分类计算一次会调用 GetStandardValueAsync 5+ 次，未缓存时每次都是一趟数据库往返。
    // 键为 standardType|hukouType|supportMode 组合；任何保存/更新/删除/初始化操作整体失效。
    private sealed record CachedStandard(ConfigStandard? Value, DateTime CachedAtUtc);
    private static readonly TimeSpan StandardCacheTtl = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, CachedStandard> _standardCache = new();

    private void InvalidateStandardCache()
    {
        _standardCache.Clear();
        LogDebug("标准配置缓存已清除");
    }

    public StandardConfigService(
        IDatabaseService dbService,
        ISchemaSyncService schemaSync,
        ISeedMergeService seedMerge,
        ILoggerService logger) : base(logger)
    {
        _dbService = dbService;
        _schemaSync = schemaSync;
        _seedMerge = seedMerge;
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

    #region 旧表方法（保持兼容）

    public async Task<Result<List<IncomeStandard>>> GetActiveIncomeStandardsAsync(CancellationToken ct = default)
    {
        LogInfo("获取当前有效的收入标准");

        var sql = @"
            SELECT * FROM nc_config_income_standards 
            WHERE effective_start_date <= CURRENT_DATE 
            AND effective_end_date >= CURRENT_DATE 
            ORDER BY hukou_type";

        var result = await _dbService.QueryAsync<IncomeStandard>(sql, ct);

        if (result.IsSuccess && result.Value is not null)
        {
            LogInfo($"获取到{result.Value.Count} 条收入标准");
            return Result.Success(result.Value.ToList());
        }

        return Result.Failure<List<IncomeStandard>>(ErrorCodes.DB_QUERY_ERROR, result.Message ?? "查询失败");
    }

    public async Task<Result<List<ClassifiedSubsidyStandard>>> GetActiveSubsidyStandardsAsync(CancellationToken ct = default)
    {
        LogInfo("获取当前有效的补贴标准");

        var sql = @"
            SELECT * FROM nc_config_subsidy_standards 
            WHERE effective_start_date <= CURRENT_DATE 
            AND effective_end_date >= CURRENT_DATE 
            ORDER BY hukou_type";

        var result = await _dbService.QueryAsync<ClassifiedSubsidyStandard>(sql, ct);

        if (result.IsSuccess && result.Value is not null)
        {
            LogInfo($"获取到{result.Value.Count} 条补贴标准");
            return Result.Success(result.Value.ToList());
        }

        return Result.Failure<List<ClassifiedSubsidyStandard>>(ErrorCodes.DB_QUERY_ERROR, result.Message ?? "查询失败");
    }

    public async Task<Result<List<DestituteSupportStandard>>> GetActiveDestituteStandardsAsync(CancellationToken ct = default)
    {
        LogInfo("获取当前有效的特困供养标准");

        var sql = @"
            SELECT * FROM nc_config_destitute_standards 
            WHERE effective_start_date <= CURRENT_DATE 
            AND effective_end_date >= CURRENT_DATE 
            ORDER BY hukou_type, support_mode";

        var result = await _dbService.QueryAsync<DestituteSupportStandard>(sql, ct);

        if (result.IsSuccess && result.Value is not null)
        {
            LogInfo($"获取到{result.Value.Count} 条特困供养标准");
            return Result.Success(result.Value.ToList());
        }

        return Result.Failure<List<DestituteSupportStandard>>(ErrorCodes.DB_QUERY_ERROR, result.Message ?? "查询失败");
    }

    public async Task<Result<List<SystemConfigStandard>>> GetSystemConfigsAsync(string category = null, CancellationToken ct = default)
    {
        LogInfo($"获取系统配置, category={category ?? "全部"}");

        var sql = category == null
            ? "SELECT * FROM nc_config_system_standards ORDER BY category, sort_order"
            : "SELECT * FROM nc_config_system_standards WHERE category = $1 ORDER BY sort_order";

        var result = category == null
            ? await _dbService.QueryAsync<SystemConfigStandard>(sql, ct)
            : await _dbService.QueryAsync<SystemConfigStandard>(sql, ct, category);

        if (result.IsSuccess && result.Value is not null)
        {
            LogInfo($"获取到{result.Value.Count} 条系统配置");
            return Result.Success(result.Value.ToList());
        }

        return Result.Failure<List<SystemConfigStandard>>(ErrorCodes.DB_QUERY_ERROR, result.Message ?? "查询失败");
    }

    public async Task<Result> UpdateSystemConfigAsync(int configId, decimal newValue, CancellationToken ct = default)
    {
        LogInfo($"更新系统配置: Id={configId}");

        var sql = @"
            UPDATE nc_config_system_standards 
            SET config_value = $1, updated_at = CURRENT_TIMESTAMP 
            WHERE id = $2";

        var result = await _dbService.ExecuteNonQueryAsync(sql, ct, newValue, configId);

        if (result.IsSuccess)
        {
            InvalidateStandardCache();
            Logger.LogBusiness("系统配置更新成功", ("ConfigId", configId), ("NewValue", newValue));
            return Result.Success();
        }

        return Result.Failure(ErrorCodes.DB_QUERY_ERROR, "更新失败");
    }

    public async Task<Result<StandardConfigOverview>> GetOverviewAsync(CancellationToken ct = default)
    {
        LogInfo("执行概览查询");

        try
        {
            var overview = new StandardConfigOverview();

            var incomeResult = await GetActiveIncomeStandardsAsync(ct);
            if (incomeResult.IsSuccess && incomeResult.Value is not null)
            {
                overview.IncomeStandardsCount = incomeResult.Value.Count;
                overview.RuralIncomeStandard = incomeResult.Value.FirstOrDefault(s => s.HukouType == "Rural")?.MonthlyStandard ?? 0;
                overview.UrbanIncomeStandard = incomeResult.Value.FirstOrDefault(s => s.HukouType == "Urban")?.MonthlyStandard ?? 0;
            }

            var subsidyResult = await GetActiveSubsidyStandardsAsync(ct);
            if (subsidyResult.IsSuccess && subsidyResult.Value is not null)
            {
                overview.SubsidyStandardsCount = subsidyResult.Value.Count;
                overview.RuralSubsidyAmount = subsidyResult.Value.FirstOrDefault(s => s.HukouType == "Rural")?.PerPersonAmount ?? 0;
                overview.UrbanSubsidyAmount = subsidyResult.Value.FirstOrDefault(s => s.HukouType == "Urban")?.PerPersonAmount ?? 0;
            }

            var destituteResult = await GetActiveDestituteStandardsAsync(ct);
            if (destituteResult.IsSuccess && destituteResult.Value is not null)
            {
                overview.DestituteStandardsCount = destituteResult.Value.Count;
            }

            var configResult = await GetSystemConfigsAsync(null, ct);
            if (configResult.IsSuccess && configResult.Value is not null)
            {
                overview.SystemConfigsCount = configResult.Value.Count;
            }

            try
            {
                var configStandardsResult = await GetConfigStandardsAsync(null, ct);
                if (configStandardsResult.IsSuccess && configStandardsResult.Value is not null)
                {
                    overview.ConfigStandardsCount = configStandardsResult.Value.Count;
                }
            }
            catch (Exception ex) when (ex.Message.Contains("nc_config_standards") || ex.Message.Contains("does not exist"))
            {
                LogWarn("配置标准表不存在，跳过统计");
                overview.ConfigStandardsCount = 0;
            }

            LogInfo("概览查询完成");
            return Result.Success(overview);
        }
        catch (Exception ex)
        {
            LogError($"操作失败: {ex.Message}");
            return Result.FromException<StandardConfigOverview>(ex);
        }
    }

    #endregion

    #region 新表方法（nc_config_standards）
    public async Task<Result<List<ConfigStandard>>> GetConfigStandardsAsync(string standardType = null, CancellationToken ct = default)
    {
        LogInfo($"获取标准配置: type={standardType ?? "全部"}");

        try
        {
            var sql = standardType == null
                ? "SELECT * FROM nc_config_standards WHERE is_active = TRUE ORDER BY standard_type, hukou_type, support_mode"
                : "SELECT * FROM nc_config_standards WHERE standard_type = $1 AND is_active = TRUE ORDER BY hukou_type, support_mode";

            var result = standardType == null
                ? await _dbService.QueryAsync<ConfigStandard>(sql, ct)
                : await _dbService.QueryAsync<ConfigStandard>(sql, ct, standardType);

            return result.IsSuccess && result.Value is not null
                ? Result.Success(result.Value.ToList())
                : Result.Success(new List<ConfigStandard>());
        }
        catch (Exception ex) when (ex.Message.Contains("nc_config_standards") || ex.Message.Contains("does not exist"))
        {
            LogError($"操作失败: {ex.Message}");
            throw new BusinessException(ErrorCodes.DB_QUERY_ERROR, "标准配置表不存在，请联系管理员");
        }
    }

    public async Task<Result<ConfigStandard>> GetConfigStandardByTypeAsync(string standardType, string hukouType = null, string supportMode = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(standardType))
            return Result<ConfigStandard>.Failure(ErrorCodes.VALIDATION_FAILED, "标准类型不能为空");

        var cacheKey = $"{standardType}|{hukouType}|{supportMode}";
        if (_standardCache.TryGetValue(cacheKey, out var cached) &&
            DateTime.UtcNow - cached.CachedAtUtc < StandardCacheTtl)
        {
            LogDebug($"从缓存获取标准配置: type={standardType}");
            return Result<ConfigStandard>.Success(cached.Value!);
        }

        LogDebug($"获取标准配置: type={standardType}");

        const string sql = @"
            SELECT * FROM nc_config_standards
            WHERE standard_type = $1
            AND ($2::VARCHAR IS NULL OR hukou_type = $2)
            AND ($3::VARCHAR IS NULL OR support_mode = $3)
            AND is_active = TRUE
            AND effective_start_date <= CURRENT_DATE
            AND (effective_end_date IS NULL OR effective_end_date >= CURRENT_DATE)
            ORDER BY effective_start_date DESC
            LIMIT 1";

        var result = await _dbService.QuerySingleAsync<ConfigStandard>(sql, ct, standardType, hukouType, supportMode);

        if (result.IsSuccess)
        {
            // 未命中记录（result.Value 为 null）同样缓存，避免重复穿透到数据库
            _standardCache[cacheKey] = new CachedStandard(result.Value, DateTime.UtcNow);
        }

        return result.IsSuccess ? Result<ConfigStandard>.Success(result.Value) : Result<ConfigStandard>.Success(null);
    }

    public async Task<Result<decimal>> GetStandardValueAsync(string standardType, string hukouType = null, string supportMode = null, CancellationToken ct = default)
    {
        var result = await GetConfigStandardByTypeAsync(standardType, hukouType, supportMode, ct);

        if (result.IsSuccess && result.Value is not null)
        {
            return Result.Success(result.Value.StandardValue);
        }

        return Result.Success(0m);
    }

    public async Task<Result<int>> CreateConfigStandardAsync(ConfigStandard standard, CancellationToken ct = default)
    {
        if (standard == null || string.IsNullOrWhiteSpace(standard.StandardType))
            return Result<int>.Failure(ErrorCodes.VALIDATION_FAILED, "标准类型不能为空");

        LogInfo("执行创建标准配置");

        const string sql = @"
            INSERT INTO nc_config_standards 
            (standard_type, standard_name, hukou_type, support_mode, standard_value, unit, 
             effective_start_date, effective_end_date, version, is_active, description, created_by)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12)
            RETURNING id";

        var result = await _dbService.QuerySingleAsync<int>(sql, ct,
            standard.StandardType,
            standard.StandardName,
            standard.HukouType,
            standard.SupportMode,
            standard.StandardValue,
            standard.Unit,
            standard.EffectiveStartDate,
            standard.EffectiveEndDate,
            standard.Version,
            standard.IsActive,
            standard.Description,
            standard.CreatedBy);

        if (result.IsSuccess)
        {
            InvalidateStandardCache();
            Logger.LogBusiness("创建标准配置", ("Type", standard.StandardType), ("Value", standard.StandardValue));
        }

        return result;
    }

    public async Task<Result> UpdateConfigStandardAsync(ConfigStandard standard, CancellationToken ct = default)
    {
        if (standard == null || standard.Id <= 0)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "标准配置参数无效");

        LogInfo("执行更新标准配置");

        const string sql = @"
            UPDATE nc_config_standards SET
                standard_name = $1,
                standard_value = $2,
                unit = $3,
                effective_start_date = $4,
                effective_end_date = $5,
                version = version + 1,
                is_active = $6,
                description = $7,
                updated_at = CURRENT_TIMESTAMP
            WHERE id = $8 AND version = $9";

        var result = await _dbService.ExecuteNonQueryAsync(sql, ct,
            standard.StandardName,
            standard.StandardValue,
            standard.Unit,
            standard.EffectiveStartDate,
            standard.EffectiveEndDate,
            standard.IsActive,
            standard.Description,
            standard.Id,
            standard.Version);

        if (result.IsSuccess && result.Value == 0)
        {
            LogWarn("版本冲突，数据已被其他人修改");
            return Result.Failure(ErrorCodes.DB_UNIQUE_VIOLATION, "数据已被其他人修改，请刷新后重试");
        }

        if (result.IsSuccess)
        {
            InvalidateStandardCache();
            Logger.LogBusiness("更新标准配置", ("Id", standard.Id), ("Value", standard.StandardValue));
        }

        return result.IsSuccess ? Result.Success() : Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, result.Message);
    }

    public async Task<Result> DeleteConfigStandardAsync(long standardId, CancellationToken ct = default)
    {
        if (standardId <= 0)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "标准配置ID无效");

        LogInfo("执行删除标准配置");

        const string sql = "UPDATE nc_config_standards SET is_active = false, updated_at = CURRENT_TIMESTAMP WHERE id = $1";

        var result = await _dbService.ExecuteNonQueryAsync(sql, ct, standardId);

        if (result.IsSuccess)
        {
            InvalidateStandardCache();
            Logger.LogBusiness("删除标准配置", ("Id", standardId));
        }

        return result.IsSuccess ? Result.Success() : Result.Failure(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, result.Message);
    }

    public async Task<Result> SyncSchemaAsync(IProgress<ProgressContext> progress = null, CancellationToken ct = default)
    {
        LogInfo("执行同步表结构");

        await ReportProgressWithDelayAsync(progress, 1, 2, "同步表结构");
        // 标准配置表同步流程有意整表重建（数据可从YAML种子恢复），显式允许删除；
        // 表结构以 Resources/Schema/config_standards/database.yaml 为唯一权威来源
        var result = await _schemaSync.SyncTableSchemaAsync(
            "nc_config_standards",
            allowDrop: true,
            null, ct);
        if (result.IsFailure)
            return result;
        await DelayAfterCompletionAsync();

        await ReportProgressWithDelayAsync(progress, 2, 2, "验证表结构");
        var verifySql = "SELECT COUNT(*) FROM information_schema.columns WHERE table_name = 'nc_config_standards'";
        var verifyResult = await _dbService.ExecuteScalarAsync(verifySql, ct);
        if (verifyResult.IsSuccess && Convert.ToInt64(verifyResult.Value) >= 10)
        {
            Logger.LogBusiness("表结构同步成功", ("ColumnCount", verifyResult.Value));
            return Result.Success();
        }

        return Result.Failure(ErrorCodes.DB_QUERY_ERROR, "表结构验证失败");
    }

    public async Task<Result> InitializeFromSeedAsync(IProgress<ProgressContext> progress = null, CancellationToken ct = default)
    {
        LogInfo("开始从Seed初始化标准配置数据");

        const int totalSteps = 5;

        await ReportProgressWithDelayAsync(progress, 1, totalSteps, "同步表结构");
        // 从Seed初始化会整表重建后回填YAML数据，显式允许删除；
        // 表结构以 Resources/Schema/config_standards/database.yaml 为唯一权威来源
        var schemaResult = await _schemaSync.SyncTableSchemaAsync(
            "nc_config_standards",
            allowDrop: true,
            null, ct);
        if (schemaResult.IsFailure)
            return schemaResult;
        await DelayAfterCompletionAsync();

        await ReportProgressWithDelayAsync(progress, 2, totalSteps, "加载YAML数据");
        var standards = LoadFromYaml();
        if (standards == null || standards.Count == 0)
        {
            LogError("YAML数据加载失败");
            return Result.Failure(ErrorCodes.FILE_NOT_FOUND, "标准配置YAML文件未找到或数据为空");
        }
        await DelayAfterCompletionAsync();

        await ReportProgressWithDelayAsync(progress, 3, totalSteps, "验证数据完整性");
        var invalidStandards = standards.Where(s => string.IsNullOrWhiteSpace(s.StandardType)).ToList();
        if (invalidStandards.Count > 0)
        {
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, $"存在 {invalidStandards.Count} 条无效标准配置");
        }
        await DelayAfterCompletionAsync();

        await using var tx = await _dbService.BeginTransactionScopeAsync(ct);
        try
        {
            await ReportProgressWithDelayAsync(progress, 4, totalSteps, "清空并插入标准配置数据");
            await _dbService.ExecuteNonQueryAsync("TRUNCATE TABLE nc_config_standards", ct);

            if (standards.Count > 0)
            {
                var insertHead = @"
                INSERT INTO nc_config_standards 
                (standard_type, standard_name, hukou_type, support_mode, standard_value, unit, 
                 effective_start_date, effective_end_date, version, is_active, description)
                VALUES ";

                var (valuesClause, insertArgs) = MultiRowValuesBuilder.Build(standards.Count, 11, i =>
                {
                    var standard = standards[i];
                    return new object?[]
                    {
                        standard.StandardType, standard.StandardName, standard.HukouType, standard.SupportMode,
                        standard.StandardValue, standard.Unit, standard.EffectiveStartDate, standard.EffectiveEndDate,
                        standard.Version, standard.IsActive, standard.Description
                    };
                });

                var insertResult = await _dbService.ExecuteNonQueryAsync(insertHead + valuesClause, ct, insertArgs);
                if (insertResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    LogError($"写入标准配置失败: {insertResult.Message}");
                    return Result.Failure(ErrorCodes.DB_QUERY_ERROR, insertResult.Message ?? "写入标准配置失败");
                }
            }
            await DelayAfterCompletionAsync();

            await ReportProgressWithDelayAsync(progress, 5, totalSteps, "提交事务");
            await tx.CommitAsync(ct);
            InvalidateStandardCache();
            Logger.LogBusiness("标准配置初始化完成", ("Count", standards.Count));

            return Result.Success();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogError($"操作失败: {ex.Message}");
            return Result.FromException(ex);
        }
    }

    private List<ConfigStandard>? LoadFromYaml()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "NewCosmos.Resources.Seed.config_standards.seed.yaml";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null) return null;

            using var reader = new StreamReader(stream);
            var yaml = reader.ReadToEnd();

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            var data = deserializer.Deserialize<ConfigStandardYaml>(yaml);
            if (data.Tables == null || data.Tables.Count == 0) return null;

            var standards = new List<ConfigStandard>();

            foreach (var table in data.Tables)
            {
                if (table.Data == null) continue;

                foreach (var item in table.Data)
                {
                    standards.Add(new ConfigStandard
                    {
                        StandardType = GetDictValue(item, "standard_type") ?? string.Empty,
                        StandardName = GetDictValue(item, "standard_name") ?? string.Empty,
                        HukouType = GetDictValue(item, "hukou_type"),
                        SupportMode = GetDictValue(item, "support_mode"),
                        StandardValue = decimal.TryParse(GetDictValue(item, "standard_value"), out var v) ? v : 0,
                        Unit = GetDictValue(item, "unit") ?? "",
                        EffectiveStartDate = DateTime.TryParse(GetDictValue(item, "effective_start_date"), out var d) ? d : DateTime.Today,
                        EffectiveEndDate = GetDictValue(item, "effective_end_date") != null && DateTime.TryParse(GetDictValue(item, "effective_end_date"), out var ed) ? ed : null,
                        Version = 1,
                        IsActive = true,
                        Description = GetDictValue(item, "description")
                    });
                }
            }

            return standards;
        }
        catch (Exception ex)
        {
            // [吞异常豁免] 唯一调用方 InitializeFromSeedAsync 在拿到 null 后立即转为显式
            // Result.Failure(ErrorCodes.FILE_NOT_FOUND, "标准配置YAML文件未找到或数据为空")，
            // 失败不会被当作"空数据"继续；异常细节由本行 LogError 落盘（err_ 日志）。
            LogError($"操作失败: {ex.Message}");
            return null;
        }
    }

    private static string? GetDictValue(Dictionary<string, string> dict, string key)
    {
        return dict.TryGetValue(key, out var value) ? value : null;
    }

    private class ConfigStandardYaml
    {
        public string Database { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public List<ConfigStandardTable> Tables { get; set; } = new();
    }

    private class ConfigStandardTable
    {
        public string Name { get; set; } = string.Empty;
        public List<Dictionary<string, string>> Data { get; set; } = new();
    }

    #endregion
}
