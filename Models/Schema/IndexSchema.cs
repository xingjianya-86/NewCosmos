using YamlDotNet.Serialization;

namespace NewCosmos.Models.Schema;

/// <summary>
/// 索引 Schema 定义
/// </summary>
public class IndexSchema
{
    [YamlMember(Alias = "name")]
    public string Name { get; set; } = string.Empty;

    [YamlMember(Alias = "columns")]
    public List<string> Columns { get; set; } = new();

    [YamlMember(Alias = "unique")]
    public bool Unique { get; set; }

    /// <summary>
    /// 部分索引 WHERE 谓词（如 "deleted_at IS NULL"）。
    /// 用于"软删除不占用唯一性"等场景——PostgreSQL 唯一约束不支持 WHERE，
    /// 必须用部分唯一索引（CREATE UNIQUE INDEX ... WHERE ...）。
    /// </summary>
    [YamlMember(Alias = "where")]
    public string Where { get; set; } = string.Empty;
}
