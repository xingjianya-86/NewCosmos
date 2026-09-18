using System.Globalization;

namespace NewCosmos.Converters;

/// <summary>
/// bool → Color 转换器（true=TrueColor, false=FalseColor）
/// </summary>
public class BoolToColorConverter : IValueConverter
{
    public Microsoft.Maui.Graphics.Color TrueColor { get; set; } = Microsoft.Maui.Graphics.Colors.LightBlue;
    public Microsoft.Maui.Graphics.Color FalseColor { get; set; } = Microsoft.Maui.Graphics.Colors.White;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            if (parameter is string paramStr && paramStr.Contains('|'))
            {
                var parts = paramStr.Split('|', 2);
                var trueColor = Color.FromArgb(parts[0]);
                var falseColor = Color.FromArgb(parts[1]);
                return b ? trueColor : falseColor;
            }
            return b ? TrueColor : FalseColor;
        }
        return FalseColor;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
