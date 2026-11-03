namespace NewCosmos.Services.Platform;

/// <summary>
/// 系统栏（状态栏 + 导航栏）形态控制。纸墨档案风下，安卓端默认沉浸式全屏隐藏系统栏。
/// 两端实现：
///  - Android：按 config\app.ini [Android] FullscreenMode 切换 Immersive（隐藏，默认）/ EdgeToEdge（透明可见）
///  - Windows：桌面无系统栏概念，实现为空操作（标题栏改色另由 WindowsTitleBarService 负责）
/// </summary>
public interface ISystemBarsService
{
    /// <summary>应用进入前台/窗口就绪时调用，套用配置的系统栏形态。</summary>
    void Apply();

    /// <summary>让系统栏短暂浮现（沉浸式下滑呼出），随后自动隐藏。</summary>
    void ShowTransient();
}
