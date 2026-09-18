namespace NewCosmos.Models.Entities;

/// <summary>
/// 乡镇/街道实体（种子数据）
/// ID 范围为0001-99999
/// </summary>
public class RegionTown
{
    /// <summary>
    /// 主键ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 外键，关联县区ID
    /// </summary>
    public int CountyId { get; set; }

    /// <summary>
    /// 乡镇/街道名称
    /// </summary>
    public string TownName { get; set; } = string.Empty;

    /// <summary>
    /// 行政区划代码（6位）
    /// </summary>
    public string TownCode { get; set; } = string.Empty;

    /// <summary>
    /// 类型：镇/?街道
    /// </summary>
    public string TownType { get; set; } = string.Empty;

    /// <summary>
    /// 排序    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsActive { get; set; } = true;
}
