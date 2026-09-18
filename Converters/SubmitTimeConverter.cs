using System.Globalization;

namespace NewCosmos.Converters;

/// <summary>
/// 提交时间转换器（空值显示"未提交"）
/// </summary>
public class SubmitTimeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is DateTime dt && dt != default)
            return dt.ToString("yyyy-MM-dd HH:mm");

        return "未提交";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
