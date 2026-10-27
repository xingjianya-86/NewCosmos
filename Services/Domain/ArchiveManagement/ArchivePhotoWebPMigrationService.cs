using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Database;

namespace NewCosmos.Services.Domain.ArchiveManagement;

/// <summary>
/// 历史档案照片批量转 WebP 实现：按 id 游标分批读取（避免 offset 抖动/死循环），
/// 单张压缩失败只记录并跳过，不阻断整批；已转换的记录下次自动跳过。
/// </summary>
public class ArchivePhotoWebPMigrationService : BaseService, IArchivePhotoWebPMigrationService
{
    protected override string ServiceName => "ArchivePhotoWebPMigrationService";

    private readonly IDatabaseService _db;

    public ArchivePhotoWebPMigrationService(IDatabaseService db, ILoggerService logger) : base(logger)
    {
        _db = db;
    }

    public async Task<Result<int>> CountPendingAsync(CancellationToken ct = default)
    {
        const string sql = @"
SELECT COUNT(*)::bigint
FROM nc_biz_archive_photos
WHERE image_format <> 'webp'";

        var result = await _db.ExecuteScalarAsync<long>(sql, ct);
        if (result.IsFailure)
        {
            LogError($"历史照片待转数量查询失败: {result.Message}");
            return Result.Failure<int>(result.ErrorCode!, result.Message!);
        }

        return Result.Success((int)Math.Min(result.Value, int.MaxValue));
    }

    public async Task<Result<PhotoWebPMigrationResult>> ConvertAllAsync(
        IProgress<PhotoWebPMigrationProgress>? progress = null,
        CancellationToken ct = default)
    {
        var totalResult = await CountPendingAsync(ct);
        if (totalResult.IsFailure)
            return Result.Failure<PhotoWebPMigrationResult>(totalResult.ErrorCode!, totalResult.Message!);

        var total = totalResult.Value;
        var processed = 0;
        var converted = 0;
        var skipped = 0;
        var failed = 0;
        var failedIds = new List<long>();
        var cancelled = false;
        var lastId = 0L;

        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();

                var batchResult = await LoadBatchAsync(lastId, ct);
                if (batchResult.IsFailure)
                    return Result.Failure<PhotoWebPMigrationResult>(batchResult.ErrorCode!, batchResult.Message!);

                var batch = batchResult.Value;
                if (batch.Count == 0) break;

                foreach (var row in batch)
                {
                    ct.ThrowIfCancellationRequested();
                    lastId = row.Id;
                    processed++;

                    var outcome = await ConvertRowAsync(row, ct);
                    switch (outcome)
                    {
                        case ConvertOutcome.Converted:
                            converted++;
                            break;
                        case ConvertOutcome.Skipped:
                            skipped++;
                            break;
                        default:
                            failed++;
                            failedIds.Add(row.Id);
                            break;
                    }

                    progress?.Report(new PhotoWebPMigrationProgress(
                        total, processed, converted, skipped, failed, row.Id));
                }
            }
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        var result = new PhotoWebPMigrationResult(
            total, converted, skipped, failed, cancelled, failedIds);

        Logger.LogBusiness("历史照片转 WebP 完成",
            ("Total", result.Total.ToString()),
            ("Converted", result.Converted.ToString()),
            ("Skipped", result.Skipped.ToString()),
            ("Failed", result.Failed.ToString()),
            ("Cancelled", result.Cancelled.ToString()));

        return Result.Success(result);
    }

    private async Task<Result<List<ArchivePhotoMigrationRow>>> LoadBatchAsync(long lastId, CancellationToken ct)
    {
        const string sql = @"
SELECT id, photo_data, photo_category, image_format
FROM nc_biz_archive_photos
WHERE image_format <> 'webp'
  AND id > $1
ORDER BY id
LIMIT $2";

        var result = await _db.QueryAsync<ArchivePhotoMigrationRow>(
            sql, ct, lastId, PhotoCompressionConstants.MigrationBatchSize);

        if (result.IsFailure)
        {
            LogError($"历史照片批量读取失败: {result.Message}");
            return Result.Failure<List<ArchivePhotoMigrationRow>>(result.ErrorCode!, result.Message!);
        }

        return Result.Success(result.Value ?? new List<ArchivePhotoMigrationRow>());
    }

    private async Task<ConvertOutcome> ConvertRowAsync(ArchivePhotoMigrationRow row, CancellationToken ct)
    {
        if (row.PhotoData == null || row.PhotoData.Length == 0)
        {
            LogWarn($"历史照片转 WebP 跳过（空数据）: Id={row.Id}");
            return ConvertOutcome.Failed;
        }

        // 已经是 WebP 且不超上限：只补元数据，不重复编码
        if (ImageTranscoder.DetectFormat(row.PhotoData) == PhotoFormatConstants.WebP
            && row.PhotoData.LongLength <= PhotoCompressionConstants.HardMaxBytes)
        {
            const string metaSql = @"
UPDATE nc_biz_archive_photos
SET image_format = 'webp',
    file_size = octet_length(photo_data)
WHERE id = $1";

            var meta = await _db.ExecuteNonQueryAsync(metaSql, ct, row.Id);
            if (meta.IsFailure)
            {
                LogError($"历史照片元数据更新失败: Id={row.Id}, {meta.Message}");
                return ConvertOutcome.Failed;
            }
            return meta.Value > 0 ? ConvertOutcome.Skipped : ConvertOutcome.Failed;
        }

        var compressed = await Task.Run(
            () => ImageCompressor.CompressToWebP(row.PhotoData, row.PhotoCategory), ct);
        if (compressed.IsFailure)
        {
            LogWarn($"历史照片转 WebP 失败: Id={row.Id}, {compressed.Message}");
            return ConvertOutcome.Failed;
        }

        var data = compressed.Value.Data;
        if (data.LongLength > PhotoCompressionConstants.HardMaxBytes)
        {
            LogWarn($"历史照片转 WebP 失败（超过硬上限）: Id={row.Id}, Size={data.LongLength}B");
            return ConvertOutcome.Failed;
        }

        const string updateSql = @"
UPDATE nc_biz_archive_photos
SET photo_data = $2,
    file_size = $3,
    image_format = 'webp'
WHERE id = $1
  AND image_format <> 'webp'";

        var update = await _db.ExecuteNonQueryAsync(updateSql, ct, row.Id, data, data.LongLength);
        if (update.IsFailure)
        {
            LogError($"历史照片转 WebP 更新失败: Id={row.Id}, {update.Message}");
            return ConvertOutcome.Failed;
        }

        return update.Value > 0 ? ConvertOutcome.Converted : ConvertOutcome.Skipped;
    }

    private enum ConvertOutcome
    {
        Converted,
        Skipped,
        Failed
    }
}

/// <summary>历史照片批量转换读取行（public setter 供 IDatabaseService 映射）。</summary>
internal sealed class ArchivePhotoMigrationRow
{
    public long Id { get; set; }
    public byte[] PhotoData { get; set; } = Array.Empty<byte>();
    public string? PhotoCategory { get; set; }
    public string? ImageFormat { get; set; }
}
