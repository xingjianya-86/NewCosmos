using Microsoft.Extensions.DependencyInjection;

namespace NewCosmos.Navigation;

/// <summary>
/// 页面解析单点（Android 手机页回退规则唯一实现）：
/// <c>Pages.X.AbcPage</c> → 优先解析兄弟类型 <c>NewCosmos.Pages.Mobile.MobileAbcPage</c>，找不到回退桌面页。
/// 此前该规则在 ViewModelBase.ResolvePage / NavigationService.ResolvePage / App.SwitchToMainPage
/// 各写一份（行为漂移风险），统一收敛到本类。
/// </summary>
public static class PageResolver
{
    /// <summary>按类型解析页面实例（Android 优先手机页）。provider 不可为 null。</summary>
    public static Microsoft.Maui.Controls.Page Resolve(IServiceProvider provider, Type pageType)
    {
        if (Microsoft.Maui.Devices.DeviceInfo.Platform == Microsoft.Maui.Devices.DevicePlatform.Android)
        {
            var mobileTypeName = $"NewCosmos.Pages.Mobile.Mobile{pageType.Name}";
            var mobileType = pageType.Assembly.GetType(mobileTypeName);
            if (mobileType != null)
            {
                var resolved = provider.GetService(mobileType) as Microsoft.Maui.Controls.Page;
                if (resolved != null) return resolved;
            }
        }
        return (Microsoft.Maui.Controls.Page)provider.GetRequiredService(pageType);
    }

    /// <summary>泛型便捷入口。</summary>
    public static Microsoft.Maui.Controls.Page Resolve<TPage>(IServiceProvider provider)
        where TPage : Microsoft.Maui.Controls.Page
        => Resolve(provider, typeof(TPage));
}
