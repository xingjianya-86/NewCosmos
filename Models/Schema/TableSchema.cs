using YamlDotNet.Serialization;

namespace NewCosmos.Models.Schema;

/// <summary>
/// ?Schema 定义
/// </summary>
public class TableSchema
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "comment")]
    public string Comment { get; set; } = string.Empty;

    [YamlMember(Alias = "columns")]
    public List<ColumnSchema> Columns { get; set; } = new();

    [YamlMember(Alias = "indexes")]
    public List<IndexSchema> Indexes { get; set; } = new();

    [YamlMember(Alias = "constraints")]
    public List<ConstraintSchema> Constraints { get; set; } = new();

    /// <summary>
    /// 表级 foreignKeys: 段。加载时由 SchemaService 归一化进 Constraints（type=FOREIGN KEY）。
    /// </summary>
    [YamlMember(Alias = "foreignKeys")]
    public List<ForeignKeySchema> ForeignKeys { get; set; } = new();
}
