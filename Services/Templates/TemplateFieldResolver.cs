namespace NewCosmos.Services.Templates;

/// <summary>
/// 模板字段投影层（方案C）：
/// 在渲染入口 MapFieldsToPlaceholders 内部按模板 config 的 indexShifts 规则执行投影，
/// 将索引字段槽位（如 FAMILY_MEMBER_NAME_1..N）移除前 Skip 个（通常是户主），
/// 其余槽位前移，使同一份共享数据字典对不同模板呈现独立视图。
/// 规则声明于各模板 config_json 的 indexShifts 数组，代码不硬编码任何模板特例。
/// </summary>
public static class TemplateFieldResolver
{
    /// <summary>
    /// 按规则投影字段字典；无规则或空字典时原样返回。
    /// 返回新字典，不修改入参（避免污染共享 _fieldData）。
    /// </summary>
    public static Dictionary<string, string> Project(
        Dictionary<string, string>? fieldValues,
        IReadOnlyList<IndexShiftRule>? rules)
    {
        if (fieldValues == null || fieldValues.Count == 0 || rules == null || rules.Count == 0)
            return fieldValues ?? new Dictionary<string, string>(StringComparer.Ordinal);

        var result = new Dictionary<string, string>(fieldValues, StringComparer.Ordinal);

        foreach (var rule in rules)
        {
            if (string.IsNullOrWhiteSpace(rule.BaseKey) || rule.Skip < 1)
                continue;

            var prefix = rule.BaseKey + "_";

            // 确定该 base 的最大索引
            var maxIndex = 0;
            foreach (var key in result.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal)
                    && key.Length > prefix.Length
                    && int.TryParse(key.AsSpan(prefix.Length), out var idx)
                    && idx > maxIndex)
                {
                    maxIndex = idx;
                }
            }

            if (maxIndex > rule.Skip)
            {
                // 从低到高搬移，避免源槽位被目标覆盖
                for (var i = rule.Skip + 1; i <= maxIndex; i++)
                {
                    var srcKey = prefix + i;
                    if (result.TryGetValue(srcKey, out var value))
                    {
                        result[prefix + (i - rule.Skip)] = value;
                        result.Remove(srcKey);
                    }
                }
            }
            else
            {
                // 无成员数据可搬移时才移除被跳过的槽位（户主），
                // 有数据时 _1 已是搬移后的首个成员，绝不能删（否则首个成员丢失）
                result.Remove(prefix + rule.Skip);
            }
        }

        return result;
    }
}