using System.Collections.ObjectModel;

namespace NewCosmos.Models.Results;

public class SchemaValidationResult
{
    public DateTime CheckedAt { get; set; }
    public int TotalSchemas { get; set; }
    public int TotalTablesInYaml { get; set; }
    public int TotalTablesInDatabase { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }
    public int InfoCount { get; set; }
    public bool IsHealthy => ErrorCount == 0;
    public ObservableCollection<SchemaDifference> Differences { get; set; } = new();

    public List<SchemaDifference> GetErrors() => Differences.Where(d => d.Severity == SeverityLevel.Error).ToList();
    public List<SchemaDifference> GetWarnings() => Differences.Where(d => d.Severity == SeverityLevel.Warning).ToList();
    public List<SchemaDifference> GetInfos() => Differences.Where(d => d.Severity == SeverityLevel.Info).ToList();

    public string Summary => IsHealthy
        ? $"检查通过：{TotalSchemas} ?Schema，{TotalTablesInYaml} 张表，无错误"
        : $"检查完成：{ErrorCount} 错误 | {WarningCount} 警告 | {InfoCount} 提示";

    public string GenerateExportText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("================");
        sb.AppendLine("表结构检查报告");
        sb.AppendLine("================");
        sb.AppendLine($"检查时间: {CheckedAt:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Schema总数: {TotalSchemas}");
        sb.AppendLine($"数据库表: {TotalTablesInDatabase} 张");
        sb.AppendLine($"YAML表: {TotalTablesInYaml} 张");
        sb.AppendLine();

        if (ErrorCount > 0)
        {
            sb.AppendLine("【错误】");
            foreach (var diff in GetErrors())
            {
                sb.AppendLine(FormatDifference(diff));
            }
            sb.AppendLine();
        }

        if (WarningCount > 0)
        {
            sb.AppendLine("【警告】");
            foreach (var diff in GetWarnings())
            {
                sb.AppendLine(FormatDifference(diff));
            }
            sb.AppendLine();
        }

        if (InfoCount > 0)
        {
            sb.AppendLine("【信息】");
            foreach (var diff in GetInfos())
            {
                sb.AppendLine(FormatDifference(diff));
            }
            sb.AppendLine();
        }

        sb.AppendLine("========");
        sb.AppendLine("检查结束");
        sb.AppendLine("========");

        return sb.ToString();
    }

    private static string FormatDifference(SchemaDifference diff)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[{diff.Severity}] 表: {diff.TableName}");

        if (!string.IsNullOrEmpty(diff.ColumnName))
        {
            sb.AppendLine($"列: {diff.ColumnName}");
        }

        if (!string.IsNullOrEmpty(diff.Expected))
        {
            sb.AppendLine($"期望: {diff.Expected}");
        }

        if (!string.IsNullOrEmpty(diff.Actual))
        {
            sb.AppendLine($"实际: {diff.Actual}");
        }

        sb.AppendLine($"描述: {diff.Description}");

        if (!string.IsNullOrEmpty(diff.Suggestion))
        {
            sb.AppendLine($"建议: {diff.Suggestion}");
        }

        sb.AppendLine();
        return sb.ToString();
    }
}
