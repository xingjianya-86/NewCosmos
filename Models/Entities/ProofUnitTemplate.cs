namespace NewCosmos.Models.Entities;

/// <summary>
/// 证明单位模板实体（nc_config_proof_unit_templates）
/// 证明接收单位与专属模板映射（全局共享）
/// </summary>
public class ProofUnitTemplate
{
    public long Id { get; set; }

    /// <summary>接收单位名称（唯一）</summary>
    public string UnitName { get; set; } = string.Empty;

    /// <summary>关联 nc_biz_templates.id（专属证明模板）</summary>
    public long TemplateId { get; set; }

    public int? CreatedBy { get; set; }

    public int? UpdatedBy { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// 单位模板展示项（含模板名称与类型，供 Picker/列表展示）
/// </summary>
public class ProofUnitTemplateItem
{
    public long Id { get; set; }

    public string UnitName { get; set; } = string.Empty;

    public long TemplateId { get; set; }

    public string TemplateName { get; set; } = string.Empty;

    public string FileType { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public string CreatedAtDisplay => CreatedAt.ToString("yyyy-MM-dd HH:mm");

    public string UpdatedAtDisplay => UpdatedAt?.ToString("yyyy-MM-dd HH:mm") ?? "--";
}