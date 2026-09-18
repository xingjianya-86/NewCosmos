using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using NewCosmos.Constants;
using NewCosmos.Models.Lottery;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Lottery;

/// <summary>
/// 彩票预测服务实现
/// 负责调用预测算法生成号码
/// </summary>
public class LotteryPredictService : ILotteryPredictService
{
    private readonly IDatabaseService _db;
    private readonly ILotteryDataService _lotteryDataService;
    private readonly ILoggerService _logger;
    private readonly IConfigService _configService;

    private static readonly string ScriptsDir = Path.Combine(AppContext.BaseDirectory, "Scripts", "Lottery");

    public LotteryPredictService(
        IDatabaseService db,
        ILotteryDataService lotteryDataService,
        ILoggerService logger,
        IConfigService configService)
    {
        _db = db;
        _lotteryDataService = lotteryDataService;
        _logger = logger;
        _configService = configService;
    }

    /// <summary>
    /// 机选号码（随机生成）
    /// </summary>
    public Task<Result<List<PredictionResult>>> RandomPickAsync(LotteryType lotteryType, int count = 1, CancellationToken ct = default)
    {
        try
        {
            var results = new List<PredictionResult>();
            var random = new Random();

            for (int i = 0; i < count; i++)
            {
                var redCount = lotteryType.GetRedCount();
                var redMax = lotteryType.GetRedMax();
                var blueCount = lotteryType.GetBlueCount();
                var blueMax = lotteryType.GetBlueMax();

                var reds = Enumerable.Range(1, redMax)
                    .OrderBy(_ => random.Next())
                    .Take(redCount)
                    .OrderBy(x => x)
                    .ToArray();

                var blues = Enumerable.Range(1, blueMax)
                    .OrderBy(_ => random.Next())
                    .Take(blueCount)
                    .OrderBy(x => x)
                    .ToArray();

                var prediction = new PredictionResult
                {
                    LotteryType = lotteryType,
                    AlgorithmVersion = "random",
                    AlgorithmName = "随机机选",
                    ConfidenceScore = 0
                };
                prediction.SetPredictedReds(reds);
                prediction.SetPredictedBlues(blues);

                results.Add(prediction);
            }

            return Task.FromResult(Result<List<PredictionResult>>.Success(results));
        }
        catch (Exception ex)
        {
            _logger.Error($"随机机选失败: {ex.Message}");
            return Task.FromResult(Result<List<PredictionResult>>.FromException<List<PredictionResult>>(ex));
        }
    }

    /// <summary>
    /// 融合算法预测（原版机器学习 LSTM 0.6 + LotteryML 0.4，评分级融合）。
    /// 内部生成 100 注候选，按位次投票选出 count 注；仅返回最终注。
    /// </summary>
    public async Task<Result<List<PredictionResult>>> PredictByFusionAsync(LotteryType lotteryType, int count = 1, CancellationToken ct = default)
    {
        try
        {
            var type = lotteryType.ToString().ToLower();
            var args = $"fused_predict.py --type {type} --count {count} " +
                       $"--ml-model-dir \"{ModelsDir}\" --lml-model-dir \"{GetModelDir()}\" {GetDbArgs()}";
            var (success, output, error) = await RunPythonScriptAsync(
                args, ct, TimeSpan.FromSeconds(LotteryConstants.PREDICT_TIMEOUT_SECONDS));
            if (!success)
            {
                _logger.Error($"融合算法预测失败: {error}");
                return Result<List<PredictionResult>>.Failure("FUSION_FAILED",
                    string.IsNullOrWhiteSpace(error) ? "融合算法预测失败（请确认模型已训练）" : error);
            }
            return ParsePredictions(output, lotteryType, "fusion", "融合算法");
        }
        catch (Exception ex)
        {
            _logger.Error($"融合算法预测异常: {ex.Message}");
            return Result<List<PredictionResult>>.FromException<List<PredictionResult>>(ex);
        }
    }

    /// <summary>融合算法所需两套模型是否都已训练（LSTM .keras + LotteryML .joblib）</summary>
    public bool IsFusionModelTrained(LotteryType lotteryType)
    {
        var type = lotteryType.ToString().ToLower();
        var lstm = Path.Combine(ModelsDir, $"{type}_model.keras");
        var lml = Path.Combine(GetModelDir(), $"{type}_models.joblib");
        return File.Exists(lstm) && File.Exists(lml);
    }

