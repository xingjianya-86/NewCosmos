using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.Printing;

public class PrintRecordService : BaseService, IPrintRecordService
{
    protected override string ServiceName => "PrintRecordService";
    private readonly IDatabaseService _db;

    public PrintRecordService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
    }

    public async Task<Result<PrintRecord>> SaveAsync(PrintRecord record, CancellationToken ct = default)
    {
        LogInfo($"保存打印记录: {record.TemplateName}");

        var sql = @"INSERT INTO nc_biz_print_records
                    (batch_no, business_type, business_id, template_id, template_name,
                     pdf_data, pdf_size, source_data, source_type, source_size, file_path, pdf_path,
                     printer_name, copies, operator_id, operator_name, status, remark, created_at)
                    VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11, $12, $13, $14, $15, $16, $17, $18, NOW())
                    RETURNING id AS Id, created_at AS CreatedAt";
        // template_id 在库中为 character varying(100)（历史遗留），实体属性为 long；
        // Npgsql 二进制参数 bigint→varchar 不做隐式转换会直接报错，必须以字符串传参。
        var result = await _db.QuerySingleAsync<PrintRecord>(sql, ct,
            record.BatchNo, record.BusinessType, record.BusinessId, record.TemplateId.ToString(), record.TemplateName,
            record.PdfData, record.PdfSize, record.SourceData, record.SourceType, record.SourceSize,
            record.FilePath, record.PdfPath,
            record.PrinterName, record.Copies, record.OperatorId, record.OperatorName, record.Status, record.Remark);

        if (result.IsFailure)
        {
            LogError($"操作失败");
            return result;
        }

        record.Id = result.Value!.Id;
        record.CreatedAt = result.Value.CreatedAt;
        LogInfo($"执行操作");
        return Result.Success(record);
    }

    public async Task<Result<List<PrintRecord>>> GetByBusinessAsync(string businessType, long businessId, CancellationToken ct = default)
    {
        LogInfo($"查询打印记录: BusinessType={businessType}");

        var sql = @"SELECT id, batch_no, business_type, business_id, template_id, template_name,
                           pdf_size, source_type, source_size, printer_name, copies,
                           operator_id, operator_name, status, remark, created_at, updated_at
                    FROM nc_biz_print_records
                     WHERE business_type = $1 AND business_id = $2
                    ORDER BY created_at DESC";

        var result = await _db.QueryAsync<PrintRecord>(sql, ct, businessType, businessId);
        return result.IsSuccess
            ? Result.Success(result.Value ?? [])
            : Result.Failure<List<PrintRecord>>(result.ErrorCode!, result.Message!);
    }

    public async Task<Result<List<PrintRecord>>> GetByBatchNoAsync(string batchNo, CancellationToken ct = default)
    {
        LogInfo($"执行操作");

        var sql = @"SELECT id, batch_no, business_type, business_id, template_id, template_name,
                            pdf_size, source_type, source_size, printer_name, copies,
                            operator_id, operator_name, status, remark, created_at, updated_at
                     FROM nc_biz_print_records
                     WHERE batch_no = $1
                     ORDER BY created_at";

        var result = await _db.QueryAsync<PrintRecord>(sql, ct, batchNo);
        return result.IsSuccess
            ? Result.Success(result.Value ?? [])
            : Result.Failure<List<PrintRecord>>(result.ErrorCode!, result.Message!);
    }

    public async Task<Result<PrintRecord>> GetByIdAsync(long id, CancellationToken ct = default)
    {
        var sql = @"SELECT id, batch_no, business_type, business_id, template_id, template_name,
                           pdf_size, source_type, source_size, printer_name, copies,
                           operator_id, operator_name, status, remark, created_at, updated_at
                     FROM nc_biz_print_records WHERE id = $1";

        var result = await _db.QuerySingleAsync<PrintRecord>(sql, ct, id);
        return result;
    }

    public async Task<Result<List<PrintRecord>>> GetByTemplateAsync(long templateId, CancellationToken ct = default)
    {
        LogInfo($"执行操作");

        var sql = @"SELECT id, batch_no, business_type, business_id, template_id, template_name,
                            pdf_size, source_type, source_size, printer_name, copies,
                            operator_id, operator_name, status, remark, created_at, updated_at
                     FROM nc_biz_print_records
                     WHERE template_id = $1
                     ORDER BY created_at DESC";

        // template_id 列为 varchar(100)（历史遗留），bigint 参数与 varchar 列比较会报
        // "operator does not exist: character varying = bigint"，故以字符串传参。
        var result = await _db.QueryAsync<PrintRecord>(sql, ct, templateId.ToString());
        return result.IsSuccess
            ? Result.Success(result.Value ?? [])
            : Result.Failure<List<PrintRecord>>(result.ErrorCode!, result.Message!);
    }
}
