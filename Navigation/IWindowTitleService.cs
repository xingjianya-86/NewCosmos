using Microsoft.Maui.Controls;

namespace NewCosmos.Navigation;

/// <summary>
/// 窗口标题管理服务（单例，UI 层基础设施）：统一维护主窗口 Title，消除 MainViewModel/ModuleHomeViewModelBase/ViewModelBase 三处重复逻辑。
/// 签名含 Page/INavigation 属 UI 层契约，故归位于 Navigation 层而非 Services.Core。
/// </summary>
public interface IWindowTitleService
{
    /// <summary>
    /// 设置窗口标题：pageTitle 非空且不等于基础标题时显示 "{pageTitle} - {baseTitle}"，否则仅显示 baseTitle。
    /// </summary>
    void SetPageTitle(string? pageTitle);

    /// <summary>
    /// 恢复窗口标题为 app.ini 配置的基础标题（返回首页或 Pop 返回时调用）。
    /// </summary>
    void RestoreToBase();

    /// <summary>
    /// 设置登录页窗口标题："{baseTitle}"。
    /// </summary>
    void SetLoginTitle();

    /// <summary>
    /// 按导航栈顶页面的 Page.Title 恢复窗口标题（栈顶无 Title 时回落基础标题）。
    /// 任何返回路径（GoBack 按钮、操作完成后自动 PopAsync/PopToRootAsync）返回后都应调用此方法，
    /// 曾因直接 PopAsync 绕过恢复逻辑导致标题残留子页（见 ArchiveProduction/ArchiveOutput 等 8 处）。
    /// </summary>
    void RestoreFromNavigation(INavigation navigation);

    /// <summary>
    /// 注册页面：页面 Appearing 时窗口标题自动跟随该页 Page.Title。
    /// 覆盖 NavigationPage 系统返回按钮（Windows 左上角）等完全绕过应用代码的 pop 路径——
    /// pop 后栈顶页重新 Appearing，标题随之恢复，无需依赖任何应用层返回入口。
    /// push 前对目标页调用一次即可（内部防重复）。
    /// </summary>
    void Register(Page page);
}
