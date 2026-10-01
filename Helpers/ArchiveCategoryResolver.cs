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
    /// operationOverride 非空时用其替换默认操作位（如「人员变更」「经济复核」），
    /// 供输出文书/变动场景按分类加载变动类模板。
    /// </summary>
    public static string[] GetRecordCategories(string businessType, string? classification, string? operationOverride = null)
    {
        if (businessType == "FamilyApplication")
        {
            // 变动操作：[classification, operation]；无 classification 时仅 [operation]
            var op = string.IsNullOrEmpty(operationOverride) ? "新增" : operationOverride;
            var list = new List<string>();
            if (!string.IsNullOrEmpty(classification)) list.Add(classification);
            list.Add(op);
            // 变动场景并入固定变动分类集（模板挂叶子 Path 时也能命中）
            if (!string.IsNullOrEmpty(operationOverride))
            {
                foreach (var c in DocumentOperationCategories)
                    if (!list.Contains(c)) list.Add(c);
            }
            return list.ToArray();
        }

        return businessType switch
        {
            "AssetVerification" => [ClassificationConstants.AssetVerification],
            "MonthlyReport" => ["月报表"],
            "EconomicReview" => BuildWithOperation(classification, operationOverride ?? "经济复核"),
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
    }

    /// <summary>
    /// 低收入人口·变动类模板分类集（输出文书/补打共用；与 TemplateCategoryProvider 变更组叶子 Path 对齐）。
    /// </summary>
    public static readonly string[] DocumentOperationCategories =
    [
        "经济复核", "资金变更", "人员变更", "户主变更",
        "档案_保障金减少", "档案_增员减员调整表", "档案_渐退期审批表", "档案_变更告知书", "档案_定期复核审批表"
    ];

    /// <summary>
    /// 补打中心批量模板库的域级分类范围（取并集，跨记录混合分类也能一次列出）：
    /// 与 GetRecordCategories 同源分类常量；返回 null 表示未接入该域，调用方回退名称前缀过滤。
    /// FamilyApplication 并入变动分类集：保证增减员表/渐退审批/保障金减少/变更告知书可被补打选到。
    /// </summary>
    public static string[]? GetReprintDomainCategories(string domainKey) => domainKey switch
    {
        "FamilyApplication" =>
            [.. ClassificationConstants.ArchiveCategoryCodes, "新增", .. DocumentOperationCategories],
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
