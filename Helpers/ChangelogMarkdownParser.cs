using NewCosmos.Models.Results;

namespace NewCosmos.Helpers;

/// <summary>
/// CHANGELOG.md 解析器（2026-11-15「更新日志随包内置」）：把升级日志唯一素材来源
/// 解析成 <see cref="UpdateHistoryItem"/> 列表（Source=bundled），供本地安装包离线展示全量更新日志。
/// 规则与发版脚本 <c>publish_all.ps1 Get-ChangelogSection</c> 同口径：
/// `## [版本号] - 日期`（或 `## [未发布]`）分节，节内保留 `### 新增/变更/修复` 分组与 `- ` 条目原文；
/// 头部说明、分节线 `---` 忽略。解析尽力而为——格式异常的行跳过不抛
/// （内置日志只作展示，不参与安装决策）。
/// </summary>
public static class ChangelogMarkdownParser
{
    public static IReadOnlyList<UpdateHistoryItem> Parse(string? markdown)
    {
        var items = new List<UpdateHistoryItem>();
        if (string.IsNullOrWhiteSpace(markdown)) return items;

        string? version = null;
        var date = DateTime.MinValue;
        var notes = new List<string>();

        void FlushSection()
        {
            if (version == null) return;
            items.Add(new UpdateHistoryItem(
                version,
                date,
                string.Join("\n", notes).Trim(),
                UpdateHistoryItem.SourceBundled));
            version = null;
            date = DateTime.MinValue;
            notes.Clear();
        }

        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var trimmed = line.Trim();

            if (trimmed.StartsWith("## ", StringComparison.Ordinal))
            {
                FlushSection();
                var (v, d) = ParseHeading(trimmed);
                version = v;      // null = 非 [版本] 形态标题：本节内容全部忽略
                date = d;
                continue;
            }

            if (version == null) continue;                       // 头部说明（# 标题 / 引用行）忽略
            if (trimmed.StartsWith("---", StringComparison.Ordinal)) continue;   // 分节线
            if (trimmed.Length == 0)
            {
                // 组间空行：仅保留一个作为分组分隔，连续空行折叠
                if (notes.Count > 0 && notes[^1].Length > 0) notes.Add(string.Empty);
                continue;
            }

            if (trimmed.StartsWith("### ", StringComparison.Ordinal) || trimmed.StartsWith("- ", StringComparison.Ordinal))
                notes.Add(trimmed);                               // 分组标题 / 条目：保留原文
            // 其它行（缩进续行等）忽略，保证 notes 恒为「分组 + 条目」结构
        }

        FlushSection();
        return items;
    }

    /// <summary>
    /// 解析节标题：<c>## [1.2.20261109] - 2026-11-09</c> / <c>## [未发布]</c>。
    /// 返回 (版本, 日期)；日期缺失为 <see cref="DateTime.MinValue"/>（界面显示空时间）。
    /// 非 [ ] 形态的 ## 标题返回 null 版本（不当事）。
    /// </summary>
    private static (string? Version, DateTime Date) ParseHeading(string heading)
    {
        var inner = heading[2..].Trim();
        if (!inner.StartsWith('[')) return (null, DateTime.MinValue);

        var close = inner.IndexOf(']');
        if (close <= 1) return (null, DateTime.MinValue);

        var version = inner[1..close].Trim();
        if (version.Length == 0) return (null, DateTime.MinValue);

        var rest = inner[(close + 1)..].Trim();
        var date = DateTime.MinValue;
        if (rest.StartsWith('-') && DateTime.TryParse(rest[1..].Trim(), out var parsed))
            date = parsed;

        return (version, date);
    }
}
