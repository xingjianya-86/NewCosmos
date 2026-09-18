using NewCosmos.Constants;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 一事一议审批实体（纯 POCO，对对应 nc_biz_special_approvals 表）
/// </summary>
public class SpecialApproval
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }
    public DateTime MeetingDate { get; set; }
    public string MeetingLocation { get; set; } = string.Empty;
    public string MeetingTopic { get; set; } = string.Empty;
    public string MeetingSummary { get; set; } = string.Empty;
    public string MeetingMembers { get; set; } = string.Empty;
    public string SpecialReason { get; set; } = string.Empty;
    public string SpecialCircumstances { get; set; } = string.Empty;
    public string OverrideClassification { get; set; } = string.Empty;
    public string ApproverName { get; set; } = string.Empty;
    public string ApproverTitle { get; set; } = string.Empty;
    public string ApproverOrganization { get; set; } = string.Empty;
    public string AttachmentFile { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; }
    public DateTime DeletedAt { get; set; }
}

/// <summary>
/// 一事一议申报表实体（纯 POCO，对对应 nc_biz_special_approval_forms 表）
/// </summary>
public class SpecialApprovalForm
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }
    public string FormNo { get; set; } = string.Empty;
    public string BasicSituation { get; set; } = string.Empty;
    public string SpecialMatters { get; set; } = string.Empty;
    public string TemplateKey { get; set; } = string.Empty;
    public string ReportUnit { get; set; } = string.Empty;
    public string HandlerName { get; set; } = string.Empty;
    public DateTime? FormDate { get; set; }
    public string AuditResult { get; set; } = string.Empty;
    public DateTime? AuditDate { get; set; }
    public string Status { get; set; } = ApplicationStatusCodes.DRAFT;
    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; }
    public DateTime DeletedAt { get; set; }
}

/// <summary>
/// 变更快照实体（纯 POCO，对对应 nc_biz_change_snapshots 表）
/// </summary>
public class ChangeSnapshot
{
    public long Id { get; set; }
    public long ChangeId { get; set; }
    public string SnapshotType { get; set; } = string.Empty;
    public string SnapshotData { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// 变更明细实体（纯 POCO，对对应 nc_biz_change_details 表）
/// </summary>
public class ChangeDetail
{
    public long Id { get; set; }
    public long ChangeId { get; set; }
    public string FieldName { get; set; } = string.Empty;
    public string FieldLabel { get; set; } = string.Empty;
    public string OldValue { get; set; } = string.Empty;
    public string NewValue { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
