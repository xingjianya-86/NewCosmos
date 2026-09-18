namespace NewCosmos.Models.Results;

public class MonthlyVerificationStats
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int TotalCount { get; set; }
    public int SubmittedCount { get; set; }      // 状态0：已提交申请
    public int HasReportCount { get; set; }      // 状态1：有报告未建档
    public int ArchivedCount { get; set; }       // 状态2：已完成建档
    public int RejectedCount { get; set; }       // 状态3：申请被拒
    public decimal TotalAssetValue { get; set; }
    public DateTime GeneratedAt { get; set; }

    // 兼容旧属性
    public int PendingCount => SubmittedCount;
    public int CompletedCount => HasReportCount;
    public int ErrorCount => RejectedCount;
}
