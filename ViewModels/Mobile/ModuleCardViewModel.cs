using System.Windows.Input;

namespace NewCosmos.ViewModels.Mobile;

/// <summary>
/// 模块卡片数据（纯数据，不含 UI 类型）：图标/标题/副标题/图标底色/点击命令。
/// 视图渲染在 MobileMainPage.xaml 的 DataTemplate 中完成。
/// </summary>
public class ModuleCardViewModel
{
    public string Icon { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;

    /// <summary>图标底色（#RRGGBB 字符串，经 StringToColorConverter 转换）</summary>
    public string BgColor { get; init; } = "#FFFFFF";

    /// <summary>点击命令（AsyncRelayCommand 包装导航方法）</summary>
    public ICommand Command { get; init; } = null!;

    /// <summary>卡片尾部箭头（功能卡 true，模块卡 false）</summary>
    public bool ShowArrow { get; init; }
}
