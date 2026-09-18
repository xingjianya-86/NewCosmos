using System.Globalization;
using NewCosmos.Constants;

namespace NewCosmos.Converters;

/// <summary>
/// 分类代码 → 中文显示名称 转换器
/// 兼容英文分类代码和中文分类值
/// </summary>
public class CategoryDisplayConverter : IValueConverter
{
    private static readonly Dictionary<string, string> CategoryMap = new()
    {
        // 英文分类代码 → 中文（唯一事实来源：ClassificationConstants.ConvertFromCode）
        [ClassificationConstants.RuralSubsistence] = ClassificationConstants.ConvertFromCode(ClassificationConstants.RuralSubsistence),
        [ClassificationConstants.UrbanSubsistence] = ClassificationConstants.ConvertFromCode(ClassificationConstants.UrbanSubsistence),
        [ClassificationConstants.RuralLowIncome] = ClassificationConstants.ConvertFromCode(ClassificationConstants.RuralLowIncome),
        [ClassificationConstants.UrbanLowIncome] = ClassificationConstants.ConvertFromCode(ClassificationConstants.UrbanLowIncome),
        [ClassificationConstants.RuralLowIncomeSingle] = ClassificationConstants.ConvertFromCode(ClassificationConstants.RuralLowIncomeSingle),
        [ClassificationConstants.UrbanLowIncomeSingle] = ClassificationConstants.ConvertFromCode(ClassificationConstants.UrbanLowIncomeSingle),
        [ClassificationConstants.RuralDestituteCentralized] = ClassificationConstants.ConvertFromCode(ClassificationConstants.RuralDestituteCentralized),
        [ClassificationConstants.UrbanDestituteCentralized] = ClassificationConstants.ConvertFromCode(ClassificationConstants.UrbanDestituteCentralized),
        [ClassificationConstants.RuralDestituteScattered] = ClassificationConstants.ConvertFromCode(ClassificationConstants.RuralDestituteScattered),
        [ClassificationConstants.UrbanDestituteScattered] = ClassificationConstants.ConvertFromCode(ClassificationConstants.UrbanDestituteScattered),
        [ClassificationConstants.RuralRigidExpenditure] = ClassificationConstants.ConvertFromCode(ClassificationConstants.RuralRigidExpenditure),
        [ClassificationConstants.UrbanRigidExpenditure] = ClassificationConstants.ConvertFromCode(ClassificationConstants.UrbanRigidExpenditure),
        [ClassificationConstants.AssetVerification] = "资产核查",
        ["AssetVerificationMonthlyReport"] = "资产核查月报表",
    };

    public object? Convert(object? value, Type? targetType, object? parameter, CultureInfo? culture)
    {
        if (value is IEnumerable<string> categories)
        {
            var displayNames = categories.Select(c => CategoryMap.TryGetValue(c, out var name) ? name : c);
            return string.Join("、", displayNames);
        }

        if (value is string[] arr)
        {
            var displayNames = arr.Select(c => CategoryMap.TryGetValue(c, out var name) ? name : c);
            return string.Join("、", displayNames);
        }

        return value?.ToString() ?? "";
    }

    public object? ConvertBack(object? value, Type? targetType, object? parameter, CultureInfo? culture)
    {
        throw new NotImplementedException();
    }
}
