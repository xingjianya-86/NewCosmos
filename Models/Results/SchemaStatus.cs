namespace NewCosmos.Models.Results;

public class SchemaStatus
{
    public bool IsInitialized { get; set; }
    public string RequiredVersion { get; set; } = "1.0";
    public string CurrentVersion { get; set; } = string.Empty;
    public string InitializedAt { get; set; } = string.Empty;
    public string InitializedBy { get; set; } = string.Empty;
    public List<string> MissingTables { get; set; } = new();
    public List<string> ExistingTables { get; set; } = new();
    public int TotalRequiredTables { get; set; }
    public int TotalExistingTables => ExistingTables.Count;
    public int TotalMissingTables => MissingTables.Count;

    public Dictionary<string, List<string>> MissingColumns { get; set; } = new();
    public int TotalMissingColumns => MissingColumns.Values.Sum(v => v.Count);
    public bool HasMissingColumns => TotalMissingColumns > 0;
    public bool HasMissingTables => TotalMissingTables > 0;
    public bool AllTablesExist => TotalMissingTables == 0 && TotalRequiredTables > 0;

    public string StatusText
    {
        get
        {
            if (AllTablesExist && !HasMissingColumns)
            {
                return IsInitialized
                    ? $"已初始化 (v{CurrentVersion})"
                    : "已就绪 (未标记初始化)";
            }

            if (HasMissingColumns)
                return $"结构异常 (缺少 {TotalMissingColumns} 个列)";

            if (HasMissingTables)
                return $"未就绪 (缺少 {TotalMissingTables} 个表)";

            if (TotalRequiredTables == 0)
                return "未初始化";

            return "未初始化";
        }
    }

    public string StatusSummary
    {
        get
        {
            var parts = new List<string>();
            parts.Add($"{TotalExistingTables}/{TotalRequiredTables} 个表");

            if (HasMissingTables)
                parts.Add($"{TotalMissingTables} 个缺失");

            if (HasMissingColumns)
                parts.Add($"{TotalMissingColumns} 个列缺失");

            return string.Join("，", parts);
        }
    }

    public string StatusIcon => StatusText switch
    {
        var t when t.StartsWith("已初始化") => "✓",
        var t when t.StartsWith("已就绪") => "○",
        var t when t.StartsWith("结构异常") => "✗",
        var t when t.StartsWith("未就绪") => "△",
        _ => "○"
    };

    public string StatusColor => StatusText switch
    {
        var t when t.StartsWith("已初始化") => "#28A745",
        var t when t.StartsWith("已就绪") => "#FFC107",
        var t when t.StartsWith("结构异常") => "#DC3545",
        var t when t.StartsWith("未就绪") => "#DC3545",
        _ => "#6C757D"
    };
}
