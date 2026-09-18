using System.Globalization;
using NewCosmos.Constants;

namespace NewCosmos.Converters;

/// <summary>
/// 状态英文转中文转换器
/// </summary>
public class StatusToTextConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = value?.ToString() ?? "";

        return status switch
        {
            ApplicationStatusCodes.DRAFT => "草稿",
            ApplicationStatusCodes.SUBMITTED => "已提交",
            ApplicationStatusCodes.APPROVED => "已审批",
            ApplicationStatusCodes.COMPLETED => "已完成",
            ApplicationStatusCodes.REFUSED => "不予受理",
            ApplicationStatusCodes.STOPPED => "已停保",
            _ => status
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
