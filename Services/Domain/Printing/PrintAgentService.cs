using System.Text.Json;
using NewCosmos.Models.Entities;
using NewCosmos.Services.Core;

namespace NewCosmos.Services.Domain.Printing;

/// <summary>
/// PC 端推送打印代理实现。
/// 后台定时认领 Pending 任务并调用 <see cref="IPrintExecuteService.ExecutePrintAsync"/> 执行打印；
/// 打印权限按当前登录的 PC 操作员判定（与手动打印口径一致）。
/// </summary>
public class PrintAgentService : IPrintAgentService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 3;

    private readonly IPrintQueueService _queueService;
    private readonly IPrintExecuteService _printExecuteService;
    private readonly ILoggerService _logger;

    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public PrintAgentService(
        IPrintQueueService queueService,
        IPrintExecuteService printExecuteService,
        ILoggerService logger)
    {
        _queueService = queueService;
        _printExecuteService = printExecuteService;
        _logger = logger;
    }

    public bool IsRunning
    {
        get { lock (_gate) { return _loop is { IsCompleted: false }; } }
    }

    public void Start()
    {
        if (!OperatingSystem.IsWindows())
            return;

        lock (_gate)
        {
            if (_loop is { IsCompleted: false })
                return;

            _cts = new CancellationTokenSource();
            _loop = Task.Run(() => RunLoopAsync(_cts.Token));
            _logger.LogBusiness("推送打印代理已启动");
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            try { _cts?.Cancel(); } catch { /* 忽略 */ }
            _cts?.Dispose();
            _cts = null;
            _loop = null;
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // 仅在已登录时处理：ExecutePrintAsync 的权限校验依赖 App.CurrentUserId
                if (App.CurrentUserId.HasValue)
                {
                    await ProcessPendingAsync(ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "推送打印代理轮询异常");
            }

            try { await Task.Delay(PollInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task ProcessPendingAsync(CancellationToken ct)
    {
        var machine = Environment.MachineName;
        var claimResult = await _queueService.ClaimPendingAsync(BatchSize, machine, ct);
        if (claimResult.IsFailure || claimResult.Value == null || claimResult.Value.Count == 0)
            return;

        foreach (var job in claimResult.Value)
        {
            if (ct.IsCancellationRequested) return;
            await ExecuteJobAsync(job, ct);
        }
    }

    private async Task ExecuteJobAsync(PrintJob job, CancellationToken ct)
    {
        if (job.TemplateId is null or <= 0)
        {
            await _queueService.FailAsync(job.Id, "任务缺少模板ID，无法打印", ct);
            return;
        }

        try
        {
            var fields = DeserializeFields(job.FieldsJson);
            var tableRows = DeserializeRows(job.TableRowsJson);
            var supporterRows = DeserializeRows(job.SupporterTableDataJson);

            // 赡养人模板（CopiesBySupporter）以赡养人数据作为表格行逐人打印，与 ArchiveOutput 口径一致
            var effectiveRows = supporterRows.Count > 0 ? supporterRows : tableRows;

            var result = await _printExecuteService.ExecutePrintAsync(
                job.TemplateId.Value,
                job.TemplateName ?? string.Empty,
                job.BusinessType,
                job.BusinessId,
                batchNo: job.JobNo,
                fields: fields,
                tableRows: effectiveRows,
                applicantName: job.ApplicantName ?? string.Empty,
                applicantIdCard: job.ApplicantIdCard ?? string.Empty,
                printerName: job.PrinterName ?? string.Empty,
                copies: job.Copies,
                isDuplex: job.IsDuplex,
                ct: ct);

            if (result.IsSuccess)
            {
                await _queueService.CompleteAsync(job.Id, ct);
            }
            else
            {
                await _queueService.FailAsync(job.Id, result.Message ?? "打印执行失败", ct);
            }
        }
        catch (OperationCanceledException)
        {
            // 交由下次轮询重试（保持 Processing 会被卡住，故标记失败以便重试）
            await _queueService.FailAsync(job.Id, "打印任务被取消，请重试", CancellationToken.None);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"推送打印任务执行异常: JobNo={job.JobNo}");
            await _queueService.FailAsync(job.Id, ex.Message, CancellationToken.None);
        }
    }

    private static Dictionary<string, string> DeserializeFields(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new(StringComparer.Ordinal);
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new(StringComparer.Ordinal);
        }
        catch
        {
            return new(StringComparer.Ordinal);
        }
    }

    private static List<Dictionary<string, string>> DeserializeRows(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<Dictionary<string, string>>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }
}
