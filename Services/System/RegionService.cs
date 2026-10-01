using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace NewCosmos.Services.System;

public class RegionService : BaseService, IRegionService
{
    protected override string ServiceName => "RegionService";

    private readonly IDatabaseService _dbService;
    private readonly ISchemaSyncService _schemaSync;
    private readonly ISeedMergeService _seedMerge;

    private readonly ConcurrentDictionary<string, RegionVillage> _villageCodeCache = new();

    // 地区数据基本静态，按查询维度缓存，任何写操作（增删改/清空/初始化/同步）整体失效。
    private volatile List<RegionCity>? _citiesCache;
    private volatile List<RegionCounty>? _allCountiesCache;
    private readonly ConcurrentDictionary<string, List<RegionCounty>> _countiesByCityCache = new();
    private readonly ConcurrentDictionary<int, List<RegionTown>> _townsByCountyCache = new();
    private readonly ConcurrentDictionary<int, List<RegionVillage>> _villagesByTownCache = new();

    private void InvalidateRegionCaches()
    {
        _citiesCache = null;
        _allCountiesCache = null;
        _countiesByCityCache.Clear();
        _townsByCountyCache.Clear();
        _villagesByTownCache.Clear();
        _villageCodeCache.Clear();
        LogDebug("地区缓存已清除");
    }

    public RegionService(
        IDatabaseService dbService,
        ISchemaSyncService schemaSync,
        ISeedMergeService seedMerge,
        ILoggerService logger) : base(logger)
    {
        _dbService = dbService;
        _schemaSync = schemaSync;
        _seedMerge = seedMerge;
    }

    private static void ReportProgress(
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
    }

    #region Schema管理

    private static bool _tablesVerified;

    public async Task<Result> EnsureTablesExistAsync(CancellationToken ct = default)
    {
        if (_tablesVerified)
        {
            LogInfo("地区表已验证（缓存）");
            return Result.Success();
        }

        LogInfo("开始检查地区表是否存在");

        var checkSql = @"
            SELECT COUNT(*) FROM information_schema.tables 
            WHERE table_schema = 'public' 
            AND table_name IN ('nc_regions_cities', 'nc_regions_counties', 'nc_regions_towns', 'nc_regions_villages')";

        var result = await _dbService.ExecuteScalarAsync(checkSql, ct);
        if (result.IsSuccess && Convert.ToInt64(result.Value) >= 4)
        {
            LogInfo("地区表已存在");
            _tablesVerified = true;
            return Result.Success();
        }

        LogInfo("地区表不完整，开始创建");
        return await SyncSchemaAsync(null, ct);
    }

    public async Task<Result> SyncSchemaAsync(IProgress<ProgressContext> progress = null, CancellationToken ct = default)
    {
        LogInfo("开始同步地区表Schema");

        // 表结构以 Resources/Schema/regions/database.yaml 为唯一权威来源
        var tables = new[]
        {
            "nc_regions_cities",
            "nc_regions_counties",
            "nc_regions_towns",
            "nc_regions_villages",
        };

        for (var i = 0; i < tables.Length; i++)
        {
            var name = tables[i];
            ReportProgress(progress, i + 1, tables.Length, $"同步表: {name}");
            // 地区表为可从种子重建的参照表，同步流程有意整表重建，显式允许删除
            var result = await _schemaSync.SyncTableSchemaAsync(name, allowDrop: true, null, ct);
            if (result.IsFailure)
            {
                LogError($"同步表失败: {name}");
                return result;
            }
        }

        InvalidateRegionCaches();
        LogInfo("地区表Schema同步完成");
        return Result.Success();
    }

    #endregion

    #region 数据初始化
    public async Task<Result> InitializeFromSeedAsync(IProgress<ProgressContext> progress = null, CancellationToken ct = default)
    {
        LogInfo("开始从种子数据初始化地区数据");

        const int totalSteps = 10;

        await using var tx = await _dbService.BeginTransactionScopeAsync(ct);
        try
        {
            ReportProgress(progress, 1, totalSteps, "确保表结构存在");
            await EnsureTablesExistAsync(ct);
            ReportProgress(progress, 2, totalSteps, "清空地级市数据");
            Logger.LogSecurity("清空表(TRUNCATE)", ("Table", "nc_regions_cities"), ("Operation", "RegionService.InitializeFromSeed"));
            await _dbService.ExecuteNonQueryAsync("TRUNCATE TABLE nc_regions_cities", ct);
            ReportProgress(progress, 3, totalSteps, "导入地级市数据");
            var cities = LoadCitiesFromYaml();
            if (cities != null && cities.Count > 0)
            {
                foreach (var city in cities)
                {
                    await InsertCityAsync(city, ct);
                }
            }
            ReportProgress(progress, 4, totalSteps, "清空县区表");
            Logger.LogSecurity("清空表(TRUNCATE)", ("Table", "nc_regions_counties"), ("Operation", "RegionService.InitializeFromSeed"));
            await _dbService.ExecuteNonQueryAsync("TRUNCATE TABLE nc_regions_counties", ct);
            ReportProgress(progress, 5, totalSteps, "导入县区数据");
            var counties = LoadCountiesFromYaml();
            if (counties == null || counties.Count == 0)
            {
                await tx.RollbackAsync(ct);
                LogError("加载县区数据失败");
                return Result.Failure(ErrorCodes.FILE_NOT_FOUND, "县区数据文件未找到");
            }
            foreach (var county in counties)
            {
                await InsertCountyAsync(county, ct);
            }
            ReportProgress(progress, 6, totalSteps, "清空乡镇表");
            Logger.LogSecurity("清空表(TRUNCATE)", ("Table", "nc_regions_towns"), ("Operation", "RegionService.InitializeFromSeed"));
            await _dbService.ExecuteNonQueryAsync("TRUNCATE TABLE nc_regions_towns", ct);
            ReportProgress(progress, 7, totalSteps, "导入乡镇数据");
            var towns = LoadTownsFromYaml();
            if (towns != null && towns.Count > 0)
            {
                foreach (var town in towns)
                {
                    await InsertTownAsync(town, ct);
                }
            }
            ReportProgress(progress, 8, totalSteps, "清空村/社区表");
            Logger.LogSecurity("清空表(TRUNCATE)", ("Table", "nc_regions_villages"), ("Operation", "RegionService.InitializeFromSeed"));
            await _dbService.ExecuteNonQueryAsync("TRUNCATE TABLE nc_regions_villages", ct);
            ReportProgress(progress, 9, totalSteps, "导入村/社区数据");
            var villagesCount = 0;
            var villages = LoadVillagesFromCsv();
            if (villages != null && villages.Count > 0)
            {
                villagesCount = await BulkInsertVillagesAsync(villages, ct);
            }
            ReportProgress(progress, 10, totalSteps, "提交事务");
            await tx.CommitAsync(ct);
            InvalidateRegionCaches();
            LogInfo($"地区数据初始化完成: 县区{counties.Count}个, 乡镇{towns?.Count ?? 0}个, 村/社区{villagesCount}个");
            return Result.Success();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogError($"初始化失败: {ex.Message}");
            return Result.FromException(ex);
        }
    }

