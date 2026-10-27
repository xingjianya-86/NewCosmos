using System.Collections.Concurrent;
using System.Reflection;
using System.Text;
using System.Text.Json;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Options;
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
    // "查无此村"负缓存：查询成功但 0 行时记一次，避免同一编码反复打库；
    // 与正缓存分开存放——查询**失败**两者都不写，否则会被 null 永久污染（见 GetVillageByCodeAsync）。
    private readonly ConcurrentDictionary<string, bool> _villageCodeMissCache = new();

    // 地区数据基本静态，按查询维度缓存，任何写操作（增删改/清空/初始化/同步）整体失效。
    private volatile List<RegionCity>? _citiesCache;
    private volatile List<RegionCounty>? _allCountiesCache;
    private readonly ConcurrentDictionary<string, List<RegionCounty>> _countiesByCityCache = new();
    private readonly ConcurrentDictionary<int, List<RegionTown>> _townsByCountyCache = new();
    private readonly ConcurrentDictionary<int, List<RegionVillage>> _villagesByTownCache = new();

    /// <summary>
    /// 本地是否持有**全量**村数据（决定「按 id/code/name 全局扫描」能否直接返回内存结果）。
    /// v2 起村不进快照、只按 town_id 懒加载 → 此标志恒为 false，全局查找必须走 DB
    /// （否则部分缓存会被误当全量：漏数据，甚至把存在但未缓存的村写成"查无此村"负缓存）。
    /// 若将来恢复全量村快照，加载成功处置 true 即可让以下分支重新生效。
    /// </summary>
    private volatile bool _villagesFullSnapshot;

    // ===== 本地优先区划快照（2026-10）=====
    // 区划是静态参照数据：查询入口先加载本地快照（含真实主键，毫秒级、离线可用）填充上述缓存，
    // 随后触发一次后台 DB 刷新（DB 为权威，成功后回写快照，stale-while-revalidate）。
    // 快照缺失/损坏且 DB 不可达时，行为不劣于改造前——回落原有 DB 查询路径。
    private readonly object _localCacheLock = new();
    private volatile bool _localCacheTried;
    private int _refreshState;              // 0=未执行 1=进行中 2=成功 3=失败（节流重试）
    private long _lastRefreshTicks;         // 失败重试节流基准（TickCount64，10 秒间隔）
    private string? _loadedSnapshotSignature;
    private readonly string _snapshotPath = string.Empty;

    private void InvalidateRegionCaches()
    {
        _citiesCache = null;
        _allCountiesCache = null;
        _countiesByCityCache.Clear();
        _townsByCountyCache.Clear();
        _villagesByTownCache.Clear();
        _villageCodeCache.Clear();
        _villageCodeMissCache.Clear();
        _villagesFullSnapshot = false;   // 全量村标志随缓存一起作废
        LogDebug("地区缓存已清除");

        // 数据已重建/清空：本地快照同步作废，防止下次启动加载陈旧数据
        _localCacheTried = false;
        _loadedSnapshotSignature = null;
        Interlocked.Exchange(ref _refreshState, 0);
        try
        {
            if (!string.IsNullOrEmpty(_snapshotPath) && File.Exists(_snapshotPath))
                File.Delete(_snapshotPath);
        }
        catch (Exception ex)
        {
            LogWarn($"地区本地快照删除失败: {ex.Message}");
        }
    }

    #region 查询收尾（失败如实上抛 · AGENTS §7）

    /// <summary>
    /// 列表查询收尾：**查询失败如实上抛**，不再折叠成 <c>Success(空列表)</c>。
    /// 历史实现让调用方无法区分"该级没有数据"与"查询失败"，表现是区划 Picker 无提示地空着、
    /// 日志零痕迹，且 36 处调用点早已写好的 <c>IsFailure</c> 分支从来没被触发过。
    /// 查询成功但 0 行仍返回 <c>Success(空)</c> —— 那是"没有数据"，不是错误。
    /// </summary>
    private Result<List<T>> CompleteList<T>(Result<List<T>> result, string what)
    {
        if (result.IsFailure)
        {
            LogWarn($"{what}查询失败（不再折叠为空结果）: {result.ErrorCode} {result.Message}");
            return result;
        }
        return Result.Success(result.Value ?? new List<T>());
    }

    /// <summary>
    /// 单条查询收尾：**查询失败如实上抛**，不再折叠成 <c>Success(null)</c>。
    /// 查询成功但 0 行仍是 <c>Success(null)</c>＝未找到，与失败是两回事。
    /// </summary>
    private Result<T> CompleteSingle<T>(Result<T> result, string what)
    {
        if (result.IsFailure)
            LogWarn($"{what}查询失败（不再折叠为「未找到」）: {result.ErrorCode} {result.Message}");
        return result;
    }

    #endregion

    public RegionService(
        IDatabaseService dbService,
        ISchemaSyncService schemaSync,
        ISeedMergeService seedMerge,
        StorageOptions storageOptions,
        ILoggerService logger) : base(logger)
    {
        _dbService = dbService;
        _schemaSync = schemaSync;
        _seedMerge = seedMerge;
        _snapshotPath = ResolveSnapshotPath(storageOptions);
    }

    /// <summary>
    /// 本地快照与日志同目录（随 StorageOptions 的 AppData/程序目录策略走，避免安装目录不可写时写失败）。
    /// </summary>
    private static string ResolveSnapshotPath(StorageOptions storageOptions)
    {
        try
        {
            var logDir = Path.GetDirectoryName(storageOptions.GetLogPath());
            if (string.IsNullOrEmpty(logDir))
                logDir = AppContext.BaseDirectory;
            return Path.Combine(logDir, "regions_cache.json");
        }
        catch
        {
            return Path.Combine(AppContext.BaseDirectory, "regions_cache.json");
        }
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

    #region 本地优先快照缓存

    private sealed class RegionLocalSnapshot
    {
        // v2：村退出快照（全国 62 万村曾把快照撑到 115MB、进程内存 +500MB），
        // 只存省市区县镇（≈4.6 万行 / 3MB）。村按 town_id 懒加载。
        // 旧 v1 文件里的 "Villages" 字段因本类已无该属性，反序列化时被直接跳过（不物化对象）。
        public int Version { get; set; } = 2;

        /// <summary>区划指纹（DB 标量聚合算出，count/max(id)/名称长度和），刷新前先比它决定要不要拉行数据。</summary>
        public string Signature { get; set; } = string.Empty;

        public List<RegionCity> Cities { get; set; } = new();
        public List<RegionCounty> Counties { get; set; } = new();
        public List<RegionTown> Towns { get; set; } = new();
    }

    /// <summary>
    /// 本地优先入口：所有表单相关查询方法在查缓存/查库之前调用。
    /// 首次调用加载本地快照（毫秒级、离线可用），并触发一次后台 DB 刷新（DB 权威，成功后回写快照）。
    /// </summary>
    private void EnsureLocalCacheLoaded()
    {
        if (!_localCacheTried)
        {
            lock (_localCacheLock)
            {
                if (!_localCacheTried)
                {
                    TryLoadLocalSnapshot();
                    _localCacheTried = true;
                }
            }
        }
        TryStartBackgroundRefresh();
    }

    /// <summary>
    /// 判定是否为 v1 旧快照（含全量村、体积 ~115MB）：读文件头一小段看 Version 字段，
    /// 不整读文件（否则淘汰逻辑本身又把 115MB 读一遍）。
    /// </summary>
    private static bool IsLegacySnapshot(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (info.Length > 8 * 1024 * 1024) return true;   // v2 轻量快照约 3MB，超 8MB 视为旧版

            Span<char> head = stackalloc char[256];
            using var reader = new StreamReader(path, Encoding.UTF8);
            var read = reader.Read(head);
            var text = new string(head[..read]);
            return text.Contains("\"Version\":1", StringComparison.Ordinal);
        }
        catch
        {
            return false;   // 判不了就按新格式读，读失败走原有 catch 分支
        }
    }

    private void TryLoadLocalSnapshot()
    {        try
        {
            if (!File.Exists(_snapshotPath))
            {
                LogDebug("地区本地快照不存在，走 DB 查询并在后台刷新成功后生成");
                return;
            }

            // v1 旧快照（含 62 万村，约 115MB）识别与淘汰：
            // 反序列化会把整个 115MB 读进内存再跳过 Villages 属性，代价与改造前无异；
            // 而它的 Signature 是旧算法，与新 DB 指纹必不相等 → 迟早要重写。直接删掉走 DB 更省。
            if (IsLegacySnapshot(_snapshotPath))
            {
                try
                {
                    File.Delete(_snapshotPath);
                    _loadedSnapshotSignature = null;
                    LogInfo("检测到 v1 旧区划快照（含全量村），已删除并改走 DB（下次生成 v2 轻量快照）");
                }
                catch (Exception ex)
                {
                    LogWarn($"删除 v1 旧快照失败（继续读取，指纹不一致会重写）: {ex.Message}");
                }
                return;
            }

            var json = File.ReadAllText(_snapshotPath);
            var snap = JsonSerializer.Deserialize<RegionLocalSnapshot>(json);
            if (snap == null) return;

            if (snap.Cities.Count > 0)
                _citiesCache = snap.Cities;

            if (snap.Counties.Count > 0)
            {
                _allCountiesCache = snap.Counties;
                foreach (var g in snap.Counties.Where(c => !string.IsNullOrEmpty(c.CityName)).GroupBy(c => c.CityName))
                    _countiesByCityCache[g.Key] = g.OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToList();
            }

            if (snap.Towns.Count > 0)
            {
                foreach (var g in snap.Towns.GroupBy(t => t.CountyId))
                    _townsByCountyCache[g.Key] = g.OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToList();
            }

            // v2：村不进快照，_villagesByTownCache 保持为空 → 村查询按 town_id 懒加载（见 GetVillagesByTownIdAsync）
            _villagesFullSnapshot = false;

            _loadedSnapshotSignature = snap.Signature;
            LogInfo($"地区本地快照已加载: 市{snap.Cities.Count} 县{snap.Counties.Count} 镇{snap.Towns.Count}（村按需加载）");
        }
        catch (Exception ex)
        {
            LogWarn($"地区本地快照加载失败（回退 DB 查询）: {ex.Message}");
            try
            {
                if (File.Exists(_snapshotPath)) File.Delete(_snapshotPath);
            }
            catch { /* 删除失败不影响运行，后台刷新成功后会覆盖 */ }
        }
    }

    private void TryStartBackgroundRefresh()
    {
        if (Interlocked.CompareExchange(ref _refreshState, 1, 0) != 0)
        {
            // 只有失败态允许节流重试；进行中/已成功直接返回
            if (Volatile.Read(ref _refreshState) != 3) return;
            var last = Interlocked.Read(ref _lastRefreshTicks);
            if (Environment.TickCount64 - last < 10_000) return;
            if (Interlocked.CompareExchange(ref _refreshState, 1, 3) != 3) return;
        }

        _ = Task.Run(async () =>
        {
            var ok = false;
            try
            {
                ok = await RefreshCachesFromDbAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                LogWarn($"地区后台刷新异常（保留本地数据）: {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _refreshState, ok ? 2 : 3);
                if (!ok)
                    Interlocked.Exchange(ref _lastRefreshTicks, Environment.TickCount64);
            }
        });
    }

    /// <summary>
    /// 后台从 DB 刷新区划缓存并回写本地快照。
    /// **P2 指纹预判**：先用一条标量聚合 SQL 算 DB 指纹，与快照内指纹一致 → 跳过全量 SELECT
    /// （正常启动四表数据没变，省掉几万行传输与反序列化）。
    /// **P1 不拉村**：村（62 万行）改为按 town_id 懒加载，本方法只拉省市区县镇（≈4.6 万行）。
    /// 指纹含村的 count/max/名称长度 → 村有增删改名同样会触发刷新并清空村缓存。
    /// </summary>
    private async Task<bool> RefreshCachesFromDbAsync(CancellationToken ct)
    {
        // 无快照（首次运行）才做表检查：DB 不可达时避免 SyncSchema 的 DDL 噪音；有快照说明表曾存在
        if (_loadedSnapshotSignature == null)
        {
            var ensure = await EnsureTablesExistAsync(ct);
            if (ensure.IsFailure)
            {
                LogWarn($"地区后台刷新: 地区表检查失败（保留本地数据）: {ensure.Message}");
                return false;
            }
        }

        // ① 标量指纹预判（一行一列，替代四表全量 SELECT）
        var fingerprint = await ComputeDbFingerprintAsync(ct);
        if (fingerprint.IsFailure)
        {
            LogWarn($"地区后台刷新: 指纹计算失败（保留本地数据）: {fingerprint.Message}");
            return false;
        }

        if (string.Equals(fingerprint.Value, _loadedSnapshotSignature, StringComparison.Ordinal)
            && _citiesCache is not null && _allCountiesCache is not null
            && !_countiesByCityCache.IsEmpty && !_townsByCountyCache.IsEmpty)
        {
            LogDebug("地区数据与快照指纹一致，跳过全量刷新");
            return true;
        }

        // ② 指纹不一致 → 只拉省市区县镇（不拉村）
        // 明确列名（AGENTS §8）；排序与既有查询口径一致
        var cities = await _dbService.QueryAsync<RegionCity>(
            "SELECT id, city_name, city_code, sort_order, is_active FROM nc_regions_cities WHERE is_active = TRUE ORDER BY sort_order, id", ct);
        var counties = await _dbService.QueryAsync<RegionCounty>(
            "SELECT id, county_name, county_code, city_name, sort_order, is_active FROM nc_regions_counties WHERE is_active = TRUE ORDER BY sort_order, id", ct);
        var towns = await _dbService.QueryAsync<RegionTown>(
            "SELECT id, county_id, town_name, town_code, town_type, sort_order, is_active FROM nc_regions_towns WHERE is_active = TRUE ORDER BY sort_order, id", ct);

        var citiesOk = cities.IsSuccess && cities.Value is { Count: > 0 };
        var countiesOk = counties.IsSuccess && counties.Value is { Count: > 0 };
        var townsOk = towns.IsSuccess && towns.Value is { Count: > 0 };

        if (citiesOk)
            _citiesCache = cities.Value;

        if (countiesOk)
        {
            _allCountiesCache = counties.Value;
            _countiesByCityCache.Clear();
            foreach (var g in counties.Value.Where(c => !string.IsNullOrEmpty(c.CityName)).GroupBy(c => c.CityName))
                _countiesByCityCache[g.Key] = g.OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToList();
        }

        if (townsOk)
        {
            _townsByCountyCache.Clear();
            foreach (var g in towns.Value.GroupBy(t => t.CountyId))
                _townsByCountyCache[g.Key] = g.OrderBy(t => t.SortOrder).ThenBy(t => t.Id).ToList();
        }

        if (citiesOk && countiesOk && townsOk)
        {
            // 指纹变了（含村级变化）→ 已缓存的村与编码解析结果一律作废，之后按需重新取
            _villagesByTownCache.Clear();
            _villageCodeCache.Clear();
            _villageCodeMissCache.Clear();
            _villagesFullSnapshot = false;

            SaveLocalSnapshot(cities.Value!, counties.Value!, towns.Value!, fingerprint.Value!);
            LogInfo($"地区后台刷新完成(不含村): 市{cities.Value!.Count} 县{counties.Value!.Count} 镇{towns.Value!.Count}；村改为按镇懒加载");
            return true;
        }

        LogWarn($"地区后台刷新未完整成功（市:{cities.Message} 县:{counties.Message} 镇:{towns.Message}），保留本地已有数据");
        return false;
    }

    /// <summary>
    /// DB 侧区划指纹：每表 `count / max(id) / sum(length(名称))` 拼成一串（仅一行一列）。
    /// 口径与快照里存的 <see cref="RegionLocalSnapshot.Signature"/> 一致——快照侧直接存本方法的结果，
    /// 因此两者可直接做相等比较（旧实现由 C# 端从已加载行算签名，村退出快照后算不出来了）。
    /// </summary>
    private async Task<Result<string>> ComputeDbFingerprintAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT
    (SELECT count(*)||'/'||COALESCE(max(id),0)||'/'||COALESCE(sum(length(city_name)),0)    FROM nc_regions_cities)
 || '|' ||
    (SELECT count(*)||'/'||COALESCE(max(id),0)||'/'||COALESCE(sum(length(county_name)),0)  FROM nc_regions_counties)
 || '|' ||
    (SELECT count(*)||'/'||COALESCE(max(id),0)||'/'||COALESCE(sum(length(town_name)),0)    FROM nc_regions_towns)
 || '|' ||
    (SELECT count(*)||'/'||COALESCE(max(id),0)||'/'||COALESCE(sum(length(village_name)),0) FROM nc_regions_villages)";

        var result = await _dbService.QuerySingleAsync<string>(sql, ct);
        if (result.IsFailure)
            return Result.Failure<string>(result.ErrorCode!, result.Message!);
        if (string.IsNullOrWhiteSpace(result.Value))
            return Result.Failure<string>(ErrorCodes.DB_QUERY_ERROR, "区划指纹为空");
        return Result.Success(result.Value!);
    }

    private void SaveLocalSnapshot(List<RegionCity> cities, List<RegionCounty> counties, List<RegionTown> towns, string fingerprint)
    {
        try
        {
            if (string.Equals(fingerprint, _loadedSnapshotSignature, StringComparison.Ordinal))
            {
                LogDebug("地区数据与本地快照一致，跳过写入");
                return;
            }

            var snapshot = new RegionLocalSnapshot
            {
                Signature = fingerprint,
                Cities = cities,
                Counties = counties,
                Towns = towns,
            };

            var dir = Path.GetDirectoryName(_snapshotPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            var tmpPath = _snapshotPath + ".tmp";
            File.WriteAllText(tmpPath, JsonSerializer.Serialize(snapshot));
            File.Move(tmpPath, _snapshotPath, true);
            _loadedSnapshotSignature = fingerprint;
            LogInfo("地区本地快照已写入");
        }
        catch (Exception ex)
        {
            LogWarn($"地区本地快照写入失败（不影响运行）: {ex.Message}");
        }
    }

    #endregion

    #region Schema管理

    private static bool _tablesVerified;

    /// <summary>
    /// 建地区取号序列（全部写死字面量，不拼接任何标识符——AGENTS §1，动态表名须过 TableNameValidator）。
    /// </summary>
    private static readonly string[] EnsureSequenceSql =
    {
        "CREATE SEQUENCE IF NOT EXISTS seq_nc_regions_cities",
        "CREATE SEQUENCE IF NOT EXISTS seq_nc_regions_counties",
        "CREATE SEQUENCE IF NOT EXISTS seq_nc_regions_towns",
        "CREATE SEQUENCE IF NOT EXISTS seq_nc_regions_villages",
    };

    /// <summary>
    /// 地区表缺失索引（同样写死字面量、幂等）。
    /// 为什么不只写进 YAML：YAML 里的索引只在「数据库管理 → 初始化数据库结构」
    /// （SchemaService.InitializeAllTablesAsync）里补建，而那个方法**没有任何自动调用方**——
    /// 靠它漏建的话，约 1 万行的村表按编码查会一直退化成顺序扫描。
    /// 与 regions/database.yaml 中的同名索引重复声明无害：两处都是 IF NOT EXISTS。
    /// </summary>
    private static readonly string[] EnsureIndexSql =
    {
        // 地址解析热路径：AddressResolver.GetVillageByCodeAsync / GetVillagesByCodesAsync
        "CREATE INDEX IF NOT EXISTS idx_villages_village_code ON nc_regions_villages (village_code)",
    };

    /// <summary>
    /// 序列对齐：setval(GREATEST(表内最大 ID, 序列已发到的值, 基线), true) → 下一个取号 = 该值 + 1。
    /// GREATEST 保证**只前进不回退**，不会把已发号的位置让回去导致主键冲突；
    /// 基线与历史 MAX(id)+1 的兜底值一致（县 9000 / 镇 90000 / 村 900000）。
    /// </summary>
    private static readonly string[] SyncSequenceSql =
    {
        @"SELECT setval('seq_nc_regions_cities', GREATEST(
              COALESCE((SELECT MAX(id) FROM nc_regions_cities), 0),
              COALESCE((SELECT last_value FROM pg_sequences WHERE schemaname = 'public' AND sequencename = 'seq_nc_regions_cities'), 0),
              0))",
        @"SELECT setval('seq_nc_regions_counties', GREATEST(
              COALESCE((SELECT MAX(id) FROM nc_regions_counties), 0),
              COALESCE((SELECT last_value FROM pg_sequences WHERE schemaname = 'public' AND sequencename = 'seq_nc_regions_counties'), 0),
              9000))",
        @"SELECT setval('seq_nc_regions_towns', GREATEST(
              COALESCE((SELECT MAX(id) FROM nc_regions_towns), 0),
              COALESCE((SELECT last_value FROM pg_sequences WHERE schemaname = 'public' AND sequencename = 'seq_nc_regions_towns'), 0),
              90000))",
        @"SELECT setval('seq_nc_regions_villages', GREATEST(
              COALESCE((SELECT MAX(id) FROM nc_regions_villages), 0),
              COALESCE((SELECT last_value FROM pg_sequences WHERE schemaname = 'public' AND sequencename = 'seq_nc_regions_villages'), 0),
              900000))",
    };

    private static bool _regionObjectsVerified;

    /// <summary>
    /// 建索引 + 建序列 + 对齐（每个进程只跑一次）。
    /// **故意不设列 DEFAULT**：所有 INSERT 仍显式传 id，序列只承担"下一个 ID"的分配，
    /// 种子/批量导入的既定 ID 区间因此完全不受影响，也不必改任何 INSERT。
    /// **失败不影响读取**：索引/序列都只是"查得快不快"和"新建地区能不能取号"的事，
    /// 建不起来只让对应功能失败并给出明确错误，不能把整个地区子系统（Picker/回显）一起拖死。
    /// </summary>
    private async Task<Result> EnsureRegionObjectsAsync(CancellationToken ct)
    {
        if (_regionObjectsVerified) return Result.Success();
        _regionObjectsVerified = true;   // 无论成败只尝试一次，避免每次查询都重复打 DDL

        foreach (var sql in EnsureIndexSql)
        {
            var r = await _dbService.ExecuteNonQueryAsync(sql, ct);
            if (r.IsFailure)
            {
                LogError($"建地区索引失败（读取不受影响，仅按编码查村会走顺序扫描）: {r.ErrorCode} {r.Message}");
                return Result.Success();
            }
        }

        foreach (var sql in EnsureSequenceSql)
        {
            var r = await _dbService.ExecuteNonQueryAsync(sql, ct);
            if (r.IsFailure)
            {
                LogError($"建地区主键序列失败（读取不受影响，仅新建地区会失败）: {r.ErrorCode} {r.Message}");
                return Result.Success();
            }
        }

        var sync = await SyncRegionSequencesAsync(ct);
        if (sync.IsFailure)
        {
            LogError($"对齐地区主键序列失败（读取不受影响，仅新建地区会失败）: {sync.Message}");
            return Result.Success();
        }

        LogInfo("地区索引与主键序列已就绪");
        return Result.Success();
    }

    /// <summary>
    /// 把四条序列对齐到各自表的 max(id)。**必须**在两类操作之后调用：
    /// ① 用显式 ID 的种子/批量写入 ② TRUNCATE。
    /// </summary>
    private async Task<Result> SyncRegionSequencesAsync(CancellationToken ct)
    {
        foreach (var sql in SyncSequenceSql)
        {
            var r = await _dbService.ExecuteScalarAsync(sql, ct);
            if (r.IsFailure)
            {
                LogWarn($"同步地区主键序列失败: {r.ErrorCode} {r.Message}");
                return Result.Failure(r.ErrorCode!, r.Message!);
            }
        }
        return Result.Success();
    }

    public async Task<Result> EnsureTablesExistAsync(CancellationToken ct = default)
    {
        if (_tablesVerified)
        {
            LogInfo("地区表已验证（缓存）");
            return await EnsureRegionObjectsAsync(ct);
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
            return await EnsureRegionObjectsAsync(ct);
        }

        LogInfo("地区表不完整，开始创建");
        var syncResult = await SyncSchemaAsync(null, ct);
        if (syncResult.IsFailure)
            return syncResult;
        // 整表重建（allowDrop）会把数据清空，序列必须重新对齐
        _regionObjectsVerified = false;
        return await EnsureRegionObjectsAsync(ct);
    }

    public async Task<Result> SyncSchemaAsync(IProgress<ProgressContext> progress = null, CancellationToken ct = default)
    {
        // 表可能被整表重建（allowDrop: true），序列对齐状态失效，下面会重新对齐
        _regionObjectsVerified = false;

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
            var citiesResult = LoadCitiesFromYaml();
            if (citiesResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                LogError("加载地级市数据失败，初始化已回滚");
                return citiesResult;
            }
            foreach (var city in citiesResult.Value)
            {
                await InsertCityAsync(city, ct);
            }
            ReportProgress(progress, 4, totalSteps, "清空县区表");
            Logger.LogSecurity("清空表(TRUNCATE)", ("Table", "nc_regions_counties"), ("Operation", "RegionService.InitializeFromSeed"));
            await _dbService.ExecuteNonQueryAsync("TRUNCATE TABLE nc_regions_counties", ct);
            ReportProgress(progress, 5, totalSteps, "导入县区数据");
            var countiesResult = LoadCountiesFromYaml();
            if (countiesResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                LogError("加载县区数据失败");
                return countiesResult;
            }
            var counties = countiesResult.Value;
            foreach (var county in counties)
            {
                await InsertCountyAsync(county, ct);
            }
            ReportProgress(progress, 6, totalSteps, "清空乡镇表");
            Logger.LogSecurity("清空表(TRUNCATE)", ("Table", "nc_regions_towns"), ("Operation", "RegionService.InitializeFromSeed"));
            await _dbService.ExecuteNonQueryAsync("TRUNCATE TABLE nc_regions_towns", ct);
            ReportProgress(progress, 7, totalSteps, "导入乡镇数据");
            var townsResult = LoadTownsFromYaml();
            if (townsResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                LogError("加载乡镇数据失败，初始化已回滚");
                return townsResult;
            }
            var towns = townsResult.Value;
            foreach (var town in towns)
            {
                await InsertTownAsync(town, ct);
            }
            ReportProgress(progress, 8, totalSteps, "清空村/社区表");
            Logger.LogSecurity("清空表(TRUNCATE)", ("Table", "nc_regions_villages"), ("Operation", "RegionService.InitializeFromSeed"));
            await _dbService.ExecuteNonQueryAsync("TRUNCATE TABLE nc_regions_villages", ct);
            ReportProgress(progress, 9, totalSteps, "导入村/社区数据");
            var villagesResult = LoadVillagesFromCsv();
            if (villagesResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                LogError("加载村/社区数据失败，初始化已回滚");
                return villagesResult;
            }
            var villagesCountResult = await BulkInsertVillagesAsync(villagesResult.Value, ct);
            if (villagesCountResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                LogError("导入村/社区数据失败，初始化已回滚");
                return Result.Failure(villagesCountResult.ErrorCode!, villagesCountResult.Message!);
            }
            var villagesCount = villagesCountResult.Value;
            ReportProgress(progress, 10, totalSteps, "提交事务");
            await tx.CommitAsync(ct);
            InvalidateRegionCaches();

            // 种子与批量导入用的是显式 ID，序列必须重新对齐到 max(id)，否则后续新建会撞主键
            var seqResult = await SyncRegionSequencesAsync(ct);
            if (seqResult.IsFailure)
            {
                _regionObjectsVerified = false;   // 下次进地区页重试
                LogWarn($"种子初始化后同步主键序列失败: {seqResult.Message}");
            }
            LogInfo($"地区数据初始化完成: 县区{counties.Count}个, 乡镇{towns.Count}个, 村/社区{villagesCount}个");
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

            // TRUNCATE 后 max(id) 归零，序列必须重新对齐，否则将来重新种入的 ID 会与取号撞车
            var seqResult = await SyncRegionSequencesAsync(ct);
            if (seqResult.IsFailure)
            {
                _regionObjectsVerified = false;
                LogWarn($"清空地区表后同步主键序列失败: {seqResult.Message}");
            }

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
        EnsureLocalCacheLoaded();

        var cached = _citiesCache;
        if (cached != null)
        {
            LogDebug("从缓存获取地级市列表");
            return Result.Success(new List<RegionCity>(cached));
        }

        await EnsureTablesExistAsync(ct);

        LogInfo("获取所有地级市");

        var sql = "SELECT id, city_name, city_code, province_id, sort_order, is_active FROM nc_regions_cities WHERE is_active = TRUE ORDER BY sort_order, id";
        var result = await _dbService.QueryAsync<RegionCity>(sql, ct);

        if (result.IsSuccess && result.Value is not null)
        {
            _citiesCache = result.Value;
            return Result.Success(new List<RegionCity>(result.Value));
        }

        return CompleteList(result, "地级市列表");
    }

    public async Task<Result<RegionCity>> GetCityByIdAsync(int id, CancellationToken ct = default)
    {
        EnsureLocalCacheLoaded();
        var local = _citiesCache;
        if (local != null)
        {
            var hit = local.FirstOrDefault(c => c.Id == id);
            if (hit != null) return Result.Success(hit);
        }

        await EnsureTablesExistAsync(ct);

        LogInfo($"获取地级市: Id={id}");

        var sql = "SELECT id, city_name, city_code, province_id, sort_order, is_active FROM nc_regions_cities WHERE id = $1";
        var result = await _dbService.QuerySingleAsync<RegionCity>(sql, ct, id);

        return CompleteSingle(result, "地级市");
    }

    public async Task<Result<RegionCity>> GetCityByNameAsync(string cityName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cityName))
            return Result.Failure<RegionCity>(ErrorCodes.VALIDATION_FAILED, "地级市名称不能为空");

        EnsureLocalCacheLoaded();
        var local = _citiesCache;
        if (local != null)
        {
            var hit = local.FirstOrDefault(c => string.Equals(c.CityName, cityName, StringComparison.Ordinal));
            if (hit != null) return Result.Success(hit);
        }

        await EnsureTablesExistAsync(ct);

        var sql = "SELECT id, city_name, city_code, province_id, sort_order, is_active FROM nc_regions_cities WHERE city_name = $1";
        var result = await _dbService.QuerySingleAsync<RegionCity>(sql, ct, cityName);

        return CompleteSingle(result, "地级市");
    }

    /// <summary>
    /// 省级行政区列表（2026-10-26 真级联省）。
    /// 与市/县不同：省表由 docs/migrations/20261026_regions_provinces.sql 建表并回填，不进
    /// SyncSchemaAsync 的 allowDrop 表数组（那个流程会整表重建清数据）。
    /// </summary>
    public async Task<Result<List<RegionProvince>>> GetProvincesAsync(CancellationToken ct = default)
    {
        await EnsureTablesExistAsync(ct);

        LogInfo("获取省级行政区列表");

        var sql = "SELECT id, province_name, province_code, sort_order, is_active FROM nc_regions_provinces WHERE is_active = TRUE ORDER BY sort_order, id";
        var result = await _dbService.QueryAsync<RegionProvince>(sql, ct);

        if (result.IsSuccess && result.Value is not null)
            return Result.Success(new List<RegionProvince>(result.Value));

        // 省表缺失（迁移未跑）时显式失败，不洗成"无省数据"
        return CompleteList(result, "省级行政区列表");
    }

    /// <summary>按省筛地级市（选省 → 筛市 级联）。省ID异常 → 空列表，不当错误。</summary>
    public async Task<Result<List<RegionCity>>> GetCitiesByProvinceIdAsync(int provinceId, CancellationToken ct = default)
    {
        if (provinceId <= 0)
            return Result.Success(new List<RegionCity>());

        await EnsureTablesExistAsync(ct);

        LogInfo($"按省获取地级市: ProvinceId={provinceId}");

        var sql = "SELECT id, city_name, city_code, province_id, sort_order, is_active FROM nc_regions_cities WHERE is_active = TRUE AND province_id = $1 ORDER BY sort_order, id";
        // $1 必须绑 provinceId：漏参会让 Npgsql 抛 08P01（bind supplies 0 parameters），
        // 表现是"选省后城市全空"（AGENTS §8：位置参数 $1..$n 与 SQL 一一对应）
        var result = await _dbService.QueryAsync<RegionCity>(sql, ct, provinceId);

        if (result.IsSuccess && result.Value is not null)
            return Result.Success(new List<RegionCity>(result.Value));

        return CompleteList(result, "省内地级市列表");
    }

    #endregion

    #region 县区查询

    public async Task<Result<List<RegionCounty>>> GetCountiesAsync(CancellationToken ct = default)
    {
        EnsureLocalCacheLoaded();

        var cached = _allCountiesCache;
        if (cached != null)
        {
            LogDebug("从缓存获取县区列表");
            return Result.Success(new List<RegionCounty>(cached));
        }

        await EnsureTablesExistAsync(ct);

        LogInfo("获取所有县区");

        var sql = "SELECT id, county_name, county_code, city_name, sort_order, is_active FROM nc_regions_counties WHERE is_active = TRUE ORDER BY sort_order, id";
        var result = await _dbService.QueryAsync<RegionCounty>(sql, ct);

        if (result.IsSuccess && result.Value is not null)
        {
            _allCountiesCache = result.Value;
            return Result.Success(new List<RegionCounty>(result.Value));
        }

        return CompleteList(result, "县区列表");
    }

    public async Task<Result<List<RegionCounty>>> GetCountiesByCityAsync(string cityName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cityName))
            return Result.Failure<List<RegionCounty>>(ErrorCodes.VALIDATION_FAILED, "地级市名称不能为空");

        EnsureLocalCacheLoaded();

        if (_countiesByCityCache.TryGetValue(cityName, out var cached))
        {
            LogDebug($"从缓存获取县区: CityName={cityName}");
            return Result.Success(new List<RegionCounty>(cached));
        }

        await EnsureTablesExistAsync(ct);

        LogInfo($"获取县区: CityName={cityName}");

        var sql = "SELECT id, county_name, county_code, city_name, sort_order, is_active FROM nc_regions_counties WHERE city_name = $1 AND is_active = TRUE ORDER BY sort_order, id";
        var result = await _dbService.QueryAsync<RegionCounty>(sql, ct, cityName);

        if (result.IsSuccess && result.Value is not null)
        {
            _countiesByCityCache[cityName] = result.Value;
            return Result.Success(new List<RegionCounty>(result.Value));
        }

        return CompleteList(result, "县/区列表");
    }

    public async Task<Result<List<RegionCounty>>> GetCountiesByCityIdAsync(int cityId, CancellationToken ct = default)
    {
        EnsureLocalCacheLoaded();

        var cityResult = await GetCityByIdAsync(cityId, ct);
        if (cityResult.IsFailure)
            return Result.Failure<List<RegionCounty>>(cityResult.ErrorCode!, cityResult.Message!);
        if (cityResult.Value == null)
            return Result.Failure<List<RegionCounty>>(ErrorCodes.REGION_CITY_NOT_FOUND, $"地级市未找到: Id={cityId}");

        return await GetCountiesByCityAsync(cityResult.Value.CityName, ct);
    }

    public async Task<Result<RegionCounty>> GetCountyByIdAsync(int id, CancellationToken ct = default)
    {
        EnsureLocalCacheLoaded();
        var local = _allCountiesCache;
        if (local != null)
        {
            var hit = local.FirstOrDefault(c => c.Id == id);
            if (hit != null) return Result.Success(hit);
        }

        await EnsureTablesExistAsync(ct);

        var sql = "SELECT id, county_name, county_code, city_name, sort_order, is_active FROM nc_regions_counties WHERE id = $1";
        var result = await _dbService.QuerySingleAsync<RegionCounty>(sql, ct, id);

        return CompleteSingle(result, "县/区");
    }

    public async Task<Result<RegionCounty>> GetCountyByCodeAsync(string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Result.Failure<RegionCounty>(ErrorCodes.VALIDATION_FAILED, "县区编码不能为空");

        EnsureLocalCacheLoaded();
        var local = _allCountiesCache;
        if (local != null)
        {
            var hit = local.FirstOrDefault(c => string.Equals(c.CountyCode, code, StringComparison.Ordinal));
            if (hit != null) return Result.Success(hit);
        }

        await EnsureTablesExistAsync(ct);

        var sql = "SELECT id, county_name, county_code, city_name, sort_order, is_active FROM nc_regions_counties WHERE county_code = $1 AND is_active = true";
        var result = await _dbService.QuerySingleAsync<RegionCounty>(sql, ct, code);

        return CompleteSingle(result, "县/区");
    }

    #endregion

    #region 乡镇查询

    public async Task<Result<List<RegionTown>>> GetTownsByCountyIdAsync(int countyId, CancellationToken ct = default)
    {
        EnsureLocalCacheLoaded();

        if (_townsByCountyCache.TryGetValue(countyId, out var cached))
        {
            LogDebug($"从缓存获取乡镇: CountyId={countyId}");
            return Result.Success(new List<RegionTown>(cached));
        }

        await EnsureTablesExistAsync(ct);

        LogInfo($"获取乡镇: CountyId={countyId}");

        var sql = "SELECT id, county_id, town_name, town_code, town_type, sort_order, is_active FROM nc_regions_towns WHERE county_id = $1 AND is_active = TRUE ORDER BY sort_order, id";
        var result = await _dbService.QueryAsync<RegionTown>(sql, ct, countyId);

        if (result.IsSuccess && result.Value is not null)
        {
            _townsByCountyCache[countyId] = result.Value;
            return Result.Success(new List<RegionTown>(result.Value));
        }

        return CompleteList(result, "乡镇/街道列表");
    }

    public async Task<Result<RegionTown>> GetTownByIdAsync(int id, CancellationToken ct = default)
    {
        EnsureLocalCacheLoaded();
        if (!_townsByCountyCache.IsEmpty)
        {
            var hit = _townsByCountyCache.Values.SelectMany(v => v).FirstOrDefault(t => t.Id == id);
            if (hit != null) return Result.Success(hit);
        }

        await EnsureTablesExistAsync(ct);

        var sql = "SELECT id, county_id, town_name, town_code, town_type, sort_order, is_active FROM nc_regions_towns WHERE id = $1";
        var result = await _dbService.QuerySingleAsync<RegionTown>(sql, ct, id);

        return CompleteSingle(result, "乡镇/街道");
    }

    public async Task<Result<List<RegionTown>>> SearchTownsAsync(string keyword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return Result.Failure<List<RegionTown>>(ErrorCodes.VALIDATION_FAILED, "搜索关键词不能为空");

        EnsureLocalCacheLoaded();
        if (!_townsByCountyCache.IsEmpty)
        {
            // 本地已有全量乡镇数据：直接内存检索（离线可用），命中口径与 LIKE '%kw%' 等价
            var localHits = _townsByCountyCache.Values
                .SelectMany(v => v)
                .Where(t => !string.IsNullOrEmpty(t.TownName) && t.TownName.Contains(keyword, StringComparison.Ordinal))
                .OrderBy(t => t.SortOrder).ThenBy(t => t.Id)
                .Take(50)
                .ToList();
            return Result.Success(localHits);
        }

        await EnsureTablesExistAsync(ct);

        LogInfo($"搜索乡镇: Keyword={keyword}");

        var sql = "SELECT id, county_id, town_name, town_code, town_type, sort_order, is_active FROM nc_regions_towns WHERE town_name LIKE $1 AND is_active = TRUE ORDER BY sort_order, id LIMIT 50";
        var result = await _dbService.QueryAsync<RegionTown>(sql, ct, $"%{keyword}%");

        return CompleteList(result, "乡镇/街道搜索");
    }

    #endregion

    #region 村/社区查询

    public async Task<Result<List<RegionVillage>>> GetVillagesByTownIdAsync(int townId, CancellationToken ct = default)
    {
        EnsureLocalCacheLoaded();

        if (_villagesByTownCache.TryGetValue(townId, out var cached))
        {
            LogDebug($"从缓存获取村/社区: TownId={townId}");
            return Result.Success(new List<RegionVillage>(cached));
        }

        await EnsureTablesExistAsync(ct);

        LogInfo($"获取村/社区: TownId={townId}");

        var sql = "SELECT id, town_id, village_name, village_code, village_type, sort_order, is_active FROM nc_regions_villages WHERE town_id = $1 AND is_active = TRUE ORDER BY sort_order, id";
        var result = await _dbService.QueryAsync<RegionVillage>(sql, ct, townId);

        if (result.IsSuccess && result.Value is not null)
        {
            _villagesByTownCache[townId] = result.Value;
            return Result.Success(new List<RegionVillage>(result.Value));
        }

        return CompleteList(result, "村/社区列表");
    }

    public async Task<Result<RegionVillage>> GetVillageByIdAsync(int id, CancellationToken ct = default)
    {
        EnsureLocalCacheLoaded();
        // 仅当本地是全量村快照才可直接内存命中；懒加载的部分缓存不能当全量用（会漏数据）
        if (_villagesFullSnapshot && !_villagesByTownCache.IsEmpty)
        {
            var hit = _villagesByTownCache.Values.SelectMany(v => v).FirstOrDefault(v => v.Id == id);
            if (hit != null) return Result.Success(hit);
        }

        await EnsureTablesExistAsync(ct);

        var sql = "SELECT id, town_id, village_name, village_code, village_type, sort_order, is_active FROM nc_regions_villages WHERE id = $1";
        var result = await _dbService.QuerySingleAsync<RegionVillage>(sql, ct, id);

        return CompleteSingle(result, "村/社区");
    }

    public async Task<Result<RegionVillage>> GetVillageByCodeAsync(string villageCode, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(villageCode))
            return Result.Failure<RegionVillage>(ErrorCodes.VALIDATION_FAILED, "村/社区编码不能为空");

        EnsureLocalCacheLoaded();

        if (_villageCodeCache.TryGetValue(villageCode, out var cached))
            return Result.Success(cached);

        if (_villageCodeMissCache.ContainsKey(villageCode))
            return Result.Success<RegionVillage>(null);

        if (_villagesFullSnapshot && !_villagesByTownCache.IsEmpty)
        {
            var local = _villagesByTownCache.Values
                .SelectMany(v => v)
                .FirstOrDefault(v => string.Equals(v.VillageCode, villageCode, StringComparison.Ordinal));
            if (local != null)
            {
                _villageCodeCache[villageCode] = local;
                return Result.Success(local);
            }
        }

        await EnsureTablesExistAsync(ct);

        var sql = "SELECT id, town_id, village_name, village_code, village_type, sort_order, is_active FROM nc_regions_villages WHERE village_code = $1 AND is_active = true";
        var result = await _dbService.QuerySingleAsync<RegionVillage>(sql, ct, villageCode);

        // 只有"查询成功"才写缓存：查询失败/会话被拒时 result.Value 为 null，
        // 一旦把 null 写进正缓存，TryGetValue 会永远返回 Success(null)，进程内该编码的
        // 村名解析（AddressResolver → 模板/打印地址）永久退化且不再重查。
        if (result.IsSuccess)
        {
            if (result.Value != null)
                _villageCodeCache[villageCode] = result.Value;
            else
                _villageCodeMissCache[villageCode] = true;   // 查无此村：负缓存
        }
        else
        {
            LogWarn($"按编码查村/社区失败（不写缓存，下次重查）: Code={villageCode}, {result.ErrorCode} {result.Message}");
        }

        // 查到→实体；查无此村→Success(null)＝未找到；查询失败→如实上抛（失败时上面已记日志）
        if (result.IsFailure)
            return Result.Failure<RegionVillage>(result.ErrorCode!, result.Message!);
        return Result.Success<RegionVillage>(result.Value!);
    }

    /// <summary>
    /// 按编码**批量**查村/社区：一次取回，替代逐条 <see cref="GetVillageByCodeAsync"/>。
    /// 供 AddressResolver 预热使用——原实现每条编码要么各打一次库（快照缺失时的 N+1），
    /// 要么全量扫一遍村表（本地快照下 N × 万级）。
    /// 命中本地缓存的直接回填；只对缺口查询，查不到的写负缓存避免重复打库。
    /// </summary>
    public async Task<Result<List<RegionVillage>>> GetVillagesByCodesAsync(
        IEnumerable<string> villageCodes, CancellationToken ct = default)
    {
        var codes = (villageCodes ?? Enumerable.Empty<string>())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => c.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (codes.Length == 0)
            return Result.Success(new List<RegionVillage>());

        EnsureLocalCacheLoaded();

        var found = new List<RegionVillage>(codes.Length);
        var missing = new List<string>(codes.Length);
        foreach (var code in codes)
        {
            if (_villageCodeCache.TryGetValue(code, out var hit)) { found.Add(hit); continue; }
            if (_villageCodeMissCache.ContainsKey(code)) continue;
            missing.Add(code);
        }

        if (missing.Count == 0)
            return Result.Success(found);

        // 仅当本地是全量村快照才可用内存索引；部分缓存下未命中的 code 必须回 DB 查，
        // 否则"没缓存到"会被误写成"查无此村"负缓存（永久污染，之后即使有数据也返回查不到）
        if (_villagesFullSnapshot && !_villagesByTownCache.IsEmpty)
        {
            var want = new HashSet<string>(missing, StringComparer.Ordinal);
            var hitMap = new Dictionary<string, RegionVillage>(StringComparer.Ordinal);
            foreach (var v in _villagesByTownCache.Values.SelectMany(x => x))
            {
                if (!string.IsNullOrEmpty(v.VillageCode) && want.Contains(v.VillageCode))
                    hitMap[v.VillageCode] = v;
            }

            foreach (var code in missing)
            {
                if (hitMap.TryGetValue(code, out var v))
                {
                    _villageCodeCache[code] = v;
                    found.Add(v);
                }
                else
                {
                    _villageCodeMissCache[code] = true;
                }
            }
            return Result.Success(found);
        }

        await EnsureTablesExistAsync(ct);

        const string sql =
            "SELECT id, town_id, village_name, village_code, village_type, sort_order, is_active "
          + "FROM nc_regions_villages WHERE village_code = ANY($1) AND is_active = true";

        // ⚠️ string[] 作为唯一实参会被 params object[] 按 normal form 摊成 $1..$N，
        //    $1 只绑到首个标量 → ANY($1) 报 42809/22P02，必须包成"1 个数组参数"。
        var result = await _dbService.QueryAsync<RegionVillage>(sql, ct, new object[] { missing.ToArray() });
        if (result.IsFailure)
            return CompleteList(result, "村/社区编码批量查询");

        var got = new HashSet<string>(StringComparer.Ordinal);
        foreach (var v in result.Value ?? new List<RegionVillage>())
        {
            if (string.IsNullOrEmpty(v.VillageCode)) continue;
            _villageCodeCache[v.VillageCode] = v;
            got.Add(v.VillageCode);
            found.Add(v);
        }
        foreach (var code in missing)
        {
            if (!got.Contains(code)) _villageCodeMissCache[code] = true;
        }

        return Result.Success(found);
    }

    public async Task<Result<List<RegionVillage>>> SearchVillagesAsync(string keyword, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(keyword))
            return Result.Failure<List<RegionVillage>>(ErrorCodes.VALIDATION_FAILED, "搜索关键词不能为空");

        EnsureLocalCacheLoaded();
        // 仅全量村快照才可内存检索；懒加载的部分缓存搜出来的结果不完整，必须回 DB LIKE 查
        if (_villagesFullSnapshot && !_villagesByTownCache.IsEmpty)
        {
            var localHits = _villagesByTownCache.Values
                .SelectMany(v => v)
                .Where(v => !string.IsNullOrEmpty(v.VillageName) && v.VillageName.Contains(keyword, StringComparison.Ordinal))
                .OrderBy(v => v.SortOrder).ThenBy(v => v.Id)
                .Take(50)
                .ToList();
            return Result.Success(localHits);
        }

        await EnsureTablesExistAsync(ct);

        LogInfo($"搜索村/社区: Keyword={keyword}");

        var sql = "SELECT id, town_id, village_name, village_code, village_type, sort_order, is_active FROM nc_regions_villages WHERE village_name LIKE $1 AND is_active = TRUE ORDER BY sort_order, id LIMIT 50";
        var result = await _dbService.QueryAsync<RegionVillage>(sql, ct, $"%{keyword}%");

        return CompleteList(result, "村/社区搜索");
    }

    #endregion

    #region 树形查询

    public async Task<Result<List<RegionCounty>>> GetCountiesWithTownsAsync(CancellationToken ct = default)
    {
        await EnsureTablesExistAsync(ct);

        LogInfo("获取县区及乡镇树形结构");

        var sql = @"
            SELECT c.id, c.county_name, c.county_code, c.city_name, c.sort_order, c.is_active FROM nc_regions_counties c
            WHERE c.is_active = TRUE 
            AND EXISTS (SELECT 1 FROM nc_regions_towns t WHERE t.county_id = c.id AND t.is_active = true)
            ORDER BY c.sort_order, c.id";
        var result = await _dbService.QueryAsync<RegionCounty>(sql, ct);

        return CompleteList(result, "县区-乡镇树形");
    }

    public async Task<Result<Dictionary<string, List<RegionTown>>>> GetAllTownsGroupedByCountyAsync(CancellationToken ct = default)
    {
        await EnsureTablesExistAsync(ct);

        LogInfo("获取按县区分组的所有乡镇");

        var sql = @"
            SELECT t.id, t.county_id, t.town_name, t.town_code, t.town_type, t.sort_order, t.is_active FROM nc_regions_towns t
            INNER JOIN nc_regions_counties c ON t.county_id = c.id
            WHERE t.is_active = TRUE AND c.is_active = TRUE
            ORDER BY c.sort_order, c.id, t.sort_order, t.id";
        var result = await _dbService.QueryAsync<RegionTown>(sql, ct);

        if (result.IsFailure)
        {
            LogWarn($"按县区分组的乡镇查询失败（不再折叠为空字典）: {result.ErrorCode} {result.Message}");
            return Result.Failure<Dictionary<string, List<RegionTown>>>(result.ErrorCode!, result.Message!);
        }

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

        // Id<=0 时走序列取号：原实现传 NULL 给 NOT NULL 主键，"新建城市"必然失败
        int cityIdArg;
        if (request.Id > 0)
        {
            cityIdArg = request.Id;
        }
        else
        {
            var nextId = await GetNextCityIdAsync(ct);
            if (nextId.IsFailure)
                return Result.Failure<RegionCity>(nextId.ErrorCode!, nextId.Message!);
            cityIdArg = nextId.Value;
        }

        var sql = @"INSERT INTO nc_regions_cities (id, city_name, city_code, sort_order, is_active)
                    VALUES ($1, $2, $3, $4, $5)
                    RETURNING id";
        var result = await _dbService.QuerySingleAsync<RegionCity>(sql, ct,
            cityIdArg,
            request.CityName, request.CityCode, request.SortOrder, request.IsActive);

        if (result.IsFailure)
            return Result.Failure<RegionCity>(result.ErrorCode!, result.Message!);
        if (result.Value == null)
            return Result.Failure<RegionCity>(ErrorCodes.DB_QUERY_ERROR, "新建地级市失败");

        InvalidateRegionCaches();
        return Result.Success(result.Value);
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

        if (result.IsFailure)
            return Result.Failure<RegionCity>(result.ErrorCode!, result.Message!);
        if (result.Value == null)
            return Result.Failure<RegionCity>(ErrorCodes.REGION_CITY_NOT_FOUND, $"地级市未找到: Id={request.Id}");

        InvalidateRegionCaches();
        return Result.Success(result.Value);
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
            var nextIdResult = await GetNextCountyIdAsync(ct);
            if (nextIdResult.IsFailure)
                return Result.Failure<RegionCounty>(nextIdResult.ErrorCode!, nextIdResult.Message!);
            var nextId = nextIdResult.Value;

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
            // 检查失败必须显式失败：不能当成"没有下级"继续停用（AGENTS §7）
            if (checkResult.IsFailure)
                return Result.Failure(checkResult.ErrorCode!, checkResult.Message!);
            if (Convert.ToInt64(checkResult.Value) > 0)
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
            var nextIdResult = await GetNextTownIdAsync(ct);
            if (nextIdResult.IsFailure)
                return Result.Failure<RegionTown>(nextIdResult.ErrorCode!, nextIdResult.Message!);
            var nextId = nextIdResult.Value;

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
            // 检查失败必须显式失败：不能当成"没有下级"继续停用（AGENTS §7）
            if (checkResult.IsFailure)
                return Result.Failure(checkResult.ErrorCode!, checkResult.Message!);
            if (Convert.ToInt64(checkResult.Value) > 0)
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
            var nextIdResult = await GetNextVillageIdAsync(ct);
            if (nextIdResult.IsFailure)
                return Result.Failure<RegionVillage>(nextIdResult.ErrorCode!, nextIdResult.Message!);
            var nextId = nextIdResult.Value;

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

    /// <summary>
    /// 取下一个主键：改用**序列**（原子），替代原来的 <c>MAX(id)+1</c>。
    /// 为什么换：MAX+1 在并发创建时两条事务会算出同一个 ID，后到的撞主键 23505；
    /// 取号失败时旧实现还只能返回兜底假 ID（90001/900001），等于拿假 ID 去 INSERT
    /// （AGENTS §7：失败不当空值/假值）。
    /// 所有 INSERT 仍**显式传 id**：种子与批量导入依赖既定 ID 区间，序列只负责"下一个 ID"。
    /// SQL 全部写死字面量（AGENTS §1），取号失败如实上抛。
    /// </summary>
    private async Task<Result<int>> GetNextCityIdAsync(CancellationToken ct)
        => await ReadNextIdAsync("SELECT nextval('seq_nc_regions_cities')", "地级市", ct);

    private async Task<Result<int>> GetNextCountyIdAsync(CancellationToken ct)
        => await ReadNextIdAsync("SELECT nextval('seq_nc_regions_counties')", "县区", ct);

    private async Task<Result<int>> GetNextTownIdAsync(CancellationToken ct)
        => await ReadNextIdAsync("SELECT nextval('seq_nc_regions_towns')", "乡镇/街道", ct);

    private async Task<Result<int>> GetNextVillageIdAsync(CancellationToken ct)
        => await ReadNextIdAsync("SELECT nextval('seq_nc_regions_villages')", "村/社区", ct);

    private async Task<Result<int>> ReadNextIdAsync(string sql, string what, CancellationToken ct)
    {
        var result = await _dbService.ExecuteScalarAsync(sql, ct);
        if (result.IsFailure)
        {
            LogWarn($"取{what}下一个主键失败（不返回兜底假ID）: {result.ErrorCode} {result.Message}");
            return Result.Failure<int>(result.ErrorCode!, result.Message!);
        }
        return Result.Success(Convert.ToInt32(result.Value));
    }

    #endregion

    #region 村/社区CSV数据加载

    private const int VILLAGE_BATCH_SIZE = 500;

    /// <summary>
    /// 加载村/社区CSV种子。
    /// 文件缺失 / 无有效行 / 读取解析异常一律返回显式 Failure——
    /// 调用方在导入前已 TRUNCATE 目标表，若把失败当"无数据"继续，会提交出空表（静默清空真实数据）。
    /// </summary>
    private Result<List<VillageCsvRecord>> LoadVillagesFromCsv()
    {
        try
        {
            var csvPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Seed", "regions", "villages.csv");
            Stream? stream;
            string source;

            if (File.Exists(csvPath))
            {
                stream = File.OpenRead(csvPath);
                source = csvPath;
                LogInfo($"从文件系统加载村/社区数据: {csvPath}");
            }
            else
            {
                // Android 不复制输出目录，CSV 以 EmbeddedResource 打进程序集（csproj android 条件项），
                // 资源名规则与 cities.yaml 等 yaml 种子一致
                var resourceName = "NewCosmos.Resources.Seed.regions.villages.csv";
                stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
                if (stream == null)
                {
                    LogError($"未找到村/社区CSV文件: {csvPath}；嵌入资源 {resourceName} 亦不存在");
                    return Result.Failure<List<VillageCsvRecord>>(
                        ErrorCodes.FILE_NOT_FOUND, "未找到村/社区数据文件(villages.csv)，初始化中止");
                }
                source = $"嵌入资源 {resourceName}";
                LogInfo($"从嵌入资源加载村/社区数据: {resourceName}");
            }

            using (stream)
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
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

                if (villages.Count == 0)
                {
                    LogError($"村/社区CSV无有效数据: {source}");
                    return Result.Failure<List<VillageCsvRecord>>(
                        ErrorCodes.FILE_FORMAT_ERROR, "村/社区数据文件无有效数据，初始化中止");
                }

                LogInfo($"从CSV加载村/社区数据: {villages.Count}条（{source}）");
                return Result.Success(villages);
            }
        }
        catch (Exception ex)
        {
            LogError($"加载村/社区CSV失败: {ex.Message}");
            return Result.Failure<List<VillageCsvRecord>>(
                ErrorCodes.FILE_READ_ERROR, $"加载村/社区数据失败: {ex.Message}");
        }
    }

    private async Task<Result<int>> BulkInsertVillagesAsync(List<VillageCsvRecord> villages, CancellationToken ct)
    {
        var townMapResult = await LoadTownCodeToIdMapAsync(ct);
        if (townMapResult.IsFailure)
            return Result.Failure<int>(townMapResult.ErrorCode!, townMapResult.Message!);
        var townMap = townMapResult.Value;
        if (townMap.Count == 0)
        {
            // 乡镇数据在同一事务中刚导入完成，映射仍为空 = 种子数据异常（此前静默跳过会白导入 847KB 村数据）
            LogError("乡镇编码映射为空，村/社区导入中止");
            return Result.Failure<int>(ErrorCodes.DB_QUERY_ERROR, "乡镇编码映射为空，无法导入村/社区数据");
        }

        var startIdResult = await GetNextVillageIdAsync(ct);
        if (startIdResult.IsFailure)
            return Result.Failure<int>(startIdResult.ErrorCode!, startIdResult.Message!);
        var startId = startIdResult.Value;
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
        return Result.Success(totalInserted);
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
        // 批插入失败必须抛出（原实现返回 0 被上层当"插入 0 行"继续，静默漏批且事务照常提交）
        if (result.IsFailure)
            throw new InvalidOperationException($"村/社区批次插入失败: {result.Message}");
        return batch.Count;
    }

    private async Task<Result<Dictionary<string, int>>> LoadTownCodeToIdMapAsync(CancellationToken ct)
    {
        var sql = "SELECT id, town_code FROM nc_regions_towns WHERE town_code IS NOT NULL AND is_active = true";
        var result = await _dbService.QueryAsync<RegionTown>(sql, ct);
        // 查询失败显式上抛（原实现返回空字典，BulkInsert 会把失败当"乡镇表为空"静默跳过村导入）
        if (result.IsFailure)
            return Result.Failure<Dictionary<string, int>>(result.ErrorCode!, result.Message!);

        return Result.Success((result.Value ?? new List<RegionTown>()).ToDictionary(t => t.TownCode!, t => t.Id));
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

    /// <summary>
    /// 加载地级市YAML种子。资源缺失 / 数据为空 / 解析异常 → 显式 Failure
    /// （调用方在导入前已 TRUNCATE，失败必须回滚，禁止当"无数据"继续提交空表）。
    /// </summary>
    private Result<List<RegionCity>> LoadCitiesFromYaml()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "NewCosmos.Resources.Seed.regions.cities.yaml";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                LogError("未找到地级市YAML资源文件");
                return Result.Failure<List<RegionCity>>(ErrorCodes.FILE_NOT_FOUND, "未找到地级市种子数据文件，初始化中止");
            }
            using var reader = new StreamReader(stream);
            var yaml = reader.ReadToEnd();
            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build();
            var data = deserializer.Deserialize<RegionCityYaml>(yaml);
            if (data?.Tables == null || data.Tables.Count == 0 || data.Tables[0].Data == null)
            {
                LogError("地级市YAML数据为空");
                return Result.Failure<List<RegionCity>>(ErrorCodes.FILE_FORMAT_ERROR, "地级市种子数据为空，初始化中止");
            }

            return Result.Success(data.Tables[0].Data.Select(c => new RegionCity
            {
                Id = c.Id,
                CityName = c.CityName ?? string.Empty,
                CityCode = c.CityCode,
                SortOrder = c.SortOrder,
                IsActive = c.IsActive
            }).ToList());
        }
        catch (Exception ex)
        {
            LogError($"加载地级市YAML失败: {ex.Message}");
            return Result.Failure<List<RegionCity>>(ErrorCodes.FILE_FORMAT_ERROR, $"加载地级市数据失败: {ex.Message}");
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

    /// <summary>
    /// 加载县区YAML种子。资源缺失 / 数据为空 / 解析异常 → 显式 Failure（禁止当"无数据"继续清空提交）。
    /// </summary>
    private Result<List<RegionCounty>> LoadCountiesFromYaml()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "NewCosmos.Resources.Seed.regions.counties.yaml";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                LogError("未找到县区YAML资源文件");
                return Result.Failure<List<RegionCounty>>(ErrorCodes.FILE_NOT_FOUND, "未找到县区种子数据文件，初始化中止");
            }

            using var reader = new StreamReader(stream);
            var yaml = reader.ReadToEnd();

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            var data = deserializer.Deserialize<RegionCountyYaml>(yaml);
            if (data?.Tables == null || data.Tables.Count == 0 || data.Tables[0].Data == null)
            {
                LogError("县区YAML数据为空");
                return Result.Failure<List<RegionCounty>>(ErrorCodes.FILE_NOT_FOUND, "县区数据文件未找到或数据为空");
            }

            return Result.Success(data.Tables[0].Data.Select(c => new RegionCounty
            {
                Id = c.Id,
                CountyName = c.CountyName ?? string.Empty,
                CountyCode = c.CountyCode,
                CityName = c.CityName,
                SortOrder = c.SortOrder,
                IsActive = c.IsActive
            }).ToList());
        }
        catch (Exception ex)
        {
            LogError($"加载县区YAML失败: {ex.Message}");
            return Result.Failure<List<RegionCounty>>(ErrorCodes.FILE_FORMAT_ERROR, $"加载县区数据失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 加载乡镇YAML种子。资源缺失 / 数据为空 / 解析异常 → 显式 Failure（禁止当"无数据"继续清空提交）。
    /// </summary>
    private Result<List<RegionTown>> LoadTownsFromYaml()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "NewCosmos.Resources.Seed.regions.towns.yaml";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                LogError("未找到乡镇YAML资源文件");
                return Result.Failure<List<RegionTown>>(ErrorCodes.FILE_NOT_FOUND, "未找到乡镇种子数据文件，初始化中止");
            }

            using var reader = new StreamReader(stream);
            var yaml = reader.ReadToEnd();

            var deserializer = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .Build();

            var data = deserializer.Deserialize<RegionTownYaml>(yaml);
            if (data?.Tables == null || data.Tables.Count == 0 || data.Tables[0].Data == null)
            {
                LogError("乡镇YAML数据为空");
                return Result.Failure<List<RegionTown>>(ErrorCodes.FILE_FORMAT_ERROR, "乡镇种子数据为空，初始化中止");
            }

            return Result.Success(data.Tables[0].Data.Select(t => new RegionTown
            {
                Id = t.Id,
                CountyId = t.CountyId,
                TownName = t.TownName ?? string.Empty,
                TownCode = t.TownCode,
                TownType = t.TownType,
                SortOrder = t.SortOrder,
                IsActive = t.IsActive
            }).ToList());
        }
        catch (Exception ex)
        {
            LogError($"加载乡镇YAML失败: {ex.Message}");
            return Result.Failure<List<RegionTown>>(ErrorCodes.FILE_FORMAT_ERROR, $"加载乡镇数据失败: {ex.Message}");
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
