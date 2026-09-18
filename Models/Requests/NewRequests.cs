namespace NewCosmos.Models.Requests;

/// <summary>
/// 模板保存请求（上传/更新）    /// </summary>
public record TemplateSaveRequest
{
    public required string TemplateId { get; init; }
    public required string TemplateName { get; init; }
    public required string TemplateType { get; init; }
    public required byte[] FileContent { get; init; }
    public required string FileName { get; init; }
    public string ConfigJson { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string CreatedBy { get; init; } = string.Empty;
}

/// <summary>
/// 变更操作请求
/// </summary>
public record ChangeRequest
{
    public required long ApplicationId { get; init; }
    public required string ChangeType { get; init; }
    public required string ChangeReason { get; init; }
    public string ChangeReasonType { get; init; } = string.Empty;
    public DateTime ChangeDate { get; init; }
    public string OperatorName { get; init; } = string.Empty;
}

/// <summary>
/// 入户调查提交请求
/// </summary>
public record SurveyRequest
{
    public required long ApplicationId { get; init; }
    public DateTime SurveyDate { get; init; }
    public string SurveyorName { get; init; } = string.Empty;
    public string SurveyorOrganization { get; init; } = string.Empty;
    public string RespondentName { get; init; } = string.Empty;
    public string RespondentRelation { get; init; } = string.Empty;
    public string SurveyNotes { get; init; } = string.Empty;
}

/// <summary>
/// 一事一议审批请求    /// </summary>
public record SpecialApprovalRequest
{
    public required long ApplicationId { get; init; }
    public DateTime MeetingDate { get; init; }
    public string MeetingLocation { get; init; } = string.Empty;
    public string MeetingTopic { get; init; } = string.Empty;
    public string MeetingSummary { get; init; } = string.Empty;
    public string MeetingMembers { get; init; } = string.Empty;
    public string SpecialReason { get; init; } = string.Empty;
    public string SpecialCircumstances { get; init; } = string.Empty;
    public required string OverrideClassification { get; init; }
    public string ApproverName { get; init; } = string.Empty;
    public string ApproverTitle { get; init; } = string.Empty;
    public string ApproverOrganization { get; init; } = string.Empty;
    public string CreatedBy { get; init; } = string.Empty;
}

/// <summary>
/// 文档生成请求
/// </summary>
public record DocumentGenerateRequest
{
    public required long ApplicationId { get; init; }
    public required List<string> TemplateIds { get; init; }
    public string OperatorName { get; init; } = string.Empty;
    public bool PrintAfterGenerate { get; init; }
    public int Copies { get; init; } = 1;
    public bool IsDuplex { get; init; }
}

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