    public async Task<Result> ClearTablesAsync(CancellationToken ct = default)
    {
        LogInfo("开始清空地区表");

        await using var tx = await _dbService.BeginTransactionScopeAsync(ct);
        try
        {
            Logger.LogSecurity("清空表(TRUNCATE)", ("Table", "nc_regions_villages"), ("Operation", "RegionService.ClearTables"));
            await _dbService.ExecuteNonQueryAsync("TRUNCATE TABLE nc_regions_villages", ct);
            Logger.LogSecurity("清空表(TRUNCATE)", ("Table", "nc_regions_towns"), ("Operation", "RegionService.ClearTables"));
            await _dbService.ExecuteNonQueryAsync("TRUNCATE TABLE nc_regions_towns", ct);
            Logger.LogSecurity("清空表(TRUNCATE)", ("Table", "nc_regions_counties"), ("Operation", "RegionService.ClearTables"));
            await _dbService.ExecuteNonQueryAsync("TRUNCATE TABLE nc_regions_counties", ct);
            await tx.CommitAsync(ct);

            InvalidateRegionCaches();
            LogInfo("地区表已清空");
            return Result.Success();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogError($"清空失败: {ex.Message}");
            return Result.FromException(ex);
        }
    }

    #endregion

    #region 地级市查询
    public async Task<Result<List<RegionCity>>> GetCitiesAsync(CancellationToken ct = default)
    {
        var cached = _citiesCache;
        if (cached != null)
        {
            LogDebug("从缓存获取地级市列表");
            return Result.Success(new List<RegionCity>(cached));
        }

        await EnsureTablesExistAsync(ct);

        LogInfo("获取所有地级市");

        var sql = "SELECT * FROM nc_regions_cities WHERE is_active = TRUE ORDER BY sort_order, id";
        var result = await _dbService.QueryAsync<RegionCity>(sql, ct);

        if (result.IsSuccess && result.Value is not null)
        {
            _citiesCache = result.Value;
            return Result.Success(new List<RegionCity>(result.Value));
        }

        return Result.Success(new List<RegionCity>());
    }

    public async Task<Result<RegionCity>> GetCityByIdAsync(int id, CancellationToken ct = default)
    {
        await EnsureTablesExistAsync(ct);

        LogInfo($"获取地级市: Id={id}");

        var sql = "SELECT * FROM nc_regions_cities WHERE id = $1";
        var result = await _dbService.QuerySingleAsync<RegionCity>(sql, ct, id);

        return result.IsSuccess
            ? Result.Success(result.Value)
            : Result.Success<RegionCity>(null);
    }

