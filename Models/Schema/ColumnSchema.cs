using YamlDotNet.Serialization;

namespace NewCosmos.Models.Schema;

/// <summary>
/// ?Schema 定义
/// </summary>
public class ColumnSchema
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "type")]
    public string Type { get; set; } = string.Empty;

    [YamlMember(Alias = "nullable")]
    public bool Nullable { get; set; } = true;

    [YamlMember(Alias = "primaryKey")]
    public bool PrimaryKey { get; set; }

    [YamlMember(Alias = "unique")]
    public bool Unique { get; set; }

    [YamlMember(Alias = "defaultValue")]
    public string Default { get; set; } = string.Empty;

    [YamlMember(Alias = "comment")]
    public string Comment { get; set; } = string.Empty;

    [YamlMember(Alias = "references")]
    public ForeignKeyReference References { get; set; } = new();

    [YamlMember(Alias = "check")]
    public string Check { get; set; } = string.Empty;
}
