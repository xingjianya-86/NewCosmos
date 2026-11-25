namespace NewCosmos.Constants;

/// <summary>
/// 月报表「按社会救助分类拆分」口径单点：分类名、分类码集合、表单键拆分/匹配。
/// 消费方：MonthlyReportViewModel（表单列表）、PrintService（表单键拆分）、
/// MonthlyReportService（新增/停保/增减发/死亡/退出等分类过滤）。
/// </summary>
public static class MonthlyReportCategoryConstants
{
    public const string Subsistence = "最低生活保障";
    public const string LowIncomeEdge = "最低生活保障边缘家庭";
    public const string Destitute = "特困人员";
    public const string RigidExpenditure = "刚性支出困难家庭";

    /// <summary>停保汇总表专用（独立停止表数据源，不是认定分类码）</summary>
    public const string OtherDifficulty = "其他困难人员";

    /// <summary>月报表单键可识别的分类后缀（顺序与界面展示一致）</summary>
    public static readonly string[] FormKeySuffixes =
    {
        Subsistence, LowIncomeEdge, Destitute, RigidExpenditure, OtherDifficulty
    };

    /// <summary>退出/死亡报表按分类拆分使用的四类（不含其他困难人员）</summary>
    public static readonly string[] SplitCategories =
    {
        Subsistence, LowIncomeEdge, Destitute, RigidExpenditure
    };

    /// <summary>
    /// 分类名 → 认定分类码集合（与旧 MonthlyReportService.CategoryCodes 完全一致）。
    /// 最低生活保障=低保+低保单人；最低生活保障边缘家庭=低保边缘；特困人员=分散/集中供养；刚性支出困难家庭=刚性支出。
    /// 空/null/未识别 → null（调用方按"不过滤"处理）。
    /// </summary>
    public static string[]? ResolveCodes(string? category) => category switch
    {
        Subsistence => new[]
        {
            ClassificationConstants.RuralSubsistence, ClassificationConstants.UrbanSubsistence,
            ClassificationConstants.RuralLowIncomeSingle, ClassificationConstants.UrbanLowIncomeSingle
        },
        LowIncomeEdge => new[] { ClassificationConstants.RuralLowIncome, ClassificationConstants.UrbanLowIncome },
        Destitute => new[]
        {
            ClassificationConstants.RuralDestituteScattered, ClassificationConstants.RuralDestituteCentralized,
            ClassificationConstants.UrbanDestituteScattered, ClassificationConstants.UrbanDestituteCentralized
        },
        RigidExpenditure => new[]
        {
            ClassificationConstants.RuralRigidExpenditure, ClassificationConstants.UrbanRigidExpenditure
        },
        _ => null
    };

    /// <summary>
    /// 分类匹配：category 为空 → 全部通过；分类码为空 → 不匹配（只进「全部」汇总）。
    /// 未识别的 category 维持既有「不过滤」语义。
    /// </summary>
    public static bool Matches(string? category, string? classificationCode)
    {
        if (string.IsNullOrWhiteSpace(category))
            return true;

        var codes = ResolveCodes(category);
        if (codes == null)
            return true;

        return !string.IsNullOrWhiteSpace(classificationCode)
            && codes.Contains(classificationCode.Trim(), StringComparer.Ordinal);
    }

    /// <summary>
    /// 拆分「基础名_分类」（分类后缀见 <see cref="FormKeySuffixes"/>）；无分类后缀则原样返回。
    /// 基础名本身可含下划线（如 档案_退出对象兜底情况纠治表），只按最后一个下划线判断。
    /// </summary>
    public static (string BaseForm, string? Category) SplitFormKey(string formKey)
    {
        var tail = formKey.LastIndexOf('_');
        if (tail > 0)
        {
            var suffix = formKey[(tail + 1)..];
            if (FormKeySuffixes.Contains(suffix, StringComparer.Ordinal))
                return (formKey[..tail], suffix);
        }
        return (formKey, null);
    }
}
