using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Options;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;

namespace NewCosmos.Services.Domain.Printing;

/// <summary>
/// 打印动作统一路由实现（方案A：本地预览 + 打印动作推送 Windows Agent，config/app.ini [Print] 开关）。
/// 只改「打印到打印机」与「推送打印」动作的去向；生成/预览/保存链路一律不动。
/// </summary>
public class QueuePrintRunner : BaseService, IQueuePrintRunner
{
    protected override string ServiceName => "QueuePrintRunner";

    private readonly IPrintJobFactory _factory;
    private readonly IPrintQueueService _queueService;
    private readonly IConfigService _configService;

    public QueuePrintRunner(
        IPrintJobFactory factory,
        IPrintQueueService queueService,
        IConfigService configService,
        ILoggerService logger) : base(logger)
    {
        _factory = factory;
        _queueService = queueService;
        _configService = configService;
    }

    public async Task<Result<QueuePrintOutcome>> RunPrintAsync(
        PrintJobRequest request,
        bool printToPrinter,
        bool includePhotos,
        Func<CancellationToken, Task<Result<PrintRecord>>> localPrint,
        CancellationToken ct = default)
    {
        ValidateNotNull(request, nameof(request));
        ValidateNotNull(localPrint, nameof(localPrint));

        // 「保存/生成」路径不入队（plan：只改 printToPrinter=true）
        if (!printToPrinter)
            return QueuePrintOutcome.FromLocal(await localPrint(ct));

        // 照片无法随队列 payload 下发（桌面 PrintJobRequest 无 images 字段），推送会静默丢图
        if (includePhotos)
        {
            LogInfo("含照片嵌入的打印不支持队列推送，走本地打印");
            return QueuePrintOutcome.FromLocal(await localPrint(ct));
        }

        // Local 模式 / 含签名占位 / 本机未注册打印代理 → 回退现状本地打印（原因在工厂侧留痕）
        if (!await _factory.TryResolveTargetAsync(request, ct))
            return QueuePrintOutcome.FromLocal(await localPrint(ct));

        var enqueue = await _factory.EnqueueAsync(request, ct);
        if (enqueue.IsFailure)
            return Result.Failure<QueuePrintOutcome>(enqueue.ErrorCode, enqueue.Message);

        var options = _configService.GetAppOptions();
        return await PollUntilSettledAsync(enqueue.Value!, options,
            async localCt => QueuePrintOutcome.FromLocal(await localPrint(localCt)), ct);
    }

    public async Task<Result<QueuePrintOutcome>> RunPushAsync(PrintJobRequest request, CancellationToken ct = default)
    {
        ValidateNotNull(request, nameof(request));

        // 现状入队：Local 模式 target 恒 NULL → 本机打印代理消费；Queue 模式解析失败/含签名同样回退本机代理
        var enqueue = await _factory.EnqueueAsync(request, ct);
        if (enqueue.IsFailure)
            return Result.Failure<QueuePrintOutcome>(enqueue.ErrorCode, enqueue.Message);

        var job = enqueue.Value!;
        if (request.TargetDeviceId is null)
            return Result.Success(new QueuePrintOutcome { UsedQueue = false, JobNo = job.JobNo });

        var options = _configService.GetAppOptions();
        return await PollUntilSettledAsync(job, options,
            localCt => EnqueueUntargetedAsync(request, localCt), ct);
    }

    /// <summary>降级回退：撤销定向语义后重新按现状入队（target 留 NULL，交本机打印代理消费）。</summary>
    private async Task<Result<QueuePrintOutcome>> EnqueueUntargetedAsync(PrintJobRequest request, CancellationToken ct)
    {
        request.SkipTargetResolution = true;
        request.TargetDeviceId = null;

        var retry = await _factory.EnqueueAsync(request, ct);
        return retry.IsSuccess
            ? Result.Success(new QueuePrintOutcome { UsedQueue = false, JobNo = retry.Value!.JobNo })
            : Result.Failure<QueuePrintOutcome>(retry.ErrorCode, retry.Message);
    }

