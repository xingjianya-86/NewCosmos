using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.ArchiveManagement;

public class ArchivePhotoPendingService : BaseService, IArchivePhotoPendingService
{
    protected override string ServiceName => "ArchivePhotoPendingService";

    private readonly IDatabaseService _db;
    private readonly IArchivePhotoService _photoService;

    public ArchivePhotoPendingService(IDatabaseService db, ILoggerService logger, IArchivePhotoService photoService)
        : base(logger)
    {
        _db = db;
        _photoService = photoService;
    }

    public async Task<Result<long>> SavePendingAsync(byte[] data, string category, string? photoName,
        string? recognizedName, string? recognizedIdCard, string? source, CancellationToken ct = default)
    {
        if (data == null || data.Length == 0)
            return Result.Failure<long>(ErrorCodes.VALIDATION_FAILED, "照片内容为空");

        var cat = string.IsNullOrWhiteSpace(category) ? PhotoCategoryConstants.OTHER_CERT : category.Trim();
        var compressed = ImageCompressor.CompressToWebP(data, cat);
        if (compressed.IsFailure)
            return Result.Failure<long>(compressed.ErrorCode ?? ErrorCodes.VALIDATION_FAILED,
                compressed.Message ?? "照片压缩失败");
        var photo = compressed.Value;

        const string sql = @"
            INSERT INTO nc_biz_archive_photo_pending
                (photo_data, image_format, photo_category, photo_name, file_size,
                 recognized_name, recognized_id_card, source, status,
                 created_by, created_by_name, created_at)
            VALUES ($1,$2,$3,$4,$5,$6,$7,$8,'Pending',$9,$10,NOW())
            RETURNING id";
        var result = await _db.ExecuteScalarAsync<long>(sql, ct,
            photo.Data, photo.Format, cat, photoName, photo.Data.Length,
            recognizedName, recognizedIdCard, source,
            App.CurrentUserId, App.CurrentUserFullName);
        if (result.IsFailure)
            return Result.Failure<long>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, result.Message ?? "待归属照片保存失败");

