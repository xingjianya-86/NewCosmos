using System.Globalization;

namespace NewCosmos.Converters;

public class DepthToMarginConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int depth)
            return new Thickness(depth * 20, 2, 0, 2);
        return new Thickness(0, 2);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
