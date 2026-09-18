namespace NewCosmos.Models.Results;

public class ImportResult
{
    public bool Success { get; set; }
    public int ImportedCount { get; set; }
    public int FamilyImportedCount { get; set; }
    public int PersonImportedCount { get; set; }
    public int ErrorCount { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public TimeSpan Duration { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string ImportType { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;

    public static ImportResult Succeeded(int count) => new() { Success = true, ImportedCount = count, Message = $"导入完成，成功{count}条" };
    public static ImportResult Failed(params string[] errors) => new() { Success = false, Errors = errors.ToList(), ErrorCount = errors.Length, Message = $"导入失败：{string.Join("; ", errors)}" };
}

public class ImportPreviewResult
{
    public bool Success { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public int TotalRows { get; set; }
    public List<ColumnMappingInfo> ColumnMappings { get; set; } = new();
    public List<Dictionary<string, object?>> PreviewRows { get; set; } = new();
}

public class ColumnMappingInfo
{
    public string SourceColumn { get; set; } = string.Empty;
    public string TargetField { get; set; } = string.Empty;
    public bool IsMapped { get; set; }
}

public class LinkResult
{
    public bool Success { get; set; }
    public int LinkedCount { get; set; }
    public int UnlinkedCount { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
}

public class ImportError
{
    public int RowNumber { get; set; }
    public string Column { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string RawValue { get; set; } = string.Empty;
}