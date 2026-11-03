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
}
