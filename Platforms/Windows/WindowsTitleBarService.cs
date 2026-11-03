#if WINDOWS
using Microsoft.UI.Windowing;
using NewCosmos.Services.Core;

namespace NewCosmos.Platforms.Windows;

/// <summary>
/// Windows 自定义标题栏（纸墨档案风）：
///  - 扩展内容进标题栏（ExtendsContentIntoTitleBar），底色 = 暖纸，与应用内报头连成一体
///  - 系统控制钮（最小化/最大化/关闭）底色透明，悬停/按下朱砂，融入纸墨风
///  - 拖拽/双击最大化/贴靠由 WinUI 在 ExtendsContentIntoTitleBar=true 时自动配置
///    （自动拖拽区已排除控制钮），本类不手动 SetDragRectangles，避免盖住可点元素。
/// 取 AppWindow 沿用项目已验证写法：Handler.PlatformView as Microsoft.UI.Xaml.Window → .AppWindow。
/// 颜色取自 Colors.xaml（经 Application.Resources），本类不硬编码新色。
/// </summary>
public sealed class WindowsTitleBarService
{
    private bool _applied;

    /// <summary>把标题栏改造接到窗口。窗口创建后调用一次即可（幂等，重复调用无副作用）。</summary>
    public void Attach(Microsoft.Maui.Controls.Window? mauiWindow)
    {
        if (_applied)
            return;

        if (mauiWindow?.Handler?.PlatformView is not Microsoft.UI.Xaml.Window nativeWindow)
            return;

        var titleBar = nativeWindow.AppWindow?.TitleBar;
        if (titleBar == null)
            return;

        try
        {
            // 扩展内容进标题栏（WinUI 自动配置拖拽区，排除控制钮）
            titleBar.ExtendsContentIntoTitleBar = true;

            // 颜色一律优先取 Colors.xaml；资源缺失时回退到纸墨风同款固定值。
            // 注意：本 TFM 下不可用 Windows.UI.Colors 静态色类，故统一用 FromArgb 构造。
            var paper = FromResource("CardBackground") ?? Argb(255, 0xF8, 0xF3, 0xE9);
            var ink = FromResource("Ink") ?? Argb(255, 0x17, 0x13, 0x10);
            var seal = FromResource("Seal") ?? Argb(255, 0xAB, 0x32, 0x27);
            var sealDark = FromResource("SealDark") ?? Argb(255, 0x7F, 0x23, 0x1B);
            var muted = FromResource("Gray500") ?? Argb(255, 0x8B, 0x7F, 0x6C);
            var white = Argb(255, 0xFF, 0xFF, 0xFF);
            var transparent = Argb(0, 0x00, 0x00, 0x00);

            // 底色 = 暖纸；标题文字 = 墨
            titleBar.BackgroundColor = paper;
            titleBar.ForegroundColor = ink;

            // 控制钮：透明底融入纸面，悬停/按下朱砂
            titleBar.ButtonBackgroundColor = transparent;
            titleBar.ButtonForegroundColor = ink;
            titleBar.ButtonHoverBackgroundColor = seal;
            titleBar.ButtonHoverForegroundColor = white;
            titleBar.ButtonPressedBackgroundColor = sealDark;
            titleBar.ButtonPressedForegroundColor = white;
            titleBar.ButtonInactiveBackgroundColor = transparent;
            titleBar.ButtonInactiveForegroundColor = muted;

            _applied = true;
        }
        catch (Exception ex)
        {
            // 标题栏定制失败不阻断启动，回退系统默认标题栏
            Serilog.Log.Warning(ex, "[WindowsTitleBarService] 自定义标题栏应用失败，回退系统默认");
        }
    }

    /// <summary>
    /// 从合并资源字典取颜色并转 Windows.UI.Color。
    /// MAUI Color 的 R/G/B/A 是 float(0–1)，Windows.UI.Color.FromArgb 需要 byte，必须转换。
    /// </summary>
    private static global::Windows.UI.Color? FromResource(string key)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var v) == true
            && v is Microsoft.Maui.Graphics.Color c)
        {
            return global::Windows.UI.Color.FromArgb(ToByte(c.Alpha), ToByte(c.Red), ToByte(c.Green), ToByte(c.Blue));
        }
        return null;
    }

    private static byte ToByte(float f) => (byte)Math.Clamp((int)(f * 255f + 0.5f), 0, 255);

    /// <summary>构造 Windows.UI.Color（本 TFM 不可用 Windows.UI.Colors 静态色类）。</summary>
    private static global::Windows.UI.Color Argb(byte a, byte r, byte g, byte b) =>
        global::Windows.UI.Color.FromArgb(a, r, g, b);
}
#endif
