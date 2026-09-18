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
}
