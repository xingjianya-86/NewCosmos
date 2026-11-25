using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.ArchiveManagement;

/// <summary>
/// 待归属照片池：身份证未定时先存照片，拿到身份证并确认人员已建档后再归属到档案照片。
/// </summary>
public interface IArchivePhotoPendingService
{
    /// <summary>保存一张待归属照片（内部压缩为 WebP），返回记录 ID。</summary>
    Task<Result<long>> SavePendingAsync(byte[] data, string category, string? photoName,
        string? recognizedName, string? recognizedIdCard, string? source, CancellationToken ct = default);

    /// <summary>分页查询待归属照片（不含二进制）。</summary>
    Task<Result<PagedResult<PendingArchivePhoto>>> GetPendingPagedAsync(string? keyword, string? category,
        int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>取回待归属照片二进制。</summary>
    Task<Result<byte[]>> GetPendingDataAsync(long id, CancellationToken ct = default);

    /// <summary>删除一张待归属照片。</summary>
    Task<Result<bool>> DeletePendingAsync(long id, CancellationToken ct = default);

    /// <summary>待归属数量（页面徽标用）。</summary>
    Task<Result<int>> CountPendingAsync(CancellationToken ct = default);

    /// <summary>
    /// 归属到已建档人员：身份证必须已存在于低收入/高龄/临时救助/资产核查档案；
    /// 复用 <see cref="IArchivePhotoService.InsertPhotosAsync"/> 的压缩/命名/入库链路。
    /// </summary>
    Task<Result<BatchPhotoInsertResult>> AssignPendingAsync(IReadOnlyList<long> ids, string idCard,
        CancellationToken ct = default);
}
