namespace NewCosmos.Navigation;

/// <summary>
/// 页面导航键（<see cref="Services.Core.INavigationService"/> 的页面标识）。
/// 只含字符串常量，无 UI 类型依赖；新增页面在此登记并在 <see cref="NavigationService"/> 注册表补充类型映射。
/// </summary>
public static class NavigationKeys
{
    /// <summary>主首页（Android 自动回退 MobileMainPage）</summary>
    public const string Main = "main";

    /// <summary>登录页（登出/会话失效后设置导航根）</summary>
    public const string Login = "login";

    /// <summary>档案输出页（文书直出）</summary>
    public const string ArchiveOutput = "archive-output";
}
