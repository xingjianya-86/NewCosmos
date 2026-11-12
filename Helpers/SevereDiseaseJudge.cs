using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using NewCosmos.Models.Results;
#if ANDROID
using Microsoft.Maui.Storage;
#endif

namespace NewCosmos.Helpers;

/// <summary>
/// 重病判定助手——客户端确定性层，与服务端 deploy\ocr\ocr_api.py 的确定性层同规则
/// （规则实现以服务端为权威，本类为随包副本，改规则须两侧同步并重跑服务端冒烟）。
/// 1) 懒加载 Resources/Icd10/severe_disease_catalog.json（与服务端清单字节一致，见清单 notes 同步约定）；
/// 2) TryJudgeDeterministic：精确 → 单向包含（输入⊃清单项，禁止反向）→ 编码段，未命中返回 false；
/// 3) NormalizeServerJudgment：服务端响应二次校验（matched_item 必须∈清单，否则置空）；
/// 4) PolicyVersion = 文件 sha256 前16位（与服务端同算法）；BuildCacheKey 规范化缓存键。
///
/// 反向包含禁止的依据（业务裁定 2026-11-11）：脑梗塞无"急性"不算（有一年恢复期）、
/// 糖尿病未分型不算——反向包含会让这类短输入误命中长清单项（"脑梗塞"⊂"急性脑梗塞"）。
/// 文件缺失/解析失败时保持空清单：确定性判定一律 miss（降级走服务端或人工，不影响表单）。
/// </summary>
public sealed class SevereDiseaseJudge
{
    private static readonly Regex CodeRegex = new(@"^([A-Z])(\d{1,3})", RegexOptions.Compiled);
    private static readonly object SyncRoot = new();
    private static SevereDiseaseJudge? _default;

    private readonly List<string> _items;
    private readonly HashSet<string> _itemSet;
    private readonly Dictionary<string, string> _aliases;
    private readonly List<CodeRangeDto> _ranges;

    private SevereDiseaseJudge(CatalogDto dto, byte[] raw)
    {
        _items = dto.Items.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        _itemSet = new HashSet<string>(_items, StringComparer.Ordinal);
        _aliases = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in dto.Aliases)
        {
            if (!string.IsNullOrWhiteSpace(k) && !string.IsNullOrWhiteSpace(v))
                _aliases[k] = v;
        }
        _ranges = dto.CodeRanges.Where(r => !string.IsNullOrWhiteSpace(r.Letter)).ToList();
        PolicyVersion = raw.Length > 0
            ? Convert.ToHexString(SHA256.HashData(raw))[..16].ToLowerInvariant()
            : string.Empty;
    }

    /// <summary>随包清单实例（懒加载；缺失/坏档降级为空清单，不抛异常）</summary>
    public static SevereDiseaseJudge Default
    {
        get
        {
            if (_default == null)
            {
                lock (SyncRoot)
                {
                    _default ??= LoadDefault();
                }
            }
            return _default;
        }
    }

    /// <summary>清单文件 sha256 前16位；空清单=空串（与服务端 policy_version 对齐口径）</summary>
    public string PolicyVersion { get; }

    /// <summary>清单条目（供测试与诊断）</summary>
    public IReadOnlyList<string> Items => _items;

    public bool HasCatalog => _items.Count > 0;

    /// <summary>从 JSON 文本构造（单测注入用；生产走 Default）</summary>
    public static SevereDiseaseJudge FromJson(string json)
        => Parse(json ?? string.Empty, Encoding.UTF8.GetBytes(json ?? string.Empty));

    private static SevereDiseaseJudge LoadDefault()
    {
        try
        {
            byte[]? raw = null;
            var jsonPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Icd10", "severe_disease_catalog.json");
            if (File.Exists(jsonPath))
                raw = File.ReadAllBytes(jsonPath);

#if ANDROID
            // 安卓端 None+CopyToOutputDirectory 不进 APK assets（同 Icd10Catalog/icd10_common.json 处理）
            raw ??= ReadAndroidPackageAsset("icd10/severe_disease_catalog.json");
#endif
            if (raw == null || raw.Length == 0)
                return new SevereDiseaseJudge(new CatalogDto(), Array.Empty<byte>());
            return Parse(Encoding.UTF8.GetString(raw), raw);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载重病清单失败: {ex.Message}");
            return new SevereDiseaseJudge(new CatalogDto(), Array.Empty<byte>());
        }
    }

    private static SevereDiseaseJudge Parse(string json, byte[] raw)
    {
        try
        {
            var dto = JsonSerializer.Deserialize<CatalogDto>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            return new SevereDiseaseJudge(dto ?? new CatalogDto(), raw);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"解析重病清单失败: {ex.Message}");
            return new SevereDiseaseJudge(new CatalogDto(), Array.Empty<byte>());
        }
    }

#if ANDROID
    private static byte[]? ReadAndroidPackageAsset(string assetPath)
    {
        try
        {
            using var stream = FileSystem.OpenAppPackageFileAsync(assetPath).GetAwaiter().GetResult();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"读取安卓重病清单资产失败: {assetPath} {ex.Message}");
            return null;
        }
    }
