using System.Text.Json;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.Printing;

/// <summary>
/// 推送打印任务队列服务实现。
/// 字段/表格以 JSONB 存储完整快照，PC 打印代理直接重放，无需在代理侧重建业务字段。
/// </summary>
public class PrintQueueService : BaseService, IPrintQueueService
{
    protected override string ServiceName => "PrintQueueService";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly IDatabaseService _db;

    public PrintQueueService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
    }

    public async Task<Result<PrintJob>> EnqueueAsync(PrintJobRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.BusinessType))
            return Result.Failure<PrintJob>(ErrorCodes.VALIDATION_FAILED, "业务类型不能为空");

        var jobNo = $"PJ{DateTime.Now:yyyyMMddHHmmssfff}";
        var fieldsJson = JsonSerializer.Serialize(request.Fields ?? new(), JsonOptions);
        var supporterJson = request.SupporterTableData == null
            ? null
            : JsonSerializer.Serialize(request.SupporterTableData, JsonOptions);

        // 照片任务校验：PhotoData 必填（PC 代理直接打印内联照片，不回查照片表）
        var jobKind = string.IsNullOrWhiteSpace(request.JobKind)
            ? PrintJobConstants.JobKindTemplate
            : request.JobKind;
        if (jobKind == PrintJobConstants.JobKindPhoto
            && (request.PhotoData == null || request.PhotoData.Length == 0))
            return Result.Failure<PrintJob>(ErrorCodes.VALIDATION_FAILED, "照片打印任务缺少照片数据");

        // 赡养人行展平（口径同 PrintAgentService 的 effectiveRows：supporter 行非空则以其为准）：
        // Windows Agent 的 payload 不带 supporter_table_data_json，只读 table_rows_json，
        // 不展平会让 CopiesBySupporter 模板在 Web 侧丢掉逐人行。
        var supporterRows = request.SupporterTableData;
        var effectiveTableRows = supporterRows is { Count: > 0 } ? supporterRows : request.TableRows;

        const string sql = @"INSERT INTO nc_biz_print_jobs
                (job_no, business_type, business_id, classification, template_id, template_name,
                 applicant_name, applicant_id_card, printer_name, copies, is_duplex,
                 fields_json, table_rows_json, supporter_table_data_json,
                 job_kind, photo_data, photo_layout, photo_category,
                 status, requested_by, requested_by_name, created_at,
                 target_user_id, target_device_id, target_printer)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11,
                        $12::jsonb, $13::jsonb, $14::jsonb,
                        $15, $16, $17, $18,
                        $21, $19, $20, NOW(),
                        $22, $23, $24)
                RETURNING id, job_no, status, created_at";

        var result = await _db.QuerySingleAsync<PrintJob>(sql, ct,
            jobNo, request.BusinessType, request.BusinessId, request.Classification,
            request.TemplateId, request.TemplateName,
            request.ApplicantName, request.ApplicantIdCard, request.PrinterName,
            request.Copies, request.IsDuplex,
            fieldsJson, JsonSerializer.Serialize(effectiveTableRows ?? new(), JsonOptions), supporterJson,
            jobKind, request.PhotoData, request.PhotoLayout, request.PhotoCategory,
            request.RequestedBy, request.RequestedByName,
            PrintJobConstants.StatusPending,
            request.TargetUserId, request.TargetDeviceId, request.TargetPrinter);

        if (result.IsFailure)
        {
            LogError($"打印任务入队失败: {result.Message}");
            return result;
        }

        var job = result.Value!;
        job.JobNo = jobNo;
        job.BusinessType = request.BusinessType;
        job.TemplateName = request.TemplateName;
        LogInfo($"打印任务入队: {jobNo} ({request.BusinessType}, Kind={jobKind})");

        var full = await GetByIdAsync(job.Id, ct);
        if (full.HasValue)
            return Result.Success(full.Value);

        // 回读失败不改变"已入队"事实，但不能静默：返回入队结果并留痕，避免 DB 默认字段缺失无从定位。
        LogWarn($"打印任务回读失败，返回入队结果: JobNo={jobNo}, {full.ErrorCode} {full.Message}");
        return Result.Success(job);
    }

    public async Task<Result<List<PrintJob>>> ClaimPendingAsync(int max, string agentMachine, CancellationToken ct = default)
    {
        if (max <= 0) max = 5;

        await using var tx = await _db.BeginTransactionScopeAsync(ct);

        // target_device_id IS NULL：本机代理只消费「无定向」任务（手机推送链路 target 恒为 NULL 不受影响）。
        // 定向任务（target_device_id 非空，含 Web LocalOp 与本机 Queue 模式推送）由对应 Windows Agent 按
        // target_device_id 认领——此前无此过滤会偷走 Web 定向给其他设备的任务并误判失败。
        const string sql = @"UPDATE nc_biz_print_jobs
                SET status = $4, agent_machine = $1, claimed_at = NOW()
                WHERE id IN (
                    SELECT id FROM nc_biz_print_jobs
                    WHERE status = $3 AND deleted_at IS NULL
                      AND target_device_id IS NULL
                    ORDER BY created_at
                    LIMIT $2
                    FOR UPDATE SKIP LOCKED
                )
                RETURNING id, job_no, business_type, business_id, classification, template_id, template_name,
                          applicant_name, applicant_id_card, printer_name, copies, is_duplex,
                          job_kind, photo_data, photo_layout, photo_category,
                          fields_json::text AS FieldsJson,
                          table_rows_json::text AS TableRowsJson,
                          supporter_table_data_json::text AS SupporterTableDataJson,
                          status, error_message, requested_by, requested_by_name, agent_machine,
                          created_at, claimed_at, completed_at";

        var result = await _db.QueryAsync<PrintJob>(sql, ct, agentMachine, max, PrintJobConstants.StatusPending, PrintJobConstants.StatusProcessing);
        if (result.IsFailure)
        {
            await tx.RollbackAsync(ct);
            return Result.Failure<List<PrintJob>>(result.ErrorCode!, result.Message!);
        }

        await tx.CommitAsync(ct);
        return Result.Success(result.Value ?? []);
    }

    public async Task<Result> CompleteAsync(long id, CancellationToken ct = default)
    {
        const string sql = @"UPDATE nc_biz_print_jobs
                SET status = $2, completed_at = NOW(), error_message = NULL
                WHERE id = $1";
        var result = await _db.ExecuteNonQueryAsync(sql, ct, id, PrintJobConstants.StatusCompleted);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode!, result.Message!);
        LogInfo($"打印任务完成: Id={id}");
        return Result.Success();
    }

    public async Task<Result> FailAsync(long id, string errorMessage, CancellationToken ct = default)
    {
        const string sql = @"UPDATE nc_biz_print_jobs
                SET status = $3, completed_at = NOW(), error_message = $2
                WHERE id = $1";
        var result = await _db.ExecuteNonQueryAsync(sql, ct, id, Truncate(errorMessage, 2000), PrintJobConstants.StatusFailed);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode!, result.Message!);
        LogWarn($"打印任务失败: Id={id} - {errorMessage}");
        return Result.Success();
    }

    public async Task<Result> RetryAsync(long id, CancellationToken ct = default)
    {
        const string sql = @"UPDATE nc_biz_print_jobs
                SET status = $2, agent_machine = NULL, claimed_at = NULL, completed_at = NULL, error_message = NULL
                WHERE id = $1 AND status = $3 AND deleted_at IS NULL";
        var result = await _db.ExecuteNonQueryAsync(sql, ct, id, PrintJobConstants.StatusPending, PrintJobConstants.StatusFailed);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode!, result.Message!);
        return Result.Success();
    }

    public async Task<Result> DeleteAsync(long id, CancellationToken ct = default)
    {
        const string sql = @"UPDATE nc_biz_print_jobs
                SET deleted_at = NOW()
                WHERE id = $1 AND status <> $2 AND deleted_at IS NULL";
        var result = await _db.ExecuteNonQueryAsync(sql, ct, id, PrintJobConstants.StatusProcessing);
        if (result.IsFailure)
            return Result.Failure(result.ErrorCode!, result.Message!);
        return Result.Success();
    }

    public async Task<Result<PagedResult<PrintJob>>> GetPagedAsync(string? status, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        if (pageIndex < 1) pageIndex = 1;
        if (pageSize < 1) pageSize = 20;
        var statusFilter = string.IsNullOrWhiteSpace(status) ? string.Empty : status;
        var offset = (pageIndex - 1) * pageSize;

        const string sql = @"SELECT id, job_no, business_type, business_id, classification, template_id, template_name,
                                    applicant_name, applicant_id_card, printer_name, copies, is_duplex,
                                    job_kind, photo_category,
                                    status, error_message, requested_by, requested_by_name, agent_machine,
                                    created_at, claimed_at, completed_at
                             FROM nc_biz_print_jobs
                             WHERE deleted_at IS NULL AND ($1 = '' OR status = $1)
                             ORDER BY created_at DESC
                             LIMIT $2 OFFSET $3";

        var result = await _db.QueryAsync<PrintJob>(sql, ct, statusFilter, pageSize, offset);
        if (result.IsFailure)
            return Result.Failure<PagedResult<PrintJob>>(result.ErrorCode!, result.Message!);

        var countResult = await _db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM nc_biz_print_jobs WHERE deleted_at IS NULL AND ($1 = '' OR status = $1)",
            ct, statusFilter);
        var total = countResult.IsSuccess ? (int)countResult.Value : 0;

        return Result.Success(PagedResult<PrintJob>.FromList(result.Value ?? [], pageIndex, pageSize, total));
    }

    public async Task<Result<int>> CountByStatusAsync(string status, CancellationToken ct = default)
    {
        var result = await _db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM nc_biz_print_jobs WHERE deleted_at IS NULL AND status = $1",
            ct, status);
        if (result.IsFailure)
            return Result.Failure<int>(result.ErrorCode!, result.Message!);
        return Result.Success((int)result.Value);
    }

    public async Task<Result<PrintJob>> GetByIdAsync(long id, CancellationToken ct = default)
    {
        const string sql = @"SELECT id, job_no, business_type, business_id, classification, template_id, template_name,
                                    applicant_name, applicant_id_card, printer_name, copies, is_duplex,
                                    job_kind, photo_data, photo_layout, photo_category,
                                    target_user_id, target_device_id, target_printer,
                                    fields_json::text AS FieldsJson,
                                    table_rows_json::text AS TableRowsJson,
                                    supporter_table_data_json::text AS SupporterTableDataJson,
                                    status, error_message, requested_by, requested_by_name, agent_machine,
                                    created_at, claimed_at, completed_at
                             FROM nc_biz_print_jobs WHERE id = $1 AND deleted_at IS NULL";
        return await _db.QuerySingleAsync<PrintJob>(sql, ct, id);
    }

    private static string Truncate(string? value, int max)
        => string.IsNullOrEmpty(value) ? string.Empty : (value.Length <= max ? value : value[..max]);
}
