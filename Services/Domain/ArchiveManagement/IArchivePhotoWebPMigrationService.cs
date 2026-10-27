using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.ArchiveManagement;

/// <summary>
/// 历史档案照片批量转 WebP：把 nc_biz_archive_photos 中 image_format != 'webp' 的旧照片
/// 逐批压缩为 WebP（单张 ≤500KB），更新 photo_data/file_size/image_format。
/// 可取消、可重复执行（已转换的记录自动跳过）；失败项保留原格式，便于修复后重试。
/// </summary>
public interface IArchivePhotoWebPMigrationService
{
    /// <summary>统计待转换的历史照片数量。</summary>
    Task<Result<int>> CountPendingAsync(CancellationToken ct = default);

    /// <summary>执行批量转换；进度通过 <paramref name="progress"/> 上报。</summary>
    Task<Result<PhotoWebPMigrationResult>> ConvertAllAsync(
        IProgress<PhotoWebPMigrationProgress>? progress = null,
        CancellationToken ct = default);
}

/// <summary>批量转换进度快照。</summary>
public sealed record PhotoWebPMigrationProgress(
    int Total, int Processed, int Converted, int Skipped, int Failed, long CurrentPhotoId);

/// <summary>批量转换结果汇总。</summary>
public sealed record PhotoWebPMigrationResult(
    int Total, int Converted, int Skipped, int Failed, bool Cancelled,
    IReadOnlyList<long> FailedPhotoIds);
