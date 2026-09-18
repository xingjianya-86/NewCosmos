using System.Globalization;

namespace NewCosmos.Converters;

public class TemplateTypeToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            "docx" => Application.Current?.Resources["Primary"],
            "xlsx" => Application.Current?.Resources["Success"],
            _ => Application.Current?.Resources["Gray400"]
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
