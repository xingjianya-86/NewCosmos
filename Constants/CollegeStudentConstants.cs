namespace NewCosmos.Constants;

/// <summary>
/// 大学生管理相关常量
/// </summary>
public static class CollegeStudentConstants
{
    /// <summary>可关联最小年龄（周岁）</summary>
    public const int MinAge = 18;

    /// <summary>可关联最大年龄（周岁，五年制学生毕业时可达23岁）</summary>
    public const int MaxAge = 23;

    /// <summary>学历层次选项（落库存中文值）</summary>
    public static readonly string[] EducationLevelOptions = { "本科", "专科" };

    /// <summary>学制选项（与临时救助教育支出一致）</summary>
    public static readonly string[] SchoolDurationOptions = { "三年制", "四年制", "五年制" };

    /// <summary>状态：在读</summary>
    public const string StatusStudying = "Studying";

    /// <summary>状态：已毕业</summary>
    public const string StatusGraduated = "Graduated";

    /// <summary>状态：不符合人员（年龄段内不在上学的）</summary>
    public const string StatusNotEligible = "NotEligible";

    /// <summary>状态：未收到高等教育</summary>
    public const string StatusNoHigherEducation = "NoHigherEducation";

    /// <summary>学制数值 → 显示文本</summary>
    public static string GetSchoolDurationDisplay(int? duration) => duration switch
    {
        3 => "三年制",
        4 => "四年制",
        5 => "五年制",
        _ => string.Empty
    };

    /// <summary>显示文本 → 学制数值</summary>
    public static int? GetSchoolDurationValue(string? display) => display switch
    {
        "三年制" => 3,
        "四年制" => 4,
        "五年制" => 5,
        _ => null
    };

    /// <summary>首页毕业提醒文案</summary>
    public static string BuildGraduationBannerText(int count) =>
        $"有 {count} 位大学生今年6月毕业，毕业后最长择业期半年，请及时前往「低收入人口救助帮扶」更新毕业状态并核查是否需退出最低生活保障";

    /// <summary>状态key → 显示文本</summary>
    public static string GetStatusDisplay(string? status) => status switch
    {
        StatusStudying => "在读",
        StatusGraduated => "已毕业",
        StatusNotEligible => "不符合人员",
        StatusNoHigherEducation => "未收到高等教育",
        _ => string.Empty
    };

    /// <summary>显示文本 → 状态key</summary>
    public static string GetStatusKey(string? display) => display switch
    {
        "在读" => StatusStudying,
        "已毕业" => StatusGraduated,
        "不符合人员" => StatusNotEligible,
        "未收到高等教育" => StatusNoHigherEducation,
        _ => StatusStudying
    };

    /// <summary>状态选项（显示文本）</summary>
    public static readonly string[] StatusOptions = { "在读", "已毕业", "不符合人员", "未收到高等教育" };
}
