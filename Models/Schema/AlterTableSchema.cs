using YamlDotNet.Serialization;

namespace NewCosmos.Models.Schema;

/// <summary>
/// ALTER TABLE Schema 定义（用于扩展字段）
/// </summary>
public class AlterTableSchema
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "comment")]
    public string Comment { get; set; } = string.Empty;

    [YamlMember(Alias = "addColumns")]
    public List<ColumnSchema> AddColumns { get; set; } = new();
}
