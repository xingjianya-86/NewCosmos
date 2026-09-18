using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using NewCosmos.Models.Options;

namespace NewCosmos.Services.Core;

/// <summary>
/// 窗口标题管理服务实现。
/// 逻辑提取自 MainViewModel.UpdateWindowTitle / ModuleHomeViewModelBase.UpdateWindowTitle / ViewModelBase.RestoreWindowTitleToBase。
/// 窗口标题约定：push 前将标题写入目标页 Page.Title，返回后按导航栈顶页面的 Page.Title 恢复。
/// </summary>
public class WindowTitleService : IWindowTitleService
{
    private readonly AppOptions _appOptions;
    private readonly ILoggerService _logger;

    /// <summary>已注册 Appearing 跟随的页面（防重复订阅；ConditionalWeakTable 不阻止实例回收）</summary>
    private readonly ConditionalWeakTable<Page, object> _registered = new();

    public WindowTitleService(AppOptions appOptions, ILoggerService logger)
    {
        _appOptions = appOptions;
        _logger = logger;
    }

    public void SetPageTitle(string? pageTitle)
    {
        try
        {
            var window = Application.Current?.Windows;
            var w = window != null && window.Count > 0 ? window[0] : null;
            if (w == null) return;

            // 与基础标题相同的值视为空（主首页 Page.Title 即基础标题，避免 "基础 - 基础" 重复拼接）
            var effective = string.IsNullOrEmpty(pageTitle) || pageTitle == _appOptions.WindowTitle
                ? null
                : pageTitle;
            w.Title = effective == null
                ? _appOptions.WindowTitle
                : $"{effective} - {_appOptions.WindowTitle}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "设置窗口标题失败");
        }
    }

    public void RestoreToBase()
    {
        try
        {
            var window = Application.Current?.Windows?.FirstOrDefault();
            if (window == null) return;

            if (!string.IsNullOrEmpty(_appOptions.WindowTitle))
                window.Title = _appOptions.WindowTitle;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "恢复窗口标题失败");
        }
    }

    public void SetLoginTitle()
    {
        try
        {
            var window = Application.Current?.Windows?.FirstOrDefault();
            if (window != null)
                window.Title = _appOptions.WindowTitle;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "设置登录窗口标题失败");
        }
    }

    public void RestoreFromNavigation(INavigation navigation)
    {
        try
        {
            // 取返回后的栈顶页面（push 前由调用点的 Page.Title 写入保证数据源；
            // pop/PopToRoot 后栈顶即返回到的页面）。栈顶无 Title 时回落基础标题。
            var stack = navigation.NavigationStack;
            var top = stack.Count > 0 ? stack[^1] : null;
            SetPageTitle(top?.Title);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "按导航栈恢复窗口标题失败");
        }
    }

    public void Register(Page page)
    {
        try
        {
            if (page == null || _registered.TryGetValue(page, out _))
                return;
            _registered.Add(page, new object());
            page.Appearing += OnRegisteredPageAppearing;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "注册页面标题跟随失败");
        }
    }

    /// <summary>页面可见时窗口标题跟随其 Page.Title——覆盖系统返回按钮等一切导航路径</summary>
    private void OnRegisteredPageAppearing(object? sender, EventArgs e)
    {
        if (sender is Page page)
            SetPageTitle(page.Title);
    }
}