    /// <summary>训练融合算法所需的两套模型：原版 LSTM（含历史购买负样本）+ LotteryML</summary>
    public async Task<Result<bool>> TrainFusionModelAsync(LotteryType lotteryType, CancellationToken ct = default)
    {
        try
        {
            var type = lotteryType.ToString().ToLower();
            var dbOptions = _configService.GetDatabaseOptions();

            // 1) 原版机器学习 LSTM（历史购买未命中作负样本）
            var lstmArgs = $"train_model.py --type {type} " +
                           $"--db-host {dbOptions.Host} --db-port {dbOptions.Port} " +
                           $"--db-name {dbOptions.DatabaseName} --db-user {dbOptions.Username} " +
                           $"--model-dir \"{ModelsDir}\" --epochs 50 --batch-size 32 --use-user-purchases";
            var (ok1, _, err1) = await RunPythonScriptAsync(
                lstmArgs, ct, TimeSpan.FromSeconds(LotteryConstants.TRAIN_TIMEOUT_SECONDS));
            if (!ok1)
            {
                var msg = string.IsNullOrWhiteSpace(err1) ? "LSTM 训练失败" : err1;
                _logger.Error($"融合模型-LSTM训练失败: {msg}");
                return Result<bool>.Failure("TRAIN_FAILED", msg);
            }

            // 2) LotteryML 逐位概率模型
            var lmlArgs = $"LotteryML_clean\\predictor.py --type {type} --force-retrain --train-only " +
                          $"--model-dir \"{GetModelDir()}\" {GetDbArgs()}";
            var (ok2, _, err2) = await RunPythonScriptAsync(
                lmlArgs, ct, TimeSpan.FromSeconds(LotteryConstants.TRAIN_TIMEOUT_SECONDS));
            if (!ok2)
            {
                var msg = string.IsNullOrWhiteSpace(err2) ? "LotteryML 训练失败" : err2;
                _logger.Error($"融合模型-LotteryML训练失败: {msg}");
                return Result<bool>.Failure("TRAIN_FAILED", msg);
            }

            _logger.Info($"融合模型训练完成: {lotteryType.GetDisplayName()}");
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.Error($"融合模型训练异常: {ex.Message}");
            return Result<bool>.FromException<bool>(ex);
        }
    }

    /// <summary>
    /// 保存预测记录到数据库
    /// </summary>
    public async Task<Result<int>> SavePredictionsAsync(List<PredictionResult> predictions, CancellationToken ct = default)
    {
        try
        {
            int savedCount = 0;

            foreach (var pred in predictions)
            {
                var sql = @"
                    INSERT INTO nc_lottery_predictions 
                    (lottery_type, draw_number, red_numbers, blue_numbers, algorithm_version, algorithm_name, confidence_score, created_at)
                    VALUES ($1, $2, $3, $4, $5, $6, $7, NOW())
                    RETURNING id";

                var redJson = JsonSerializer.Serialize(pred.GetPredictedReds());
                var blueJson = JsonSerializer.Serialize(pred.GetPredictedBlues());

                var result = await _db.ExecuteScalarAsync<int>(sql, ct,
                    pred.LotteryType.ToString(),
                    pred.TargetDrawNumber,
                    redJson,
                    blueJson,
                    pred.AlgorithmVersion,
                    pred.AlgorithmName,
                    pred.ConfidenceScore);

                if (result.IsSuccess)
                {
                    savedCount++;
                }
            }

            return Result<int>.Success(savedCount);
        }
        catch (Exception ex)
        {
            _logger.Error($"保存预测记录失败: {ex.Message}");
            return Result<int>.FromException<int>(ex);
        }
    }

    /// <summary>
    /// 获取历史预测记录
    /// </summary>
    public async Task<Result<List<PredictionResult>>> GetPredictionHistoryAsync(LotteryType lotteryType, int pageNo = 1, int pageSize = 30, CancellationToken ct = default)
    {
        try
        {
            var offset = (pageNo - 1) * pageSize;
            var sql = @"
                SELECT id, lottery_type, draw_number, red_numbers, blue_numbers,
                       algorithm_version, algorithm_name, confidence_score,
                       is_hit_red, is_hit_blue, red_hit_count, blue_hit_count,
                       prize_level, prize_amount, created_at
                FROM nc_lottery_predictions
                WHERE lottery_type = $1
                ORDER BY created_at DESC
                LIMIT $2 OFFSET $3";

            var result = await _db.QueryAsync<PredictionRecord>(sql, ct, lotteryType.ToString(), pageSize, offset);
            if (!result.IsSuccess)
            {
                return Result<List<PredictionResult>>.Failure(result.ErrorCode!, result.Message!);
            }

            var predictions = result.Value.Select(r => r.ToPredictionResult()).ToList();
            return Result<List<PredictionResult>>.Success(predictions);
        }
        catch (Exception ex)
        {
            _logger.Error($"获取预测历史失败: {ex.Message}");
            return Result<List<PredictionResult>>.FromException<List<PredictionResult>>(ex);
        }
    }

