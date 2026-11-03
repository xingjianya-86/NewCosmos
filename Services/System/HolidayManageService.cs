using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Utilities;

namespace NewCosmos.Services.System;

/// <summary>
/// 节假日管理服务：节假日数据维护（手工 CRUD）、接口下载更新（随国务院年度安排更新）、
/// 数据库 → HolidayService 内存快照刷新。
/// </summary>
public interface IHolidayManageService
{
    /// <summary>确保节假日表存在（幂等，缺表时按 Schema YAML 建表）</summary>
    Task<Result> EnsureTablesExistAsync(CancellationToken ct = default);

    /// <summary>表为空时写入内置 2026 年种子数据（兜底，source=SEED）</summary>
    Task<Result> EnsureSeedDataAsync(CancellationToken ct = default);

    /// <summary>获取指定年份的节假日列表</summary>
    Task<Result<List<SysHoliday>>> GetHolidaysAsync(int year, CancellationToken ct = default);

    /// <summary>新增或更新节假日（按日期幂等覆盖）</summary>
    Task<Result> UpsertHolidayAsync(DateTime date, string dateType, string name, string source, CancellationToken ct = default);

    /// <summary>删除节假日记录</summary>
    Task<Result> DeleteHolidayAsync(long id, CancellationToken ct = default);

    /// <summary>从节假日接口下载指定全年数据（覆盖同日记录），成功后刷新内存快照</summary>
    Task<Result<HolidayDownloadSummary>> DownloadYearAsync(int year, CancellationToken ct = default);

    /// <summary>从数据库加载节假日数据并整体替换 HolidayService 内存快照</summary>
    Task<Result> RefreshCacheAsync(CancellationToken ct = default);
}

public class HolidayManageService : BaseService, IHolidayManageService
{
    protected override string ServiceName => "HolidayManageService";

    private readonly IDatabaseService _dbService;
    private readonly ISchemaSyncService _schemaSync;
    private readonly IHolidayService _holidayService;
    private readonly ILoggerService _logger;
    private bool _tablesReady;

