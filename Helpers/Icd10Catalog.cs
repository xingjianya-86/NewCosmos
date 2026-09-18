using System.Text.Json;
using System.Text.RegularExpressions;

namespace NewCosmos.Helpers;

/// <summary>
/// ICD-10 疾病目录共享助手：
/// 1) CodeToNameMap 懒加载 Resources/Icd10/icd10_common.json（编码→名称，随程序集分发），
///    供临时救助与低收入申请表单共用，避免字典重复加载；
/// 2) TryGetName 按编码精确匹配查名称；
/// 3) MapCategory 按 ICD-10 章节把编码归类到低收入表单"一级疾病"分类项
///    （分类名与 nc_dict_items.DiseaseCategories 字典保持一致；归不进的落"其他"）。
/// 文件缺失/解析失败时保持空字典，不影响手动录入。
/// </summary>
public static class Icd10Catalog
{
    private static IReadOnlyDictionary<string, string>? _codeToNameMap;
    private static readonly object _loadLock = new();

    /// <summary>编码 → 疾病名称 内置字典</summary>
    public static IReadOnlyDictionary<string, string> CodeToNameMap
    {
        get
        {
            if (_codeToNameMap == null)
            {
                lock (_loadLock)
                {
                    _codeToNameMap ??= Load();
                }
            }
            return _codeToNameMap;
        }
    }

    private static IReadOnlyDictionary<string, string> Load()
    {
        try
        {
            var jsonPath = Path.Combine(AppContext.BaseDirectory, "Resources", "Icd10", "icd10_common.json");
            if (!File.Exists(jsonPath))
                return new Dictionary<string, string>();

            var json = File.ReadAllText(jsonPath);
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json,
                new JsonSerializerOptions { ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return map != null && map.Count > 0 ? map : new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载 ICD-10 字典失败: {ex.Message}");
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// 规范化编码（去空格、转大写）；命中内置字典返回疾病名称，未命中返回 false。
    /// </summary>
    public static bool TryGetName(string? code, out string name)
    {
        name = string.Empty;
        if (string.IsNullOrWhiteSpace(code)) return false;
        var key = code.Trim().ToUpperInvariant();
        if (CodeToNameMap.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            name = value;
            return true;
        }
        return false;
    }

    // 一级疾病分类常量（与 nc_dict_items.DiseaseCategories 字典 item_key 一致）
    public const string CategoryNone = "无任何疾病";
    public const string CategoryMalignantTumor = "恶性肿瘤";
    public const string CategoryChronicKidneyDisease = "慢性肾功能不全";
    public const string CategoryHeartDisease = "心脏病";
    public const string CategoryCerebrovascular = "脑血管疾病";
    public const string CategoryDiabetes = "糖尿病";
    public const string CategoryChronicRespiratory = "慢性呼吸系统疾病";
    public const string CategoryOther = "其他";
    public const string CategoryUrinary = "泌尿系统疾病";
    public const string CategoryPediatricCongenital = "儿科先天性疾病";
    public const string CategoryInfectious = "感染性疾病";
    public const string CategoryOrthopedic = "骨科及结缔组织疾病";
    public const string CategoryPsychiatricNeurological = "精神神经系统疾病";
    public const string CategoryImmune = "免疫系统疾病";
    public const string CategoryDigestive = "消化系统疾病";
    public const string CategoryHematological = "血液系统疾病";
    public const string CategorySevereTrauma = "严重创伤及并发症";

    private static readonly Regex CodePrefixRegex = new(@"^([A-Z])(\d{1,3})", RegexOptions.Compiled);

    /// <summary>
    /// 按 ICD-10 章节把编码归类到一级疾病分类（启发式映射，人工可在表单中覆盖）。
    /// 无法识别的编码返回"其他"。
    /// </summary>
    public static string MapCategory(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return CategoryOther;

        var match = CodePrefixRegex.Match(code.Trim().ToUpperInvariant());
        if (!match.Success) return CategoryOther;

        var letter = match.Groups[1].Value[0];
        var num = int.TryParse(match.Groups[2].Value, out var n) ? n : -1;

        switch (letter)
        {
            case 'A' or 'B':
                return CategoryInfectious;                       // A00-B99 传染病和寄生虫病

            case 'C':
                return CategoryMalignantTumor;                   // C00-C97 恶性肿瘤
            case 'D':
                return num <= 48 ? CategoryMalignantTumor        // D00-D48 肿瘤（含动态未定）
                                 : CategoryHematological;        // D50-D89 血液及造血器官疾病

            case 'E':
                return num is >= 10 and <= 14 ? CategoryDiabetes // E10-E14 糖尿病
                                              : CategoryOther;   // 其余内分泌/营养/代谢

            case 'F' or 'G':
                return CategoryPsychiatricNeurological;          // F01-F99 精神行为、G00-G99 神经系统

            case 'I':
                if (num is >= 60 and <= 69) return CategoryCerebrovascular; // I60-I69 脑血管病
                if (num >= 30 && num <= 52) return CategoryHeartDisease;    // I30-I52 心脏病
                return CategoryOther;                                       // 高血压/动脉等归其他

            case 'J':
                return CategoryChronicRespiratory;               // J00-J99 呼吸系统

            case 'K':
                return CategoryDigestive;                        // K00-K95 消化系统

            case 'M':
                return CategoryOrthopedic;                       // M00-M99 肌肉骨骼与结缔组织

            case 'N':
                return num is >= 17 and <= 19 ? CategoryChronicKidneyDisease // N17-N19 急慢性肾衰
                                              : CategoryUrinary;             // N00-N99 泌尿生殖系统

            case 'P' or 'Q':
                return CategoryPediatricCongenital;              // P 围产期、Q 先天性畸形/染色体异常

            case 'S' or 'T':
                return CategorySevereTrauma;                     // S00-T98 损伤中毒

            case 'U':
                return CategoryInfectious;                       // U07 新冠等特殊用途编码

            default:
                return CategoryOther;                            // H 眼耳、L 皮肤、O 妊娠、R 症状、V-Z 外因等
        }
    }
}
