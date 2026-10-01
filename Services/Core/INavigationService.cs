namespace NewCosmos.Services.Core;

/// <summary>
/// 导航服务：Service 层导航出入口（签名不含任何 UI 类型，Service 可安全依赖）。
/// 页面由 <see cref="Navigation.NavigationKeys"/> 键定位，实现在 UI 层（NavigationService）。
/// ViewModelBase.NavigateToPageAsync&lt;TPage&gt; 泛型重载为过渡通道，后续逐页迁移到本接口。
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// 设置导航根页面（登录成功后切换主首页用，不经 push）。
    /// Android 自动解析 Pages.Mobile.Mobile{页面名} 回退页。
    /// </summary>
    Task SetRootAsync(string pageKey, CancellationToken ct = default);

    /// <summary>
    /// 压入页面：DI 解析（Android 手机页回退）→ 窗口标题注册 → 设置标题 → PushAsync → 按新栈顶恢复标题。
    /// </summary>
    /// <param name="pageKey">导航键（<see cref="Navigation.NavigationKeys"/>）</param>
    /// <param name="pageTitle">页面标题（非空时写入 Page.Title，作为窗口标题数据源）</param>
    Task PushAsync(string pageKey, string? pageTitle = null, CancellationToken ct = default);
}
