using NewCosmos.Constants;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.ArchiveManagement;

/// <summary>
/// 档案照片服务实现：证件/核查照片 bytea 存库（仿 nc_biz_signatures 先例，避开输出根迁移断链）。
/// 列表查询只取元数据（不含 photo_data），预览/打印时按 Id 单独取二进制，避免列表传输大字段。
/// </summary>
public class ArchivePhotoService : BaseService, IArchivePhotoService
{
    protected override string ServiceName => "ArchivePhotoService";

    private readonly IDatabaseService _db;

    public ArchivePhotoService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
    }

    public async Task<Result<long>> SavePhotoAsync(ArchivePhoto photo, CancellationToken ct = default)
    {
        if (photo == null)
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "照片数据不能为空");
        if (photo.PhotoData == null || photo.PhotoData.Length == 0)
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "照片内容为空");
        if (string.IsNullOrWhiteSpace(photo.ApplicantIdCard))
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "照片缺少身份证号关联");

        const string sql = @"INSERT INTO nc_biz_archive_photos
                (photo_data, applicant_id_card, application_id, photo_category,
                 photo_name, file_size, created_by, created_by_name, created_at)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, NOW())
                RETURNING id";

        var result = await _db.ExecuteScalarAsync<long>(sql, ct,
            photo.PhotoData,
            photo.ApplicantIdCard,
            photo.ApplicationId,
            string.IsNullOrWhiteSpace(photo.PhotoCategory) ? PhotoCategoryConstants.OTHER_CERT : photo.PhotoCategory,
            photo.PhotoName,
            photo.PhotoData.LongLength,
            photo.CreatedBy,
            photo.CreatedByName);

        if (result.IsFailure)
        {
            LogError($"照片保存失败: {result.Message}");
            return result;
        }

        LogInfo($"照片保存成功: Id={result.Value}, Category={photo.PhotoCategory}, Size={photo.PhotoData.LongLength}B");
        return Result.Success(result.Value);
    }

    public async Task<Result<List<ArchivePhoto>>> GetByPersonAsync(
        string applicantIdCard, string? category = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(applicantIdCard))
            return Result.Failure<List<ArchivePhoto>>(ErrorCodes.VALIDATION_FAILED, "身份证号不能为空");

        // 列表只取元数据，排除 photo_data 大字段
        const string sql = @"SELECT id, applicant_id_card, application_id, photo_category,
                photo_name, file_size, created_by, created_by_name, created_at
                FROM nc_biz_archive_photos
                WHERE applicant_id_card = $1
                  AND ($2::varchar IS NULL OR photo_category = $2)
                ORDER BY created_at DESC
                LIMIT 200";

        var result = await _db.QueryAsync<ArchivePhoto>(sql, ct, applicantIdCard, category);
        if (result.IsFailure)
        {
            LogError($"照片列表查询失败: {result.Message}");
            return Result.Failure<List<ArchivePhoto>>(result.ErrorCode!, result.Message!);
        }

        return Result.Success(result.Value ?? new List<ArchivePhoto>());
    }

    public async Task<Result<byte[]>> GetPhotoDataAsync(long photoId, CancellationToken ct = default)
    {
        const string sql = "SELECT photo_data FROM nc_biz_archive_photos WHERE id = $1";
        var result = await _db.QuerySingleAsync<byte[]>(sql, ct, photoId);

        if (result.IsFailure)
        {
            LogError($"照片二进制查询失败: {result.Message}");
            return Result.Failure<byte[]>(result.ErrorCode!, result.Message!);
        }

        if (result.Value == null || result.Value.Length == 0)
            return Result.Failure<byte[]>(ErrorCodes.NOT_FOUND, $"照片不存在或为空: Id={photoId}");

        return Result.Success(result.Value);
    }

    public async Task<Result<bool>> DeletePhotoAsync(long photoId, CancellationToken ct = default)
    {
        const string sql = "DELETE FROM nc_biz_archive_photos WHERE id = $1";
        var result = await _db.ExecuteScalarAsync<long>(sql, ct, photoId);
        if (result.IsFailure)
        {
            LogError($"照片删除失败: {result.Message}");
            return Result.Failure<bool>(result.ErrorCode!, result.Message!);
        }

        LogInfo($"照片删除: Id={photoId}");
        return Result.Success(true);
    }
}
