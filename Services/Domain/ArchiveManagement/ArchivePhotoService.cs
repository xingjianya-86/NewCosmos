using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.ArchiveManagement;

/// <summary>
/// 档案照片服务实现：证件/核查照片 bytea 存库（仿 nc_biz_signatures 先例，避开输出根迁移断链）。
/// 新采集统一压缩为 WebP（单张 ≤500KB）后入库；旧 JPEG/PNG 通过 image_format 与魔数识别兼容。
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

        // 唯一写入收口：统一压缩为 WebP 并校验 500KB 硬上限，禁止原图旁路入库。
        var compressResult = await Task.Run(
            () => ImageCompressor.CompressToWebP(photo.PhotoData, photo.PhotoCategory), ct);
        if (compressResult.IsFailure)
        {
            LogWarn($"照片压缩失败: {compressResult.Message}");
            return Result.Failure<long>(compressResult.ErrorCode!, compressResult.Message!);
        }

        var compressed = compressResult.Value;
        photo.PhotoData = compressed.Data;
        photo.FileSize = compressed.Data.LongLength;
        photo.ImageFormat = PhotoFormatConstants.WebP;

        if (!ImageCompressor.IsWebP(photo.PhotoData))
        {
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "照片压缩结果不是 WebP，拒绝入库");
        }

        if (photo.PhotoData.LongLength > PhotoCompressionConstants.HardMaxBytes)
        {
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED,
                $"照片超过 {PhotoCompressionConstants.HardMaxBytes / 1024}KB 上限，拒绝入库");
        }

        const string sql = @"INSERT INTO nc_biz_archive_photos
                (photo_data, image_format, applicant_id_card, application_id, photo_category,
                 photo_name, file_size, created_by, created_by_name, created_at)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, NOW())
                RETURNING id";

        var result = await _db.ExecuteScalarAsync<long>(sql, ct,
            photo.PhotoData,
            photo.ImageFormat,
            photo.ApplicantIdCard,
            photo.ApplicationId,
            string.IsNullOrWhiteSpace(photo.PhotoCategory) ? PhotoCategoryConstants.OTHER_CERT : photo.PhotoCategory,
            photo.PhotoName,
            photo.FileSize,
            photo.CreatedBy,
            photo.CreatedByName);

        if (result.IsFailure)
        {
            LogError($"照片保存失败: {result.Message}");
            return result;
        }

        LogInfo($"照片保存成功: Id={result.Value}, Category={photo.PhotoCategory}, Format={photo.ImageFormat}, " +
                $"Size={photo.FileSize}B, Original={compressed.OriginalBytes}B, " +
                $"{compressed.Width}x{compressed.Height}, Q={compressed.Quality}");
        return Result.Success(result.Value);
    }

    public async Task<Result<List<ArchivePhoto>>> GetByPersonAsync(
        string applicantIdCard, string? category = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(applicantIdCard))
            return Result.Failure<List<ArchivePhoto>>(ErrorCodes.VALIDATION_FAILED, "身份证号不能为空");

        // 列表只取元数据，排除 photo_data 大字段
        const string sql = @"SELECT id, applicant_id_card, application_id, photo_category,
                photo_name, file_size, image_format, created_by, created_by_name, created_at
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

    /// <summary>
    /// 照片补传候选（人员维度）：三域 UNION ALL → 按业务域/关键词过滤 → 按身份证聚合（业务域、代表业务ID）
    /// → LEFT JOIN 照片张数。不做"未传"过滤：已传照片的人同样可继续补传。
    /// </summary>
    public async Task<Result<PagedResult<PhotoBackfillCandidate>>> GetBackfillCandidatesAsync(
        string? businessType, string? keyword,
        int pageIndex, int pageSize, CancellationToken ct = default)
    {
        pageIndex = Math.Max(1, pageIndex);
        pageSize = Math.Clamp(pageSize, 1, 100);

        const string cte = @"
WITH biz AS (
    SELECT 'ElderlyBenefits'::text AS business_type, e.id AS business_id,
           e.name AS person_name, e.id_card
    FROM nc_biz_elderly_applications e
    WHERE e.deleted_at IS NULL

    UNION ALL

    SELECT 'SocialAssistance', a.id, a.applicant_name, a.applicant_id_card
    FROM nc_biz_applications a
    WHERE a.deleted_at IS NULL

    UNION ALL

    SELECT 'TempRelief', t.id, t.applicant_name, t.applicant_id_card
    FROM nc_biz_temp_relief_applications t
    WHERE t.deleted_at IS NULL
),
filtered AS (
    SELECT * FROM biz
    WHERE ($1::text = '' OR business_type = $1::text)
      AND ($2::text = '' OR person_name ILIKE '%' || $2::text || '%' OR id_card ILIKE '%' || $2::text || '%')
),
people AS (
    SELECT id_card,
           MAX(person_name) AS person_name,
           string_agg(DISTINCT business_type, '/' ORDER BY business_type) AS business_types,
           MAX(business_id) AS business_id
    FROM filtered
    GROUP BY id_card
),
counted AS (
    SELECT p.id_card, p.person_name, p.business_types, p.business_id,
           COALESCE(c.photo_count, 0)::int AS photo_count
    FROM people p
    LEFT JOIN (
        SELECT applicant_id_card, COUNT(*)::int AS photo_count
        FROM nc_biz_archive_photos
        GROUP BY applicant_id_card
    ) c ON c.applicant_id_card = p.id_card
)";

        var type = businessType ?? string.Empty;
        var kw = keyword?.Trim() ?? string.Empty;

        var countResult = await _db.ExecuteScalarAsync<long>(
            cte + " SELECT COUNT(*) FROM counted", ct, type, kw);
        if (countResult.IsFailure)
        {
            LogError($"补传候选计数失败: {countResult.Message}");
            return Result.Failure<PagedResult<PhotoBackfillCandidate>>(countResult.ErrorCode!, countResult.Message!);
        }

        var listResult = await _db.QueryAsync<PhotoBackfillCandidate>(
            cte + @"
SELECT id_card, person_name, business_types, business_id, photo_count
FROM counted
ORDER BY person_name, id_card
LIMIT $3 OFFSET $4",
            ct, type, kw, pageSize, (pageIndex - 1) * pageSize);

        if (listResult.IsFailure)
        {
            LogError($"补传候选查询失败: {listResult.Message}");
            return Result.Failure<PagedResult<PhotoBackfillCandidate>>(listResult.ErrorCode!, listResult.Message!);
        }

        return Result.Success(new PagedResult<PhotoBackfillCandidate>
        {
            Items = listResult.Value ?? new List<PhotoBackfillCandidate>(),
            PageIndex = pageIndex,
            PageSize = pageSize,
            TotalCount = (int)countResult.Value
        });
    }

    /// <summary>照片查询（桌面端/导出用）：关键词匹配身份证/备注，类别可过滤，返回元数据分页。</summary>
    public async Task<Result<PagedResult<ArchivePhoto>>> GetPhotosPagedAsync(
        string? keyword, string? category, int pageIndex, int pageSize, CancellationToken ct = default)
    {
        pageIndex = Math.Max(1, pageIndex);
        pageSize = Math.Clamp(pageSize, 1, 100);

        const string where = @"
WHERE ($1::text = '' OR applicant_id_card ILIKE '%' || $1::text || '%'
       OR COALESCE(photo_name, '') ILIKE '%' || $1::text || '%')
  AND ($2::text = '' OR photo_category = $2::text)";

        var kw = keyword?.Trim() ?? string.Empty;
        var cat = category ?? string.Empty;

        var countResult = await _db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM nc_biz_archive_photos" + where, ct, kw, cat);
        if (countResult.IsFailure)
        {
            LogError($"照片查询计数失败: {countResult.Message}");
            return Result.Failure<PagedResult<ArchivePhoto>>(countResult.ErrorCode!, countResult.Message!);
        }

        var listResult = await _db.QueryAsync<ArchivePhoto>(
            @"SELECT id, applicant_id_card, application_id, photo_category, photo_name,
                     file_size, image_format, created_by, created_by_name, created_at
              FROM nc_biz_archive_photos" + where + @"
              ORDER BY created_at DESC, id DESC
              LIMIT $3 OFFSET $4",
            ct, kw, cat, pageSize, (pageIndex - 1) * pageSize);

        if (listResult.IsFailure)
        {
            LogError($"照片查询失败: {listResult.Message}");
            return Result.Failure<PagedResult<ArchivePhoto>>(listResult.ErrorCode!, listResult.Message!);
        }

        return Result.Success(new PagedResult<ArchivePhoto>
        {
            Items = listResult.Value ?? new List<ArchivePhoto>(),
            PageIndex = pageIndex,
            PageSize = pageSize,
            TotalCount = (int)countResult.Value
        });
    }
}
