using System.Globalization;

namespace NewCosmos.Converters;

public class DepthToBoldConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int depth)
            return depth == 0 ? FontAttributes.Bold : FontAttributes.None;
        return FontAttributes.None;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
