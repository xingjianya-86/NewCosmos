using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.Printing;

/// <summary>
/// 推送打印任务队列服务：手机端入队，PC 端打印代理轮询认领并执行。
/// </summary>
public interface IPrintQueueService
{
    /// <summary>入队一个打印任务（状态 Pending）。</summary>
    Task<Result<PrintJob>> EnqueueAsync(PrintJobRequest request, CancellationToken ct = default);

    /// <summary>打印代理认领一批待打印任务（Pending → Processing，FOR UPDATE SKIP LOCKED）。</summary>
    Task<Result<List<PrintJob>>> ClaimPendingAsync(int max, string agentMachine, CancellationToken ct = default);

    /// <summary>标记任务完成。</summary>
    Task<Result> CompleteAsync(long id, CancellationToken ct = default);

    /// <summary>标记任务失败并记录原因。</summary>
    Task<Result> FailAsync(long id, string errorMessage, CancellationToken ct = default);

    /// <summary>失败任务重试（Failed → Pending）。</summary>
    Task<Result> RetryAsync(long id, CancellationToken ct = default);

    /// <summary>软删除任务（打印中不可删除）。</summary>
    Task<Result> DeleteAsync(long id, CancellationToken ct = default);

    /// <summary>分页查询任务（status 为空表示全部）。</summary>
    Task<Result<PagedResult<PrintJob>>> GetPagedAsync(string? status, int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>按状态统计数量。</summary>
    Task<Result<int>> CountByStatusAsync(string status, CancellationToken ct = default);
}
