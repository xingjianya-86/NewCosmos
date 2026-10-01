using System.Text.RegularExpressions;
using NewCosmos.Models.Results;

namespace NewCosmos.Helpers;

/// <summary>
/// 身份证 OCR 文本行解析：从原始识别行中抽取「姓名 + 身份证号」。
/// 纯托管实现，跨平台可复用；身份证号优先取校验码通过者。
/// </summary>
public static partial class IdCardOcrParser
{
    /// <summary>身份证正面已知标签词，避免被误当作姓名</summary>
    private static readonly HashSet<string> Labels = new(StringComparer.Ordinal)
    {
        "姓名", "性别", "民族", "出生", "住址", "公民身份号码", "身份证号码", "签发机关", "有效期限", "中华人民共和国", "居民身份证"
    };

    /// <summary>
    /// 解析 OCR 原始文本行。
    /// </summary>
    /// <param name="rawLines">按行自上而下的识别结果</param>
    public static IdCardInfo Parse(IReadOnlyList<string> rawLines)
    {
        var lines = rawLines
            .Select(l => (Raw: (l ?? string.Empty).Trim(), Norm: Normalize(l)))
            .Where(x => x.Norm.Length > 0)
            .ToList();

        var idCard = ExtractIdCard(lines.Select(x => x.Norm));
        var name = ExtractName(lines.Select(x => x.Norm));

        return new IdCardInfo
        {
            Name = name,
            IdCard = idCard,
            IdCardChecksumValid = idCard.Length > 0 && IdCardValidator.IsValid(idCard),
            RawLines = rawLines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList()
        };
    }

    /// <summary>抽取身份证号：优先校验码通过的 18 位号码，其次首个 18 位形态号码</summary>
    private static string ExtractIdCard(IEnumerable<string> lines)
    {
        string? firstCandidate = null;
        foreach (var line in lines)
        {
            foreach (Match m in IdCardRegex().Matches(line))
            {
                var value = m.Value.ToUpperInvariant();
                if (IdCardValidator.IsValid(value))
                    return value;
                firstCandidate ??= value;
            }
        }
        return firstCandidate ?? string.Empty;
    }

    /// <summary>抽取姓名：优先「姓名」标签行剩余部分，其次首个 2-4 位纯中文且非标签词</summary>
    private static string ExtractName(IEnumerable<string> lines)
    {
        string? fallback = null;
        foreach (var line in lines)
        {
            if (line.StartsWith("姓名", StringComparison.Ordinal))
            {
                var rest = CjkOnly(line[2..]);
                if (rest.Length is >= 2 and <= 4)
                    return rest;
            }

            if (fallback is null && line.Length is >= 2 and <= 4 && IsAllCjk(line) && !Labels.Contains(line))
                fallback = line;
        }
        return fallback ?? string.Empty;
    }

    /// <summary>去除所有空白，便于标签/号码匹配</summary>
    private static string Normalize(string? text)
        => string.IsNullOrEmpty(text) ? string.Empty : WhitespaceRegex().Replace(text!, string.Empty);

    /// <summary>仅保留中文汉字</summary>
    private static string CjkOnly(string text)
        => NonCjkRegex().Replace(text, string.Empty);

    private static bool IsAllCjk(string text)
    {
        foreach (var ch in text)
        {
            if (ch is < '\u4e00' or > '\u9fa5')
                return false;
        }
        return text.Length > 0;
    }

    /// <summary>18 位身份证号形态（末位可为 X）</summary>
    [GeneratedRegex(@"[1-9]\d{16}[\dXx]")]
    private static partial Regex IdCardRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[^\u4e00-\u9fa5]")]
    private static partial Regex NonCjkRegex();
}
