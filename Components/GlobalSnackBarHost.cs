using System.Runtime.CompilerServices;
using NewCosmos.Helpers;
using NewCosmos.Services.Core;

namespace NewCosmos.Components;

/// <summary>
/// SnackBar 全局宿主：订阅 IDialogService.SnackBarRequested，在当前最上层页面（模态优先）
/// 的根布局内挂一个共享 SnackBarView 显示轻提示。
/// 每页一个宿主实例，经 ConditionalWeakTable 关联，页面回收时宿主随之释放，不泄漏。
/// </summary>
public static class GlobalSnackBarHost
{
    private static readonly ConditionalWeakTable<Page, SnackBarView> Hosts = new();
    private static bool _attached;

    /// <summary>启动时调用一次（App.CreateWindow 成功后）。重复调用自动忽略。</summary>
    public static void Attach(IDialogService dialogService)
    {
        if (_attached) return;
        _attached = true;
        dialogService.SnackBarRequested += OnSnackBarRequested;
    }

    private static void OnSnackBarRequested(string message, SnackBarType type, int duration)
    {
        MainThread.BeginInvokeOnMainThread(() => Show(message, type, duration));
    }

    private static void Show(string message, SnackBarType type, int duration)
    {
        var page = ResolveTopPage();
        if (page == null) return;

        var host = GetOrCreateHost(page);
        if (host == null) return;

        host.Message = message;
        host.Type = type;
        host.Duration = duration;
        // IsVisible 置 true 触发 SnackBarView 自身的显示动画与定时隐藏
        host.IsVisible = true;
    }

    /// <summary>模态栈顶优先，其次导航栈顶，最后根页面。</summary>
    private static Page? ResolveTopPage()
    {
        var root = WindowNavigator.CurrentPage;
        if (root is NavigationPage nav)
        {
            var modal = nav.Navigation.ModalStack.LastOrDefault();
            if (modal != null) return modal;
            return nav.CurrentPage;
        }
        return root;
    }

    private static SnackBarView? GetOrCreateHost(Page page)
    {
        if (Hosts.TryGetValue(page, out var existing)) return existing;
        if (page is not ContentPage contentPage || contentPage.Content == null) return null;

        var host = CreateHostView();

        if (contentPage.Content is Grid grid)
        {
            grid.Add(host);
        }
        else if (contentPage.Content is Layout layout)
        {
            // 非 Grid 布局无法叠层，包一层 Grid 再放宿主
            var wrapper = new Grid();
            contentPage.Content = wrapper;
            wrapper.Add(layout);
            wrapper.Add(host);
        }
        else
        {
            var wrapper = new Grid();
            var content = contentPage.Content;
            contentPage.Content = wrapper;
            wrapper.Add(content);
            wrapper.Add(host);
        }

        Hosts.Add(page, host);
        return host;
    }

    private static SnackBarView CreateHostView() => new()
    {
        IsVisible = false,
        VerticalOptions = LayoutOptions.End,
        HorizontalOptions = LayoutOptions.Fill,
        Margin = new Thickness(16, 0, 16, 24),
        InputTransparent = false
    };
}
