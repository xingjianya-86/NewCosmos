using System.Collections.Concurrent;
using System.Globalization;

namespace NewCosmos.Converters;

/// <summary>
/// 字符串颜色码 → Color 转换器（支持 #RRGGBB 格式）
/// </summary>
public class StringToColorConverter : IValueConverter
{
    /// <summary>解析结果缓存：颜色码有限且不可变，列表/网格大量单元格反复求值时避免重复解析</summary>
    private static readonly ConcurrentDictionary<string, Microsoft.Maui.Graphics.Color> _cache = new();

    public Microsoft.Maui.Graphics.Color DefaultColor { get; set; } = Microsoft.Maui.Graphics.Colors.Gray;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string colorString && !string.IsNullOrEmpty(colorString))
        {
            return _cache.GetOrAdd(colorString, c =>
            {
                try
                {
                    return Microsoft.Maui.Graphics.Color.FromArgb(c);
                }
                catch
                {
                    return DefaultColor;
                }
            });
        }
        return DefaultColor;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