    /// <summary>
    /// 每秒轮询任务状态直到 Completed / Failed / 超时。
    /// Failed 且 QueueFallbackToLocal → 回退本地；超时先撤销未开始的任务再降级，避免降级后远端补打成双份。
    /// </summary>
    private async Task<Result<QueuePrintOutcome>> PollUntilSettledAsync(
        PrintJob job,
        AppOptions options,
        Func<CancellationToken, Task<Result<QueuePrintOutcome>>> fallback,
        CancellationToken ct)
    {
        var waitSeconds = options.PrintQueueWaitSeconds;
        var deadline = DateTime.UtcNow.AddSeconds(waitSeconds);
        PrintJob? last = job; // 入队回读快照（Pending）

        try
        {
            while (true)
            {
                var got = await _queueService.GetByIdAsync(job.Id, ct);
                if (got.IsSuccess && got.Value != null)
                    last = got.Value;

                if (last != null)
                {
                    if (last.Status == PrintJobConstants.StatusCompleted)
                    {
                        LogInfo($"打印任务完成: JobNo={last.JobNo}");
                        return Result.Success(new QueuePrintOutcome { UsedQueue = true, JobNo = last.JobNo });
                    }

                    if (last.Status == PrintJobConstants.StatusFailed)
                    {
                        if (options.PrintQueueFallbackToLocal)
                        {
                            LogWarn($"打印任务失败，按配置回退本地: JobNo={last.JobNo}, {last.ErrorMessage}");
                            return await fallback(ct);
                        }
                        return Result.Failure<QueuePrintOutcome>(ErrorCodes.PRINT_FAILED,
                            $"打印任务失败: {last.ErrorMessage ?? "未知原因"}");
                    }
                }

                if (DateTime.UtcNow >= deadline)
                    break;

                await Task.Delay(PrintQueueConstants.PollIntervalMs, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // 用户取消：撤销尚未开始的任务，避免"已取消却稍后被代理打出"
            await TryCancelJobAsync(job.Id, "打印等待被取消");
            return Result.Failure<QueuePrintOutcome>(ErrorCodes.CANCELLED, "打印任务已取消");
        }

        return await HandleTimeoutAsync(job, last, options, fallback);
    }

    /// <summary>超时降级：远端正在打印则不回退（防双打），否则撤销未开始的任务后按开关回退本地。</summary>
    private async Task<Result<QueuePrintOutcome>> HandleTimeoutAsync(
        PrintJob job,
        PrintJob? last,
        AppOptions options,
        Func<CancellationToken, Task<Result<QueuePrintOutcome>>> fallback)
    {
        var waitSeconds = options.PrintQueueWaitSeconds;

        // 超时点再回读一次，避免与"刚好完成"竞争：已完成就按成功收口
        var fresh = await _queueService.GetByIdAsync(job.Id, CancellationToken.None);
        if (fresh.IsSuccess && fresh.Value != null)
            last = fresh.Value;

        if (last?.Status == PrintJobConstants.StatusCompleted)
            return Result.Success(new QueuePrintOutcome { UsedQueue = true, JobNo = last.JobNo });

        if (last?.Status == PrintJobConstants.StatusProcessing)
        {
            LogWarn($"打印任务超时且代理正在执行，不回退本地以免双打: JobNo={last.JobNo}");
            return Result.Failure<QueuePrintOutcome>(ErrorCodes.PRINT_FAILED,
                $"打印任务正在打印代理执行中，超过 {waitSeconds} 秒未回报，请稍后在打印任务列表查看结果");
        }

        // 仍 Pending（或状态未知）：先撤销，避免降级本地后远端补打成双份
        if (last?.Status == null || last.Status == PrintJobConstants.StatusPending)
            await TryCancelJobAsync(job.Id, "打印任务超时");

        if (options.PrintQueueFallbackToLocal)
        {
            LogWarn($"打印任务超过 {waitSeconds} 秒未回报，按配置回退本地: JobNo={job.JobNo}");
            return await fallback(CancellationToken.None);
        }

        return Result.Failure<QueuePrintOutcome>(ErrorCodes.PRINT_FAILED,
            $"打印任务超过 {waitSeconds} 秒未完成，且未开启本地回退");
    }

    /// <summary>撤销未开始的任务（Processing 时 DeleteAsync 不生效，只留痕）。</summary>
    private async Task TryCancelJobAsync(long jobId, string reason)
    {
        var cancel = await _queueService.DeleteAsync(jobId, CancellationToken.None);
        if (cancel.IsFailure)
            LogWarn($"撤销打印任务失败（{reason}）: JobId={jobId}, {cancel.ErrorCode} {cancel.Message}");
        else
            LogInfo($"已撤销打印任务（{reason}）: JobId={jobId}");
    }
}
