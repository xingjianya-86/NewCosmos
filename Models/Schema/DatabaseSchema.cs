using YamlDotNet.Serialization;

namespace NewCosmos.Models.Schema;

/// <summary>
/// 数据库 Schema 定义
/// </summary>
public class DatabaseSchema
{
    [YamlMember(Alias = "database")]
    public string Database { get; set; } = string.Empty;

    [YamlMember(Alias = "version")]
    public string Version { get; set; } = "1.0";

    [YamlMember(Alias = "description")]
    public string Description { get; set; } = string.Empty;

    [YamlMember(Alias = "tables")]
    public List<TableSchema> Tables { get; set; } = new();

    [YamlMember(Alias = "sequences")]
    public List<SequenceSchema> Sequences { get; set; } = new();

    [YamlMember(Alias = "views")]
    public List<ViewSchema> Views { get; set; } = new();

    [YamlMember(Alias = "alterTables")]
    public List<AlterTableSchema> AlterTables { get; set; } = new();
}
