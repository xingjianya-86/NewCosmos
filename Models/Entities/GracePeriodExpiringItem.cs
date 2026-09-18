namespace NewCosmos.Models.Entities;

/// <summary>
/// 渐退期到期列表项（JOIN 渐退期表 + 申请表，供列表页面展示）
/// </summary>
public class GracePeriodExpiringItem
{
    /// <summary>申请ID（导航到经济复核用）</summary>
    public long ApplicationId { get; set; }

    /// <summary>申请编号</summary>
    public string? ApplicationNo { get; set; }

    /// <summary>申请人姓名</summary>
    public string? ApplicantName { get; set; }

    /// <summary>申请人身份证号</summary>
    public string? ApplicantIdCard { get; set; }

    /// <summary>当前分类结果</summary>
    public string? ClassificationResult { get; set; }

    /// <summary>渐退期开始日期</summary>
    public DateTime? GracePeriodStartDate { get; set; }

    /// <summary>渐退期结束日期（已过期）</summary>
    public DateTime? GracePeriodEndDate { get; set; }

    /// <summary>渐退期月数</summary>
    public int? GracePeriodMonths { get; set; }

    /// <summary>已过期天数（TODAY - end_date）</summary>
    public int ExpiredDays { get; set; }

    /// <summary>渐退前原分类</summary>
    public string? OriginalClassification { get; set; }

    /// <summary>渐退前原保障金额</summary>
    public decimal? OriginalGuaranteeAmount { get; set; }
}
