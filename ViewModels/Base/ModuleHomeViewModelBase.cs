using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Services.Core;
using NewCosmos.Navigation;

namespace NewCosmos.ViewModels.Base;

/// <summary>
/// 业务域模块首页 ViewModel 基类。
/// 统一承载：OnAppearing 一次性初始化守卫、子功能导航辅助（权限门控 → PushAsync → 窗口标题）、
/// 无权限提示与统计加载失败降级。权限与统计数据由子类 LoadDataAsync 实现。
/// </summary>
public abstract class ModuleHomeViewModelBase : ViewModelBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILoggerService _logger;
    private readonly IWindowTitleService _windowTitleService;
    private bool _initialized;
    private bool _permissionsLoaded;

    /// <summary>统计加载失败时的占位显示（降级不阻塞页面）</summary>
    protected const string StatNA = "—";

    protected ModuleHomeViewModelBase(
        IServiceProvider serviceProvider,
        IDialogService dialogService,
        ILoggerService logger,
        IWindowTitleService windowTitleService)
    {
        _serviceProvider = serviceProvider;
        DialogService = dialogService;
        _logger = logger;
        _windowTitleService = windowTitleService;
    }

    protected override IServiceProvider ServiceProvider => _serviceProvider;

    protected override ILoggerService Logger => _logger;

    protected IDialogService DialogService { get; }

    /// <summary>
    /// 安全执行统计加载：成功则应用结果；失败仅降级记日志（对应统计项显示 "—"），不阻塞页面。
    /// </summary>
    protected async Task SafeLoadAsync<T>(Func<Task<Models.Results.Result<T>>> call, Action<T> apply, string failLog)
    {
        try
        {
            var result = await call();
            if (result.IsSuccess && result.Value != null)
                apply(result.Value);
            else
                Logger.Warn($"{failLog}: {result.Message}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, failLog);
        }
    }

    /// <summary>
    /// 页面 OnAppearing 调用。权限未成功加载前不锁定，允许下次进入重试；
    /// 成功后（含统计失败仅降级）只执行一次。
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_initialized && _permissionsLoaded)
            return;

        await LoadDataAsync();

        // 权限成功标记由子类在 LoadDataAsync 内调用 MarkPermissionsLoaded()
        if (_permissionsLoaded)
            _initialized = true;
    }

    /// <summary>子类在权限成功应用后调用，允许基类锁定初始化（失败则下次 OnAppearing 重试）。</summary>
    protected void MarkPermissionsLoaded() => _permissionsLoaded = true;

    /// <summary>
    /// 子类实现：批量加载权限与模块统计数据。
    /// 权限成功时须调用 <see cref="MarkPermissionsLoaded"/>；失败不得清零已有 CanAccess*。
    /// </summary>
    protected abstract Task LoadDataAsync();

    /// <summary>
    /// 导航到子功能页：权限门控 → push 前将窗口标题写入 Page.Title → PushAsync。
    /// 窗口标题由 WindowTitleService.Attach 的 push/pop 事件按栈顶 Page.Title 自动驱动。
    /// </summary>
    /// <typeparam name="TPage">目标页类型</typeparam>
    /// <param name="granted">当前用户是否持有该功能权限</param>
    /// <param name="windowTitle">导航后的窗口标题（写入目标页 Page.Title）</param>
    /// <param name="featureName">功能名（日志与错误提示用）</param>
    /// <param name="configure">页面配置钩子（如统一补打页定向初始域）</param>
    protected async Task NavigateToFeatureAsync<TPage>(bool granted, string windowTitle, string featureName, Action<TPage>? configure = null) where TPage : Page
    {
        if (!granted)
        {
            await ShowNoPermissionAlertAsync();
            return;
        }

        try
        {
            var resolvedPage = ResolvePage<TPage>();
            // Android 手机页与桌面页为兄弟类型，安全 cast 后再调用 configure；
            // 标题与注册统一用基类 Page，避免兄弟类型转换异常
            resolvedPage.Title = windowTitle;
            _windowTitleService.Register(resolvedPage);
            // configure 仅在类型匹配时调用
            if (resolvedPage is TPage page && configure != null)
            {
                configure(page);
            }
            await Helpers.WindowNavigator.CurrentPage!.Navigation.PushAsync(resolvedPage);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"打开{featureName}页面失败");
            await DialogService.DisplayAlertAsync("错误", $"打开{featureName}页面失败: {ex.Message}", "确定");
        }
    }

    protected async Task ShowNoPermissionAlertAsync()
    {
        try
        {
            await DialogService.DisplayAlertAsync("权限不足", "您没有此功能的访问权限", "确定");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "显示权限不足提示失败");
        }
    }
}
