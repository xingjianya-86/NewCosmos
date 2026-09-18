using YamlDotNet.Serialization;

namespace NewCosmos.Models.Schema;

/// <summary>
/// 约束 Schema 定义
/// </summary>
public class ConstraintSchema
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "type")]
    public string Type { get; set; } = string.Empty;

    [YamlMember(Alias = "columns")]
    public List<string> Columns { get; set; } = new();

    [YamlMember(Alias = "expression")]
    public string Expression { get; set; } = string.Empty;

    [YamlMember(Alias = "references")]
    public ForeignKeyReference References { get; set; } = new();
}
