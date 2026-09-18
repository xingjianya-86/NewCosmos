namespace NewCosmos.Models.Results;

/// <summary>
/// 模板信息结果
/// </summary>
public record TemplateInfoResult
{
    public required string TemplateId { get; init; }
    public required string TemplateName { get; init; }
    public required string TemplateType { get; init; }
    public required string FileName { get; init; }
    public long FileSize { get; init; }
    public int Version { get; init; }
    public bool IsActive { get; init; }
    public string Description { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// 模板版本信息
/// </summary>
public record TemplateVersionResult
{
    public int Version { get; init; }
    public long FileSize { get; init; }
    public string ChangeLog { get; init; } = string.Empty;
    public string CreatedBy { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}

/// <summary>
/// 文档生成结果
/// </summary>
public record DocumentResult
{
    public required bool Success { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public string FilePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public required string TemplateId { get; init; }
}

/// <summary>
/// 打印结果
/// </summary>
public record PrintResult
{
    public required bool Success { get; init; }
    public string ErrorMessage { get; init; } = string.Empty;
    public long PrintRecordId { get; init; }
    public string PdfPath { get; init; } = string.Empty;
    public int RetryCount { get; init; }
}
