namespace NewCosmos.Models.Entities;

/// <summary>
/// 签名留痕表实体（nc_biz_signatures）。
/// 存储手写签名 PNG（bytea）及其关联的业务上下文，供文书渲染时嵌入「本人签名」等占位符位置。
/// 签名属生物特征留痕数据，仅存 PNG 图像，不做比对识别。
/// </summary>
public class Signature
{
    public long Id { get; set; }

    /// <summary>签名 PNG 二进制（灰度图，白底黑字）。</summary>
    public byte[] SignatureData { get; set; } = Array.Empty<byte>();

    /// <summary>申请人/签名关联人身份证号（跨端匹配打印任务的关键字段）。</summary>
    public string ApplicantIdCard { get; set; } = string.Empty;

    /// <summary>业务类型（与打印 businessType 同一口径，如 FamilyApplication / TempRelief）。</summary>
    public string BusinessType { get; set; } = string.Empty;

    /// <summary>业务记录 ID（如 nc_biz_applications.id）。</summary>
    public long? BusinessId { get; set; }

    /// <summary>签名显示名（如「张三」），仅用于留痕展示。</summary>
    public string? SignerName { get; set; }

    /// <summary>签名角色：本人 / 代领人 / 经办人。</summary>
    public string SignerRole { get; set; } = "本人";

    /// <summary>采集人 ID（nc_sys_users.id）。</summary>
    public int? CreatedBy { get; set; }

    /// <summary>采集人姓名。</summary>
    public string? CreatedByName { get; set; }

    /// <summary>采集时间。</summary>
    public DateTime CreatedAt { get; set; }
}
