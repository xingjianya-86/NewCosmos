using System.Text.RegularExpressions;

namespace NewCosmos.Helpers;

/// <summary>
/// 村范围判定单点（死亡情况说明「严格按本村」口径的唯一维护点）。
///
/// 现状事实（2026-10）：
///   · nc_biz_applications 表单只写 community（文本村名）与 hukou_village_id，village_id 多为空；
///   · nc_biz_elderly_applications 用 family_village_id / hukou_village_id（数值ID）；
///   · nc_biz_temp_relief 只有 village 文本（公示表用）。
/// 故本村判定 = 数值ID 精确匹配 OR 文本村名归一化相等（后缀剥离，与 PublicityVillageResolver 同口径）。
/// 禁止各查询另写一套村过滤——一律通过本类的片段拼装。
/// </summary>
public static class VillageScopeResolver
{
    /// <summary>后缀表（长的在前，避免先剥「村」把「村委会」剥残）</summary>
    private static readonly string[] Suffixes =
    {
        "村民委员会", "居民委员会", "村公所", "居委会", "村委会", "委员会", "社区", "村"
    };

    /// <summary>剥离村/社区后缀，返回核心名（如「复兴村村民委员会」→「复兴」）</summary>
    public static string StripSuffix(string? name)
    {
        var value = name?.Trim();
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var changed = true;
        while (changed && value.Length > 0)
        {
            changed = false;
            foreach (var suffix in Suffixes)
            {
                if (value.Length > suffix.Length && value.EndsWith(suffix, StringComparison.Ordinal))
                {
                    value = value[..^suffix.Length].Trim();
                    changed = true;
                    break;
                }
            }
        }
        return value;
    }

    /// <summary>村名归一化（去后缀 + 空白规整），用于与数据库文本村字段比对</summary>
    public static string Normalize(string? name)
    {
        var stripped = StripSuffix(name);
        if (string.IsNullOrEmpty(stripped))
            return string.Empty;
        return Regex.Replace(stripped, @"\s+", "");
    }

    /// <summary>
    /// 文本村字段归一化比对 SQL 片段：把列值剥掉「村民委员会/村委会/社区…」后缀再与 $paramIndex 相等。
    /// 例：VillageScopeResolver.NameMatchSql("a.community", 2)
    /// → regexp_replace(COALESCE(a.community,''), '(村民委员会|...|村)+$', '') = $2
    /// </summary>
    public static string NameMatchSql(string column, int paramIndex)
    {
        var pattern = "(村民委员会|居民委员会|村公所|居委会|村委会|委员会|社区|村)+$";
        return $"regexp_replace(COALESCE({column}, ''), '{pattern}', '') = ${paramIndex}";
    }
}
