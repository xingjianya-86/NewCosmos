namespace NewCosmos.Models.Entities;

public class Template
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FileType { get; set; } = string.Empty;
    public byte[] FileData { get; set; } = [];
    public string[] Categories { get; set; } = [];
    /// <summary>文档类型（方案B精准定位维度；词表见 Constants/TemplateDocTypes.cs）</summary>
    public string DocType { get; set; } = string.Empty;
    public string ConfigJson { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; }
}
