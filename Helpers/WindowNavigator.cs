namespace NewCosmos.Helpers;

/// <summary>
/// 单窗口应用的当前窗口页访问（替代 .NET 9 已弃用的 Application.Current.MainPage）
/// </summary>
public static class WindowNavigator
{
    /// <summary>
    /// 当前窗口的根页面。
    /// get：等价于旧 Application.Current.MainPage（窗口未创建时返回 null）。
    /// set：等价于旧 MainPage 赋值——写入 Windows[0].Page；窗口不存在或值为 null 时静默跳过。
    /// </summary>
    public static Page? CurrentPage
    {
        get => Application.Current?.Windows.FirstOrDefault()?.Page;
        set
        {
            var w = Application.Current?.Windows.FirstOrDefault();
            if (w != null && value != null)
            {
                w.Page = value;
            }
        }
    }

    /// <summary>当前根页面的导航栈（等价于旧 Application.Current.MainPage.Navigation）</summary>
    public static INavigation? CurrentNavigation => CurrentPage?.Navigation;

    /// <summary>
    /// 弹栈回到指定类型页面（含 Android 孪生页 Mobile{Name} 判定）：
    /// 栈内命中 → PopToAsync 保留该页及其以下页面（业务列表页得以保留，OnAppearing 自动刷新）；
    /// 未命中 → 退回 PopToRootAsync（历史行为兜底）。返回 true=回指定页，false=回了根页。
    /// 用于「归档完成后回工作流列表」：原 PopToRoot 会把列表页弹出栈，批量办理每户都要重新导航两层。
    /// </summary>
    public static async Task<bool> PopToPageAsync(Type pageType)
    {
        var navigation = CurrentNavigation;
        if (navigation == null || navigation.NavigationStack.Count <= 1)
            return false;

        // Android 孪生页规则与 PageResolver 同口径（Mobile{Name} 优先）
        var mobileType = pageType.Assembly.GetType(
            $"NewCosmos.Pages.Mobile.Mobile{pageType.Name}");

        for (var i = navigation.NavigationStack.Count - 1; i >= 0; i--)
        {
            var page = navigation.NavigationStack[i];
            if (pageType.IsInstanceOfType(page) || (mobileType != null && mobileType.IsInstanceOfType(page)))
            {
                // .NET MAUI 的 INavigation 无 PopToAsync（Xamarin 遗留 API 已移除）——逐页 Pop 到目标页成为栈顶
                for (var j = navigation.NavigationStack.Count - 1; j > i; j--)
                    await navigation.PopAsync(animated: false);
                return true;
            }
        }

        await navigation.PopToRootAsync();
        return false;
    }
}
