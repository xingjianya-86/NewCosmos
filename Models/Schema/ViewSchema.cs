using YamlDotNet.Serialization;

namespace NewCosmos.Models.Schema;

/// <summary>
/// 视图 Schema 定义
/// </summary>
public class ViewSchema
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "definition")]
    public string Definition { get; set; } = string.Empty;

    [YamlMember(Alias = "comment")]
    public string Comment { get; set; } = string.Empty;
}