    /// <summary>
    /// 验证预测结果
    /// </summary>
    public async Task<Result<int>> VerifyPredictionsAsync(LotteryType lotteryType, string drawNumber, CancellationToken ct = default)
    {
        try
        {
            // 获取实际开奖结果
            var drawResult = await _lotteryDataService.GetDrawByNumberAsync(lotteryType, drawNumber, ct);
            if (!drawResult.IsSuccess || drawResult.Value == null)
            {
                return Result<int>.Failure("DRAW_NOT_FOUND", "未找到该期开奖记录");
            }

            var actualDraw = drawResult.Value;
            var actualReds = actualDraw.GetRedNumbers();
            var actualBlues = actualDraw.GetBlueNumbers();

            // 获取该期的预测记录
            var sql = @"
                SELECT id, lottery_type, draw_number, red_numbers, blue_numbers,
                       algorithm_version, algorithm_name, confidence_score,
                       is_hit_red, is_hit_blue, red_hit_count, blue_hit_count,
                       prize_level, prize_amount, created_at
                FROM nc_lottery_predictions
                WHERE lottery_type = $1 AND draw_number = $2";

            var predResult = await _db.QueryAsync<PredictionRecord>(sql, ct, lotteryType.ToString(), drawNumber);
            if (!predResult.IsSuccess || predResult.Value.Count == 0)
            {
                return Result<int>.Success(0);
            }

            int verifiedCount = 0;

            foreach (var predRecord in predResult.Value)
            {
                var predictedReds = JsonSerializer.Deserialize<int[]>(predRecord.red_numbers) ?? Array.Empty<int>();
                var predictedBlues = JsonSerializer.Deserialize<int[]>(predRecord.blue_numbers) ?? Array.Empty<int>();

                // 计算命中数
                var redHitCount = predictedReds.Intersect(actualReds).Count();
                var blueHitCount = predictedBlues.Intersect(actualBlues).Count();

                // 判断是否中奖（简化版）
                var prizeLevel = DeterminePrizeLevel(lotteryType, redHitCount, blueHitCount);

                // 更新预测记录
                var updateSql = @"
                    UPDATE nc_lottery_predictions
                    SET is_hit_red = $1, is_hit_blue = $2, red_hit_count = $3, blue_hit_count = $4,
                        prize_level = $5, prize_amount = $6
                    WHERE id = $7";

                await _db.ExecuteNonQueryAsync(updateSql, ct,
                    redHitCount > 0,
                    blueHitCount > 0,
                    redHitCount,
                    blueHitCount,
                    prizeLevel,
                    GetPrizeAmount(lotteryType, prizeLevel),
                    predRecord.id);

                verifiedCount++;
            }

            return Result<int>.Success(verifiedCount);
        }
        catch (Exception ex)
        {
            _logger.Error($"验证预测结果失败: {ex.Message}");
            return Result<int>.FromException<int>(ex);
        }
    }

    #region 私有方法

    private static readonly string PythonExe = Path.Combine(AppContext.BaseDirectory, "python-embed", "python.exe");

    /// <summary>原版 ML（TensorFlow LSTM）模型目录</summary>
    private static readonly string ModelsDir = Path.Combine(AppContext.BaseDirectory, "Scripts", "Lottery", "models");

    /// <summary>
    /// 构建数据库连接参数字符串（密码不在此传递，改由 PGPASSWORD 环境变量注入）
    /// </summary>
    private string GetDbArgs()
    {
        try
        {
            var dbOptions = _configService.GetDatabaseOptions();
            return $"--db-host {dbOptions.Host} --db-port {dbOptions.Port} " +
                   $"--db-name {dbOptions.DatabaseName} " +
                   $"--db-user {dbOptions.Username}";
        }
        catch
        {
            return string.Empty;
        }
    }

    /// <summary>把数据库密码注入子进程环境变量 PGPASSWORD（避免明文出现在命令行）</summary>
    private void TrySetDbPasswordEnv(ProcessStartInfo startInfo)
    {
        try
        {
            var pwd = _configService.GetDatabaseOptions().Password;
            if (!string.IsNullOrEmpty(pwd))
                startInfo.EnvironmentVariables["PGPASSWORD"] = pwd;
        }
        catch { }
    }

