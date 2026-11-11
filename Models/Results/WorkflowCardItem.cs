using CommunityToolkit.Mvvm.ComponentModel;

namespace NewCosmos.Models.Results;

/// <summary>
/// 业务申请工作流卡片操作类型
/// </summary>
public enum WorkflowCardAction
{
    None = 0,
    /// <summary>资产核查 Tab：档案制作</summary>
    ArchiveProduction = 1,
    /// <summary>申请 Tab：编辑 + 删除</summary>
    EditDelete = 2,
    /// <summary>已完结 Tab：查看档案（只读）</summary>
    ViewArchive = 3
}

/// <summary>
/// 业务申请工作流统一卡片展示项。
/// 五个 Tab 共用同一套卡片模板，差异仅由本模型承载：
/// 状态文案/色调、是否单人保、元信息（关系或编号）、地址（资产两 Tab）、操作类型。
/// </summary>
public class WorkflowCardItem : ObservableObject
{
    /// <summary>原始实体（AssetVerificationTask 或 Application），供命令分派</summary>
    public object Source { get; init; } = null!;

    public string Name { get; init; } = string.Empty;

    public string IdCard { get; init; } = string.Empty;

    /// <summary>状态文案：已提交 / 有报告 / 草稿 / 已建档 / 已完结</summary>
    public string StatusText { get; init; } = string.Empty;

    /// <summary>状态色调：Info / Warning / Danger / Success / Neutral（XAML DataTrigger 选取）</summary>
    public string StatusKind { get; init; } = "Info";

    /// <summary>是否显示"单人保"徽章</summary>
    public bool ShowSingleRescue { get; init; }

    /// <summary>渐退期注释（草稿/已建档 Tab 有有效渐退期时有值，如"渐退期至 2026-12-31"）</summary>
    public string GraceNote { get; init; } = string.Empty;

    public bool HasGraceNote => !string.IsNullOrEmpty(GraceNote);

    /// <summary>批次顺延提示（按入户调查日期锚点判定，如"本批已顺延至 2026年12月"；未顺延为空）</summary>
    public string BatchHintText { get; init; } = string.Empty;

    public bool HasBatchHint => !string.IsNullOrEmpty(BatchHintText);

    /// <summary>元信息标签（"与户主关系" / "编号"）</summary>
    public string MetaLabel { get; init; } = string.Empty;

    /// <summary>元信息值（关系中文 / 申请编号）</summary>
    public string MetaValue { get; init; } = string.Empty;

    public bool HasMeta => !string.IsNullOrEmpty(MetaLabel);

    /// <summary>家庭住址（仅资产核查两 Tab 有值）</summary>
    public string? Address { get; init; }

    public bool HasAddress => !string.IsNullOrEmpty(Address);

    /// <summary>申请添加时间</summary>
    public DateTime Time { get; init; }

    public WorkflowCardAction Action { get; init; }

    public bool ShowArchiveButton => Action == WorkflowCardAction.ArchiveProduction;

    public bool ShowEditButtons => Action == WorkflowCardAction.EditDelete;

    public bool ShowViewArchiveButton => Action == WorkflowCardAction.ViewArchive;

    /// <summary>是否显示"改期"按钮（仅草稿 Tab 的申请卡片：修改申请日期，权限 CHANGE_MANAGE）</summary>
    public bool ShowChangeDateButton { get; init; }

    /// <summary>是否显示"近亲属备案"按钮（仅申请卡片；资产核查任务卡不显示）</summary>
    public bool ShowNearRelativeButton => Source is NewCosmos.Models.Entities.Application;

    #region 批量归档勾选

    /// <summary>是否允许批量勾选（仅"已建档未提交"Tab 的申请卡片为 true）</summary>
    public bool IsBatchSelectable { get; init; }

    /// <summary>是否显示批量勾选框（= IsBatchSelectable）</summary>
    public bool ShowBatchCheckbox => IsBatchSelectable;

    /// <summary>是否显示"出文书"直达按钮（= IsBatchSelectable：已建档卡片可绕道五步表单直达档案制作页）</summary>
    public bool ShowDirectOutputButton => IsBatchSelectable;

    private bool _isSelected;

    /// <summary>批量勾选状态（ObservableObject 通知，供批量归档命令收集）</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    #endregion
}
