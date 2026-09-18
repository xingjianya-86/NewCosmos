using System.Diagnostics;

namespace NewCosmos.Helpers;

/// <summary>
/// 应用自重启：以分离进程延迟拉起自身，规避单实例互斥锁
/// （立即启动会在旧进程退出前被单例检查杀掉）。调用方随后应立即 Application.Current.Quit()。
/// </summary>
public static class AppRestartHelper
{
    /// <summary>安排 delaySeconds 秒后重新启动应用；返回是否安排成功</summary>
    public static bool Restart(int delaySeconds = 3)
    {
        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                Serilog.Log.Warning("[AppRestartHelper] 无法获取当前进程路径，取消自动重启");
                return false;
            }

            var waitSeconds = Math.Max(1, delaySeconds);
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c ping 127.0.0.1 -n {waitSeconds + 1} >nul & start \"\" \"{exePath}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            Process.Start(psi);
            Serilog.Log.Information("[AppRestartHelper] 已安排 {Seconds} 秒后重启: {Exe}", waitSeconds, exePath);
            return true;
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "[AppRestartHelper] 安排重启失败");
            return false;
        }
    }
}
