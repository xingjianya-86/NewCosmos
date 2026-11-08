namespace NewCosmos.Models.Requests;

/// <summary>
/// 一事一议申报表请求（保存/提交申报）
/// </summary>
public record SpecialApprovalFormRequest
{
    public required long ApplicationId { get; init; }
    public string FormNo { get; init; } = string.Empty;
    public string BasicSituation { get; init; } = string.Empty;
    public string SpecialMatters { get; init; } = string.Empty;
    public string TemplateKey { get; init; } = string.Empty;
    public string ReportUnit { get; init; } = string.Empty;
    public string HandlerName { get; init; } = string.Empty;
    public DateTime? FormDate { get; init; }
    public string AuditResult { get; init; } = string.Empty;
    public DateTime? AuditDate { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
}

/// <summary>
/// 一事一议会议审议请求（写入最终会议审议结果并闭环申报表状态）
/// </summary>
public record SpecialApprovalReviewRequest
{
    public required long ApplicationId { get; init; }
    public DateTime MeetingDate { get; init; }
    public string MeetingLocation { get; init; } = string.Empty;
    public string MeetingTopic { get; init; } = string.Empty;
    public string MeetingSummary { get; init; } = string.Empty;
    public string MeetingMembers { get; init; } = string.Empty;
    public string SpecialReason { get; init; } = string.Empty;
    public string SpecialCircumstances { get; init; } = string.Empty;
    public string OverrideClassification { get; init; } = string.Empty;
    public string ApproverName { get; init; } = string.Empty;
    public string ApproverTitle { get; init; } = string.Empty;
    public string ApproverOrganization { get; init; } = string.Empty;
    public bool Approved { get; init; }
    public string CreatedBy { get; init; } = string.Empty;
}
