namespace NewCosmos.Models.Entities;

/// <summary>
/// 字典项实体（种子数据    /// ID 为固定值，非自    /// </summary>
public class SysDictionaryItem
{
    /// <summary>
    /// 主键ID（固定值）
    /// </summary>
    public int Id { get; set; }
    
    public string Category { get; set; } = string.Empty;
    public string ItemKey { get; set; } = string.Empty;
    public string ItemValue { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
    public string Description { get; set; } = string.Empty;
}