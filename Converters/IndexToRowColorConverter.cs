using System.Collections;
using System.Globalization;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace NewCosmos.Converters;

/// <summary>
/// 列表行交替色转换器：按项在 CollectionView.ItemsSource 中的索引奇偶返回 白 / 浅灰（#F8F9FA），
/// 用于列表页行区块划分。用法：<c>{Binding ., Converter={StaticResource IndexToRowColorConverter}, ConverterParameter={x:Reference ListX}}</c>
/// </summary>
public class IndexToRowColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is CollectionView cv && cv.ItemsSource is IList list && value != null)
        {
            var index = list.IndexOf(value);
            return index >= 0 && index % 2 == 1 ? Color.FromArgb("#F8F9FA") : Colors.White;
        }
        return Colors.White;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}