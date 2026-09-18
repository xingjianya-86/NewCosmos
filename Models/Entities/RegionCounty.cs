namespace NewCosmos.Models.Entities;

/// <summary>
/// 县区实体（种子数据）
/// ID 范围为001-9999
/// </summary>
public class RegionCounty
{
    /// <summary>
    /// 主键ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 县区名称，如"哈尔滨市道里区）
    /// </summary>
    public string CountyName { get; set; } = string.Empty;

    /// <summary>
    /// 行政区划代码（6位），如"230102"
    /// </summary>
    public string CountyCode { get; set; } = string.Empty;

    /// <summary>
    /// 所属地级市名称，如"哈尔滨市"
    /// </summary>
    public string CityName { get; set; } = string.Empty;

    /// <summary>
    /// 排序    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool IsActive { get; set; } = true;
}
