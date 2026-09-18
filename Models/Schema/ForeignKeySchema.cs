using YamlDotNet.Serialization;

namespace NewCosmos.Models.Schema;

/// <summary>
/// 表级 foreignKeys: 段的 Schema 定义。
/// 历史上 TableSchema 没有映射此段，配合 IgnoreUnmatchedProperties()，
/// 所有 YAML 中声明的外键都被静默丢弃——数据库里从未真正建立过外键约束。
/// 加载后由 SchemaService 归一化为 ConstraintSchema（type=FOREIGN KEY）走既有约束管线。
/// </summary>
public class ForeignKeySchema
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "columns")]
    public List<string> Columns { get; set; } = new();

    [YamlMember(Alias = "references")]
    public ForeignKeyTargetSchema References { get; set; } = new();

    [YamlMember(Alias = "onDelete")]
    public string OnDelete { get; set; } = string.Empty;
}

/// <summary>
/// foreignKeys.references 段（目标表 + 目标列，YAML 中列为复数形式）
/// </summary>
public class ForeignKeyTargetSchema
{
    [YamlMember(Alias = "table")]
    public string Table { get; set; } = string.Empty;

    [YamlMember(Alias = "columns")]
    public List<string> Columns { get; set; } = new();
}
