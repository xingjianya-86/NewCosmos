namespace NewCosmos.Constants;

/// <summary>
/// 年度种植补贴（四合一）长表模板的列与分类单一来源：
/// 导入表头识别、模板导出、分类校验三处共用，禁止各处重抄列名/分类名。
/// 模板形态（长表，一行 = 一人 × 一分类 × 一年段）：
///   数据年份(可选) | 姓名 | 身份证号 | 分类 | 金额（元）
/// 分类严格限定为 7 个规范名（= 申请表单 SubsidyType，读取侧零映射）；
/// 落库 category 直接用规范名，值不允许别名（模板里做下拉数据验证从源头约束）。
/// </summary>
public static class AnnualPlantingColumns
{
    /// <summary>列 Key（导入映射字典的键）</summary>
    public const string YEAR = "year";
    public const string NAME = "name";
    public const string ID_CARD = "id_card";
    public const string CATEGORY = "category";
    public const string AMOUNT = "amount";

    /// <summary>
    /// 模板列（顺序固定；导入侧按表头识别，源文件列顺序可不同）。
    /// Required=false 的列缺失不报错：数据年份缺失时回落到「上一个有效年份 → 文件名 20xx → 当前年」。
    /// </summary>
    public static readonly (string Key, string Header, string[] Keywords, bool Required)[] Columns =
    {
        (YEAR,    "数据年份",  new[] { "数据年份", "年份", "年度", "年段" }, false),
        (NAME,    "姓名",      new[] { "姓名" },                            true),
        (ID_CARD, "身份证号",  new[] { "身份证" },                          true),
        (CATEGORY, "分类",     new[] { "分类" },                            true),
        (AMOUNT,  "金额（元）", new[] { "金额" },                            true),
    };

    /// <summary>
    /// 7 个规范分类（严格匹配，取值 = 申请表单补贴类型名）。
    /// </summary>
    public static readonly string[] Categories =
    {
        DictionaryConstants.SubsidyType.CORN,              // 玉米补贴
        DictionaryConstants.SubsidyType.SOYBEAN,           // 大豆补贴
        DictionaryConstants.SubsidyType.SURFACE_WATER_RICE,// 地表水水稻
        DictionaryConstants.SubsidyType.GROUND_WATER_RICE, // 地下水水稻
        DictionaryConstants.SubsidyType.LAND_FERTILITY,    // 地力补贴
        DictionaryConstants.SubsidyType.ROTATION,          // 轮作补贴
        DictionaryConstants.SubsidyType.SOYBEAN_VARIETY,   // 大豆品种补贴
    };

    /// <summary>必填列 Key（缺任一 → 整份报错）</summary>
    public static readonly string[] RequiredKeys =
        Columns.Where(c => c.Required).Select(c => c.Key).ToArray();

    /// <summary>模板表头（按 Columns 顺序）</summary>
    public static string[] GetTemplateHeaders() => Columns.Select(c => c.Header).ToArray();

    /// <summary>必填列的中文名（报错文案用）</summary>
    public static string GetHeader(string key)
        => Columns.FirstOrDefault(c => c.Key == key).Header ?? key;

    /// <summary>
    /// 分类归一：仅做空白与全半角容错（去空格/全角空格、括号全→半角），然后**完全等于**规范名才通过；
    /// 不做别名映射（口径已拍板：严格 7 个规范名），认不出返回 null 由调用方报行号。
    /// </summary>
    public static string? NormalizeCategory(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var s = raw.Trim()
                   .Replace("　", "")   // 全角空格
                   .Replace(" ", "")
                   .Replace("（", "(")
                   .Replace("）", ")");

        foreach (var c in Categories)
        {
            if (string.Equals(s, c, StringComparison.Ordinal)) return c;
        }
        return null;
    }
}
