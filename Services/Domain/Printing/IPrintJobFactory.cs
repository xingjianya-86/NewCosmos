using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.Printing;

/// <summary>
/// 推送打印任务构建服务：解析模板并构建字段快照后入队。
/// 手机端调用本服务，避免在手机侧直接依赖打印执行链路。
/// </summary>
public interface IPrintJobFactory
{
    /// <summary>按请求入队（若未提供 TemplateId 则按业务类型+分类解析模板）。</summary>
    Task<Result<PrintJob>> EnqueueAsync(PrintJobRequest request, CancellationToken ct = default);

    /// <summary>临时救助推送打印（构建单据字段后入队）。</summary>
    Task<Result<PrintJob>> EnqueueTempReliefAsync(
        long applicationId,
        int copies = 1,
        bool isDuplex = false,
        string? printerName = null,
        CancellationToken ct = default);

    /// <summary>高龄津贴推送打印（classification：新增 / Stop / Review 等）。</summary>
    Task<Result<PrintJob>> EnqueueElderlyAsync(
        long applicationId,
        string classification,
        int copies = 1,
        bool isDuplex = false,
        string? printerName = null,
        CancellationToken ct = default);
}