        Logger.LogBusiness("待归属照片保存",
            ("PendingId", result.Value),
            ("Category", cat),
            ("Size", photo.Data.Length),
            ("RecognizedIdCard", DataMasker.MaskIdCard(recognizedIdCard ?? string.Empty)));
        return Result.Success(result.Value);
    }

    public async Task<Result<PagedResult<PendingArchivePhoto>>> GetPendingPagedAsync(string? keyword, string? category,
        int pageIndex, int pageSize, CancellationToken ct = default)
    {
        var conditions = new SqlConditionBuilder()
            .Add("status = 'Pending'")
            .AddIf(!string.IsNullOrWhiteSpace(category), "photo_category = {0}", category)
            .AddIf(!string.IsNullOrWhiteSpace(keyword),
                "(recognized_name ILIKE {0} OR recognized_id_card ILIKE {0} OR photo_name ILIKE {0})",
                $"%{keyword!.Trim()}%");

        var where = conditions.ToWhereClause();
        var countResult = await _db.ExecuteScalarAsync<long>(
            $"SELECT COUNT(*) FROM nc_biz_archive_photo_pending{where}", ct, conditions.GetParameters());
        if (countResult.IsFailure)
            return Result.Failure<PagedResult<PendingArchivePhoto>>(countResult.ErrorCode!, countResult.Message!);

        var offset = (pageIndex - 1) * pageSize;
        var querySql =
            "SELECT id, image_format, photo_category, photo_name, file_size, recognized_name, recognized_id_card," +
            " source, status, assigned_photo_id, assigned_at, created_by, created_by_name, created_at" +
            $" FROM nc_biz_archive_photo_pending{where}" +
            $" ORDER BY created_at DESC, id DESC LIMIT ${conditions.ParamCount + 1} OFFSET ${conditions.ParamCount + 2}";
        var pageParams = new List<object?>(conditions.GetParameters()) { pageSize, offset };
        var listResult = await _db.QueryAsync<PendingArchivePhoto>(querySql, ct, pageParams.ToArray());
        if (listResult.IsFailure)
            return Result.Failure<PagedResult<PendingArchivePhoto>>(listResult.ErrorCode!, listResult.Message!);

        return Result.Success(PagedResult<PendingArchivePhoto>.FromList(
            listResult.Value ?? new List<PendingArchivePhoto>(), pageIndex, pageSize,
            Convert.ToInt32(countResult.Value)));
    }

    public async Task<Result<byte[]>> GetPendingDataAsync(long id, CancellationToken ct = default)
    {
        var result = await _db.QuerySingleAsync<byte[]>(
            "SELECT photo_data FROM nc_biz_archive_photo_pending WHERE id = $1 AND status = 'Pending'", ct, id);
        if (result.IsFailure)
            return Result.Failure<byte[]>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, result.Message ?? "读取待归属照片失败");
        if (result.Value == null || result.Value.Length == 0)
            return Result.Failure<byte[]>(ErrorCodes.RECORD_NOT_FOUND, "待归属照片不存在或已归属");
        return Result.Success(result.Value);
    }

    public async Task<Result<bool>> DeletePendingAsync(long id, CancellationToken ct = default)
    {
        var result = await _db.ExecuteNonQueryAsync(
            "DELETE FROM nc_biz_archive_photo_pending WHERE id = $1 AND status = 'Pending'", ct, id);
        if (result.IsFailure)
            return Result.Failure<bool>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, result.Message ?? "删除待归属照片失败");
        if (result.Value == 0)
            return Result.Failure<bool>(ErrorCodes.RECORD_NOT_FOUND, "待归属照片不存在或已归属");

        Logger.LogBusiness("删除待归属照片", ("PendingId", id));
        return Result.Success(true);
    }

    public async Task<Result<int>> CountPendingAsync(CancellationToken ct = default)
    {
        var result = await _db.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM nc_biz_archive_photo_pending WHERE status = 'Pending'", ct);
        if (result.IsFailure)
            return Result.Failure<int>(result.ErrorCode ?? ErrorCodes.DB_QUERY_ERROR, result.Message ?? "统计待归属照片失败");
        return Result.Success(Convert.ToInt32(result.Value));
    }

    public async Task<Result<BatchPhotoInsertResult>> AssignPendingAsync(IReadOnlyList<long> ids, string idCard,
        CancellationToken ct = default)
    {
        if (ids == null || ids.Count == 0)
            return Result.Failure<BatchPhotoInsertResult>(ErrorCodes.VALIDATION_FAILED, "请选择待归属照片");

        var ownerResult = await _photoService.ResolveOwnerAsync(idCard, ct);
        if (ownerResult.IsFailure)
            return Result.Failure<BatchPhotoInsertResult>(ownerResult.ErrorCode!, ownerResult.Message!);
        var owner = ownerResult.Value;
        if (owner == null || string.IsNullOrWhiteSpace(owner.IdCard))
            return Result.Failure<BatchPhotoInsertResult>(ErrorCodes.RECORD_NOT_FOUND,
                "该身份证不在低收入/高龄/临时救助/资产核查档案中，请先建立申请");

        var idArray = ids.Distinct().ToArray();
        var rowsResult = await _db.QueryAsync<PendingPhotoDataRow>(
            @"SELECT id, photo_category, photo_data
              FROM nc_biz_archive_photo_pending
              WHERE id = ANY($1::bigint[]) AND status = 'Pending'", ct, (object)idArray);
        if (rowsResult.IsFailure)
            return Result.Failure<BatchPhotoInsertResult>(rowsResult.ErrorCode!, rowsResult.Message!);

        var rows = rowsResult.Value ?? new List<PendingPhotoDataRow>();
        if (rows.Count == 0)
            return Result.Failure<BatchPhotoInsertResult>(ErrorCodes.RECORD_NOT_FOUND, "所选待归属照片不存在或已归属");

        var insertedIds = new List<long>();
        string? firstError = null;
        var succeeded = 0;
        foreach (var row in rows)
        {
            var insert = await _photoService.InsertPhotosAsync(
                owner, row.PhotoCategory, new[] { row.PhotoData }, null, ct);
            if (insert.IsFailure)
            {
                firstError ??= insert.Message;
                LogWarn($"待归属照片归属失败: PendingId={row.Id}, {insert.ErrorCode} {insert.Message}");
                continue;
            }
            if (insert.Value == null || insert.Value.PhotoIds.Count == 0)
            {
                firstError ??= "照片入库未返回 ID";
                continue;
            }

            var newId = insert.Value.PhotoIds[0];
            var update = await _db.ExecuteNonQueryAsync(
                @"UPDATE nc_biz_archive_photo_pending
                  SET status = 'Assigned', assigned_photo_id = $1, assigned_at = NOW()
                  WHERE id = $2 AND status = 'Pending'", ct, newId, row.Id);
            if (update.IsFailure)
            {
                firstError ??= update.Message;
                LogWarn($"待归属照片标记失败: PendingId={row.Id}, {update.Message}");
                continue;
            }

            insertedIds.Add(newId);
            succeeded++;
        }

        Logger.LogBusiness("待归属照片归属",
            ("IdCard", DataMasker.MaskIdCard(idCard)),
            ("Requested", idArray.Length),
            ("Succeeded", succeeded));

        return Result.Success(new BatchPhotoInsertResult
        {
            Total = idArray.Length,
            Succeeded = succeeded,
            FirstError = firstError,
            PhotoIds = insertedIds
        });
    }

    private class PendingPhotoDataRow
    {
        public long Id { get; set; }
        public string PhotoCategory { get; set; } = PhotoCategoryConstants.OTHER_CERT;
        public byte[] PhotoData { get; set; } = Array.Empty<byte>();
    }
}
