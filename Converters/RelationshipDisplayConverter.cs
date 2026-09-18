using System.Globalization;
using NewCosmos.Helpers;

namespace NewCosmos.Converters;

/// <summary>
/// 家庭关系代码 → 中文显示 转换器。
/// 数据源：字典 FamilyRelationships（DictDisplayHelper 缓存）。
/// 空值 → "-"；已中文或字典未命中 → 原样返回，兼容历史记录。
/// </summary>
public class RelationshipDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type? targetType, object? parameter, CultureInfo? culture)
        => DictDisplayHelper.GetFamilyRelationshipDisplay(value?.ToString());

    public object? ConvertBack(object? value, Type? targetType, object? parameter, CultureInfo? culture)
        => throw new NotSupportedException();
}
