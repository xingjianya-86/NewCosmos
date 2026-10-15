using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.ArchiveManagement;

/// <summary>
/// 档案照片服务：手机端采集的证件/核查照片的存取。
/// 照片按人（applicant_id_card）+ 申请（application_id）+ 类别（photo_category）归档，存库 bytea。
/// </summary>
public interface IArchivePhotoService
{
    /// <summary>保存一张照片，返回新记录 ID。</summary>
    Task<Result<long>> SavePhotoAsync(ArchivePhoto photo, CancellationToken ct = default);

    /// <summary>列出指定人的照片元数据（不含 photo_data 二进制，列表用；按类别可选过滤）。</summary>
    Task<Result<List<ArchivePhoto>>> GetByPersonAsync(
        string applicantIdCard, string? category = null, CancellationToken ct = default);

    /// <summary>取回单张照片的二进制（预览/打印用）。</summary>
    Task<Result<byte[]>> GetPhotoDataAsync(long photoId, CancellationToken ct = default);

    /// <summary>删除一张照片（采集人误删/重拍场景）。</summary>
    Task<Result<bool>> DeletePhotoAsync(long photoId, CancellationToken ct = default);
}