    public async Task<Result<RegionCity>> GetCityByNameAsync(string cityName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cityName))
            return Result.Failure<RegionCity>(ErrorCodes.VALIDATION_FAILED, "地级市名称不能为空");

        await EnsureTablesExistAsync(ct);

        var sql = "SELECT * FROM nc_regions_cities WHERE city_name = $1";
        var result = await _dbService.QuerySingleAsync<RegionCity>(sql, ct, cityName);

        return result.IsSuccess
            ? Result.Success(result.Value)
            : Result.Success<RegionCity>(null);
    }

    #endregion

    #region 县区查询

    public async Task<Result<List<RegionCounty>>> GetCountiesAsync(CancellationToken ct = default)
    {
        var cached = _allCountiesCache;
        if (cached != null)
        {
            LogDebug("从缓存获取县区列表");
            return Result.Success(new List<RegionCounty>(cached));
        }

        await EnsureTablesExistAsync(ct);

        LogInfo("获取所有县区");

        var sql = "SELECT * FROM nc_regions_counties WHERE is_active = TRUE ORDER BY sort_order, id";
        var result = await _dbService.QueryAsync<RegionCounty>(sql, ct);

        if (result.IsSuccess && result.Value is not null)
        {
            _allCountiesCache = result.Value;
            return Result.Success(new List<RegionCounty>(result.Value));
        }

        return Result.Success(new List<RegionCounty>());
    }

    public async Task<Result<List<RegionCounty>>> GetCountiesByCityAsync(string cityName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cityName))
            return Result.Failure<List<RegionCounty>>(ErrorCodes.VALIDATION_FAILED, "地级市名称不能为空");

        if (_countiesByCityCache.TryGetValue(cityName, out var cached))
        {
            LogDebug($"从缓存获取县区: CityName={cityName}");
            return Result.Success(new List<RegionCounty>(cached));
        }

        await EnsureTablesExistAsync(ct);

        LogInfo($"获取县区: CityName={cityName}");

        var sql = "SELECT * FROM nc_regions_counties WHERE city_name = $1 AND is_active = TRUE ORDER BY sort_order, id";
        var result = await _dbService.QueryAsync<RegionCounty>(sql, ct, cityName);

        if (result.IsSuccess && result.Value is not null)
        {
            _countiesByCityCache[cityName] = result.Value;
            return Result.Success(new List<RegionCounty>(result.Value));
        }

        return Result.Success(new List<RegionCounty>());
    }

    public async Task<Result<List<RegionCounty>>> GetCountiesByCityIdAsync(int cityId, CancellationToken ct = default)
    {
        await EnsureTablesExistAsync(ct);

        var cityResult = await GetCityByIdAsync(cityId, ct);
        if (cityResult.IsFailure || cityResult.Value == null)
            return Result.Failure<List<RegionCounty>>(ErrorCodes.REGION_CITY_NOT_FOUND, $"地级市未找到: Id={cityId}");

        return await GetCountiesByCityAsync(cityResult.Value.CityName, ct);
    }

    public async Task<Result<RegionCounty>> GetCountyByIdAsync(int id, CancellationToken ct = default)
    {
        await EnsureTablesExistAsync(ct);

        var sql = "SELECT * FROM nc_regions_counties WHERE id = $1";
        var result = await _dbService.QuerySingleAsync<RegionCounty>(sql, ct, id);

        return result.IsSuccess
            ? Result.Success(result.Value)
            : Result.Success<RegionCounty>(null);
    }

    public async Task<Result<RegionCounty>> GetCountyByCodeAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Result.Failure<RegionCounty>(ErrorCodes.VALIDATION_FAILED, "县区编码不能为空");

        await EnsureTablesExistAsync(ct);

        var sql = "SELECT * FROM nc_regions_counties WHERE county_code = $1 AND is_active = true";
        var result = await _dbService.QuerySingleAsync<RegionCounty>(sql, ct, code);

        return result.IsSuccess
            ? Result.Success(result.Value)
            : Result.Success<RegionCounty>(null);
    }

    #endregion

    #region 乡镇查询

    public async Task<Result<List<RegionTown>>> GetTownsByCountyIdAsync(int countyId, CancellationToken ct = default)
    {
        if (_townsByCountyCache.TryGetValue(countyId, out var cached))
        {
            LogDebug($"从缓存获取乡镇: CountyId={countyId}");
            return Result.Success(new List<RegionTown>(cached));
        }

        await EnsureTablesExistAsync(ct);

        LogInfo($"获取乡镇: CountyId={countyId}");

        var sql = "SELECT * FROM nc_regions_towns WHERE county_id = $1 AND is_active = TRUE ORDER BY sort_order, id";
        var result = await _dbService.QueryAsync<RegionTown>(sql, ct, countyId);

        if (result.IsSuccess && result.Value is not null)
        {
            _townsByCountyCache[countyId] = result.Value;
            return Result.Success(new List<RegionTown>(result.Value));
        }

        return Result.Success(new List<RegionTown>());
    }

    public async Task<Result<RegionTown>> GetTownByIdAsync(int id, CancellationToken ct = default)
    {
        await EnsureTablesExistAsync(ct);

        var sql = "SELECT * FROM nc_regions_towns WHERE id = $1";
        var result = await _dbService.QuerySingleAsync<RegionTown>(sql, ct, id);

        return result.IsSuccess
            ? Result.Success(result.Value)
            : Result.Success<RegionTown>(null);
    }

    public async Task<Result<List<RegionTown>>> SearchTownsAsync(string keyword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return Result.Failure<List<RegionTown>>(ErrorCodes.VALIDATION_FAILED, "搜索关键词不能为空");

        await EnsureTablesExistAsync(ct);

        LogInfo($"搜索乡镇: Keyword={keyword}");

        var sql = "SELECT * FROM nc_regions_towns WHERE town_name LIKE $1 AND is_active = TRUE ORDER BY sort_order, id LIMIT 50";
        var result = await _dbService.QueryAsync<RegionTown>(sql, ct, $"%{keyword}%");

        return result.IsSuccess && result.Value is not null
            ? Result.Success(result.Value)
            : Result.Success(new List<RegionTown>());
    }

    #endregion

    #region 村/社区查询

    public async Task<Result<List<RegionVillage>>> GetVillagesByTownIdAsync(int townId, CancellationToken ct = default)
    {
        if (_villagesByTownCache.TryGetValue(townId, out var cached))
        {
            LogDebug($"从缓存获取村/社区: TownId={townId}");
            return Result.Success(new List<RegionVillage>(cached));
        }

        await EnsureTablesExistAsync(ct);

        LogInfo($"获取村/社区: TownId={townId}");

        var sql = "SELECT * FROM nc_regions_villages WHERE town_id = $1 AND is_active = TRUE ORDER BY sort_order, id";
        var result = await _dbService.QueryAsync<RegionVillage>(sql, ct, townId);

        if (result.IsSuccess && result.Value is not null)
        {
            _villagesByTownCache[townId] = result.Value;
            return Result.Success(new List<RegionVillage>(result.Value));
        }

        return Result.Success(new List<RegionVillage>());
    }

    public async Task<Result<RegionVillage>> GetVillageByIdAsync(int id, CancellationToken ct = default)
    {
        await EnsureTablesExistAsync(ct);

        var sql = "SELECT * FROM nc_regions_villages WHERE id = $1";
        var result = await _dbService.QuerySingleAsync<RegionVillage>(sql, ct, id);

        return result.IsSuccess
            ? Result.Success(result.Value)
            : Result.Success<RegionVillage>(null);
    }

    public async Task<Result<RegionVillage>> GetVillageByCodeAsync(string villageCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(villageCode))
            return Result.Failure<RegionVillage>(ErrorCodes.VALIDATION_FAILED, "村/社区编码不能为空");

        if (_villageCodeCache.TryGetValue(villageCode, out var cached))
            return Result.Success(cached);

        await EnsureTablesExistAsync(ct);

        var sql = "SELECT * FROM nc_regions_villages WHERE village_code = $1 AND is_active = true";
        var result = await _dbService.QuerySingleAsync<RegionVillage>(sql, ct, villageCode);

        _villageCodeCache[villageCode] = result.Value;

        return result.IsSuccess
            ? Result.Success(result.Value)
            : Result.Success<RegionVillage>(null);
    }

    public async Task<Result<List<RegionVillage>>> SearchVillagesAsync(string keyword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return Result.Failure<List<RegionVillage>>(ErrorCodes.VALIDATION_FAILED, "搜索关键词不能为空");

        await EnsureTablesExistAsync(ct);

        LogInfo($"搜索村/社区: Keyword={keyword}");

        var sql = "SELECT * FROM nc_regions_villages WHERE village_name LIKE $1 AND is_active = TRUE ORDER BY sort_order, id LIMIT 50";
        var result = await _dbService.QueryAsync<RegionVillage>(sql, ct, $"%{keyword}%");

        return result.IsSuccess && result.Value is not null
            ? Result.Success(result.Value)
            : Result.Success(new List<RegionVillage>());
    }

    #endregion

    #region 树形查询

    public async Task<Result<List<RegionCounty>>> GetCountiesWithTownsAsync(CancellationToken ct = default)
    {
        await EnsureTablesExistAsync(ct);

        LogInfo("获取县区及乡镇树形结构");

        var sql = @"
            SELECT c.* FROM nc_regions_counties c
            WHERE c.is_active = TRUE 
            AND EXISTS (SELECT 1 FROM nc_regions_towns t WHERE t.county_id = c.id AND t.is_active = true)
            ORDER BY c.sort_order, c.id";
        var result = await _dbService.QueryAsync<RegionCounty>(sql, ct);

        return result.IsSuccess && result.Value is not null
            ? Result.Success(result.Value)
            : Result.Success(new List<RegionCounty>());
    }

    public async Task<Result<Dictionary<string, List<RegionTown>>>> GetAllTownsGroupedByCountyAsync(CancellationToken ct = default)
    {
        await EnsureTablesExistAsync(ct);

        LogInfo("获取按县区分组的所有乡镇");

        var sql = @"
            SELECT t.* FROM nc_regions_towns t
            INNER JOIN nc_regions_counties c ON t.county_id = c.id
            WHERE t.is_active = TRUE AND c.is_active = TRUE
            ORDER BY c.sort_order, c.id, t.sort_order, t.id";
        var result = await _dbService.QueryAsync<RegionTown>(sql, ct);

        if (!result.IsSuccess || result.Value is null)
            return Result.Success(new Dictionary<string, List<RegionTown>>());

        var grouped = result.Value
            .GroupBy(t => t.CountyId.ToString())
            .ToDictionary(g => g.Key, g => g.ToList());

        return Result.Success(grouped);
    }

    #endregion

    #region 地级市管理(增删改)

    public async Task<Result<RegionCity>> CreateCityAsync(RegionCitySaveRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.CityName))
            return Result.Failure<RegionCity>(ErrorCodes.VALIDATION_FAILED, "地级市名称不能为空");

        await EnsureTablesExistAsync(ct);

        LogInfo($"新建地级市: {request.CityName}");

        var sql = @"INSERT INTO nc_regions_cities (id, city_name, city_code, sort_order, is_active)
                    VALUES ($1, $2, $3, $4, $5)
                    RETURNING id";
        var result = await _dbService.QuerySingleAsync<RegionCity>(sql, ct,
            request.Id > 0 ? request.Id : (object?)null,
            request.CityName, request.CityCode, request.SortOrder, request.IsActive);

        if (result.IsSuccess && result.Value != null)
        {
            InvalidateRegionCaches();
            return Result.Success(result.Value);
        }

        return Result.Failure<RegionCity>(ErrorCodes.DB_QUERY_ERROR, "新建地级市失败");
    }

    public async Task<Result<RegionCity>> UpdateCityAsync(RegionCitySaveRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.CityName))
            return Result.Failure<RegionCity>(ErrorCodes.VALIDATION_FAILED, "地级市名称不能为空");
        if (request.Id <= 0)
            return Result.Failure<RegionCity>(ErrorCodes.REGION_CITY_NOT_FOUND, "地级市ID无效");

        await EnsureTablesExistAsync(ct);

        LogInfo($"更新地级市: Id={request.Id}");

        var sql = @"UPDATE nc_regions_cities SET city_name = $1, city_code = $2,
                    sort_order = $3, is_active = $4 WHERE id = $5
                    RETURNING id";
        var result = await _dbService.QuerySingleAsync<RegionCity>(sql, ct,
            request.CityName, request.CityCode, request.SortOrder, request.IsActive, request.Id);

        if (result.IsSuccess && result.Value != null)
        {
            InvalidateRegionCaches();
            return Result.Success(result.Value);
        }

        return Result.Failure<RegionCity>(ErrorCodes.REGION_CITY_NOT_FOUND, $"地级市未找到: Id={request.Id}");
    }

    public async Task<Result> DeleteCityAsync(int cityId, CancellationToken ct = default)
    {
        if (cityId <= 0)
            return Result.Failure(ErrorCodes.REGION_CITY_NOT_FOUND, "地级市ID无效");

        await EnsureTablesExistAsync(ct);

        LogInfo($"删除地级市: Id={cityId}");

        var sql = "DELETE FROM nc_regions_cities WHERE id = $1";
        var result = await _dbService.ExecuteNonQueryAsync(sql, ct, cityId);

        if (result.IsSuccess)
        {
            InvalidateRegionCaches();
            return Result.Success();
        }

        return Result.Failure(ErrorCodes.DB_QUERY_ERROR, "删除地级市失败");
    }

    #endregion

    #region 县区管理(增删改)

    public async Task<Result<RegionCounty>> CreateCountyAsync(RegionCountySaveRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.CountyName))
            return Result.Failure<RegionCounty>(ErrorCodes.VALIDATION_FAILED, "县区名称不能为空");

        await EnsureTablesExistAsync(ct);

        LogInfo($"新建县区: {request.CountyName}");

        try
        {
            var nextId = await GetNextCountyIdAsync(ct);

            var sql = @"INSERT INTO nc_regions_counties (id, county_name, county_code, city_name, sort_order, is_active)
                        VALUES ($1, $2, $3, $4, $5, $6) RETURNING *";

            var result = await _dbService.QuerySingleAsync<RegionCounty>(sql, ct,
                nextId, request.CountyName, request.CountyCode,
                request.CityName, request.SortOrder, request.IsActive);

            if (result.IsFailure || result.Value is null)
                return Result.Failure<RegionCounty>(ErrorCodes.DB_QUERY_ERROR, "新建县区失败");

            InvalidateRegionCaches();
            LogInfo($"县区创建成功: Id={result.Value.Id}");
            return Result.Success(result.Value);
        }
        catch (Exception ex)
        {
            LogError($"创建县区失败: {ex.Message}");
            return Result.FromException<RegionCounty>(ex);
        }
    }

    public async Task<Result<RegionCounty>> UpdateCountyAsync(RegionCountySaveRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.CountyName))
            return Result.Failure<RegionCounty>(ErrorCodes.VALIDATION_FAILED, "县区名称不能为空");

        if (request.Id <= 0)
            return Result.Failure<RegionCounty>(ErrorCodes.REGION_COUNTY_NOT_FOUND, "县区ID无效");

        await EnsureTablesExistAsync(ct);

        LogInfo($"更新县区: Id={request.Id}");

        try
        {
            var sql = @"UPDATE nc_regions_counties
                        SET county_name = $1, county_code = $2, city_name = $3,
                            sort_order = $4, is_active = $5
                        WHERE id = $6 RETURNING *";

            var result = await _dbService.QuerySingleAsync<RegionCounty>(sql, ct,
                request.CountyName, request.CountyCode, request.CityName,
                request.SortOrder, request.IsActive, request.Id);

            if (result.IsFailure || result.Value is null)
                return Result.Failure<RegionCounty>(ErrorCodes.REGION_COUNTY_NOT_FOUND, $"县区未找到: Id={request.Id}");

            InvalidateRegionCaches();
            LogInfo($"县区更新成功: Id={result.Value.Id}");
            return Result.Success(result.Value);
        }
        catch (Exception ex)
        {
            LogError($"更新县区失败: {ex.Message}");
            return Result.FromException<RegionCounty>(ex);
        }
    }

    public async Task<Result> DeleteCountyAsync(int countyId, CancellationToken ct = default)
    {
        if (countyId <= 0)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "县区ID无效");

        await EnsureTablesExistAsync(ct);

        LogInfo($"删除县区: Id={countyId}");

        try
        {
            var checkSql = "SELECT COUNT(*) FROM nc_regions_towns WHERE county_id = $1 AND is_active = true";
            var checkResult = await _dbService.ExecuteScalarAsync(checkSql, ct, countyId);
            if (checkResult.IsSuccess && Convert.ToInt64(checkResult.Value) > 0)
                return Result.Failure(ErrorCodes.REGION_HAS_CHILDREN, "该县区下存在乡镇，不能删除");

            var sql = "UPDATE nc_regions_counties SET is_active = FALSE WHERE id = $1";
            var result = await _dbService.ExecuteNonQueryAsync(sql, ct, countyId);

            if (result.IsFailure)
                return Result.Failure(ErrorCodes.REGION_COUNTY_NOT_FOUND, $"县区未找到: Id={countyId}");

            InvalidateRegionCaches();
            LogInfo($"县区已停用: Id={countyId}");
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogError($"删除县区失败: {ex.Message}");
            return Result.FromException(ex);
        }
    }

    #endregion

    #region 乡镇管理(增删改)

    public async Task<Result<RegionTown>> CreateTownAsync(RegionTownSaveRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.TownName))
            return Result.Failure<RegionTown>(ErrorCodes.VALIDATION_FAILED, "乡镇名称不能为空");

        if (request.CountyId <= 0)
            return Result.Failure<RegionTown>(ErrorCodes.VALIDATION_FAILED, "所属县区ID无效");

        await EnsureTablesExistAsync(ct);

        LogInfo($"新增乡镇: {request.TownName} (countyId={request.CountyId})");

        try
        {
            var nextId = await GetNextTownIdAsync(ct);

            var sql = @"INSERT INTO nc_regions_towns (id, county_id, town_name, town_code, town_type, sort_order, is_active)
                        VALUES ($1, $2, $3, $4, $5, $6, $7) RETURNING *";

            var result = await _dbService.QuerySingleAsync<RegionTown>(sql, ct,
                nextId, request.CountyId, request.TownName, request.TownCode,
                request.TownType, request.SortOrder, request.IsActive);

            if (result.IsFailure || result.Value is null)
                return Result.Failure<RegionTown>(ErrorCodes.DB_QUERY_ERROR, "新建乡镇失败");

            InvalidateRegionCaches();
            LogInfo($"乡镇创建成功: Id={result.Value.Id}");
            return Result.Success(result.Value);
        }
        catch (Exception ex)
        {
            LogError($"创建乡镇失败: {ex.Message}");
            return Result.FromException<RegionTown>(ex);
        }
    }

    public async Task<Result<RegionTown>> UpdateTownAsync(RegionTownSaveRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.TownName))
            return Result.Failure<RegionTown>(ErrorCodes.VALIDATION_FAILED, "乡镇名称不能为空");

        if (request.Id <= 0)
            return Result.Failure<RegionTown>(ErrorCodes.REGION_TOWN_NOT_FOUND, "乡镇ID无效");

        await EnsureTablesExistAsync(ct);

        LogInfo($"更新乡镇: Id={request.Id}");

        try
        {
            var sql = @"UPDATE nc_regions_towns
                        SET county_id = $1, town_name = $2, town_code = $3,
                            town_type = $4, sort_order = $5, is_active = $6
                        WHERE id = $7 RETURNING *";

            var result = await _dbService.QuerySingleAsync<RegionTown>(sql, ct,
                request.CountyId, request.TownName, request.TownCode,
                request.TownType, request.SortOrder, request.IsActive, request.Id);

            if (result.IsFailure || result.Value is null)
                return Result.Failure<RegionTown>(ErrorCodes.REGION_TOWN_NOT_FOUND, $"乡镇未找到: Id={request.Id}");

            InvalidateRegionCaches();
            LogInfo($"乡镇更新成功: Id={result.Value.Id}");
            return Result.Success(result.Value);
        }
        catch (Exception ex)
        {
            LogError($"更新乡镇失败: {ex.Message}");
            return Result.FromException<RegionTown>(ex);
        }
    }

    public async Task<Result> DeleteTownAsync(int townId, CancellationToken ct = default)
    {
        if (townId <= 0)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "乡镇ID无效");

        await EnsureTablesExistAsync(ct);

        LogInfo($"删除乡镇: Id={townId}");

        try
        {
            var checkSql = "SELECT COUNT(*) FROM nc_regions_villages WHERE town_id = $1 AND is_active = true";
            var checkResult = await _dbService.ExecuteScalarAsync(checkSql, ct, townId);
            if (checkResult.IsSuccess && Convert.ToInt64(checkResult.Value) > 0)
                return Result.Failure(ErrorCodes.REGION_HAS_CHILDREN, "该乡镇下存在村/社区，不能删除");

            var sql = "UPDATE nc_regions_towns SET is_active = FALSE WHERE id = $1";
            var result = await _dbService.ExecuteNonQueryAsync(sql, ct, townId);

            if (result.IsFailure)
                return Result.Failure(ErrorCodes.REGION_TOWN_NOT_FOUND, $"乡镇未找到: Id={townId}");

            InvalidateRegionCaches();
            LogInfo($"乡镇已停用: Id={townId}");
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogError($"删除乡镇失败: {ex.Message}");
            return Result.FromException(ex);
        }
    }

    #endregion

    #region 村/社区管理(增删改)

    public async Task<Result<RegionVillage>> CreateVillageAsync(RegionVillageSaveRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.VillageName))
            return Result.Failure<RegionVillage>(ErrorCodes.VALIDATION_FAILED, "村/社区名称不能为空");

        if (request.TownId <= 0)
            return Result.Failure<RegionVillage>(ErrorCodes.VALIDATION_FAILED, "所属乡镇ID无效");

        await EnsureTablesExistAsync(ct);

        LogInfo($"新增村/社区: {request.VillageName} (townId={request.TownId})");

        try
        {
            var nextId = await GetNextVillageIdAsync(ct);

            var sql = @"INSERT INTO nc_regions_villages (id, town_id, village_name, village_code, village_type, sort_order, is_active)
                        VALUES ($1, $2, $3, $4, $5, $6, $7) RETURNING *";

            var result = await _dbService.QuerySingleAsync<RegionVillage>(sql, ct,
                nextId, request.TownId, request.VillageName, request.VillageCode,
                request.VillageType, request.SortOrder, request.IsActive);

            if (result.IsFailure || result.Value is null)
                return Result.Failure<RegionVillage>(ErrorCodes.DB_QUERY_ERROR, "新建村/社区失败");

            InvalidateRegionCaches();
            LogInfo($"村/社区创建成功: Id={result.Value.Id}");
            return Result.Success(result.Value);
        }
        catch (Exception ex)
        {
            LogError($"创建村/社区失败: {ex.Message}");
            return Result.FromException<RegionVillage>(ex);
        }
    }

    public async Task<Result<RegionVillage>> UpdateVillageAsync(RegionVillageSaveRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.VillageName))
            return Result.Failure<RegionVillage>(ErrorCodes.VALIDATION_FAILED, "村/社区名称不能为空");

        if (request.Id <= 0)
            return Result.Failure<RegionVillage>(ErrorCodes.REGION_VILLAGE_NOT_FOUND, "村/社区ID无效");

        await EnsureTablesExistAsync(ct);

        LogInfo($"更新村/社区: Id={request.Id}");

        try
        {
            var sql = @"UPDATE nc_regions_villages
                        SET town_id = $1, village_name = $2, village_code = $3,
                            village_type = $4, sort_order = $5, is_active = $6
                        WHERE id = $7 RETURNING *";

            var result = await _dbService.QuerySingleAsync<RegionVillage>(sql, ct,
                request.TownId, request.VillageName, request.VillageCode,
                request.VillageType, request.SortOrder, request.IsActive, request.Id);

            if (result.IsFailure || result.Value is null)
                return Result.Failure<RegionVillage>(ErrorCodes.REGION_VILLAGE_NOT_FOUND, $"村/社区未找到: Id={request.Id}");

            InvalidateRegionCaches();
            LogInfo($"村/社区更新成功: Id={result.Value.Id}");
            return Result.Success(result.Value);
        }
        catch (Exception ex)
        {
            LogError($"更新村/社区失败: {ex.Message}");
            return Result.FromException<RegionVillage>(ex);
        }
    }

    public async Task<Result> DeleteVillageAsync(int villageId, CancellationToken ct = default)
    {
        if (villageId <= 0)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "村/社区ID无效");

        await EnsureTablesExistAsync(ct);

        LogInfo($"删除村/社区: Id={villageId}");

        try
        {
            var sql = "UPDATE nc_regions_villages SET is_active = FALSE WHERE id = $1";
            var result = await _dbService.ExecuteNonQueryAsync(sql, ct, villageId);

            if (result.IsFailure)
                return Result.Failure(ErrorCodes.REGION_VILLAGE_NOT_FOUND, $"村/社区未找到: Id={villageId}");

            InvalidateRegionCaches();
            LogInfo($"村/社区已停用: Id={villageId}");
            return Result.Success();
        }
        catch (Exception ex)
        {
            LogError($"删除村/社区失败: {ex.Message}");
            return Result.FromException(ex);
        }
    }

    #endregion

    #region ID生成辅助方法

    private async Task<int> GetNextCountyIdAsync(CancellationToken ct)
    {
        var sql = "SELECT COALESCE(MAX(id), 9000) + 1 FROM nc_regions_counties";
        var result = await _dbService.ExecuteScalarAsync(sql, ct);
        return result.IsSuccess ? Convert.ToInt32(result.Value) : 9001;
    }

    private async Task<int> GetNextTownIdAsync(CancellationToken ct)
    {
        var sql = "SELECT COALESCE(MAX(id), 90000) + 1 FROM nc_regions_towns";
        var result = await _dbService.ExecuteScalarAsync(sql, ct);
        return result.IsSuccess ? Convert.ToInt32(result.Value) : 90001;
    }

    private async Task<int> GetNextVillageIdAsync(CancellationToken ct)
    {
        var sql = "SELECT COALESCE(MAX(id), 900000) + 1 FROM nc_regions_villages";
        var result = await _dbService.ExecuteScalarAsync(sql, ct);
        return result.IsSuccess ? Convert.ToInt32(result.Value) : 900001;
    }

    #endregion

    #region 村/社区CSV数据加载

    private const int VILLAGE_BATCH_SIZE = 500;

    private List<VillageCsvRecord>? LoadVillagesFromCsv()
    {
        try
        {
            var csvPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Seed", "regions", "villages.csv");
            
            if (!File.Exists(csvPath))
            {
                LogWarn($"未找到村/社区CSV文件: {csvPath}");
                return null;
            }

            LogInfo($"从文件系统加载村/社区数据: {csvPath}");
            
            using var reader = new StreamReader(csvPath, Encoding.UTF8);
            var villages = new List<VillageCsvRecord>();

            reader.ReadLine(); // 跳过表头

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var parts = line.Split(',');
                if (parts.Length < 3) continue;

                villages.Add(new VillageCsvRecord
                {
                    VillageCode = parts[0],
                    VillageName = parts[1],
                    StreetCode = parts[2]
                });
            }

            LogInfo($"从CSV加载村/社区数据: {villages.Count}条");
            return villages;
        }
        catch (Exception ex)
        {
            LogError($"加载村/社区CSV失败: {ex.Message}");
            return null;
        }
    }

    private async Task<int> BulkInsertVillagesAsync(List<VillageCsvRecord> villages, CancellationToken ct)
    {
        var townMap = await LoadTownCodeToIdMapAsync(ct);
        if (townMap.Count == 0)
        {
            LogWarn("乡镇编码映射为空，跳过村/社区导入");
            return 0;
        }

        var startId = await GetNextVillageIdAsync(ct);
        var processed = new List<(int TownId, string Name, string Code, string Type, int SortOrder, bool IsActive)>();

        var grouped = villages.GroupBy(v => v.StreetCode);
        foreach (var group in grouped)
        {
            if (!townMap.TryGetValue(group.Key, out var townId)) continue;

            var sort = 1;
            foreach (var v in group)
            {
                var type = DetermineVillageType(v.VillageName);
                processed.Add((townId, v.VillageName, v.VillageCode, type, sort++, true));
            }
        }

        var totalInserted = 0;
        for (var i = 0; i < processed.Count; i += VILLAGE_BATCH_SIZE)
        {
            var batch = processed.Skip(i).Take(VILLAGE_BATCH_SIZE).ToList();
            totalInserted += await InsertVillageBatchAsync(batch, startId + totalInserted, ct);
        }

        LogInfo($"村/社区批量导入完成: {totalInserted}条");
        return totalInserted;
    }

    private async Task<int> InsertVillageBatchAsync(List<(int TownId, string Name, string Code, string Type, int SortOrder, bool IsActive)> batch, int startId, CancellationToken ct)
    {
        var paramValues = new List<object>();
        var valueClauses = new List<string>();

        for (var i = 0; i < batch.Count; i++)
        {
            var item = batch[i];
            var offset = i * 7;
            valueClauses.Add($"(${offset + 1}, ${offset + 2}, ${offset + 3}, ${offset + 4}, ${offset + 5}, ${offset + 6}, ${offset + 7})");
            paramValues.Add(startId + i);
            paramValues.Add(item.TownId);
            paramValues.Add(item.Name);
            paramValues.Add(item.Code);
            paramValues.Add(item.Type);
            paramValues.Add(item.SortOrder);
            paramValues.Add(item.IsActive);
        }

        var sql = "INSERT INTO nc_regions_villages (id, town_id, village_name, village_code, village_type, sort_order, is_active) VALUES " +
                  string.Join(", ", valueClauses);

        var result = await _dbService.ExecuteNonQueryAsync(sql, ct, paramValues.ToArray());
        return result.IsSuccess ? batch.Count : 0;
    }

    private async Task<Dictionary<string, int>> LoadTownCodeToIdMapAsync(CancellationToken ct)
    {
        var sql = "SELECT id, town_code FROM nc_regions_towns WHERE town_code IS NOT NULL AND is_active = true";
        var result = await _dbService.QueryAsync<RegionTown>(sql, ct);
        if (result.IsFailure || result.Value is null) return new Dictionary<string, int>();

        return result.Value.ToDictionary(t => t.TownCode!, t => t.Id);
    }

    private static string DetermineVillageType(string name)
    {
        if (name.Contains("居委会")) return "居委会";
        if (name.Contains("社区")) return "居委会";
        if (name.Contains("村委会")) return "村委会";
        return "村委会";
    }

    private class VillageCsvRecord
    {
        public string VillageCode { get; set; } = string.Empty;
        public string VillageName { get; set; } = string.Empty;
        public string StreetCode { get; set; } = string.Empty;
    }

    #endregion

    #region 种子数据加载

    private List<RegionCity>? LoadCitiesFromYaml()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "NewCosmos.Resources.Seed.regions.cities.yaml";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                LogWarn("未找到地级市YAML资源文件");
                return null;
            }
            using var reader = new StreamReader(stream);
            var yaml = reader.ReadToEnd();
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            var data = deserializer.Deserialize<RegionCityYaml>(yaml);
            return data.Tables[0].Data.Select(c => new RegionCity
            {
                Id = c.Id,
                CityName = c.CityName ?? string.Empty,
                CityCode = c.CityCode,
                SortOrder = c.SortOrder,
                IsActive = c.IsActive
            }).ToList();
        }
        catch (Exception ex)
        {
            LogError($"加载地级市YAML失败: {ex.Message}");
            return null;
        }
    }

    private async Task InsertCityAsync(RegionCity city, CancellationToken ct)
    {
        var checkSql = "SELECT COUNT(*) FROM nc_regions_cities WHERE id = $1";
        var exists = await _dbService.ExecuteScalarAsync(checkSql, ct, city.Id);
        if (exists.IsSuccess && Convert.ToInt32(exists.Value) == 0)
        {
            var sql = @"INSERT INTO nc_regions_cities (id, city_name, city_code, sort_order, is_active)
                        VALUES ($1, $2, $3, $4, $5)";
            await _dbService.ExecuteNonQueryAsync(sql, ct,
                city.Id, city.CityName, city.CityCode, city.SortOrder, city.IsActive);
        }
    }

    private List<RegionCounty>? LoadCountiesFromYaml()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "NewCosmos.Resources.Seed.regions.counties.yaml";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                LogWarn("未找到县区YAML资源文件");
                return null;
            }

            using var reader = new StreamReader(stream);
            var yaml = reader.ReadToEnd();

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            var data = deserializer.Deserialize<RegionCountyYaml>(yaml);
            if (data.Tables == null || data.Tables.Count == 0 || data.Tables[0].Data == null)
                return null;

            return data.Tables[0].Data.Select(c => new RegionCounty
            {
                Id = c.Id,
                CountyName = c.CountyName ?? string.Empty,
                CountyCode = c.CountyCode,
                CityName = c.CityName,
                SortOrder = c.SortOrder,
                IsActive = c.IsActive
            }).ToList();
        }
        catch (Exception ex)
        {
            LogError($"加载县区YAML失败: {ex.Message}");
            return null;
        }
    }

    private List<RegionTown>? LoadTownsFromYaml()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "NewCosmos.Resources.Seed.regions.towns.yaml";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                LogWarn("未找到乡镇YAML资源文件");
                return null;
            }

            using var reader = new StreamReader(stream);
            var yaml = reader.ReadToEnd();

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            var data = deserializer.Deserialize<RegionTownYaml>(yaml);
            if (data.Tables == null || data.Tables.Count == 0 || data.Tables[0].Data == null)
                return null;

            return data.Tables[0].Data.Select(t => new RegionTown
            {
                Id = t.Id,
                CountyId = t.CountyId,
                TownName = t.TownName ?? string.Empty,
                TownCode = t.TownCode,
                TownType = t.TownType,
                SortOrder = t.SortOrder,
                IsActive = t.IsActive
            }).ToList();
        }
        catch (Exception ex)
        {
            LogError($"加载乡镇YAML失败: {ex.Message}");
            return null;
        }
    }

    #endregion

    #region 数据插入

    private async Task InsertCountyAsync(RegionCounty county, CancellationToken ct)
    {
        var checkSql = "SELECT COUNT(*) FROM nc_regions_counties WHERE id = $1";
        var exists = await _dbService.ExecuteScalarAsync(checkSql, ct, county.Id);
        if (exists.IsSuccess && Convert.ToInt32(exists.Value) == 0)
        {
            var sql = @"INSERT INTO nc_regions_counties (id, county_name, county_code, city_name, sort_order, is_active)
                        VALUES ($1, $2, $3, $4, $5, $6)";
            await _dbService.ExecuteNonQueryAsync(sql, ct,
                county.Id, county.CountyName, county.CountyCode,
                county.CityName, county.SortOrder, county.IsActive);
        }
    }

    private async Task InsertTownAsync(RegionTown town, CancellationToken ct)
    {
        var checkSql = "SELECT COUNT(*) FROM nc_regions_towns WHERE id = $1";
        var exists = await _dbService.ExecuteScalarAsync(checkSql, ct, town.Id);
        if (exists.IsSuccess && Convert.ToInt32(exists.Value) == 0)
        {
            var sql = @"INSERT INTO nc_regions_towns (id, county_id, town_name, town_code, town_type, sort_order, is_active)
                        VALUES ($1, $2, $3, $4, $5, $6, $7)";
            await _dbService.ExecuteNonQueryAsync(sql, ct,
                town.Id, town.CountyId, town.TownName, town.TownCode,
                town.TownType, town.SortOrder, town.IsActive);
        }
    }

    #endregion

    #region YAML反序列化模型

    private class RegionCountyYaml
    {
        public string Database { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public List<RegionCountyTable> Tables { get; set; } = new();
    }

    private class RegionCountyTable
    {
        public string Name { get; set; } = string.Empty;
        public List<RegionCountyData> Data { get; set; } = new();
    }

    private class RegionCountyData
    {
        public int Id { get; set; }
        public string CountyName { get; set; } = string.Empty;
        public string CountyCode { get; set; } = string.Empty;
        public string CityName { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }

    private class RegionTownYaml
    {
        public string Database { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public List<RegionTownTable> Tables { get; set; } = new();
    }

    private class RegionTownTable
    {
        public string Name { get; set; } = string.Empty;
        public List<RegionTownData> Data { get; set; } = new();
    }

    private class RegionTownData
    {
        public int Id { get; set; }
        public int CountyId { get; set; }
        public string TownName { get; set; } = string.Empty;
        public string TownCode { get; set; } = string.Empty;
        public string TownType { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }

    private class RegionCityYaml
    {
        public string Database { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public List<RegionCityTable> Tables { get; set; } = new();
    }

    private class RegionCityTable
    {
        public string Name { get; set; } = string.Empty;
        public List<RegionCityData> Data { get; set; } = new();
    }

    private class RegionCityData
    {
        public int Id { get; set; }
        public string CityName { get; set; } = string.Empty;
        public string CityCode { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }

    #endregion
}
