namespace NewCosmos.Constants;

/// <summary>
/// 推送打印队列路由常量（config/app.ini [Print] 节，方案A：本地预览 + 打印动作推送 Windows Agent）。
/// 开关只决定「打印动作」的路由，不改变生成/预览链路。
/// </summary>
public static class PrintQueueConstants
{
    /// <summary>本地打印（默认，零回归）：打印动作直接走 PrintExecuteService.ExecutePrintAsync</summary>
    public const string OutputModeLocal = "Local";

    /// <summary>队列推送：打印动作写入 nc_biz_print_jobs 并固化 target_device_id，由 Windows Agent 认领打印</summary>
    public const string OutputModeQueue = "Queue";

    /// <summary>Queue 模式等待远端回报的上限（秒），超时按 QueueFallbackToLocal 降级</summary>
    public const int DefaultWaitSeconds = 60;

    /// <summary>QueueWaitSeconds 合法下限</summary>
    public const int MinWaitSeconds = 5;

    /// <summary>QueueWaitSeconds 合法上限</summary>
    public const int MaxWaitSeconds = 600;

    /// <summary>Queue 模式任务失败/超时时是否回退本地打印（默认回退）</summary>
    public const bool DefaultFallbackToLocal = true;

    /// <summary>轮询 nc_biz_print_jobs 状态的间隔（毫秒）</summary>
    public const int PollIntervalMs = 1000;
}
