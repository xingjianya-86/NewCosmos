using YamlDotNet.Serialization;

namespace NewCosmos.Models.Schema;

/// <summary>
/// 序列 Schema 定义
/// </summary>
public class SequenceSchema
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "start")]
    public long Start { get; set; } = 1;

    [YamlMember(Alias = "increment")]
    public int Increment { get; set; } = 1;

    [YamlMember(Alias = "minvalue")]
    public long MinValue { get; set; }

    [YamlMember(Alias = "maxvalue")]
    public long MaxValue { get; set; }

    [YamlMember(Alias = "cycle")]
    public bool Cycle { get; set; }

    [YamlMember(Alias = "comment")]
    public string Comment { get; set; } = string.Empty;
}
