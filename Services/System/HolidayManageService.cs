using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;
using NewCosmos.Services.Utilities;
using System.Text.Json;

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
        await _dbService.BeginTransactionAsync(ct);
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
                    await _dbService.RollbackTransactionAsync(ct);
                    return insertResult;
                }
            }

            await _dbService.CommitTransactionAsync(ct);
            Logger.LogBusiness($"节假日种子数据写入完成: {SeedHolidays2026.Length} 条");
            return Result.Success();
        }
        catch (Exception ex)
        {
            await _dbService.RollbackTransactionAsync(ct);
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

    // timor.tech 响应 DTO（字段名按接口原文，保持小写）
    private sealed class TimorResponse
    {
        public int code { get; set; }
        public Dictionary<string, TimorHolidayDay>? holiday { get; set; }
    }

    private sealed class TimorHolidayDay
    {
        public bool holiday { get; set; }
        public string name { get; set; } = string.Empty;
        public string date { get; set; } = string.Empty;
    }

    public async Task<Result<HolidayDownloadSummary>> DownloadYearAsync(int year, CancellationToken ct = default)
    {
        if (year < 2000 || year > 2100)
            return Result.Failure<HolidayDownloadSummary>(ErrorCodes.DUTY_INVALID_DATE, "年份无效");

        var ensureResult = await EnsureTablesExistAsync(ct);
        if (ensureResult.IsFailure)
            return Result.Failure<HolidayDownloadSummary>(ensureResult.ErrorCode!, ensureResult.Message!);

        // 1. 解析接口地址（设置表可改，缺省用内置地址）
        var urlSetting = await GetSettingValueAsync(DutyConstants.SettingKeys.HOLIDAY_API_URL, ct);
        var urlTemplate = urlSetting.IsSuccess && !string.IsNullOrWhiteSpace(urlSetting.Value)
            ? urlSetting.Value
            : DutyConstants.DefaultHolidayApiUrl;
        var requestUrl = urlTemplate.Replace("{year}", year.ToString());

        LogInfo($"开始下载 {year} 年节假日数据: {requestUrl}");

        // 2. 请求接口
        string json;
        try
        {
            using var response = await _httpClient.GetAsync(requestUrl, ct);
            response.EnsureSuccessStatusCode();
            json = await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            LogError($"节假日接口请求失败: {ex.Message}");
            return Result.Failure<HolidayDownloadSummary>(ErrorCodes.HOLIDAY_API_UNREACHABLE, $"请求失败: {ex.Message}");
        }

        // 3. 解析响应
        List<(DateTime Date, string DateType, string Name)> rows;
        try
        {
            var payload = JsonSerializer.Deserialize<TimorResponse>(json);
            if (payload == null || payload.code != 0 || payload.holiday == null || payload.holiday.Count == 0)
            {
                LogWarn($"节假日接口返回数据无效（可能尚未发布 {year} 年安排）");
                return Result.Failure<HolidayDownloadSummary>(ErrorCodes.HOLIDAY_DATA_INVALID, "接口无有效数据（当年安排可能尚未发布）");
            }

            rows = new List<(DateTime, string, string)>();
            foreach (var day in payload.holiday.Values)
            {
                if (!DateTime.TryParse(day.date, out var date)) continue;
                var dateType = day.holiday
                    ? DutyConstants.HolidayRowTypes.HOLIDAY
                    : DutyConstants.HolidayRowTypes.MAKEUP;
                rows.Add((date.Date, dateType, day.name?.Trim() ?? string.Empty));
            }

            if (rows.Count == 0)
            {
                return Result.Failure<HolidayDownloadSummary>(ErrorCodes.HOLIDAY_DATA_INVALID, "接口数据解析为空");
            }
        }
        catch (JsonException ex)
        {
            LogError($"节假日接口数据解析失败: {ex.Message}");
            return Result.Failure<HolidayDownloadSummary>(ErrorCodes.HOLIDAY_DATA_INVALID, ex.Message);
        }

        // 4. 事务落库：该年份的旧 API 数据整体替换（MANUAL/SEED 数据保留，逐行 ON CONFLICT 覆盖）
        var holidayCount = rows.Count(r => r.DateType == DutyConstants.HolidayRowTypes.HOLIDAY);
        var makeupCount = rows.Count - holidayCount;

        await _dbService.BeginTransactionAsync(ct);
        try
        {
            var deleteResult = await _dbService.ExecuteNonQueryAsync(
                "DELETE FROM nc_sys_holidays WHERE year = $1 AND source = $2", ct, year, DutyConstants.Sources.API);
            if (deleteResult.IsFailure)
            {
                await _dbService.RollbackTransactionAsync(ct);
                return Result.Failure<HolidayDownloadSummary>(deleteResult.ErrorCode!, deleteResult.Message!);
            }

            // 逐行参数化 upsert（接口返回的外部数据，严禁内插）
            foreach (var (date, dateType, name) in rows)
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
                    date, dateType, name, year, DutyConstants.Sources.API);
                if (upsertResult.IsFailure)
                {
                    await _dbService.RollbackTransactionAsync(ct);
                    return Result.Failure<HolidayDownloadSummary>(upsertResult.ErrorCode!, upsertResult.Message!);
                }
            }

            await _dbService.CommitTransactionAsync(ct);
        }
        catch (Exception ex)
        {
            await _dbService.RollbackTransactionAsync(ct);
            LogException(ex, "节假日数据写入失败");
            return Result.FromException<HolidayDownloadSummary>(ex);
        }

        Logger.LogBusiness("下载节假日数据",
            ("Year", year),
            ("HolidayCount", holidayCount),
            ("MakeupCount", makeupCount));

        // 5. 刷新内存快照（失败不回滚落库结果，仅提示）
        var refreshResult = await RefreshCacheAsync(ct);
        if (refreshResult.IsFailure)
        {
            LogWarn($"节假日快照刷新失败（数据已入库）: {refreshResult.Message}");
        }

        return Result.Success(new HolidayDownloadSummary
        {
            Year = year,
            HolidayCount = holidayCount,
            MakeupCount = makeupCount
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
