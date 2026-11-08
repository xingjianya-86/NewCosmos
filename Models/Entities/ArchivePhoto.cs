using NewCosmos.Constants;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 档案照片实体（nc_biz_archive_photos）。
/// 手机端采集的证件/核查照片，按人（applicant_id_card）+ 申请（application_id）+ 类别（photo_category）归档。
/// 照片属公民身份敏感信息：存库 bytea（内网 PostgreSQL），新采集统一 WebP（单张 ≤500KB）。
/// </summary>
public class ArchivePhoto
{
    public long Id { get; set; }

    /// <summary>照片二进制（新采集为 WebP，长边压缩后；旧数据可能为 JPEG/PNG）。</summary>
    public byte[] PhotoData { get; set; } = Array.Empty<byte>();

    /// <summary>照片格式（webp/jpeg/png）。2026-10-24 起新采集统一为 webp；旧数据默认 jpeg。</summary>
    public string ImageFormat { get; set; } = PhotoFormatConstants.Jpeg;

    /// <summary>照片所属人身份证号（跨申请按人聚合的关键字段）。</summary>
    public string ApplicantIdCard { get; set; } = string.Empty;

    /// <summary>申请/档案 ID（nc_biz_applications.id）。</summary>
    public long? ApplicationId { get; set; }

    /// <summary>照片类别（PhotoCategoryConstants：身份证/户口本/公示核查/其他证件）。</summary>
    public string PhotoCategory { get; set; } = PhotoCategoryConstants.OTHER_CERT;

    /// <summary>照片备注/说明（如"户口本第2页"）。
    /// 2026-11-04 起新采集按规则生成：姓名_身份证后4位_类别_编号（该人该类别内流水，往下递增不覆盖）。</summary>
    public string? PhotoName { get; set; }

    /// <summary>列表显示名：有 PhotoName 显示之，否则回退"照片 #Id"（旧数据未回填时）。
    /// 只读计算属性，不参与 DB 列映射（映射仅取可写属性）。</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(PhotoName) ? $"照片 #{Id}" : PhotoName;

    /// <summary>照片字节数（压缩后）。</summary>
    public long FileSize { get; set; }

    /// <summary>采集人 ID（nc_sys_users.id）。</summary>
    public int? CreatedBy { get; set; }

    /// <summary>采集人姓名。</summary>
    public string? CreatedByName { get; set; }

    /// <summary>采集时间。</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>类别中文名（列表展示用）。</summary>
    public string PhotoCategoryName => PhotoCategoryConstants.GetDisplayName(PhotoCategory);
}
