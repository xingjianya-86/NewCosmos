using CommunityToolkit.Mvvm.ComponentModel;

namespace NewCosmos.Models.Results;

/// <summary>
/// 侧边栏分组（用于档案制作页面右侧信息面板）
/// </summary>
public class SidebarGroup
{
    public string GroupName { get; set; } = string.Empty;
    public List<SidebarItem> Items { get; set; } = new();

    public SidebarGroup() { }

    public SidebarGroup(string groupName)
    {
        GroupName = groupName;
    }
}

/// <summary>
/// 侧边栏单行数据项（可复制、可标记自动填充状态）
/// </summary>
public partial class SidebarItem : ObservableObject
{
    /// <summary>
    /// 字段显示名（中文）
    /// </summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// 字段值
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// 对应的 FieldKeys 常量（如 APPLICANT_NAME）
    /// </summary>
    public string FieldKey { get; set; } = string.Empty;

    /// <summary>
    /// 是否可匹配到 FieldKeys
    /// </summary>
    public bool IsMatchable { get; set; }

    /// <summary>
    /// 是否已自动填充到主表单
    /// </summary>
    [ObservableProperty]
    private bool _isAutoFilled;

    /// <summary>
    /// 显示文本（Label: Value）
    /// </summary>
    public string DisplayText => $"{Label}: {Value}";

    /// <summary>
    /// 是否有值
    /// </summary>
    public bool HasValue => !string.IsNullOrWhiteSpace(Value);
}
