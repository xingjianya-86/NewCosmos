using System.Diagnostics;

namespace NewCosmos.Services.Lottery;

/// <summary>
/// 彩票 Python 子进程执行单点：python-embed 解析、代理环境透传、PGPASSWORD 注入（密码不进命令行）、
/// 输出/错误收集、超时终止。
/// 此前 LotteryDataService.LotteryPredictService 各持一份复制实现，且数据侧那份
/// 【无超时无 Kill】——fetch_history 网络挂起时子进程永久滞留（2026-11 合并治理）。
/// </summary>
public static class PythonScriptRunner
{
    /// <summary>python-embed 路径（安装目录）。</summary>
    public static readonly string PythonExe = Path.Combine(AppContext.BaseDirectory, "python-embed", "python.exe");

    /// <summary>脚本目录（Scripts/Lottery）。</summary>
    public static readonly string ScriptsDir = Path.Combine(AppContext.BaseDirectory, "Scripts", "Lottery");

    /// <summary>
    /// 执行 python 脚本并等待退出。timeout 到时 Kill 整棵进程树并返回失败。
    /// </summary>
    /// <param name="args">命令行参数</param>
    /// <param name="workingDirectory">工作目录（默认 Scripts/Lottery）</param>
    /// <param name="ct">调用方取消令牌</param>
    /// <param name="timeout">超时（null = 不限，仅响应 ct）</param>
    /// <param name="dbPassword">数据库密码（注入 PGPASSWORD；null = 不注入）。由调用方从 IConfigService 取，密码不进命令行。</param>
    public static async Task<(bool Success, string Output, string Error)> RunAsync(
        string args,
        string workingDirectory,
        CancellationToken ct,
        TimeSpan? timeout = null,
        string? dbPassword = null)
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
                WorkingDirectory = workingDirectory
            };

            // 透传代理环境变量（fetch_history 走外网时依赖本地代理）
            var httpProxy = Environment.GetEnvironmentVariable("HTTP_PROXY") ?? Environment.GetEnvironmentVariable("http_proxy");
            var httpsProxy = Environment.GetEnvironmentVariable("HTTPS_PROXY") ?? Environment.GetEnvironmentVariable("https_proxy");
            if (!string.IsNullOrEmpty(httpProxy))
                startInfo.EnvironmentVariables["HTTP_PROXY"] = httpProxy;
            if (!string.IsNullOrEmpty(httpsProxy))
                startInfo.EnvironmentVariables["HTTPS_PROXY"] = httpsProxy;

            if (!string.IsNullOrEmpty(dbPassword))
                startInfo.EnvironmentVariables["PGPASSWORD"] = dbPassword;

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
                try { process.Kill(entireProcessTree: true); } catch { /* 进程可能已退出 */ }
                return (false, "", $"执行超时（{timeout!.Value.TotalSeconds:F0} 秒），已终止");
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
}
