using NewCosmos.Models.Results;

namespace NewCosmos.Services.Import;

public interface ICombinedImportService
{
    string ImportTypeName { get; }
    string FamilyFilePattern { get; }
    string PersonFilePattern { get; }

    Task<CombinedPreviewResult> PreviewCombinedAsync(
        string familyFilePath,
        string personFilePath,
        int previewRows = 10,
        CancellationToken ct = default);

    Task<CombinedImportResult> ImportCombinedAsync(
        string familyFilePath,
        string personFilePath,
        bool clearBeforeImport,
        IProgress<string>? progress = null,
        CancellationToken ct = default);

    Task<Result> ClearTablesAsync(CancellationToken ct = default);
}

public class CombinedPreviewResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string FamilyFilePath { get; set; } = string.Empty;
    public string PersonFilePath { get; set; } = string.Empty;
    public int FamilyTotalRows { get; set; }
    public int PersonTotalRows { get; set; }
    public List<ColumnMappingInfo> FamilyColumnMappings { get; set; } = new();
    public List<ColumnMappingInfo> PersonColumnMappings { get; set; } = new();
    public List<Dictionary<string, object?>> FamilyPreviewRows { get; set; } = new();
    public List<Dictionary<string, object?>> PersonPreviewRows { get; set; } = new();
}

public class CombinedImportResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public int FamilyImportedCount { get; set; }
    public int PersonImportedCount { get; set; }
    public int LinkedCount { get; set; }
    public int UnlinkedCount { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<UnlinkedPersonInfo> UnlinkedPersons { get; set; } = new();
    public TimeSpan Duration { get; set; }
}

public class UnlinkedPersonInfo
{
    public string Name { get; set; } = string.Empty;
    public string IdCard { get; set; } = string.Empty;
    public string HeadIdCard { get; set; } = string.Empty;
    public int RowNumber { get; set; }
}
