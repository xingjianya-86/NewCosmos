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
public class WorkflowCardItem
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
}
