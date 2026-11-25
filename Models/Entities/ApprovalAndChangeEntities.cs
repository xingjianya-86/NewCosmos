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

    /// <summary>适用情形代码（新规 8 类；见 SpecialApprovalConstants.CaseTypes）</summary>
    public string? CaseType { get; set; }

    /// <summary>报县日期（10 个工作日评议计时起点）</summary>
    public DateTime? SubmittedAt { get; set; }

    /// <summary>备案日期（5 个工作日备案终点）</summary>
    public DateTime? FiledAt { get; set; }

    /// <summary>负面清单勾选项（code 逗号分隔；正常提交应为空，留痕用）</summary>
    public string? NegativeItems { get; set; }

    /// <summary>备案回执文件（文件名/路径）</summary>
    public string? FilingReceipt { get; set; }

    /// <summary>是否纳入长期公示（一事一议通过对象标记）</summary>
    public bool IsLongTermPublicized { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; }
    public DateTime DeletedAt { get; set; }
}
