using NewCosmos.Models.Entities;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Domain.ArchiveManagement;

/// <summary>
/// 档案照片服务：手机端采集的证件/核查照片的存取。
/// 照片按人（applicant_id_card）+ 申请（application_id）+ 类别（photo_category）归档，存库 bytea。
/// 新采集统一由保存入口压缩为 WebP（单张 ≤500KB）；旧 JPEG/PNG 通过 image_format 兼容。
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

    /// <summary>
    /// 照片补传候选（人员维度）：按姓名/身份证检索用户可补传的人员，附带所属业务域与已有照片数。
    /// businessType 空 = 全部域；同一身份证跨域聚合为一行；不做"未传"过滤（已传也可继续补传）。
    /// </summary>
    Task<Result<PagedResult<PhotoBackfillCandidate>>> GetBackfillCandidatesAsync(
        string? businessType, string? keyword,
        int pageIndex, int pageSize, CancellationToken ct = default);

    /// <summary>
    /// 照片查询（桌面端/导出用）：关键词匹配身份证或照片备注，类别可过滤，分页返回元数据（不含二进制）。
    /// </summary>
    Task<Result<PagedResult<ArchivePhoto>>> GetPhotosPagedAsync(
        string? keyword, string? category, int pageIndex, int pageSize, CancellationToken ct = default);
}
