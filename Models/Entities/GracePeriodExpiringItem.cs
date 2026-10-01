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

    /// <summary>渐退期开始日期（SQL 别名 grace_period_start_date）</summary>
    public DateTime? GracePeriodStartDate { get; set; }

    /// <summary>渐退期结束日期（SQL 别名 grace_period_end_date）</summary>
    public DateTime? GracePeriodEndDate { get; set; }

    /// <summary>映射兼容：start_date 列（若别名未生效）</summary>
    public DateTime? StartDate { get => GracePeriodStartDate; set => GracePeriodStartDate = value; }

    /// <summary>映射兼容：end_date 列</summary>
    public DateTime? EndDate { get => GracePeriodEndDate; set => GracePeriodEndDate = value; }

    /// <summary>渐退期月数</summary>
    public int? GracePeriodMonths { get; set; }

    /// <summary>已过期天数（TODAY - end_date；进行中为负）</summary>
    public int ExpiredDays { get; set; }

    /// <summary>时间长度天数（|today - end_date|）</summary>
    public int DurationDays { get; set; }

    /// <summary>是否进行中（end_date &gt;= today）</summary>
    public bool IsActivePeriod { get; set; }

    /// <summary>状态文案：进行中 / 已到期</summary>
    public string StatusText => IsActivePeriod ? "进行中" : "已到期";

    /// <summary>时间长度文案：剩 N 天 / 逾期 N 天</summary>
    public string DurationText => IsActivePeriod
        ? $"剩 {DurationDays} 天"
        : $"逾期 {DurationDays} 天";

    /// <summary>渐退前原分类</summary>
    public string? OriginalClassification { get; set; }

    /// <summary>渐退前原保障金额</summary>
    public decimal? OriginalGuaranteeAmount { get; set; }

    /// <summary>渐退期内实际应发月保障金（已封顶）</summary>
    public decimal? GraceGrantAmount { get; set; }
}
