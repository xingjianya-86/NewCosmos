using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.Printing;

/// <summary>
/// 打印动作路由结果（方案A：本地预览 + 打印动作推送 Windows Agent）。
/// </summary>
public sealed class QueuePrintOutcome
{
    /// <summary>true=走了定向队列推送并等到远端回报；false=本地执行/现状入队</summary>
    public bool UsedQueue { get; init; }

    /// <summary>任务号（队列路径 = nc_biz_print_jobs.job_no；本地打印路径 = 留痕批次号）</summary>
    public string JobNo { get; init; } = string.Empty;

    /// <summary>本地执行留痕（UsedQueue=false 且本地动作为打印时有值；推送入口为 null）</summary>
    public PrintRecord? LocalRecord { get; init; }

    /// <summary>本地打印路径的包装。</summary>
    public static Result<QueuePrintOutcome> FromLocal(Result<PrintRecord> record)
        => record.IsSuccess
            ? Result.Success(new QueuePrintOutcome
            {
                UsedQueue = false,
                LocalRecord = record.Value,
                JobNo = record.Value?.BatchNo ?? string.Empty
            })
            : Result.Failure<QueuePrintOutcome>(record.ErrorCode, record.Message);
}

/// <summary>
/// 打印动作统一路由（config/app.ini [Print] OutputMode 开关，见 <see cref="Constants.PrintQueueConstants"/>）：
/// <list type="bullet">
/// <item>OutputMode=Local / 含 SIGNATURE_* 占位 / 本机未注册打印代理 / 含照片嵌入 → 现状本地执行（零回归）</item>
/// <item>否则入队定向推送并每秒轮询回报：Completed=成功；Failed 或超 QueueWaitSeconds
/// 按 QueueFallbackToLocal 回退本地或报错</item>
/// </list>
/// 只接「打印到打印机」（printToPrinter=true）与「推送打印」入口；保存/生成路径不接。
/// </summary>
public interface IQueuePrintRunner
{
    /// <summary>
    /// 打印入口统一路由。
    /// </summary>
    /// <param name="request">入队快照（Fields/TableRows 与本地打印同一份数据）</param>
    /// <param name="printToPrinter">false=「保存/生成」路径，直接本地执行不入队</param>
    /// <param name="includePhotos">true=需嵌入档案照片，队列 payload 不带照片 → 强制本地</param>
    /// <param name="localPrint">现状本地打印（PrintExecuteService.ExecutePrintAsync）</param>
    Task<Result<QueuePrintOutcome>> RunPrintAsync(
        PrintJobRequest request,
        bool printToPrinter,
        bool includePhotos,
        Func<CancellationToken, Task<Result<PrintRecord>>> localPrint,
        CancellationToken ct = default);

    /// <summary>
    /// 推送入口统一路由（现状即入队）：Local 模式 / 含签名 / 本机未注册代理 → 现状入队（target 留 NULL，本机代理消费）；
    /// 已定向则轮询回报，失败/超时按 QueueFallbackToLocal 回退「不带 target 的现状入队」或报错。
    /// </summary>
    Task<Result<QueuePrintOutcome>> RunPushAsync(PrintJobRequest request, CancellationToken ct = default);
}
