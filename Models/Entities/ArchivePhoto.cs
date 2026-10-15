using NewCosmos.Constants;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 档案照片实体（nc_biz_archive_photos）。
/// 手机端采集的证件/核查照片，按人（applicant_id_card）+ 申请（application_id）+ 类别（photo_category）归档。
/// 照片属公民身份敏感信息：存库 bytea（内网 PostgreSQL），仅作留痕存档，打印在批次三实现。
/// </summary>
public class ArchivePhoto
{
    public long Id { get; set; }

    /// <summary>照片二进制（JPEG，长边压缩后）。</summary>
    public byte[] PhotoData { get; set; } = Array.Empty<byte>();

    /// <summary>照片所属人身份证号（跨申请按人聚合的关键字段）。</summary>
    public string ApplicantIdCard { get; set; } = string.Empty;

    /// <summary>申请/档案 ID（nc_biz_applications.id）。</summary>
    public long? ApplicationId { get; set; }

    /// <summary>照片类别（PhotoCategoryConstants：身份证/户口本/公示核查/其他证件）。</summary>
    public string PhotoCategory { get; set; } = PhotoCategoryConstants.OTHER_CERT;

    /// <summary>照片备注/说明（如"户口本第2页"）。</summary>
    public string? PhotoName { get; set; }

    /// <summary>照片字节数（压缩后）。</summary>
    public long FileSize { get; set; }

    /// <summary>采集人 ID（nc_sys_users.id）。</summary>
    public int? CreatedBy { get; set; }

    /// <summary>采集人姓名。</summary>
    public string? CreatedByName { get; set; }

    /// <summary>采集时间。</summary>
    public DateTime CreatedAt { get; set; }
}
