namespace NewCosmos.Constants;

/// <summary>
/// 输出根下的一级分类目录名（B 类导出）——单一事实来源，禁止在 ViewModel/Service 里散落字面量。
///
/// <para>两套目录口径（勿混用）：</para>
/// <list type="bullet">
/// <item>A 类·文书：{输出根}\{分类}\{yyyy-MM-dd}\{主体_身份证}\{文件}，
/// 分类名取 <c>OutputPathHelper.GetCategoryDisplayName</c>（按人按日的正式文书）。</item>
/// <item>B 类·报表/批量导出：{输出根}\{分类}\{期间}\ 或 {输出根}\{分类}\（本类），
/// 按期间或按主体平铺，不套 A 类的日期+主体两层。</item>
/// </list>
///
/// <para>同名提示：<see cref="MonthlyReport"/>（月报表）与 <see cref="ArchivePhoto"/>（档案照片）
/// 与 A 类分类名同值——A 类下 MonthlyReport 无实际调用方（月报走本类），
/// 档案照片两类子层级不同（A 类=日期\主体，本类=身份证后4位），不会撞名。</para>
/// </summary>
public static class OutputCategories
{
    /// <summary>月报表（F4，期间 = yyyyMM）</summary>
    public const string MonthlyReport = "月报表";

    /// <summary>高龄月报（F5，期间 = yyyyMM）</summary>
    public const string ElderlyReport = "高龄月报";

    /// <summary>核查报告（F6 资产核查月报 + 档案制作页核查分支，平铺不带期间）</summary>
    public const string VerificationReport = "核查报告";

    /// <summary>公示文件（每月公示名单，期间 = yyyy-MM）</summary>
    public const string PublicityFile = "公示文件";

    /// <summary>档案照片批量导出（一级 = 身份证后4位）</summary>
    public const string ArchivePhoto = "档案照片";

    /// <summary>彩票预测结果导出（平铺）</summary>
    public const string LotteryPrediction = "彩票预测";
}
