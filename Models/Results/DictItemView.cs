namespace NewCosmos.Models.Results;

/// <summary>
/// 字典项视图结果（查询专用    /// 合并种子数据和用户更新数    /// </summary>
public class DictItemView
{
    public int Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string ItemKey { get; set; } = string.Empty;
    public string ItemValue { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; }
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 数据状态：seed（种子数据）?updated（已更新    /// </summary>
    public string Status { get; set; } = "seed";

    /// <summary>
    /// 更新来源：user（用户）?system（系统）
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// 操作类型：update（更新）、add（新增）、delete（删除）
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// 更新人ID
    /// </summary>
    public int UpdatedBy { get; set; }
}