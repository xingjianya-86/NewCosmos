using System.Globalization;

namespace NewCosmos.Converters;

/// <summary>
/// bool → FontAttributes 转换器（true=Bold, false=None）
/// 用于 Tab 选中状态切换字体粗细
/// </summary>
public class BoolToFontAttributeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool b && b ? FontAttributes.Bold : FontAttributes.None;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
