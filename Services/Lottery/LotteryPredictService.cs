using System.Diagnostics;
using System.Text.Json;
using NewCosmos.Constants;
using NewCosmos.Models.Lottery;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;

namespace NewCosmos.Services.Lottery;

/// <summary>
/// 彩票预测服务实现
/// 负责调用预测算法生成号码
/// </summary>
public class LotteryPredictService : ILotteryPredictService
{
    private readonly ILoggerService _logger;
    private readonly IConfigService _configService;

    private static readonly string ScriptsDir = Path.Combine(AppContext.BaseDirectory, "Scripts", "Lottery");

    public LotteryPredictService(
        ILoggerService logger,
        IConfigService configService)
    {
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
    /// 机器学习(LSTM)预测：内嵌原版工程（KittenCN/predict_Lottery_ticket，GPL-3.0，
    /// 「基于tensorflow lstm模型的彩票预测」）统一入口 lstm_entry.py——
    /// 从 nc_lottery_draws 导出历史 → 取最后 window 期作为输入 → 模型逐位概率 →
    /// 负样本降权（未中奖购彩高频号）→ 温度采样 N 注互异。
    /// </summary>
    public async Task<Result<List<PredictionResult>>> PredictByLstmAsync(LotteryType lotteryType, int count = 1, CancellationToken ct = default)
    {
        try
        {
            EnsureSeededLstmModels();
            if (!IsLstmModelTrained(lotteryType))
            {
                return Result<List<PredictionResult>>.Failure("MODEL_NOT_TRAINED",
                    $"{lotteryType.GetDisplayName()}的LSTM模型未训练，请先在首页点击「训练预测模型」");
            }
            var type = lotteryType.ToString().ToLower();
            var args = $"lstm_entry.py predict --name {type} --count {count} " +
                       $"--window {LotteryConstants.LSTM_WINDOW} {GetDbArgs()}";
            var (success, output, error) = await RunPythonScriptAsync(
                args, ct, TimeSpan.FromSeconds(LotteryConstants.PREDICT_TIMEOUT_SECONDS));
            if (!success)
            {
                _logger.Error($"LSTM预测失败: {error}");
                return Result<List<PredictionResult>>.Failure("LSTM_FAILED",
                    string.IsNullOrWhiteSpace(error) ? "LSTM预测失败（请确认模型已训练）" : error);
            }
            return ParsePredictions(output, lotteryType, "lstm", LotteryConstants.LSTM_ALGORITHM_NAME);
        }
        catch (Exception ex)
        {
            _logger.Error($"LSTM预测异常: {ex.Message}");
            return Result<List<PredictionResult>>.FromException<List<PredictionResult>>(ex);
        }
    }

    /// <summary>LSTM 模型是否已训练（运行主目录 model/&lt;code&gt;/window_W/red+blue.keras）</summary>
    public bool IsLstmModelTrained(LotteryType lotteryType)
    {
        var dir = Path.Combine(LstmHome, "model", lotteryType.ToString().ToLower(),
            $"window_{LotteryConstants.LSTM_WINDOW}");
        return File.Exists(Path.Combine(dir, "red.keras")) && File.Exists(Path.Combine(dir, "blue.keras"));
    }

    /// <summary>
    /// 训练 LSTM 模型：lstm_entry.py train——从开奖数据下载链路（nc_lottery_draws）导出全量历史 +
    /// GitHub 默认超参（epochs/batch/学习率/早停全原版默认）+ 窗口 LSTM_WINDOW。
    /// </summary>
    public async Task<Result<bool>> TrainLstmModelAsync(LotteryType lotteryType, CancellationToken ct = default)
    {
        try
        {
            EnsureSeededLstmModels();
            var type = lotteryType.ToString().ToLower();
            var args = $"lstm_entry.py train --name {type} --window {LotteryConstants.LSTM_WINDOW} {GetDbArgs()}";
            var (ok, output, err) = await RunPythonScriptAsync(
                args, ct, TimeSpan.FromSeconds(LotteryConstants.TRAIN_TIMEOUT_SECONDS));
            if (!ok)
            {
                var msg = string.IsNullOrWhiteSpace(err) ? "LSTM训练失败" : err;
                _logger.Error($"LSTM训练失败: {msg}");
                return Result<bool>.Failure("TRAIN_FAILED", msg);
            }

            _logger.Info($"LSTM训练完成: {lotteryType.GetDisplayName()}（窗口{LotteryConstants.LSTM_WINDOW}）");
            return Result<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.Error($"LSTM训练异常: {ex.Message}");
            return Result<bool>.FromException<bool>(ex);
        }
    }

    /// <summary>
    /// 播种：把随包预训练模型从安装目录复制到可写运行主目录（Program Files 只读安全）；
    /// 已有该彩种的运行模型不覆盖。
    /// </summary>
    private static void EnsureSeededLstmModels()
    {
        try
        {
            foreach (var code in new[] { "ssq", "dlt" })
            {
                var bundle = Path.Combine(AppContext.BaseDirectory, "Scripts", "Lottery",
                    LotteryConstants.LSTM_PROJECT_DIR, "model", code);
                var target = Path.Combine(LstmHome, "model", code);
                if (Directory.Exists(bundle) && !Directory.Exists(target))
                {
                    CopyDirectory(bundle, target);
                }
            }
        }
        catch
        {
            // 播种失败不阻断：训练路径会自行创建模型目录
        }
    }

    private static void CopyDirectory(string src, string dst)
    {
        Directory.CreateDirectory(dst);
        foreach (var dir in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(dst, Path.GetRelativePath(src, dir)));
        foreach (var file in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(dst, Path.GetRelativePath(src, file)), true);
    }

    #region 私有方法

    private static readonly string PythonExe = Path.Combine(AppContext.BaseDirectory, "python-embed", "python.exe");

    /// <summary>
    /// LSTM 运行主目录（data/model/predict/logs；与 lstm_entry.py 的 _resolve_home 同口径）。
    /// 固定 AppData：安装目录只读时数据/模型仍可写。
    /// </summary>
    private static readonly string LstmHome = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NewCosmos", "LotteryLSTM");

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

    #endregion
}