#endif

    /// <summary>
    /// 确定性判定：① 精确（清单项/别名）② 单向包含（输入⊃清单项/别名，最长优先，禁止反向）
    /// ③ 编码段。命中返回 true（verdict=severe, source=deterministic）；未命中返回 false，交服务端 LLM。
    /// </summary>
    public bool TryJudgeDeterministic(string? name, string? code, out SevereDiseaseJudgment judgment)
    {
        judgment = null!;
        var norm = NormalizeName(name);
        if (norm.Length == 0 || _items.Count == 0) return false;

        // ① 精确
        foreach (var item in _items)
        {
            if (norm == NormalizeName(item))
            {
                judgment = Hit(item, $"清单精确命中：{item}");
                return true;
            }
        }
        foreach (var (alias, canonical) in _aliases)
        {
            if (norm == NormalizeName(alias))
            {
                judgment = Hit(canonical, $"清单别名命中：{alias}→{canonical}");
                return true;
            }
        }

        // ② 单向包含（最长优先；只查"输入⊃清单项"，不查反向——业务裁定见类注释）
        var bestText = string.Empty;
        var bestItem = string.Empty;
        var bestAlias = string.Empty;
        foreach (var item in _items)
        {
            var ni = NormalizeName(item);
            if (ni.Length >= 2 && norm.Contains(ni, StringComparison.Ordinal) && ni.Length > bestText.Length)
            {
                bestText = ni;
                bestItem = item;
                bestAlias = string.Empty;
            }
        }
        foreach (var (alias, canonical) in _aliases)
        {
            var na = NormalizeName(alias);
            if (na.Length >= 2 && norm.Contains(na, StringComparison.Ordinal) && na.Length > bestText.Length)
            {
                bestText = na;
                bestItem = canonical;
                bestAlias = alias;
            }
        }
        if (bestItem.Length > 0)
        {
            judgment = Hit(bestItem, bestAlias.Length > 0
                ? $"清单别名包含命中：{bestAlias}→{bestItem}"
                : $"清单包含命中：{bestItem}");
            return true;
        }

        // ③ 编码段
        var codeUpper = (code ?? string.Empty).Trim().ToUpperInvariant();
        var m = CodeRegex.Match(codeUpper);
        if (m.Success && int.TryParse(m.Groups[2].Value, out var num))
        {
            foreach (var r in _ranges)
            {
                if (r.Letter == m.Groups[1].Value && num >= r.Min && num <= r.Max)
                {
                    judgment = Hit(r.Item, $"编码 {codeUpper} 命中 {r.Item}");
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// 服务端响应二次校验：verdict 白名单、matched_item∈清单（别名归一、胡扯项置空）、
    /// confidence 截断 [0,1]、reason 截断。双侧防线，客户端不信任裸响应。
    /// </summary>
    public SevereDiseaseJudgment NormalizeServerJudgment(
        string? verdict, string? matchedItem, double confidence, string? reason,
        string? source, string? policyVersion, string? model = null)
    {
        var v = (verdict ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            SevereDiseaseJudgment.VerdictSevere => SevereDiseaseJudgment.VerdictSevere,
            SevereDiseaseJudgment.VerdictNotSevere => SevereDiseaseJudgment.VerdictNotSevere,
            _ => SevereDiseaseJudgment.VerdictUnknown,
        };
        var item = ResolveMatchedItem(matchedItem);
        var src = source == SevereDiseaseJudgment.SourceDeterministic
            ? SevereDiseaseJudgment.SourceDeterministic
            : SevereDiseaseJudgment.SourceLlm;
        return new SevereDiseaseJudgment
        {
            Verdict = v,
            MatchedItem = item,
            Confidence = Math.Clamp(confidence, 0, 1),
            Reason = string.IsNullOrWhiteSpace(reason) ? "待人工判定" : reason.Trim()[..Math.Min(reason.Trim().Length, 300)],
            Source = src,
            PolicyVersion = policyVersion ?? string.Empty,
            Model = model ?? string.Empty,
        };
    }

    /// <summary>matched_item 归一：∈清单原样、别名转规范项、其余（胡扯项）置空</summary>
    public string ResolveMatchedItem(string? raw)
    {
        var t = (raw ?? string.Empty).Trim();
        if (t.Length == 0) return string.Empty;
        if (_itemSet.Contains(t)) return t;
        if (_aliases.TryGetValue(t, out var canonical)) return canonical;
        return string.Empty;
    }

    /// <summary>缓存键：名称规范化（去空白/全角转半角/小写）+ 编码大写，等价输入同键</summary>
    public static string BuildCacheKey(string? name, string? code)
        => $"{NormalizeName(name)}|{(code ?? string.Empty).Trim().ToUpperInvariant()}";

    /// <summary>名称规范化（与服务端 _norm_name 同口径：去全部空白 → 全角转半角 → 小写）</summary>
    public static string NormalizeName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsWhiteSpace(ch)) continue;
            sb.Append(ch switch
            {
                '（' => '(',
                '）' => ')',
                '：' => ':',
                '，' => ',',
                '－' => '-',
                _ => ch,
            });
        }
        return sb.ToString().ToLowerInvariant();
    }

    private SevereDiseaseJudgment Hit(string item, string reason) => new()
    {
        Verdict = SevereDiseaseJudgment.VerdictSevere,
        MatchedItem = item,
        Confidence = 1.0,
        Reason = reason,
        Source = SevereDiseaseJudgment.SourceDeterministic,
        PolicyVersion = PolicyVersion,
    };

    private sealed class CatalogDto
    {
        public List<string> Items { get; set; } = new();
        public Dictionary<string, string> Aliases { get; set; } = new(StringComparer.Ordinal);
        [JsonPropertyName("code_ranges")]
        public List<CodeRangeDto> CodeRanges { get; set; } = new();
    }

    private sealed class CodeRangeDto
    {
        public string Letter { get; set; } = string.Empty;
        public int Min { get; set; }
        public int Max { get; set; }
        public string Item { get; set; } = string.Empty;
    }
}
