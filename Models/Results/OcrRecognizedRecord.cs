using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NewCosmos.Constants;
using NewCosmos.Helpers;
using NewCosmos.Services.Platform;

namespace NewCosmos.Models.Results;

/// <summary>
/// 分配页的可编辑识别字段条目（Entry 双向绑定 Value，确认回填以编辑后值为准）。
/// </summary>
public sealed partial class OcrFieldItem : ObservableObject
{
    public OcrFieldItem(string key, string label, string value)
    {
        Key = key;
        Label = label;
        _value = value;
    }

    /// <summary>字段键（OcrFieldKeys 契约）</summary>
    public string Key { get; }

    /// <summary>中文标签（展示）</summary>
    public string Label { get; }

    /// <summary>字段值（可编辑）</summary>
    [ObservableProperty] private string _value;
}

/// <summary>
/// 分配页的工作记录：一条识别结果 + 可编辑字段 + 目标选项 + 当前选中目标。
/// </summary>
public sealed partial class OcrRecognizedRecord : ObservableObject
{
    public OcrRecognizedRecord(OcrRecognizeResult result)
    {
        Result = result;
        Category = result.Category;
        foreach (var kv in result.Fields)
        {
            if (!string.IsNullOrWhiteSpace(kv.Key))
                FieldItems.Add(new OcrFieldItem(kv.Key, OcrFieldKeys.GetFieldLabel(kv.Key), kv.Value ?? string.Empty));
        }
    }

    /// <summary>原始识别结果（Raw/Lines 展示用）</summary>
    public OcrRecognizeResult Result { get; }

    public string Category { get; }

    /// <summary>可编辑字段条目</summary>
    public ObservableCollection<OcrFieldItem> FieldItems { get; } = new();

    /// <summary>目标下拉选项（Initialize 时由上下文固化；含"不使用"）</summary>
    public ObservableCollection<OcrAssignmentTarget> TargetOptions { get; } = new();

    /// <summary>当前选中目标（Picker SelectedItem 双向绑定）</summary>
    [ObservableProperty]
    private OcrAssignmentTarget? _selectedTarget;

    /// <summary>当前选中目标 Key（规划器/冲突检测用）</summary>
    public string SelectedTargetKey => SelectedTarget?.Key ?? OcrAssignmentPlanner.TargetKeys.Ignore;

    partial void OnSelectedTargetChanged(OcrAssignmentTarget? value)
        => OnPropertyChanged(nameof(SelectedTargetKey));

    /// <summary>身份证校验码是否不符（仅 ID_CARD 类别有意义；显示警告徽标）</summary>
    [ObservableProperty] private bool _idCardChecksumInvalid;

    /// <summary>记录是否有可回填内容（字段全空 = 无内容，目标锁定"不使用"）</summary>
    public bool HasContent => Result.HasAny;

    /// <summary>取编辑后的字段值（回填用；键见 OcrFieldKeys，缺省空串）</summary>
    public string GetField(string key)
        => FieldItems.FirstOrDefault(f => f.Key == key)?.Value?.Trim() ?? string.Empty;

    /// <summary>原始识别文本（Expander 展示）</summary>
    public string RawText => string.IsNullOrWhiteSpace(Result.Raw)
        ? string.Join("\n", Result.Lines)
        : Result.Raw;
}
