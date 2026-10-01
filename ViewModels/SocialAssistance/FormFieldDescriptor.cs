using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.RegularExpressions;

namespace NewCosmos.ViewModels.SocialAssistance;

/// <summary>
/// 表单字段描述符
/// 封装字段的验证状态、样式和动画
/// </summary>
public partial class FormFieldDescriptor : ObservableObject
{
    /// <summary>
    /// 字段唯一标识符（用于日志和调试）
    /// </summary>
    public string FieldId { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>
    /// 字段标签
    /// </summary>
    [ObservableProperty]
    private string _label = string.Empty;

    /// <summary>
    /// 字段值    /// </summary>
    [ObservableProperty]
    private string _value = string.Empty;

    /// <summary>
    /// 占位符    /// </summary>
    [ObservableProperty]
    private string _placeholder = string.Empty;

    /// <summary>
    /// 是否必填
    /// </summary>
    [ObservableProperty]
    private bool _isRequired;

    /// <summary>
    /// 是否验证通过
    /// </summary>
    [ObservableProperty]
    private bool _isValid;

    /// <summary>
    /// 验证错误消息
    /// </summary>
    [ObservableProperty]
    private string _validationMessage = string.Empty;

    /// <summary>
    /// 是否获得焦点
    /// </summary>
    [ObservableProperty]
    private bool _isFocused;

    /// <summary>
    /// 自定义验证规则（正则表达式）
    /// </summary>
    public string ValidationPattern { get; set; } = string.Empty;

    /// <summary>
    /// 最小长度    /// </summary>
    public int MinLength { get; set; }

    /// <summary>
    /// 最大长度    /// </summary>
    public int MaxLength { get; set; }

    /// <summary>
    /// 样式状态（根据焦点和验证状态动态变化）
    /// </summary>
    public string FieldStyle => IsFocused ? "FormFieldFocusedStyle" :
                                !IsValid ? "FormFieldErrorStyle" :
                                "FormFieldDefaultStyle";

    /// <summary>
    /// 验证方法
    /// </summary>
    public bool Validate()
    {
        // 必填验证
        if (IsRequired && string.IsNullOrWhiteSpace(Value))
        {
            IsValid = false;
            ValidationMessage = $"{Label}不能为空";
            OnPropertyChanged(nameof(FieldStyle));
            return false;
        }

        // 如果值为空且非必填，直接通过
        if (string.IsNullOrWhiteSpace(Value))
        {
            IsValid = true;
            ValidationMessage = string.Empty;
            OnPropertyChanged(nameof(FieldStyle));
            return true;
        }

        // 最小长度验证
        if (MinLength > 0 && Value.Length < MinLength)
        {
            IsValid = false;
            ValidationMessage = $"{Label}至少需要{MinLength}个字符";
            OnPropertyChanged(nameof(FieldStyle));
            return false;
        }

        // 最大长度验证
        if (MaxLength > 0 && Value.Length > MaxLength)
        {
            IsValid = false;
            ValidationMessage = $"{Label}不能超过{MaxLength}个字符";
            OnPropertyChanged(nameof(FieldStyle));
            return false;
        }

        // 正则表达式验证
        if (!string.IsNullOrEmpty(ValidationPattern) && !Regex.IsMatch(Value, ValidationPattern))
        {
            IsValid = false;
            ValidationMessage = $"{Label}格式不正确";
            OnPropertyChanged(nameof(FieldStyle));
            return false;
        }

        IsValid = true;
        ValidationMessage = string.Empty;
        OnPropertyChanged(nameof(FieldStyle));
        return true;
    }

    /// <summary>
    /// 焦点变化时触发动画    /// </summary>
    partial void OnIsFocusedChanged(bool value)
    {
        OnPropertyChanged(nameof(FieldStyle));
    }

    /// <summary>
    /// 值变化时重新验证
    /// </summary>
    partial void OnValueChanged(string value)
    {
        Validate();
    }

    /// <summary>
    /// 必填属性变化时触发验证
    /// </summary>
    partial void OnIsRequiredChanged(bool value)
    {
        Validate();
    }

    public override string ToString()
    {
        return $"{FieldId}: {Label}";
    }
}
