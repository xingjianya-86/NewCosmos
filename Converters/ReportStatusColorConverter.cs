using System.Globalization;

namespace NewCosmos.Converters;

/// <summary>
/// 核查报告状态 → 颜色转换器（"已上传"=绿色, 其他=灰色）
/// </summary>
public class ReportStatusColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string status && status == "已上传")
            return Microsoft.Maui.Graphics.Colors.Green;
        return Microsoft.Maui.Graphics.Colors.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
