using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.SocialAssistance;

/// <summary>
/// 回访记录服务实现（nc_biz_visit_records）。
/// 审计：补录记 LogBusiness（不含身份证/姓名，§10 脱敏）；查询失败显式 Failure（§7 不吞异常）。
/// </summary>
public class VisitRecordService : BaseService, IVisitRecordService
{
    protected override string ServiceName => "VisitRecordService";
    private readonly IDatabaseService _db;
    private readonly ILoggerService _audit;

    public VisitRecordService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
        _audit = logger;
    }

    public async Task<Result<List<VisitRecord>>> GetListByApplicationIdAsync(long applicationId, CancellationToken ct = default)
    {
        const string sql = @"
SELECT id, application_id, applicant_id_card, visit_at, visit_by, visit_result, visit_remark,
       created_by, created_at, updated_at, deleted_at
FROM nc_biz_visit_records
WHERE application_id = $1 AND deleted_at IS NULL
ORDER BY visit_at DESC, id DESC";
        var result = await _db.QueryAsync<VisitRecord>(sql, ct, applicationId);
        return result.IsSuccess
            ? Result.Success(result.Value ?? new List<VisitRecord>())
            : Result.Failure<List<VisitRecord>>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR,
                result.Message ?? "回访记录查询失败");
    }

    public async Task<Result<VisitRecord?>> GetLatestByApplicationIdAsync(long applicationId, CancellationToken ct = default)
    {
        var list = await GetListByApplicationIdAsync(applicationId, ct);
        if (list.IsFailure)
            return Result.Failure<VisitRecord?>(list.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, list.Message ?? "回访记录查询失败");
        return Result.Success(list.Value.Count > 0 ? list.Value[0] : null);
    }

    public async Task<Result<VisitRecord?>> GetLatestByIdCardAsync(string idCard, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idCard))
            return Result.Success<VisitRecord?>(null);

        const string sql = @"
SELECT id, application_id, applicant_id_card, visit_at, visit_by, visit_result, visit_remark,
       created_by, created_at, updated_at, deleted_at
FROM nc_biz_visit_records
WHERE applicant_id_card = $1 AND deleted_at IS NULL
ORDER BY visit_at DESC, id DESC
LIMIT 1";
        var result = await _db.QueryAsync<VisitRecord>(sql, ct, idCard);
        if (result.IsFailure)
            return Result.Failure<VisitRecord?>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, result.Message ?? "回访记录查询失败");
        var rows = result.Value;
        return Result.Success(rows != null && rows.Count > 0 ? rows[0] : null);
    }

    public async Task<Result<long>> AddAsync(VisitRecord record, string? createdBy, CancellationToken ct = default)
    {
        // applicant_id_card 由申请表同源带出（防录入侧与档案不一致），无匹配行 = 申请不存在
        const string sql = @"
INSERT INTO nc_biz_visit_records
    (application_id, applicant_id_card, visit_at, visit_by, visit_result, visit_remark, created_by, created_at, updated_at)
SELECT a.id, a.applicant_id_card, $2, $3, $4, $5, $6, NOW(), NOW()
FROM nc_biz_applications a
WHERE a.id = $1
RETURNING id";
        // 历史非泛型重载（GracePeriodService 同款生产先例）：无行返回 Success(0)，以 <=0 判申请不存在
        var result = await _db.ExecuteScalarAsync(
            sql, ct, record.ApplicationId, record.VisitAt, record.VisitBy,
            record.VisitResult, record.VisitRemark, createdBy);

        if (result.IsFailure)
            return Result.Failure<long>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, result.Message ?? "回访记录保存失败");
        if (result.Value <= 0)
            return Result.Failure<long>(ErrorCodes.DB_QUERY_ERROR, "回访补录失败：申请不存在");

        _audit.LogBusiness("回访记录补录",
            ("ApplicationId", record.ApplicationId.ToString()),
            ("VisitResult", record.VisitResult));
        return Result.Success(result.Value);
    }
}
