using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.Signatures;

/// <summary>
/// 签名留痕服务实现。
/// 存储/取回手写签名 PNG（bytea），按身份证号 + 业务类型 + 角色匹配最新一条供文书渲染嵌入。
/// </summary>
public class SignatureService : BaseService, ISignatureService
{
    protected override string ServiceName => "SignatureService";

    private readonly IDatabaseService _db;

    public SignatureService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
    }

    public async Task<Result<long>> SaveSignatureAsync(Signature signature, CancellationToken ct = default)
    {
        if (signature == null)
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "签名数据不能为空");
        if (signature.SignatureData == null || signature.SignatureData.Length == 0)
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "签名图像为空");
        if (string.IsNullOrWhiteSpace(signature.ApplicantIdCard))
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "签名缺少身份证号关联");
        if (string.IsNullOrWhiteSpace(signature.BusinessType))
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "签名缺少业务类型");

        const string sql = @"INSERT INTO nc_biz_signatures
                (signature_data, applicant_id_card, business_type, business_id,
                 signer_name, signer_role, created_by, created_by_name, created_at)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, NOW())
                RETURNING id";

        var result = await _db.ExecuteScalarAsync<long>(sql, ct,
            signature.SignatureData,
            signature.ApplicantIdCard,
            signature.BusinessType,
            signature.BusinessId,
            signature.SignerName,
            string.IsNullOrWhiteSpace(signature.SignerRole) ? "本人" : signature.SignerRole,
            signature.CreatedBy,
            signature.CreatedByName);

        if (result.IsFailure)
        {
            LogError($"签名保存失败: {result.Message}");
            return result;
        }

        LogInfo($"签名保存成功: Id={result.Value}, Role={signature.SignerRole}, Type={signature.BusinessType}");
        return Result.Success(result.Value);
    }

    public async Task<Result<Signature?>> GetLatestAsync(
        string applicantIdCard,
        string businessType,
        long? businessId = null,
        string signerRole = "本人",
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(applicantIdCard) || string.IsNullOrWhiteSpace(businessType))
            return Result.Success<Signature?>(null);

        // businessId 为空时忽略该条件（同一身份证号 + 业务类型 + 角色取最近一条）
        const string sql = @"SELECT id, signature_data, applicant_id_card, business_type, business_id,
                signer_name, signer_role, created_by, created_by_name, created_at
                FROM nc_biz_signatures
                WHERE applicant_id_card = $1
                  AND business_type = $2
                  AND ($3::bigint IS NULL OR business_id = $3)
                  AND signer_role = $4
                ORDER BY created_at DESC
                LIMIT 1";

        var result = await _db.QuerySingleAsync<Signature>(sql, ct,
            applicantIdCard, businessType, businessId, signerRole);

        if (result.IsFailure)
        {
            LogError($"签名查询失败: {result.Message}");
            return Result.Failure<Signature?>(result.ErrorCode!, result.Message!);
        }

        return Result.Success<Signature?>(result.Value);
    }
}
