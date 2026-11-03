using System.Windows.Input;

namespace NewCosmos.ViewModels.Main;

/// <summary>
/// 首页模块卡片（模型驱动，卡片视觉统一走 MainPage.xaml 的模块卡片 DataTemplate）。
/// 图标为 Segoe Fluent Icons 码位——Android 端不渲染 MainPage（PageResolver 回退 MobileMainPage）。
/// </summary>
public class ModuleCardItem
{
    public string Title { get; init; } = string.Empty;

    public string Subtitle { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;

    /// <summary>图标码位（Segoe Fluent Icons）</summary>
    public string IconGlyph { get; init; } = string.Empty;

    /// <summary>图标底色（hex，与 Resources\Styles\Colors.xaml 的 Module* 色板保持一致）</summary>
    public string IconBgColor { get; init; } = "#FFFFFF";

    /// <summary>图标字色（hex）</summary>
    public string IconColor { get; init; } = "#000000";

    /// <summary>按钮文案（进入模块 / 访问网站）</summary>
    public string ButtonText { get; init; } = "进入模块";

    public ICommand Command { get; init; } = null!;

    public object? CommandParameter { get; init; }

    /// <summary>组内 3 列流动排位（由 MainViewModel.RebuildDashboard 写入）</summary>
    public int Row { get; set; }

    /// <summary>组内 3 列流动排位（由 MainViewModel.RebuildDashboard 写入）</summary>
    public int Col { get; set; }
}
