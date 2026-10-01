namespace NewCosmos.Services.Domain.Printing;

/// <summary>
/// PC 端推送打印代理：后台轮询 nc_biz_print_jobs，认领并执行打印。
/// 仅 Windows 端启用（依赖 Office/WPS COM）。
/// </summary>
public interface IPrintAgentService
{
    bool IsRunning { get; }

    /// <summary>启动轮询（幂等，可重复调用）。</summary>
    void Start();

    /// <summary>停止轮询。</summary>
    void Stop();
}