    /// <summary>节假日接口复用的 HttpClient（Singleton 服务内共享，线程安全）</summary>
    private static readonly HttpClient _httpClient = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = global::System.Net.DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(DutyConstants.HolidayApiTimeoutSeconds)
        });
        client.Timeout = TimeSpan.FromSeconds(DutyConstants.HolidayApiTimeoutSeconds);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("NewCosmos-DutyModule/1.0");
        return client;
    }

    public HolidayManageService(
        IDatabaseService dbService,
        ISchemaSyncService schemaSync,
        IHolidayService holidayService,
        ILoggerService logger) : base(logger)
    {
        _dbService = dbService;
        _schemaSync = schemaSync;
        _holidayService = holidayService;
        _logger = logger;
    }

    #region 内置 2026 年种子（与 HolidayService 兜底快照同源，含节日名称）

    private static readonly (string Date, string DateType, string Name)[] SeedHolidays2026 =
    {
        ("2026-01-01", DutyConstants.HolidayRowTypes.HOLIDAY, "元旦"),
        ("2026-01-02", DutyConstants.HolidayRowTypes.HOLIDAY, "元旦"),
        ("2026-01-03", DutyConstants.HolidayRowTypes.HOLIDAY, "元旦"),
        ("2026-01-04", DutyConstants.HolidayRowTypes.MAKEUP, "元旦后补班"),
        ("2026-02-14", DutyConstants.HolidayRowTypes.MAKEUP, "春节前补班"),
        ("2026-02-15", DutyConstants.HolidayRowTypes.HOLIDAY, "春节"),
        ("2026-02-16", DutyConstants.HolidayRowTypes.HOLIDAY, "除夕"),
        ("2026-02-17", DutyConstants.HolidayRowTypes.HOLIDAY, "春节"),
        ("2026-02-18", DutyConstants.HolidayRowTypes.HOLIDAY, "春节"),
        ("2026-02-19", DutyConstants.HolidayRowTypes.HOLIDAY, "春节"),
        ("2026-02-20", DutyConstants.HolidayRowTypes.HOLIDAY, "春节"),
        ("2026-02-21", DutyConstants.HolidayRowTypes.HOLIDAY, "春节"),
        ("2026-02-22", DutyConstants.HolidayRowTypes.HOLIDAY, "春节"),
        ("2026-02-23", DutyConstants.HolidayRowTypes.HOLIDAY, "春节"),
        ("2026-02-28", DutyConstants.HolidayRowTypes.MAKEUP, "春节后补班"),
        ("2026-04-04", DutyConstants.HolidayRowTypes.HOLIDAY, "清明节"),
        ("2026-04-05", DutyConstants.HolidayRowTypes.HOLIDAY, "清明节"),
        ("2026-04-06", DutyConstants.HolidayRowTypes.HOLIDAY, "清明节"),
        ("2026-05-01", DutyConstants.HolidayRowTypes.HOLIDAY, "劳动节"),
        ("2026-05-02", DutyConstants.HolidayRowTypes.HOLIDAY, "劳动节"),
        ("2026-05-03", DutyConstants.HolidayRowTypes.HOLIDAY, "劳动节"),
        ("2026-05-04", DutyConstants.HolidayRowTypes.HOLIDAY, "劳动节"),
        ("2026-05-05", DutyConstants.HolidayRowTypes.HOLIDAY, "劳动节"),
        ("2026-05-09", DutyConstants.HolidayRowTypes.MAKEUP, "劳动节后补班"),
        ("2026-06-19", DutyConstants.HolidayRowTypes.HOLIDAY, "端午节"),
        ("2026-06-20", DutyConstants.HolidayRowTypes.HOLIDAY, "端午节"),
        ("2026-06-21", DutyConstants.HolidayRowTypes.HOLIDAY, "端午节"),
        ("2026-09-20", DutyConstants.HolidayRowTypes.MAKEUP, "中秋节前补班"),
        ("2026-09-25", DutyConstants.HolidayRowTypes.HOLIDAY, "中秋节"),
        ("2026-09-26", DutyConstants.HolidayRowTypes.HOLIDAY, "中秋节"),
        ("2026-09-27", DutyConstants.HolidayRowTypes.HOLIDAY, "中秋节"),
        ("2026-10-01", DutyConstants.HolidayRowTypes.HOLIDAY, "国庆节"),
        ("2026-10-02", DutyConstants.HolidayRowTypes.HOLIDAY, "国庆节"),
        ("2026-10-03", DutyConstants.HolidayRowTypes.HOLIDAY, "国庆节"),
        ("2026-10-04", DutyConstants.HolidayRowTypes.HOLIDAY, "国庆节"),
        ("2026-10-05", DutyConstants.HolidayRowTypes.HOLIDAY, "国庆节"),
        ("2026-10-06", DutyConstants.HolidayRowTypes.HOLIDAY, "国庆节"),
        ("2026-10-07", DutyConstants.HolidayRowTypes.HOLIDAY, "国庆节"),
        ("2026-10-10", DutyConstants.HolidayRowTypes.MAKEUP, "国庆节后补班"),
    };

    #endregion

    #region Schema 管理

    public async Task<Result> EnsureTablesExistAsync(CancellationToken ct = default)
    {
        if (_tablesReady) return Result.Success();

        LogInfo("执行节假日表检查");

        var checkSql = @"
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_schema = 'public' AND table_name = 'nc_sys_holidays'";

        var result = await _dbService.ExecuteScalarAsync<long>(checkSql, ct);
        if (result.IsSuccess && result.Value >= 1)
        {
            _tablesReady = true;
            LogInfo("节假日表已存在");
            return Result.Success();
        }

        LogInfo("节假日表不存在，开始创建");
        var syncResult = await _schemaSync.SyncTableSchemaAsync("nc_sys_holidays", allowDrop: false, null, ct);
        if (syncResult.IsSuccess)
        {
            _tablesReady = true;
            LogInfo("节假日表创建完成");
        }
        else
        {
            LogError($"节假日表创建失败: {syncResult.Message}");
        }
        return syncResult;
    }

    public async Task<Result> EnsureSeedDataAsync(CancellationToken ct = default)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure) return ensureResult;

        var countResult = await _dbService.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM nc_sys_holidays", ct);
        if (countResult.IsSuccess && countResult.Value > 0)
        {
            LogInfo("节假日数据已存在，跳过种子写入");
            return Result.Success();
        }

        LogInfo("节假日表为空，写入内置 2026 年种子数据");
        await using var tx = await _dbService.BeginTransactionScopeAsync(ct);
        try
        {
            // 逐行参数化写入（种子为编译期常量，仍统一走位置参数规范）
            foreach (var (dateText, dateType, name) in SeedHolidays2026)
            {
                var date = DateTime.Parse(dateText);
                var insertResult = await _dbService.ExecuteNonQueryAsync(@"
                    INSERT INTO nc_sys_holidays (holiday_date, date_type, name, year, source, updated_at)
                    VALUES ($1, $2, $3, $4, $5, NOW())
                    ON CONFLICT (holiday_date) DO NOTHING", ct,
                    date, dateType, name, date.Year, DutyConstants.Sources.SEED);
                if (insertResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return insertResult;
                }
            }

            await tx.CommitAsync(ct);
            Logger.LogBusiness($"节假日种子数据写入完成: {SeedHolidays2026.Length} 条");
            return Result.Success();
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "节假日种子写入失败");
            return Result.FromException(ex);
        }
    }

    #endregion

    #region 查询与维护

    public async Task<Result<List<SysHoliday>>> GetHolidaysAsync(int year, CancellationToken ct = default)
    {
        if (year < 2000 || year > 2100)
            return Result.Failure<List<SysHoliday>>(ErrorCodes.DUTY_INVALID_DATE, "年份无效");

        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure) return Result.Failure<List<SysHoliday>>(ensureResult.ErrorCode!, ensureResult.Message!);

        LogInfo($"获取节假日列表: {year}");

        var sql = @"
            SELECT id, holiday_date, date_type, name, year, source, updated_at
            FROM nc_sys_holidays
            WHERE year = $1
            ORDER BY holiday_date";

        var result = await _dbService.QueryAsync<SysHoliday>(sql, ct, year);
        return result.IsSuccess && result.Value is not null
            ? Result.Success(result.Value)
            : Result.Success(new List<SysHoliday>());
    }

    public async Task<Result> UpsertHolidayAsync(DateTime date, string dateType, string name, string source, CancellationToken ct = default)
    {
        if (dateType != DutyConstants.HolidayRowTypes.HOLIDAY && dateType != DutyConstants.HolidayRowTypes.MAKEUP)
            return Result.Failure(ErrorCodes.VALIDATION_FAILED, "日期类型无效");

        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure) return ensureResult;

        var safeName = (name ?? string.Empty).Trim().Replace("'", "''");
        var sql = @"
            INSERT INTO nc_sys_holidays (holiday_date, date_type, name, year, source, updated_at)
            VALUES ($1, $2, $3, $4, $5, NOW())
            ON CONFLICT (holiday_date)
            DO UPDATE SET date_type = EXCLUDED.date_type,
                         name = EXCLUDED.name,
                         year = EXCLUDED.year,
                         source = EXCLUDED.source,
                         updated_at = NOW()";

        var result = await _dbService.ExecuteNonQueryAsync(sql, ct, date.Date, dateType, safeName, date.Year, source);
        if (result.IsSuccess)
        {
            Logger.LogBusiness("维护节假日数据",
                ("Date", date.ToString("yyyy-MM-dd")),
                ("DateType", dateType),
                ("Name", safeName),
                ("Source", source));
            return Result.Success();
        }
        return result;
    }

    public async Task<Result> DeleteHolidayAsync(long id, CancellationToken ct = default)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure) return ensureResult;

        var result = await _dbService.ExecuteNonQueryAsync("DELETE FROM nc_sys_holidays WHERE id = $1", ct, id);
        if (result.IsSuccess)
        {
            Logger.LogBusiness("删除节假日记录", ("Id", id));
            return Result.Success();
        }
        return result;
    }

    #endregion

    #region 接口下载

    /// <summary>单次数据源请求的结果分类（用于主备链路的降级判定）</summary>
    private enum FetchStatus
    {
        /// <summary>拿到该年度有效数据</summary>
        Success,
        /// <summary>响应正常但该年度无数据（安排尚未发布）</summary>
        EmptyYear,
        /// <summary>响应结构无法识别（字段损坏 / 非预期 code）</summary>
        Malformed,
        /// <summary>网络不可达 / 超时 / 非 2xx</summary>
        Unreachable
    }

    private sealed class FetchOutcome
    {
        public FetchStatus Status { get; init; }
        public List<HolidayApiParser.HolidayRow>? Rows { get; init; }
        /// <summary>备用源携带的国务院公文链接（审计线索）</summary>
        public string? PaperUrl { get; init; }
    }

    /// <summary>
    /// 请求单个数据源并解析。请求失败只降级为 Unreachable，不抛出（主备链路需要继续尝试下一个源）。
    /// 用户主动取消（ct 触发）例外，原样上抛以免被误判为"网络不可达"。
    /// </summary>
    private async Task<FetchOutcome> TryFetchAsync(string url,
        Func<string, HolidayApiParser.ParseResult> parse, CancellationToken ct)
    {
        string json;
        try
        {
            using var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();
            json = await response.Content.ReadAsStringAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // 用户取消，不是网络故障
        }
        catch (Exception ex)
        {
            LogWarn($"节假日数据源请求失败: {url} - {ex.Message}");
            return new FetchOutcome { Status = FetchStatus.Unreachable };
        }

        var parsed = parse(json);
        if (parsed.IsMalformed)
        {
            LogWarn($"节假日数据源响应格式异常: {url}");
            return new FetchOutcome { Status = FetchStatus.Malformed };
        }

        if (parsed.IsEmptyYear || parsed.Rows.Count == 0)
        {
            LogInfo($"数据源无该年度数据（安排尚未发布）: {url}");
            return new FetchOutcome { Status = FetchStatus.EmptyYear };
        }

        return new FetchOutcome
        {
            Status = FetchStatus.Success,
            Rows = parsed.Rows,
            PaperUrl = parsed.PaperUrl
        };
    }

    /// <summary>
    /// 构造候选数据源序列：主接口在前，备用接口在后。
    /// 备用地址可在设置表覆盖（holiday_fallback_api_url），未配置时使用 jsdelivr + GitHub raw 双地址互备
    /// （jsdelivr 在国内多数环境可用，raw 在 jsdelivr 抽风时兜底）。
    /// </summary>
    private async Task<List<(string Url, bool IsFallback, Func<string, HolidayApiParser.ParseResult> Parse)>>
        BuildSourcesAsync(int year, CancellationToken ct)
    {
        var sources = new List<(string Url, bool IsFallback, Func<string, HolidayApiParser.ParseResult> Parse)>();

        // 主源（默认 timor.tech，设置表可改）
        var primarySetting = await GetSettingValueAsync(DutyConstants.SettingKeys.HOLIDAY_API_URL, ct);
        var primaryTemplate = primarySetting.IsSuccess && !string.IsNullOrWhiteSpace(primarySetting.Value)
            ? primarySetting.Value
            : DutyConstants.DefaultHolidayApiUrl;
        sources.Add((primaryTemplate.Replace("{year}", year.ToString()), IsFallback: false,
            HolidayApiParser.ParseTimor));

        // 备用源（holiday-cn，跟踪国务院办公厅公报）
        var fallbackSetting = await GetSettingValueAsync(DutyConstants.SettingKeys.HOLIDAY_FALLBACK_API_URL, ct);
        if (fallbackSetting.IsSuccess && !string.IsNullOrWhiteSpace(fallbackSetting.Value))
        {
            var url = fallbackSetting.Value.Replace("{year}", year.ToString());
            sources.Add((url, IsFallback: true, json => HolidayApiParser.ParseHolidayCn(json, year)));
        }
        else
        {
            foreach (var template in new[]
                     {
                         DutyConstants.DefaultHolidayFallbackApiUrl,
                         DutyConstants.DefaultHolidayFallbackApiUrlAlt
                     })
            {
                sources.Add((template.Replace("{year}", year.ToString()), IsFallback: true,
                    json => HolidayApiParser.ParseHolidayCn(json, year)));
            }
        }

        return sources;
    }

    public async Task<Result<HolidayDownloadSummary>> DownloadYearAsync(int year, CancellationToken ct = default)
    {
        if (year < 2000 || year > 2100)
            return Result.Failure<HolidayDownloadSummary>(ErrorCodes.DUTY_INVALID_DATE, "年份无效");

        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<HolidayDownloadSummary>(ensureResult.ErrorCode!, ensureResult.Message!);

        // 1. 依次尝试主、备数据源，首个成功者胜出
        LogInfo($"开始下载 {year} 年节假日数据");
        var sources = await BuildSourcesAsync(year, ct);

        List<HolidayApiParser.HolidayRow>? rows = null;
        string? usedUrl = null;
        bool isFromFallback = false;
        string? paperUrl = null;
        // 降级判定：拿到数据 > 至少一个源明确回答"尚未发布" > 至少一个源格式异常 > 全部不可达
        bool anyEmptyYear = false;
        bool anyMalformed = false;

        foreach (var (url, isFallback, parse) in sources)
        {
            var outcome = await TryFetchAsync(url, parse, ct);
            if (outcome.Status == FetchStatus.Success)
            {
                rows = outcome.Rows;
                usedUrl = url;
                isFromFallback = isFallback;
                paperUrl = outcome.PaperUrl;
                break;
            }

            anyEmptyYear |= outcome.Status == FetchStatus.EmptyYear;
            anyMalformed |= outcome.Status == FetchStatus.Malformed;
        }

        if (rows == null || rows.Count == 0)
        {
            if (anyEmptyYear)
            {
                LogWarn($"{year} 年放假安排尚未发布（所有数据源均无该年度数据）");
                return Result.Failure<HolidayDownloadSummary>(ErrorCodes.HOLIDAY_YEAR_NOT_PUBLISHED,
                    "主、备数据源均未收录该年度放假安排");
            }

            if (anyMalformed)
            {
                LogError($"{year} 年节假日数据解析失败（数据源返回格式异常）");
                return Result.Failure<HolidayDownloadSummary>(ErrorCodes.HOLIDAY_DATA_INVALID,
                    "主、备数据源响应均无法解析");
            }

            LogError($"{year} 年节假日数据源全部不可达");
            return Result.Failure<HolidayDownloadSummary>(ErrorCodes.HOLIDAY_API_UNREACHABLE,
                "主、备数据源均请求失败，请检查网络");
        }

        LogInfo($"节假日数据已获取: {usedUrl}（{(isFromFallback ? "备用源" : "主源")}）");

        // 2. 事务落库：该年份的旧 API 数据整体替换（MANUAL/SEED 数据保留，逐行 ON CONFLICT 覆盖）
        var holidayCount = rows.Count(r => r.DateType == DutyConstants.HolidayRowTypes.HOLIDAY);
        var makeupCount = rows.Count - holidayCount;

        await using var tx = await _dbService.BeginTransactionScopeAsync(ct);
        try
        {
            var deleteResult = await _dbService.ExecuteNonQueryAsync(
                "DELETE FROM nc_sys_holidays WHERE year = $1 AND source = $2", ct, year, DutyConstants.Sources.API);
            if (deleteResult.IsFailure)
            {
                await tx.RollbackAsync(ct);
                return Result.Failure<HolidayDownloadSummary>(deleteResult.ErrorCode!, deleteResult.Message!);
            }

            // 逐行参数化 upsert（接口返回的外部数据，严禁内插）
            foreach (var row in rows)
            {
                var upsertResult = await _dbService.ExecuteNonQueryAsync(@"
                    INSERT INTO nc_sys_holidays (holiday_date, date_type, name, year, source, updated_at)
                    VALUES ($1, $2, $3, $4, $5, NOW())
                    ON CONFLICT (holiday_date)
                    DO UPDATE SET date_type = EXCLUDED.date_type,
                                 name = EXCLUDED.name,
                                 year = EXCLUDED.year,
                                 source = EXCLUDED.source,
                                 updated_at = NOW()", ct,
                    row.Date.ToDateTime(TimeOnly.MinValue), row.DateType, row.Name, year, DutyConstants.Sources.API);
                if (upsertResult.IsFailure)
                {
                    await tx.RollbackAsync(ct);
                    return Result.Failure<HolidayDownloadSummary>(upsertResult.ErrorCode!, upsertResult.Message!);
                }
            }

            await tx.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync(ct);
            LogException(ex, "节假日数据写入失败");
            return Result.FromException<HolidayDownloadSummary>(ex);
        }

        // 公文链接仅作审计线索记日志（不落业务表，避免给 schema 加列）
        if (!string.IsNullOrWhiteSpace(paperUrl))
            LogInfo($"节假日数据公文来源: {paperUrl}");

        Logger.LogBusiness("下载节假日数据",
            ("Year", year),
            ("HolidayCount", holidayCount),
            ("MakeupCount", makeupCount),
            ("SourceUrl", usedUrl ?? string.Empty),
            ("IsFromFallback", isFromFallback));

        // 3. 刷新内存快照（失败不回滚落库结果，仅提示）
        var refreshResult = await RefreshCacheAsync(ct);
        if (refreshResult.IsFailure)
        {
            LogWarn($"节假日快照刷新失败（数据已入库）: {refreshResult.Message}");
        }

        return Result.Success(new HolidayDownloadSummary
        {
            Year = year,
            HolidayCount = holidayCount,
            MakeupCount = makeupCount,
            IsFromFallback = isFromFallback,
            PaperUrl = paperUrl,
            SourceDisplayName = isFromFallback ? "备用源" : "主源"
        });
    }

    #endregion

    #region 内存快照刷新

    public async Task<Result> RefreshCacheAsync(CancellationToken ct = default)
    {
        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure) return ensureResult;

        var sql = "SELECT holiday_date, date_type, name FROM nc_sys_holidays";
        var result = await _dbService.QueryAsync<SysHoliday>(sql, ct);
        if (!result.IsSuccess)
        {
            LogError($"节假日快照加载失败: {result.Message}");
            return result;
        }

        var rows = result.Value ?? new List<SysHoliday>();
        var legal = rows
            .Where(r => r.DateType == DutyConstants.HolidayRowTypes.HOLIDAY)
            .Select(r => DateOnly.FromDateTime(r.HolidayDate))
            .ToList();
        var makeup = rows
            .Where(r => r.DateType == DutyConstants.HolidayRowTypes.MAKEUP)
            .Select(r => DateOnly.FromDateTime(r.HolidayDate))
            .ToList();
        // 节日名（如 春节/元旦），供政务值班表"{星期与农历N}"以节日名替代星期
        var holidayNames = rows
            .Where(r => r.DateType == DutyConstants.HolidayRowTypes.HOLIDAY)
            .GroupBy(r => DateOnly.FromDateTime(r.HolidayDate))
            .ToDictionary(g => g.Key, g => g.First().Name ?? string.Empty);

        _holidayService.ReplaceSnapshot(legal, makeup, holidayNames);
        LogInfo($"节假日快照刷新完成: {legal.Count} 个放假日, {makeup.Count} 个补班日");
        return Result.Success();
    }

    #endregion

    #region 设置读取（接口地址）

    private async Task<Result<string>> GetSettingValueAsync(string key, CancellationToken ct)
    {
        var sql = "SELECT setting_value FROM nc_duty_settings WHERE setting_key = $1";
        var result = await _dbService.ExecuteScalarAsync<string>(sql, ct, key);
        return result.IsSuccess
            ? Result.Success(result.Value ?? string.Empty)
            : Result.Failure<string>(result.ErrorCode!, result.Message!);
    }

    #endregion
}
