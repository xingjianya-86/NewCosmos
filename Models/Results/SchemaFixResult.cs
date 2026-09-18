namespace NewCosmos.Models.Results;

public class SchemaFixResult
{
    public int TotalFixed { get; set; }
    public int FailedCount { get; set; }
    public int SkippedCount { get; set; }
    public List<string> FixedItems { get; set; } = new();
    public List<string> FailedItems { get; set; } = new();
    public List<string> SkippedItems { get; set; } = new();

    public bool HasFailures => FailedCount > 0;

    public string Summary => TotalFixed == 0 && FailedCount == 0 && SkippedCount == 0
        ? "无需修复"
        : $"修复完成：成{TotalFixed} 项，失败 {FailedCount} 项，跳过 {SkippedCount} 项（多余表不处理";
}
