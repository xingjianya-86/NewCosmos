namespace NewCosmos.Models.Entities;

/// <summary>
/// 档案归档主表实体（纯 POCO，对对应 nc_biz_archives 表）
/// </summary>
public class Archive
{
    public long Id { get; set; }
    public long ApplicationId { get; set; }
    public string ArchiveNo { get; set; } = string.Empty;
    public string ArchiveType { get; set; } = string.Empty;
    public string ClassificationResult { get; set; } = string.Empty;
    public DateTime ArchivedAt { get; set; } = DateTime.UtcNow;
    public string ArchivedBy { get; set; } = string.Empty;
    public string OutputFiles { get; set; } = string.Empty;
    public long? RelatedArchiveId { get; set; }
    public string RelationType { get; set; } = string.Empty;
    public string RelationDescription { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime DeletedAt { get; set; }
}

/// <summary>
/// 归档文件实体（纯 POCO，对对应 nc_biz_archive_files 表）
/// </summary>
public class ArchiveFile
{
    public long Id { get; set; }
    public long ArchiveId { get; set; }
    public string TemplateId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string OutputPath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