    /// <summary>LotteryML 模型目录（用户 AppData，安装目录只读时仍可写）</summary>
    private static string GetModelDir()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NewCosmos", "LotteryML");
        return dir;
    }

    private async Task<(bool Success, string Output, string Error)> RunPythonScriptAsync(
        string args, CancellationToken ct, TimeSpan? timeout = null)
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

            // 传递代理环境变量
            var httpProxy = Environment.GetEnvironmentVariable("HTTP_PROXY") ?? Environment.GetEnvironmentVariable("http_proxy");
            var httpsProxy = Environment.GetEnvironmentVariable("HTTPS_PROXY") ?? Environment.GetEnvironmentVariable("https_proxy");
            if (!string.IsNullOrEmpty(httpProxy))
                startInfo.EnvironmentVariables["HTTP_PROXY"] = httpProxy;
            if (!string.IsNullOrEmpty(httpsProxy))
                startInfo.EnvironmentVariables["HTTPS_PROXY"] = httpsProxy;

            TrySetDbPasswordEnv(startInfo);

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);

            using var timeoutCts = timeout.HasValue
                ? CancellationTokenSource.CreateLinkedTokenSource(ct)
                : null;
            timeoutCts?.CancelAfter(timeout!.Value);

            try
            {
                await process.WaitForExitAsync(timeoutCts?.Token ?? ct);
            }
            catch (OperationCanceledException) when (timeoutCts is { IsCancellationRequested: true } && !ct.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                var msg = $"执行超时（{timeout!.Value.TotalSeconds:F0} 秒），已终止";
                _logger.Warn($"Python 脚本{msg}: {args}");
                return (false, "", msg);
            }

            var output = await outputTask;
            var error = await errorTask;

            return (process.ExitCode == 0, output, error);
        }
        catch (Exception ex)
        {
            return (false, "", ex.Message);
        }
    }

    private Result<List<PredictionResult>> ParsePredictions(string output, LotteryType lotteryType, string algorithm, string algorithmName)
    {
        try
        {
            var json = JsonSerializer.Deserialize<JsonElement>(ExtractJson(output));
            var predictions = json.GetProperty("predictions");

            var results = new List<PredictionResult>();

            foreach (var pred in predictions.EnumerateArray())
            {
                var redNumbers = pred.GetProperty("red_numbers")
                    .EnumerateArray()
                    .Select(x => x.GetInt32())
                    .ToArray();

                var blueNumbers = pred.GetProperty("blue_numbers")
                    .EnumerateArray()
                    .Select(x => x.GetInt32())
                    .ToArray();

                var confidence = pred.TryGetProperty("confidence", out var conf) ? conf.GetDouble() : 0;

                var prediction = new PredictionResult
                {
                    LotteryType = lotteryType,
                    AlgorithmVersion = algorithm,
                    AlgorithmName = algorithmName,
                    ConfidenceScore = (decimal)confidence
                };
                prediction.SetPredictedReds(redNumbers);
                prediction.SetPredictedBlues(blueNumbers);

                results.Add(prediction);
            }

            return Result<List<PredictionResult>>.Success(results);
        }
        catch (Exception ex)
        {
            _logger.Error($"解析预测结果失败: {ex.Message}");
            var snippet = string.IsNullOrEmpty(output) ? "" : (output.Length > 800 ? output[..800] : output);
            _logger.Warn($"预测输出片段（供排查）: {snippet}");
            return Result<List<PredictionResult>>.Failure("PARSE_ERROR", "解析预测结果失败");
        }
    }

    /// <summary>
    /// 从进程 stdout 中提取 JSON 对象：容忍前置/后置日志行（取首个 '{' 至末个 '}'）。
    /// </summary>
    private static string ExtractJson(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return output ?? string.Empty;
        var start = output.IndexOf('{');
        var end = output.LastIndexOf('}');
        return start >= 0 && end > start ? output[start..(end + 1)] : output;
    }

    private int DeterminePrizeLevel(LotteryType lotteryType, int redHitCount, int blueHitCount)
    {
        // 简化版奖级判定
        if (lotteryType == LotteryType.SSQ)
        {
            // 双色球规则
            if (redHitCount == 6 && blueHitCount == 1) return 1;  // 一等奖
            if (redHitCount == 6) return 2;                       // 二等奖
            if (redHitCount == 5 && blueHitCount == 1) return 3;  // 三等奖
            if (redHitCount == 5 || (redHitCount == 4 && blueHitCount == 1)) return 4; // 四等奖
            if (redHitCount == 4 || (redHitCount == 3 && blueHitCount == 1)) return 5; // 五等奖
            if (blueHitCount == 1) return 6;                      // 六等奖
            return 0; // 未中奖
        }
        else // DLT
        {
            // 大乐透规则（简化）
            if (redHitCount == 5 && blueHitCount == 2) return 1;
            if (redHitCount == 5 && blueHitCount == 1) return 2;
            if (redHitCount == 5) return 3;
            if (redHitCount == 4 && blueHitCount == 2) return 4;
            if (redHitCount == 4 && blueHitCount == 1) return 5;
            if (redHitCount == 3 && blueHitCount == 2) return 5;
            return 0;
        }
    }

    private decimal GetPrizeAmount(LotteryType lotteryType, int prizeLevel)
    {
        // 简化版奖金（实际应从数据库或配置读取）
        return prizeLevel switch
        {
            1 => 5000000,    // 一等奖 500万
            2 => 200000,     // 二等奖 20万
            3 => 3000,       // 三等奖 3000
            4 => 200,        // 四等奖 200
            5 => 10,         // 五等奖 10
            6 => 5,          // 六等奖 5
            _ => 0
        };
    }

    #endregion

    #region DTO 类

    private class PredictionRecord
    {
        public int id { get; set; }
        public string lottery_type { get; set; } = "";
        public string draw_number { get; set; } = "";
        public string red_numbers { get; set; } = "[]";
        public string blue_numbers { get; set; } = "[]";
        public string algorithm_version { get; set; } = "";
        public string algorithm_name { get; set; } = "";
        public decimal confidence_score { get; set; }
        public bool? is_hit_red { get; set; }
        public bool? is_hit_blue { get; set; }
        public int? red_hit_count { get; set; }
        public int? blue_hit_count { get; set; }
        public int? prize_level { get; set; }
        public decimal? prize_amount { get; set; }
        public DateTime created_at { get; set; }

        public PredictionResult ToPredictionResult()
        {
            var lotteryType = Enum.TryParse<LotteryType>(lottery_type, true, out var lt) ? lt : LotteryType.SSQ;
            return new PredictionResult
            {
                Id = id,
                LotteryType = lotteryType,
                TargetDrawNumber = draw_number,
                PredictedRedJson = red_numbers,
                PredictedBlueJson = blue_numbers,
                AlgorithmVersion = algorithm_version,
                AlgorithmName = algorithm_name,
                ConfidenceScore = confidence_score,
                IsHitRed = is_hit_red,
                IsHitBlue = is_hit_blue,
                RedHitCount = red_hit_count,
                BlueHitCount = blue_hit_count,
                PrizeLevel = prize_level,
                PrizeAmount = prize_amount,
                CreatedAt = created_at
            };
        }
    }

    #endregion

    #region Python 执行

    /// <summary>
    /// 运行 Python 脚本并实时返回输出（用于长时间任务如训练）
    /// </summary>
    public async Task<Result<string>> RunPythonScriptWithProgressAsync(string args, Action<string>? onOutput = null, CancellationToken ct = default)
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

            var httpProxy = Environment.GetEnvironmentVariable("HTTP_PROXY") ?? Environment.GetEnvironmentVariable("http_proxy");
            var httpsProxy = Environment.GetEnvironmentVariable("HTTPS_PROXY") ?? Environment.GetEnvironmentVariable("https_proxy");
            if (!string.IsNullOrEmpty(httpProxy))
                startInfo.EnvironmentVariables["HTTP_PROXY"] = httpProxy;
            if (!string.IsNullOrEmpty(httpsProxy))
                startInfo.EnvironmentVariables["HTTPS_PROXY"] = httpsProxy;

            TrySetDbPasswordEnv(startInfo);

            using var process = new Process { StartInfo = startInfo };
            process.Start();

            // 使用 DataReceivedEventArgs 事件驱动方式读取，避免死锁
            var outputBuilder = new global::System.Text.StringBuilder();
            var errorBuilder = new global::System.Text.StringBuilder();
            var outputLock = new object();

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    lock (outputLock)
                    {
                        outputBuilder.AppendLine(e.Data);
                    }
                    onOutput?.Invoke(e.Data);
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    lock (outputLock)
                    {
                        errorBuilder.AppendLine(e.Data);
                    }
                }
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(ct);

            // 等待异步读取完成
            process.CancelOutputRead();
            process.CancelErrorRead();

            var output = outputBuilder.ToString();

            if (process.ExitCode == 0)
            {
                return Result<string>.Success(output);
            }
            else
            {
                var error = errorBuilder.ToString();
                return Result<string>.Failure("PYTHON_ERROR", string.IsNullOrWhiteSpace(error) ? output : error);
            }
        }
        catch (Exception ex)
        {
            return Result<string>.FromException<string>(ex);
        }
    }

    #endregion
}
