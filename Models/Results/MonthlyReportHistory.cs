using NewCosmos.Services.Domain.AssetVerification;

namespace NewCosmos.Models.Results;

public class MonthlyReportHistory
{
    public long Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; }
    public string ReportType { get; set; } = string.Empty;
    public int TotalCount { get; set; }
    public int CompletedCount { get; set; }
    public int PendingCount { get; set; }
    public int ErrorCount { get; set; }
    public string GeneratedBy { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public string FilePath { get; set; } = string.Empty;

    // 周报支持
    public int? WeekNumber { get; set; }
    public DateTime? WeekStart { get; set; }
    public DateTime? WeekEnd { get; set; }
    public string PeriodType { get; set; } = "monthly";

    /// <summary>展示用：月报显示"7月"，周报显示"第31周"</summary>
    public string PeriodDisplay => PeriodType == "weekly" && WeekNumber.HasValue
        ? $"第{WeekNumber}周"
        : $"{Month}月";
}

public class MonthlyReportData
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int? WeekNumber { get; set; }
    public string PeriodType { get; set; } = "monthly";
    public DateTime GeneratedAt { get; set; }
    public string GeneratedBy { get; set; } = string.Empty;
    public MonthlyVerificationStats Stats { get; set; } = new();
    public List<AssetVerificationTask> Tasks { get; set; } = new();
}

public enum ReportTemplateType
{
    Default,
    Custom
}
