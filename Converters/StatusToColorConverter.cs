using Microsoft.Maui.Graphics;
using NewCosmos.Constants;
using System.Globalization;

namespace NewCosmos.Converters;

public class StatusToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null) return Colors.Gray;

        var status = value?.ToString() ?? "";

        return status switch
        {
            ApplicationStatusCodes.DRAFT => Colors.Orange,
            ApplicationStatusCodes.SUBMITTED => Colors.DodgerBlue,
            ApplicationStatusCodes.APPROVED => Colors.Green,
            ApplicationStatusCodes.COMPLETED => Colors.DarkGreen,
            ApplicationStatusCodes.REFUSED => Colors.Red,
            ApplicationStatusCodes.STOPPED => Colors.DimGray,
            "0" => Colors.Orange,      // 待处理
            "1" => Colors.DodgerBlue,  // 有报告
            "2" => Colors.Green,       // 已列入
            "3" => Colors.Red,         // 已拒绝
            ApplicationStatusCodes.PENDING => Colors.Orange,
            "Error" => Colors.Gray,    // 异常
            _ => Colors.Gray
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
