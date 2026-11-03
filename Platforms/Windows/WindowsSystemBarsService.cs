#if WINDOWS
using NewCosmos.Services.Platform;

namespace NewCosmos.Platforms.Windows;

/// <summary>
/// Windows 系统栏控制空实现：桌面无安卓式状态栏/导航栏概念。
/// 窗口标题栏改色由 <see cref="WindowsTitleBarService"/> 负责，与本服务职责分离。
/// 保留实现是为了让 <see cref="ISystemBarsService"/> 在两端都可注入，调用处无需平台分支。
/// </summary>
public sealed class WindowsSystemBarsService : ISystemBarsService
{
    public void Apply() { }

    public void ShowTransient() { }
}
#endif
