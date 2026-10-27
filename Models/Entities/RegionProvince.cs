namespace NewCosmos.Models.Entities;

/// <summary>
/// 省级行政区实体（2026-10-26 新增，真级联省）
/// ID 1..34，与 sort_order 一致；province_code 为民政 2 位省码（与 city_code 前 2 位对齐）。
/// </summary>
public class RegionProvince
{
    /// <summary>主键ID</summary>
    public int Id { get; set; }

    /// <summary>省级行政区名称，如"黑龙江省"</summary>
    public string ProvinceName { get; set; } = string.Empty;

    /// <summary>民政 2 位省码，如 23=黑龙江</summary>
    public string ProvinceCode { get; set; } = string.Empty;

    /// <summary>排序号</summary>
    public int SortOrder { get; set; }

    /// <summary>是否启用</summary>
    public bool IsActive { get; set; } = true;
}
