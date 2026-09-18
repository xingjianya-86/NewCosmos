using CommunityToolkit.Mvvm.ComponentModel;

namespace NewCosmos.Models.Results;

/// <summary>
/// 字段项模型（用于右侧字段选择边栏）
/// </summary>
public partial class FieldItem : ObservableObject
{
    /// <summary>
    /// 显示名称（中文）
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// 字段值
    /// </summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// 目标属性名（用于填充到表单）
    /// </summary>
    public string TargetProperty { get; set; } = string.Empty;

    /// <summary>
    /// 字段分组（基本信息、地址、经济信息等）
    /// </summary>
    public string Group { get; set; } = string.Empty;

    /// <summary>
    /// 是否选中
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// 是否已填充
    /// </summary>
    [ObservableProperty]
    private bool _isFilled;

    /// <summary>
    /// 是否有值
    /// </summary>
    public bool HasValue => !string.IsNullOrWhiteSpace(Value);

    public FieldItem() { }

    public FieldItem(string displayName, string value, string targetProperty, string group = "")
    {
        DisplayName = displayName;
        Value = value ?? string.Empty;
        TargetProperty = targetProperty;
        Group = group;
    }
}
