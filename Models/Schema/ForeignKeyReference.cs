using YamlDotNet.Serialization;

namespace NewCosmos.Models.Schema;

/// <summary>
/// 外键引用定义
/// </summary>
public class ForeignKeyReference
{
    [YamlMember(Alias = "table")]
    public string Table { get; set; } = string.Empty;

    [YamlMember(Alias = "column")]
    public string Column { get; set; } = string.Empty;

    [YamlMember(Alias = "onDelete")]
    public string OnDelete { get; set; } = string.Empty;
}
