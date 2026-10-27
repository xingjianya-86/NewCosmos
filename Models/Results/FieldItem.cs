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
    /// 数据来源（资产核查 / 历史档案 / 导入台账 / 系统默认 / 无）
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// 该字段所在表单步骤（1~5）；无默认值时"去填写"跳转用
    /// </summary>
    public int TargetStep { get; set; }

    /// <summary>
    /// 是否无默认值（需手动填写；已填充的不再算缺失）
    /// </summary>
    public bool IsMissing => !HasValue && !IsFilled;

    /// <summary>
    /// 无默认值提示文案
    /// </summary>
    public string MissingText => IsMissing ? "无默认值，需手填" : string.Empty;

    /// <summary>
    /// 是否选中
    /// </summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// 是否已填充
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMissing))]
    [NotifyPropertyChangedFor(nameof(MissingText))]
    private bool _isFilled;

    /// <summary>
    /// 是否有值
    /// </summary>
    public bool HasValue => !string.IsNullOrWhiteSpace(Value);

    public FieldItem() { }

    public FieldItem(string displayName, string value, string targetProperty, string group = "",
        int targetStep = 0, string source = "")
    {
        DisplayName = displayName;
        Value = value ?? string.Empty;
        TargetProperty = targetProperty;
        Group = group;
        TargetStep = targetStep;
        Source = source;
    }
}
