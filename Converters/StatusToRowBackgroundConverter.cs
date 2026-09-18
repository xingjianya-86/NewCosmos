using Microsoft.Maui.Graphics;
using NewCosmos.Constants;
using System.Globalization;

namespace NewCosmos.Converters;

/// <summary>
/// 状态转行背景色转换器（草稿行特殊背景）
/// </summary>
public class StatusToRowBackgroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value?.ToString() ?? "";

        return status switch
        {
            ApplicationStatusCodes.DRAFT => Color.FromArgb("#FFF8E1"),  // 浅黄色
            _ => Colors.Transparent
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
