using System.Globalization;

namespace NewCosmos.Converters;

/// <summary>
/// 自动填充状态 → 背景色转换器
/// 用于侧边栏数据项的背景色标记
/// - 已自动填充（IsAutoFilled=true）→ 浅绿色
/// - 可匹配但未填充（IsMatchable=true, IsAutoFilled=false）→ 浅蓝色
/// - 不可匹配 → 白色
/// </summary>
public class AutoFillColorConverter : IValueConverter
{
    // 颜色只解析一次，避免每次 Convert 调用重复解析
    private static readonly Microsoft.Maui.Graphics.Color AutoFilledBackground = Microsoft.Maui.Graphics.Color.FromArgb("#E8F5E9"); // 浅绿色
    private static readonly Microsoft.Maui.Graphics.Color MatchableBackground = Microsoft.Maui.Graphics.Color.FromArgb("#E3F2FD"); // 浅蓝色

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isAutoFilled)
        {
            if (isAutoFilled)
                return AutoFilledBackground;

            // 通过 parameter 传入 IsMatchable
            if (parameter is bool isMatchable && isMatchable)
                return MatchableBackground;
        }

        return Microsoft.Maui.Graphics.Colors.White;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// 自动填充状态 → 左边框颜色转换器
/// - 已自动填充 → 绿色边框
/// - 可匹配 → 蓝色边框
/// - 不可匹配 → 灰色边框
/// </summary>
public class AutoFillBorderColorConverter : IValueConverter
{
    // 颜色只解析一次，避免每次 Convert 调用重复解析
    private static readonly Microsoft.Maui.Graphics.Color AutoFilledBorder = Microsoft.Maui.Graphics.Color.FromArgb("#4CAF50"); // 绿色
    private static readonly Microsoft.Maui.Graphics.Color MatchableBorder = Microsoft.Maui.Graphics.Color.FromArgb("#2196F3"); // 蓝色
    private static readonly Microsoft.Maui.Graphics.Color DefaultBorder = Microsoft.Maui.Graphics.Color.FromArgb("#E0E0E0"); // 灰色

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isAutoFilled)
        {
            if (isAutoFilled)
                return AutoFilledBorder;

            if (parameter is bool isMatchable && isMatchable)
                return MatchableBorder;
        }

        return DefaultBorder;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// 自动填充状态 → 图标文字转换器
/// - 已自动填充 → "✓"
/// - 可匹配 → "○"
/// - 不可匹配 → ""
/// </summary>
public class AutoFillIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isAutoFilled)
        {
            if (isAutoFilled)
                return "✓";

            if (parameter is bool isMatchable && isMatchable)
                return "○";
        }

        return "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
