using System.Globalization;
using NewCosmos.Constants;

namespace NewCosmos.Converters;

/// <summary>数据范围代码 → 中文名（角色编辑器 Picker 显示用）</summary>
public class DataScopeToDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var scope = value as string;
        return string.IsNullOrEmpty(scope) ? scope : DataScopeConstants.GetDisplayName(scope);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
