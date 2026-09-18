namespace NewCosmos.Models.Entities;

/// <summary>
/// ?社区实体（种子数据）
/// ID 范围为00001-999999
/// </summary>
public class RegionVillage
{
    /// <summary>
    /// 主键ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 外键，关联乡镇ID
    /// </summary>
    public int TownId { get; set; }

    /// <summary>
    /// ?社区名称
    /// </summary>
    public string VillageName { get; set; } = string.Empty;

    /// <summary>
    /// 行政区划代码（12位）
    /// </summary>
    public string VillageCode { get; set; } = string.Empty;

    /// <summary>
    /// 类型：村委会/居委    /// </summary>
    public string VillageType { get; set; } = string.Empty;

    /// <summary>
    /// 排序    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsActive { get; set; } = true;
}
