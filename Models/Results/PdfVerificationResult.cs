namespace NewCosmos.Models.Results;

// 已删除（2026-11）：PdfVerificationResult 类——仅被死方法 BatchVerifyAsync 引用（全仓零调用方取证）。

public class PdfVerificationRecord
{
    public long Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ExpectedName { get; set; } = string.Empty;
    public bool IsValid { get; set; }
    public long VerifiedBy { get; set; }
    public string VerifiedByName { get; set; } = string.Empty;
    public DateTime VerifiedAt { get; set; }
    public string Notes { get; set; } = string.Empty;
    public int Version { get; set; }
    public bool IsCurrent { get; set; }
    public int ReportSize { get; set; }
}

/// <summary>同户同 hash 报告匹配结果（重复上传时用于“未变更/恢复历史版本”）。</summary>
public class ReportHashMatch
{
    public long ReportId { get; set; }
    public bool IsCurrent { get; set; }
    public int Version { get; set; }
}
