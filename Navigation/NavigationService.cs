using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Services.Core;

namespace NewCosmos.Navigation;

/// <summary>
/// 导航服务实现（UI 层基础设施）：页面注册表 + DI 解析 + Android 手机页回退 + 窗口标题跟随。
/// 对应 ViewModelBase.NavigateToPageAsync 的五步导航（Resolve→Register→configure→Push→恢复标题）。
/// </summary>
public class NavigationService : INavigationService
{
    /// <summary>页面注册表：导航键 → 桌面端 Page 类型（Android 回退 Mobile{类型名}）。</summary>
    private static readonly Dictionary<string, Type> PageRegistry = new(StringComparer.Ordinal)
    {
        [NavigationKeys.Main] = typeof(Pages.Main.MainPage),
        [NavigationKeys.Login] = typeof(Pages.Auth.LoginPage),
    };

    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerService _logger;

    public NavigationService(IServiceProvider serviceProvider, ILoggerService logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task SetRootAsync(string pageKey, CancellationToken ct = default)
    {
        var pageType = ResolveType(pageKey);
        var pageName = pageType.Name;
        _logger.LogNavigationStart("NavigationService", pageName);
        try
        {
            var page = ResolvePage(pageType);
            SuppressSystemTitleBar(page);
            _serviceProvider.GetService<IWindowTitleService>()?.Register(page);

            // 主首页是 NavigationPage 的 root（不经 push），必须显式注册标题跟随，
            // 否则从模块页点系统返回按钮回主首页时标题残留（LoginViewModel 迁移前的实测 BUG）
            Helpers.WindowNavigator.CurrentPage = new Microsoft.Maui.Controls.NavigationPage(page);
            _logger.LogNavigationSuccess("NavigationService", pageName);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogNavigationFailed("NavigationService", pageName, ex.Message);
            _logger.LogError(ex, $"设置导航根页面 {pageName} 失败");
            throw;
        }
    }


    /// <summary>
    /// 全局关闭系统导航标题栏（纸墨档案风：页头由 PageHeaderView 自绘，不走 MAUI 标题条）。
    /// 安卓沉浸式全屏下再留一条标题条会破坏"无标题"观感，故在 push/root 处统一兜底，
    /// 避免遗漏个别未设 NavigationPage.HasNavigationBar 的页面。
    /// </summary>
    private static void SuppressSystemTitleBar(Microsoft.Maui.Controls.Page page)
    {
        Microsoft.Maui.Controls.NavigationPage.SetHasNavigationBar(page, false);
    }

    private static Type ResolveType(string pageKey)
    {
        if (!PageRegistry.TryGetValue(pageKey, out var type))
            throw new ArgumentException($"未登记的导航键: {pageKey}", nameof(pageKey));
        return type;
    }

    /// <summary>DI 解析页面；Android 上先解析 Pages.Mobile.Mobile{页面名}，找不到回退桌面页（单点实现见 PageResolver）。</summary>
    private Microsoft.Maui.Controls.Page ResolvePage(Type pageType)
        => PageResolver.Resolve(_serviceProvider, pageType);
}
