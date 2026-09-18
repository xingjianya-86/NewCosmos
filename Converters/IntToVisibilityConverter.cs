using System.Globalization;

namespace NewCosmos.Converters;

/// <summary>
/// int → bool 转换器（>0 为 true，用于徽章可见性）
/// </summary>
public class IntToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int i)
            return i > 0;
        if (value is long l)
            return l > 0;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
