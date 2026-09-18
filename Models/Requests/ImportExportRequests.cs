namespace NewCosmos.Models.Requests;

/// <summary>
/// 数据导入请求
/// </summary>
public class DataImportRequest
{
    public string FilePath { get; set; } = string.Empty;
    public string ImportType { get; set; } = string.Empty;
    public bool SkipFirstRow { get; set; } = true;
    public string Encoding { get; set; } = string.Empty;
    public string ImportedBy { get; set; } = string.Empty;
}

/// <summary>
/// 数据导出请求
/// </summary>
public class DataExportRequest
{
    public string ExportType { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string Filter { get; set; } = string.Empty;
    public string[] Columns { get; set; } = [];
    public string ExportedBy { get; set; } = string.Empty;
}

/// <summary>
/// 批量操作请求
/// </summary>
public class BatchOperationRequest
{
    public List<long> Ids { get; set; } = new();
    public string Operation { get; set; } = string.Empty;
    public string OperatedBy { get; set; } = string.Empty;
}

/// <summary>
/// 文件上传请求
/// </summary>
public class FileUploadRequest
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public string Category { get; set; } = string.Empty;
    public string UploadedBy { get; set; } = string.Empty;
}

/// <summary>
/// 报表生成请求
/// </summary>
public class ReportGenerateRequest
{
    public string ReportType { get; set; } = string.Empty;
    public int Year { get; set; }
    public int Month { get; set; }
    public string Town { get; set; } = string.Empty;
    public string Village { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string GeneratedBy { get; set; } = string.Empty;
}

/// <summary>
/// 备份请求
/// </summary>
public class BackupRequest
{
    public string BackupPath { get; set; } = string.Empty;
    public bool IncludeFiles { get; set; }
    public bool IncludeDatabase { get; set; } = true;
    public string Description { get; set; } = string.Empty;
    public string CreatedBy { get; set; } = string.Empty;
}

/// <summary>
/// 恢复请求
/// </summary>
public class RestoreRequest
{
    public string BackupFilePath { get; set; } = string.Empty;
    public bool RestoreFiles { get; set; }
    public bool RestoreDatabase { get; set; } = true;
    public string RestoredBy { get; set; } = string.Empty;
}

/// <summary>
/// 系统配置更新请求
/// </summary>
public class SystemConfigUpdateRequest
{
    public string ConfigKey { get; set; } = string.Empty;
    public string ConfigValue { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string UpdatedBy { get; set; } = string.Empty;
}