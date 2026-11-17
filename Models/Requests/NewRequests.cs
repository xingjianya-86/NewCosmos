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
    // 申报内容（审议锁定表单前的最后一并落库：原实现只写会议字段，提交后新填的内容会被静默丢弃）
    public string BasicSituation { get; init; } = string.Empty;
    public string TemplateKey { get; init; } = string.Empty;
    public DateTime MeetingDate { get; init; }
    // 会议地点/议题/纪要三栏已于 2026-11-17 移除（地点由模板承载；议题/纪要无任何读取方）
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
