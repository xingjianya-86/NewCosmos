namespace NewCosmos.Models.Entities;

/// <summary>
/// 字典项更新实体（写入 nc_dict_items_updates 表）
/// </summary>
public class DictItemUpdate
{
    /// <summary>
    /// 引用种子数据ID
    /// </summary>
    public int Id { get; set; }

    public string Category { get; set; } = string.Empty;
    public string ItemKey { get; set; } = string.Empty;
    public string ItemValue { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 更新来源：user（用户）?system（系统）
    /// </summary>
    public string Source { get; set; } = "user";

    /// <summary>
    /// 操作类型：update（更新）、add（新增）、delete（删除）
    /// </summary>
    public string Action { get; set; } = "update";

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 创建人ID
    /// </summary>
    public int CreatedBy { get; set; }

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}