using System.Globalization;

namespace NewCosmos.Converters;

public class BoolToTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            var param = parameter?.ToString() ?? "True|false";
            var parts = param.Split('|');

            if (parts.Length >= 2)
            {
                return boolValue ? parts[0] : parts[1];
            }

            return boolValue ? parts[0] : "false";
        }

        return value?.ToString() ?? "Unknown";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var param = parameter?.ToString() ?? "True|false";
        var parts = param.Split('|');

        if (parts.Length >= 2)
        {
            return value?.ToString() == parts[0];
        }

        return false;
    }
}
