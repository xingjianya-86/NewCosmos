using NewCosmos.Constants;

namespace NewCosmos.Helpers;

/// <summary>
/// 档案模板分类解析（唯一事实来源）：
/// 档案制作（ArchiveOutputViewModel）与补打中心批量模板库都按模板 categories 选模板，避免名称前缀带来的漏选/误选。
/// </summary>
public static class ArchiveCategoryResolver
{
    /// <summary>
    /// 单条档案的业务分类（与档案输出逐条口径一致）：
    /// 传入 businessType 与当前记录的 classification，返回应匹配的模板分类数组。
    /// </summary>
    public static string[] GetRecordCategories(string businessType, string? classification) => businessType switch
    {
        "AssetVerification" => [ClassificationConstants.AssetVerification],
        "FamilyApplication" => BuildWithOperation(classification, "新增"),
        "MonthlyReport" => ["月报表"],
        "EconomicReview" => BuildWithOperation(classification, "经济复核"),
        "ElderlyBenefits" => classification switch
        {
            "Stop" => ["普惠高龄/停止"],
            "Review" => ["普惠高龄/停止", "普惠高龄/新增", "普惠高龄/变更"],
            "MonthlyNew" => ["普惠高龄/月报"],
            "MonthlyStop" => ["普惠高龄/月报"],
            _ => ["普惠高龄/新增"]
        },
        TempReliefConstants.BusinessType => classification == "Small"
            ? [TempReliefConstants.CategorySmall]
            : [TempReliefConstants.CategoryLarge],
        "Recovery" => ["追缴"],
        _ => [businessType]
    };

    /// <summary>
    /// 补打中心批量模板库的域级分类范围（取并集，跨记录混合分类也能一次列出）：
    /// 与 GetRecordCategories 同源分类常量；返回 null 表示未接入该域，调用方回退名称前缀过滤。
    /// </summary>
    public static string[]? GetReprintDomainCategories(string domainKey) => domainKey switch
    {
        "FamilyApplication" =>
            [.. ClassificationConstants.ArchiveCategoryCodes, "新增"],
        TempReliefConstants.BusinessType =>
            [TempReliefConstants.CategoryLarge, TempReliefConstants.CategorySmall],
        "ElderlyBenefits" =>
            ["普惠高龄/新增", "普惠高龄/停止", "普惠高龄/变更", "普惠高龄/月报"],
        "AssetVerification" => [ClassificationConstants.AssetVerification],
        _ => null
    };

    private static string[] BuildWithOperation(string? classification, string operation)
    {
        var categories = new List<string>(2);
        if (!string.IsNullOrEmpty(classification))
            categories.Add(classification);
        categories.Add(operation);
        return categories.ToArray();
    }
}
