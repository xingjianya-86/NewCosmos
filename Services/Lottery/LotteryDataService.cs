using System.Diagnostics;
using System.Text.Json;
using NewCosmos.Models.Lottery;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Lottery;

/// <summary>
/// 彩票数据服务实现
/// 负责开奖数据的获取、存储和查询
/// </summary>
public class LotteryDataService : ILotteryDataService
{
    private readonly IDatabaseService _db;
    private readonly ILoggerService _logger;
    private readonly IConfigService _configService;

    private static readonly string ScriptsDir = Path.Combine(AppContext.BaseDirectory, "Scripts", "Lottery");
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "Data", "Lottery");
    private static readonly string PythonExe = Path.Combine(AppContext.BaseDirectory, "python-embed", "python.exe");

    public LotteryDataService(
        IDatabaseService db,
        ILoggerService logger,
        IConfigService configService)
    {
        _db = db;
        _logger = logger;
        _configService = configService;
    }

    /// <summary>
    /// 增量同步开奖数据：抓取开奖日期 >= sinceDate 的记录并 upsert；
    /// sinceDate 为空表示首次全量抓取。返回本次真实新增（不含仅更新）的记录数。
    /// </summary>
    public async Task<Result<int>> SyncHistoryDataAsync(LotteryType lotteryType, DateTime? sinceDate = null, CancellationToken ct = default)
    {
        try
        {
            var scope = sinceDate.HasValue ? $"增量（开奖日期 >= {sinceDate:yyyy-MM-dd}）" : "全量";
            _logger.Info($"开始{scope}同步 {lotteryType.GetDisplayName()} 开奖数据...");

            // 调用 Python 脚本获取数据
            var scriptPath = Path.Combine(ScriptsDir, "fetch_history.py");
            var outputDir = Path.Combine(DataDir, lotteryType.ToString().ToLower());

            var sinceArg = sinceDate.HasValue ? $" --since-date {sinceDate.Value:yyyy-MM-dd}" : string.Empty;
            var args = $"\"{scriptPath}\" --type {lotteryType.ToString().ToLower()} --format json --output \"{outputDir}\"{sinceArg}";

            var (success, output, error) = await RunPythonScriptAsync(args, ct);

            if (!success)
            {
                _logger.Error($"Python脚本执行失败: {error}");
                return Result<int>.Failure("SYNC_FAILED", $"数据同步失败: {error}");
            }

            // 读取获取的数据并保存到数据库
            var jsonFile = Path.Combine(outputDir, $"{lotteryType.ToString().ToLower()}_history.json");
            if (!File.Exists(jsonFile))
            {
                return Result<int>.Failure("FILE_NOT_FOUND", "数据文件未生成");
            }

            var jsonContent = await File.ReadAllTextAsync(jsonFile, ct);
            var draws = JsonSerializer.Deserialize<List<LotteryDrawDto>>(jsonContent);

            if (draws == null || draws.Count == 0)
            {
                _logger.Info("无新增开奖数据");
                return Result<int>.Success(0);
            }

            // 真实新增计数：先查已存在的期号（ON CONFLICT 的受影响行数含更新，不能直接当新增）
            var incomingNumbers = draws
                .Select(d => d.draw_number)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            var existingResult = await _db.QueryAsync<string>(
                "SELECT draw_number FROM nc_lottery_draws WHERE lottery_type = $1 AND draw_number = ANY($2)",
                ct, lotteryType.ToString(), incomingNumbers.ToArray());
            var existing = existingResult.IsSuccess
                ? new HashSet<string>(existingResult.Value ?? new List<string>(), StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            var newCount = incomingNumbers.Count(n => !existing.Contains(n));

            var affected = await BatchInsertDrawsAsync(lotteryType, draws, ct);

            _logger.Info($"同步完成：抓取 {draws.Count} 期，新增 {newCount} 期，写入/更新 {affected} 期");
            return Result<int>.Success(newCount);
        }
        catch (Exception ex)
        {
            _logger.Error($"同步历史数据失败: {ex.Message}");
            return Result<int>.FromException<int>(ex);
        }
    }

    /// <summary>
    /// 获取指定彩种的开奖记录列表
    /// </summary>
    public async Task<Result<List<LotteryDraw>>> GetDrawListAsync(LotteryType lotteryType, int pageNo = 1, int pageSize = 30, CancellationToken ct = default)
    {
        try
        {
            var offset = (pageNo - 1) * pageSize;
            var sql = @"
                SELECT id, lottery_type, draw_number, draw_date, 
                       red_numbers_json, blue_numbers_json,
                       sales_amount, pool_money, prize_grades_json,
                       prize_description, created_at
                FROM nc_lottery_draws
                WHERE lottery_type = $1
                ORDER BY draw_date DESC
                LIMIT $2 OFFSET $3";

            var result = await _db.QueryAsync<LotteryDrawRecord>(sql, ct, lotteryType.ToString(), pageSize, offset);
            if (!result.IsSuccess)
            {
                return Result<List<LotteryDraw>>.Failure(result.ErrorCode!, result.Message!);
            }

            var draws = result.Value.Select(r => r.ToLotteryDraw()).ToList();
            return Result<List<LotteryDraw>>.Success(draws);
        }
        catch (Exception ex)
        {
            _logger.Error($"获取开奖记录列表失败: {ex.Message}");
            return Result<List<LotteryDraw>>.FromException<List<LotteryDraw>>(ex);
        }
    }

    /// <summary>
    /// 获取指定彩种的开奖记录总数
    /// </summary>
    public async Task<Result<int>> GetDrawCountAsync(LotteryType lotteryType, CancellationToken ct = default)
    {
        try
        {
            var sql = "SELECT COUNT(*) FROM nc_lottery_draws WHERE lottery_type = $1";
            var count = await _db.ExecuteScalarAsync<int?>(sql, ct, lotteryType.ToString());
            if (!count.IsSuccess)
            {
                return Result<int>.Failure(count.ErrorCode!, count.Message!);
            }
            return Result<int>.Success(count.Value ?? 0);
        }
        catch (Exception ex)
        {
            _logger.Error($"获取开奖记录总数失败: {ex.Message}");
            return Result<int>.FromException<int>(ex);
        }
    }

    /// <summary>
    /// 获取指定期号的开奖记录
    /// </summary>
    public async Task<Result<LotteryDraw?>> GetDrawByNumberAsync(LotteryType lotteryType, string drawNumber, CancellationToken ct = default)
    {
        try
        {
            var sql = @"
                SELECT id, lottery_type, draw_number, draw_date, 
                       red_numbers_json, blue_numbers_json,
                       sales_amount, pool_money, prize_grades_json,
                       prize_description, created_at
                FROM nc_lottery_draws
                WHERE lottery_type = $1 AND draw_number = $2";

            var result = await _db.QuerySingleAsync<LotteryDrawRecord>(sql, ct, lotteryType.ToString(), drawNumber);
            if (!result.IsSuccess)
            {
                return Result<LotteryDraw?>.Failure(result.ErrorCode!, result.Message!);
            }

            return Result<LotteryDraw?>.Success(result.Value?.ToLotteryDraw());
        }
        catch (Exception ex)
        {
            _logger.Error($"获取开奖记录失败: {ex.Message}");
            return Result<LotteryDraw?>.FromException<LotteryDraw?>(ex);
        }
    }

    /// <summary>
    /// 获取最新的开奖记录
    /// </summary>
    public async Task<Result<LotteryDraw?>> GetLatestDrawAsync(LotteryType lotteryType, CancellationToken ct = default)
    {
        try
        {
            var sql = @"
                SELECT id, lottery_type, draw_number, draw_date, 
                       red_numbers_json, blue_numbers_json,
                       sales_amount, pool_money, prize_grades_json,
                       prize_description, created_at
                FROM nc_lottery_draws
                WHERE lottery_type = $1
                ORDER BY draw_date DESC
                LIMIT 1";

            var result = await _db.QuerySingleAsync<LotteryDrawRecord>(sql, ct, lotteryType.ToString());
            if (!result.IsSuccess)
            {
                return Result<LotteryDraw?>.Failure(result.ErrorCode!, result.Message!);
            }

            return Result<LotteryDraw?>.Success(result.Value?.ToLotteryDraw());
        }
        catch (Exception ex)
        {
            _logger.Error($"获取最新开奖记录失败: {ex.Message}");
            return Result<LotteryDraw?>.FromException<LotteryDraw?>(ex);
        }
    }

    /// <summary>
    /// 获取最近N期的开奖记录
    /// </summary>
    public async Task<Result<List<LotteryDraw>>> GetRecentDrawsAsync(LotteryType lotteryType, int count = 30, CancellationToken ct = default)
    {
        try
        {
            var sql = @"
                SELECT id, lottery_type, draw_number, draw_date, 
                       red_numbers_json, blue_numbers_json,
                       sales_amount, pool_money, prize_grades_json,
                       prize_description, created_at
                FROM nc_lottery_draws
                WHERE lottery_type = $1
                ORDER BY draw_date DESC
                LIMIT $2";

            var result = await _db.QueryAsync<LotteryDrawRecord>(sql, ct, lotteryType.ToString(), count);
            if (!result.IsSuccess)
            {
                return Result<List<LotteryDraw>>.Failure(result.ErrorCode!, result.Message!);
            }

            var draws = result.Value.Select(r => r.ToLotteryDraw()).ToList();
            return Result<List<LotteryDraw>>.Success(draws);
        }
        catch (Exception ex)
        {
            _logger.Error($"获取最近开奖记录失败: {ex.Message}");
            return Result<List<LotteryDraw>>.FromException<List<LotteryDraw>>(ex);
        }
    }

    /// <summary>
    /// 获取号码统计数据
    /// </summary>
    public async Task<Result<List<NumberStatistics>>> GetNumberStatisticsAsync(LotteryType lotteryType, int periodCount = 100, CancellationToken ct = default)
    {
        try
        {
            // 获取最近N期数据
            var drawsResult = await GetRecentDrawsAsync(lotteryType, periodCount, ct);
            if (!drawsResult.IsSuccess)
            {
                return Result<List<NumberStatistics>>.Failure(drawsResult.ErrorCode!, drawsResult.Message!);
            }

            var draws = drawsResult.Value;
            var stats = new List<NumberStatistics>();

            // 统计红球
            var redMax = lotteryType.GetRedMax();
            for (int i = 1; i <= redMax; i++)
            {
                var stat = CalculateNumberStatistics(i, draws, true, lotteryType);
                stats.Add(stat);
            }

            // 统计蓝球
            var blueMax = lotteryType.GetBlueMax();
            for (int i = 1; i <= blueMax; i++)
            {
                var stat = CalculateNumberStatistics(i, draws, false, lotteryType);
                stats.Add(stat);
            }

            return Result<List<NumberStatistics>>.Success(stats);
        }
        catch (Exception ex)
        {
            _logger.Error($"获取号码统计数据失败: {ex.Message}");
            return Result<List<NumberStatistics>>.FromException<List<NumberStatistics>>(ex);
        }
    }

    /// <summary>
    /// 获取冷号列表
    /// </summary>
    public async Task<Result<List<NumberStatistics>>> GetColdNumbersAsync(LotteryType lotteryType, int count = 10, CancellationToken ct = default)
    {
        var statsResult = await GetNumberStatisticsAsync(lotteryType, 100, ct);
        if (!statsResult.IsSuccess)
        {
            return Result<List<NumberStatistics>>.Failure(statsResult.ErrorCode!, statsResult.Message!);
        }

        var coldNumbers = statsResult.Value
            .OrderByDescending(s => s.CurrentMissing)
            .Take(count)
            .ToList();

        return Result<List<NumberStatistics>>.Success(coldNumbers);
    }

    /// <summary>
    /// 获取热号列表
    /// </summary>
    public async Task<Result<List<NumberStatistics>>> GetHotNumbersAsync(LotteryType lotteryType, int count = 10, CancellationToken ct = default)
    {
        var statsResult = await GetNumberStatisticsAsync(lotteryType, 100, ct);
        if (!statsResult.IsSuccess)
        {
            return Result<List<NumberStatistics>>.Failure(statsResult.ErrorCode!, statsResult.Message!);
        }

        var hotNumbers = statsResult.Value
            .OrderByDescending(s => s.Frequency)
            .Take(count)
            .ToList();

        return Result<List<NumberStatistics>>.Success(hotNumbers);
    }

    #region 私有方法

    private async Task<(bool Success, string Output, string Error)> RunPythonScriptAsync(string args, CancellationToken ct)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = PythonExe,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = ScriptsDir
            };

            // 传递代理环境变量（如果系统配置了的话）
            var httpProxy = Environment.GetEnvironmentVariable("HTTP_PROXY") ?? Environment.GetEnvironmentVariable("http_proxy");
            var httpsProxy = Environment.GetEnvironmentVariable("HTTPS_PROXY") ?? Environment.GetEnvironmentVariable("https_proxy");
            if (!string.IsNullOrEmpty(httpProxy))
                startInfo.EnvironmentVariables["HTTP_PROXY"] = httpProxy;
            if (!string.IsNullOrEmpty(httpsProxy))
                startInfo.EnvironmentVariables["HTTPS_PROXY"] = httpsProxy;

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);

            var output = await outputTask;
            var error = await errorTask;

            return (process.ExitCode == 0, output, error);
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }

    private async Task<int> BatchInsertDrawsAsync(LotteryType lotteryType, List<LotteryDrawDto> draws, CancellationToken ct)
    {
        int insertedCount = 0;

        foreach (var draw in draws)
        {
            try
            {
                var sql = @"
                    INSERT INTO nc_lottery_draws (lottery_type, draw_number, draw_date, red_numbers_json, blue_numbers_json, sales_amount, pool_money, prize_grades_json, prize_description)
                    VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
                    ON CONFLICT (lottery_type, draw_number) DO UPDATE
                        SET prize_grades_json = CASE
                            WHEN EXCLUDED.prize_grades_json <> '[]' THEN EXCLUDED.prize_grades_json
                            ELSE nc_lottery_draws.prize_grades_json
                        END";

                var redJson = JsonSerializer.Serialize(draw.red_numbers);
                var blueJson = JsonSerializer.Serialize(draw.blue_numbers);
                var prizeJson = JsonSerializer.Serialize(draw.prize_grades);

                var result = await _db.ExecuteNonQueryAsync(sql, ct,
                    lotteryType.ToString(),
                    draw.draw_number,
                    DateTime.TryParse(draw.draw_date, out var date) ? date : DateTime.MinValue,
                    redJson,
                    blueJson,
                    draw.sales_amount,
                    draw.pool_money,
                    prizeJson,
                    draw.prize_description ?? "");

                if (result.IsSuccess && result.Value > 0)
                {
                    insertedCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"插入记录 {draw.draw_number} 失败: {ex.Message}");
            }
        }

        return insertedCount;
    }

    private NumberStatistics CalculateNumberStatistics(int number, List<LotteryDraw> draws, bool isRed, LotteryType lotteryType)
    {
        var totalDraws = draws.Count;
        var frequency = 0;
        var currentMissing = 0;
        var maxMissing = 0;
        var tempMissing = 0;
        var lastAppearPeriod = "";
        var recentFrequency = 0;

        // 近30期统计
        var recentCount = Math.Min(30, totalDraws);

        for (int i = 0; i < totalDraws; i++)
        {
            var draw = draws[i];
            var numbers = isRed ? draw.GetRedNumbers() : draw.GetBlueNumbers();

            if (numbers.Contains(number))
            {
                frequency++;
                if (string.IsNullOrEmpty(lastAppearPeriod))
                {
                    lastAppearPeriod = draw.DrawNumber;
                }
                if (i < recentCount)
                {
                    recentFrequency++;
                }
                tempMissing = 0;
            }
            else
            {
                tempMissing++;
                maxMissing = Math.Max(maxMissing, tempMissing);
            }
        }

        // 计算当前遗漏（从最新一期开始）
        currentMissing = 0;
        foreach (var draw in draws)
        {
            var numbers = isRed ? draw.GetRedNumbers() : draw.GetBlueNumbers();
            if (numbers.Contains(number))
            {
                break;
            }
            currentMissing++;
        }

        var frequencyRate = totalDraws > 0 ? (decimal)frequency / totalDraws * 100 : 0;
        var averageMissing = frequency > 0 ? (decimal)(totalDraws - frequency) / frequency : totalDraws;

        return new NumberStatistics
        {
            Number = number,
            Frequency = frequency,
            FrequencyRate = Math.Round(frequencyRate, 2),
            CurrentMissing = currentMissing,
            AverageMissing = Math.Round(averageMissing, 2),
            MaxMissing = maxMissing,
            LastAppearPeriod = lastAppearPeriod,
            RecentFrequency = recentFrequency
        };
    }

    #endregion

    #region DTO 类

    /// <summary>
    /// 用于JSON反序列化的DTO
    /// </summary>
    private class LotteryDrawDto
    {
        public string draw_number { get; set; } = "";
        public string draw_date { get; set; } = "";
        public int[] red_numbers { get; set; } = Array.Empty<int>();
        public int[] blue_numbers { get; set; } = Array.Empty<int>();
        public decimal sales_amount { get; set; }
        public decimal pool_money { get; set; }
        public object[] prize_grades { get; set; } = Array.Empty<object>();
        public string? prize_description { get; set; }
    }

    /// <summary>
    /// 数据库查询结果DTO
    /// </summary>
    private class LotteryDrawRecord
    {
        public int id { get; set; }
        public string lottery_type { get; set; } = "";
        public string draw_number { get; set; } = "";
        public DateTime draw_date { get; set; }
        public string red_numbers_json { get; set; } = "[]";
        public string blue_numbers_json { get; set; } = "[]";
        public decimal sales_amount { get; set; }
        public decimal pool_money { get; set; }
        public string prize_grades_json { get; set; } = "[]";
        public string? prize_description { get; set; }
        public DateTime created_at { get; set; }

        public LotteryDraw ToLotteryDraw()
        {
            var lotteryType = Enum.TryParse<LotteryType>(lottery_type, true, out var lt) ? lt : LotteryType.SSQ;
            return new LotteryDraw
            {
                Id = id,
                LotteryType = lotteryType,
                DrawNumber = draw_number,
                DrawDate = draw_date,
                RedNumbersJson = red_numbers_json,
                BlueNumbersJson = blue_numbers_json,
                SalesAmount = sales_amount,
                PoolMoney = pool_money,
                PrizeGradesJson = prize_grades_json,
                PrizeDescription = prize_description,
                CreatedAt = created_at
            };
        }
    }

    #endregion
}
