using System.Reflection;
using System.Text.RegularExpressions;
using NewCosmos.Constants;
using NewCosmos.Services.Templates;

namespace NewCosmos.Helpers;

/// <summary>
/// 模板 config_json 字段键体检（批6 长效机制，2026-10-16）。
/// 挂在上传/保存入口：校验 fields[] 的 fieldKey ∈ FieldKeys 白名单、placeholder 为单个 {内容}、
/// 同模板占位符不重复；命中只警告不阻断（留人工裁量，避免误伤合规模板）。
/// 防的是「config 有键、代码无供给」的占位符残留/空值事故（先例：2026-10-15 {经办人}）与
/// 「同占位符多键后写覆盖」的值串事故；离线侧另有 Scripts\check_template_fields.ps1 发版体检兜底。
/// </summary>
public static class TemplateFieldKeyValidator
{
    private static readonly Regex PlaceholderPattern = new(@"^\{[^{}]+\}$", RegexOptions.Compiled);

    // 白名单 = FieldKeys.*.cs（partial）全部 public const string 值，反射一次缓存。
    // typeof(FieldKeys) 是编译期常量，linker 对 typeof(T).GetFields(...) 模式静态保留字段（Android AOT/trim 安全）。
    private static readonly Lazy<HashSet<string>> Whitelist = new(() =>
        typeof(FieldKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(f => f.FieldType == typeof(string))
            .Select(f => f.GetValue(null) as string)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal));

    /// <summary>校验 config JSON，返回问题描述列表（空 = 通过）。config 为 null/空白 → 空列表。</summary>
    public static IReadOnlyList<string> Validate(string? configJson)
    {
        if (string.IsNullOrWhiteSpace(configJson)) return Array.Empty<string>();

        TemplateConfig? config;
        try
        {
            config = TemplateConfig.FromJson(configJson);
        }
        catch (Exception ex)
        {
            return new[] { $"config 解析失败：{ex.Message}" };
        }

        var fields = config?.Fields;
        if (fields == null || fields.Count == 0) return Array.Empty<string>();

        var issues = new List<string>();
        var seenPlaceholders = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < fields.Count; i++)
        {
            var f = fields[i];
            var key = f?.FieldKey;
            if (string.IsNullOrEmpty(key))
            {
                issues.Add($"第 {i + 1} 行：fieldKey 为空");
                continue;
            }

            if (!Whitelist.Value.Contains(key))
            {
                issues.Add($"第 {i + 1} 行：fieldKey『{key}』不在 FieldKeys 白名单——新键须先在 Constants\\FieldKeys.*.cs " +
                           "声明（值=键名、禁中文），并在供给该模板的 Builder 中双写供给");
            }

            var ph = f?.Placeholder;
            if (!string.IsNullOrEmpty(ph))
            {
                if (!PlaceholderPattern.IsMatch(ph))
                {
                    issues.Add($"第 {i + 1} 行：placeholder『{ph}』非法——须为单个 {{内容}}（禁止空值、复合多占位符、嵌套花括号）");
                }
                if (!seenPlaceholders.Add(ph))
                {
                    issues.Add($"第 {i + 1} 行：placeholder『{ph}』与前文重复——同占位符多键会后写覆盖（MultiSource，值会串）");
                }
            }
        }

        return issues;
    }
}
