using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using NewCosmos.Constants;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Navigation;
using NewCosmos.Services.Platform;
using System.Reflection;

namespace NewCosmos.ViewModels.Base;

/// <summary>
/// ViewModel 基类
/// 使用 CommunityToolkit.Mvvm Source Generators
/// 支持 CancellationToken、Result Pattern、统一异常处理
/// </summary>
public abstract partial class ViewModelBase : ObservableObject, IDisposable
{
    /// <summary>
    /// 每个 VM 类型的 IRelayCommand 属性缓存。
    /// 旧实现每次 IsBusy 翻转都做一次全属性反射扫描（一次命令执行翻转两次），
    /// 对 ApplicationFormViewModel（73 个命令）意味着每次操作 2 次反射扫描 + 146 次通知。
    /// </summary>
    private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<Type, PropertyInfo[]> _commandPropertyCache = new();

    private CancellationTokenSource? _currentCts;
    private bool _disposed;

    /// <summary>
    /// 通知本 VM 全部命令重新评估 CanExecute（使用类型级缓存的属性列表）
    /// </summary>
    protected void NotifyAllCommandsCanExecuteChanged()
    {
        try
        {
            var properties = _commandPropertyCache.GetOrAdd(GetType(), t =>
                t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => typeof(IRelayCommand).IsAssignableFrom(p.PropertyType))
                    .ToArray());

            foreach (var property in properties)
            {
                if (property.GetValue(this) is IRelayCommand cmd)
                {
                    cmd.NotifyCanExecuteChanged();
                }
            }
        }
        catch (Exception ex)
        {
            global::System.Diagnostics.Debug.WriteLine($"[ERROR] NotifyAllCommandsCanExecuteChanged failed: {ex.Message}");
        }
    }

    #region 抽象属性（子类必须实现）
    /// <summary>
    /// 服务提供者（子类必须实现）
    /// </summary>
    protected abstract IServiceProvider ServiceProvider { get; }

    /// <summary>
    /// 日志服务（子类必须实现）
    /// </summary>
    protected abstract ILoggerService Logger { get; }

    #endregion

    #region Observable Properties

    /// <summary>
    /// 是否正在加载
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    partial void OnIsBusyChanged(bool value)
    {
        OnBusyStateChanged();
        NotifyAllCommandsCanExecuteChanged();
    }

    /// <summary>
    /// 忙碌状态变化钩子（默认空实现；子类可覆写以联动自身计算属性通知，如组合场景的 IsOverallBusy）
    /// </summary>
    protected virtual void OnBusyStateChanged()
    {
    }

    /// <summary>
    /// 加载提示消息
    /// </summary>
    [ObservableProperty]
    private string _loadingMessage = "加载中...";

    /// <summary>
    /// 错误消息
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = string.Empty;

    /// <summary>
    /// 页面标题
    /// </summary>
    [ObservableProperty]
    private string _title = string.Empty;

    #endregion

    #region Computed Properties

    /// <summary>
    /// 是否未处于加载状态
    /// </summary>
    public bool IsNotBusy => !IsBusy;

    /// <summary>
    /// 是否有错误消息
    /// </summary>
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    /// <summary>
    /// 当前取消令牌（只读，无副作用）。
    /// ⚠️ 历史实现的 getter 每次读取都会取消上一个令牌并新建——
    /// 两个并发的 fire-and-forget 操作各读一次就会互相取消（OrganizationTreeViewModel 曾实际触发）。
    /// 需要"开启新操作作用域"（取消旧操作+发新令牌）时，调用 CancelPreviousOperation() 后再读本属性。
    /// </summary>
    protected CancellationToken CancellationToken => _currentCts?.Token ?? CancellationToken.None;

    #endregion

    #region 执行包装方法

    /// <summary>
    /// 执行异步操作（自动处理加载状态、异常、取消）
    /// </summary>
    protected async Task<Result<T>> ExecuteAsync<T>(
        Func<Task<Result<T>>> operation,
        string loadingMessage = null)
    {
        if (IsBusy)
        {
            return Result.Failure<T>(ErrorCodes.OPERATION_IN_PROGRESS, "操作进行中，请稍后");
        }

        CancelPreviousOperation();

        try
        {
            IsBusy = true;
            LoadingMessage = loadingMessage ?? "加载中...";
            ErrorMessage = null;

            var result = await operation();

            if (result.IsFailure)
            {
                Logger.Warn($"ExecuteAsync<T> 操作失败: {result.ErrorCode} {result.Message}");
                ErrorMessage = UserFriendlyMessages.Get(result.ErrorCode!, result.Message);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            return Result.Failure<T>(ErrorCodes.CANCELLED, "操作已取消");
        }
        catch (Exception ex)
        {
            ErrorMessage = UserFriendlyMessages.Get(ErrorCodes.UNKNOWN_ERROR, ex.Message);
            return Result.FromException<T>(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 执行异步操作（无返回值）
    /// </summary>
    protected async Task<Result> ExecuteAsync(
        Func<Task<Result>> operation,
        string loadingMessage = null)
    {
        if (IsBusy)
        {
            return Result.Failure(ErrorCodes.OPERATION_IN_PROGRESS, "操作进行中，请稍后");
        }

        CancelPreviousOperation();

        try
        {
            IsBusy = true;
            LoadingMessage = loadingMessage ?? "加载中...";
            ErrorMessage = null;

            var result = await operation();

            if (result.IsFailure)
            {
                Logger.Warn($"ExecuteAsync 操作失败: {result.ErrorCode} {result.Message}");
                ErrorMessage = UserFriendlyMessages.Get(result.ErrorCode!, result.Message);
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            return Result.Failure(ErrorCodes.CANCELLED, "操作已取消");
        }
        catch (Exception ex)
        {
            ErrorMessage = UserFriendlyMessages.Get(ErrorCodes.UNKNOWN_ERROR, ex.Message);
            return Result.FromException(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 执行异步操作（带 CancellationToken 参数，无返回值）
    /// </summary>
    protected async Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        string loadingMessage = null)
    {
        if (IsBusy)
        {
            return;
        }

        CancelPreviousOperation();

        try
        {
            IsBusy = true;
            LoadingMessage = loadingMessage ?? "加载中...";
            ErrorMessage = null;

            await operation(CancellationToken);
        }
        catch (OperationCanceledException)
        {
            ErrorMessage = "操作已取消";
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"ExecuteAsync 异常: {ex.Message}");
            ErrorMessage = UserFriendlyMessages.Get(ErrorCodes.UNKNOWN_ERROR, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 取消之前的操作
    /// </summary>
    protected void CancelPreviousOperation()
    {
        if (_currentCts != null && !_currentCts.IsCancellationRequested)
        {
            _currentCts.Cancel();
            _currentCts.Dispose();
        }
        _currentCts = new CancellationTokenSource();
    }

    #endregion

    #region 页面生命周期

    /// <summary>
    /// 页面显示时调用
    /// </summary>
    public virtual async Task OnAppearingAsync()
    {
        await Task.CompletedTask;
    }

    /// <summary>
    /// 页面消失时调用
    /// </summary>
    public virtual void OnDisappearing()
    {
        CancelPreviousOperation();
    }

    #endregion

    #region 通用导航方法

    /// <summary>
    /// Android 上尝试解析手机专用页面（Pages.Mobile.Mobile{原页面名}），找不到则回退原页面。
    /// </summary>
    protected Page ResolvePage<TPage>() where TPage : Page
    {
        if (DeviceInfo.Platform == DevicePlatform.Android)
        {
            var mobileTypeName = $"NewCosmos.Pages.Mobile.Mobile{typeof(TPage).Name}";
            var mobileType = typeof(TPage).Assembly.GetType(mobileTypeName);
            if (mobileType != null)
            {
                var resolved = ServiceProvider.GetService(mobileType) as Page;
                if (resolved != null) return resolved;
            }
        }
        return ServiceProvider.GetRequiredService<TPage>();
    }

    /// <summary>
    /// 带页面标题的导航：push 前将标题写入 Page.Title，使其成为窗口标题随导航栈恢复的数据源
    /// （push/pop 后窗口标题统一按栈顶 Page.Title 恢复）。
    /// </summary>
    protected Task NavigateToPageAsync<TPage>(string pageTitle) where TPage : Page
        => NavigateToPageAsync<TPage>((Action<TPage>)(p => p.Title = pageTitle));

    /// <summary>
    /// 通用页面导航方法（支持同步配置）
    /// </summary>
    protected async Task NavigateToPageAsync<TPage>(Action<TPage>? configure = null) where TPage : Page
    {
        var pageName = typeof(TPage).Name;
        var fromPage = Title ?? "Unknown";
        
        Logger.LogNavigationStart(fromPage, pageName);
        
        try
        {
            // 1. DI 解析页面（Android 自动解析手机专用页面）
            Logger.LogDiResolution(pageName, true);
            var resolvedPage = ResolvePage<TPage>();
            ServiceProvider.GetService<IWindowTitleService>()?.Register(resolvedPage);
            
            // 2. 配置页面（Android 手机页与桌面页为兄弟类型，安全 cast 后才调用 configure）
            if (resolvedPage is TPage typedPage)
            {
                configure?.Invoke(typedPage);
            }
            
            // 3. 推送页面
            await Helpers.WindowNavigator.CurrentPage!.Navigation.PushAsync(resolvedPage);
            
            // 4. 导航成功并按新栈顶页面的 Page.Title 恢复窗口标题
            Logger.LogNavigationSuccess(fromPage, pageName);
            RestoreWindowTitleFromNavigation();
        }
        catch (Exception ex)
        {
            // 5. 导航失败
            Logger.LogNavigationFailed(fromPage, pageName, ex.Message);
            Logger.LogError(ex, $"导航到 {pageName} 失败");
            throw;
        }
    }

    /// <summary>
    /// 通用页面导航方法（支持异步配置）
    /// </summary>
    protected async Task NavigateToPageAsync<TPage>(Func<TPage, Task>? configure = null) where TPage : Page
    {
        var pageName = typeof(TPage).Name;
        var fromPage = Title ?? "Unknown";
        
        Logger.LogNavigationStart(fromPage, pageName);
        
        try
        {
            // 1. DI 解析页面（Android 自动解析手机专用页面）
            Logger.LogDiResolution(pageName, true);
            var resolvedPage = ResolvePage<TPage>();
            ServiceProvider.GetService<IWindowTitleService>()?.Register(resolvedPage);
            
            // 2. 配置页面（Android 手机页与桌面页为兄弟类型，安全 cast 后才调用 configure）
            if (resolvedPage is TPage typedPage && configure != null)
            {
                await configure(typedPage);
            }
            
            // 3. 推送页面
            await Helpers.WindowNavigator.CurrentPage!.Navigation.PushAsync(resolvedPage);
            
            // 4. 导航成功并按新栈顶页面的 Page.Title 恢复窗口标题
            Logger.LogNavigationSuccess(fromPage, pageName);
            RestoreWindowTitleFromNavigation();
        }
        catch (Exception ex)
        {
            // 5. 导航失败
            Logger.LogNavigationFailed(fromPage, pageName, ex.Message);
            Logger.LogError(ex, $"导航到 {pageName} 失败");
            throw;
        }
    }

    /// <summary>
    /// 无参页面导航（推荐入口）：转发到 configure 重载，避免无参调用在同步/异步 configure 重载间的二义性。
    /// </summary>
    protected Task NavigateToPageAsync<TPage>() where TPage : Page
        => NavigateToPageAsync<TPage>((Action<TPage>?)null);

    /// <summary>
    /// 参数化页面导航（单一传参通道）：DI 解析 → 按 <see cref="IParameterizedPage{TParam}"/> 注入参数 → 推送。
    /// Android 手机专用页（Pages.Mobile.Mobile{页面名}）与桌面页为兄弟类型，<b>同样按接口注入参数</b>，
    /// 因此手机页只需实现相同的 <see cref="IParameterizedPage{TParam}"/> 契约，业务 ViewModel 零改动。
    /// </summary>
    protected async Task NavigateToPageAsync<TPage, TParam>(TParam parameter) where TPage : Page, IParameterizedPage<TParam>
    {
        var pageName = typeof(TPage).Name;
        var fromPage = Title ?? "Unknown";

        Logger.LogNavigationStart(fromPage, pageName);

        try
        {
            // 1. DI 解析页面（Android 自动解析手机专用页面）
            Logger.LogDiResolution(pageName, true);
            var resolvedPage = ResolvePage<TPage>();
            ServiceProvider.GetService<IWindowTitleService>()?.Register(resolvedPage);

            // 2. 参数注入：桌面页与手机页统一走接口，不再受限于 resolvedPage is TPage
            if (resolvedPage is IParameterizedPage<TParam> parameterized)
            {
                await parameterized.SetParameterAsync(parameter);
            }

            // 3. 推送页面
            await Helpers.WindowNavigator.CurrentPage!.Navigation.PushAsync(resolvedPage);

            // 4. 导航成功并按新栈顶页面的 Page.Title 恢复窗口标题
            Logger.LogNavigationSuccess(fromPage, pageName);
            RestoreWindowTitleFromNavigation();
        }
        catch (Exception ex)
        {
            // 5. 导航失败
            Logger.LogNavigationFailed(fromPage, pageName, ex.Message);
            Logger.LogError(ex, $"导航到 {pageName} 失败");
            throw;
        }
    }

    /// <summary>
    /// 返回上一页（全库统一的页面级返回入口）：
    /// 导航栈守卫 → PopAsync → 按返回后的栈顶页面 Page.Title 恢复窗口标题。
    /// （历史实现在此一刀切恢复基础标题，导致从子页返回模块首页时丢失模块前缀）
    /// 生成的 <see cref="GoBackCommand"/> 供各页 XAML 直接绑定；
    /// 子类如需额外清理逻辑请 override 本方法（完成清理后调 base.GoBackAsync()）。
    /// </summary>
    [RelayCommand]
    public virtual async Task GoBackAsync()
    {
        try
        {
            var navigation = Helpers.WindowNavigator.CurrentNavigation;
            if (navigation == null || navigation.NavigationStack.Count <= 1)
            {
                Logger.Warn("返回失败：无导航栈或已在根页面");
                return;
            }

            await navigation.PopAsync();

            // 恢复窗口标题为返回后栈顶页面的标题（子页→模块首页时保留模块前缀；→主首页时为基础标题）
            var windowTitleService = ServiceProvider?.GetService<IWindowTitleService>();
            windowTitleService?.RestoreFromNavigation(navigation);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "返回上一页失败");
        }
    }

    /// <summary>
    /// Pop/PopToRoot 返回后按导航栈顶页面的 Page.Title 恢复窗口标题。
    /// 供绕过 GoBackAsync 的自动返回路径（表单保存/提交完成后自动 PopAsync/PopToRootAsync）调用，
    /// 否则窗口标题将残留被弹出的子页标题（历史 BUG：档案生成 PopToRootAsync 回主首页后标题不恢复）。
    /// </summary>
    protected void RestoreWindowTitleFromNavigation()
    {
        try
        {
            var navigation = Helpers.WindowNavigator.CurrentNavigation;
            if (navigation == null)
                return;
            var windowTitleService = ServiceProvider?.GetService<IWindowTitleService>();
            windowTitleService?.RestoreFromNavigation(navigation);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "恢复窗口标题失败");
        }
    }

    #endregion

    #region ShowFailure

    /// <summary>
    /// 统一失败提示：弹错误对话框（Result 携带消息则原样展示，否则 "{operationName}失败"），并记 Warn 日志。
    /// 收敛各 ViewModel 重复的 DisplayAlertAsync("错误", result.Message ?? "X失败", "确定") 样板。
    /// </summary>
    /// <param name="title">对话框标题，默认"错误"；历史变体标题（如"删除失败"）经此保持原文案</param>
    protected async Task ShowFailureAsync(Models.Results.Result result, string operationName, string title = "错误",
        [System.Runtime.CompilerServices.CallerMemberName] string callerName = "")
    {
        try
        {
            if (!string.IsNullOrEmpty(result.Message))
                Logger?.Warn($"{callerName} 失败: {result.Message}");

            var dialog = ServiceProvider?.GetService<IDialogService>();
            if (dialog != null)
                await dialog.DisplayAlertAsync(title, result.Message ?? $"{operationName}失败", "确定");
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, $"[{callerName}] 显示失败提示异常");
        }
    }

    #endregion

    #region 导出流程（统一反馈，杜绝“点了没反应”）

    /// <summary>
    /// 统一的导出目录选择：无论取消/失败都立即给出可见反馈。
    /// 返回非空路径表示用户已选择；返回 null 表示用户取消或选择器失败（两种情况均已有提示）。
    /// </summary>
    protected async Task<string?> PickExportFolderAsync(string title, string? initialDirectory = null,
        string cancelMessage = "已取消导出")
    {
        var dialog = ServiceProvider?.GetService<IDialogService>();
        var picker = ServiceProvider?.GetService<IFolderPickerService>();

        if (picker == null)
        {
            Logger?.Error("[导出] 文件夹选择服务未注册");
            if (dialog != null)
                await dialog.DisplayAlertAsync("导出失败", "文件夹选择服务不可用，请联系管理员", "确定");
            return null;
        }

        // 记忆上次目录：有可用记录则先询问是否复用，避免每次都重新浏览
        var remembered = initialDirectory;
        if (string.IsNullOrWhiteSpace(remembered))
            remembered = picker.GetLastDirectory();

        if (!string.IsNullOrWhiteSpace(remembered) && Directory.Exists(remembered) && dialog != null)
        {
            var reuse = await dialog.DisplayAlertAsync("选择导出目录",
                $"上次使用的目录：\n{remembered}\n\n是否继续使用该目录？", "使用上次目录", "选择其他目录");
            if (reuse)
                return remembered;
        }

        FolderPickResult result;
        try
        {
            result = await picker.PickFolderAsync(title, remembered);
        }
        catch (Exception ex)
        {
            Logger?.Error($"[导出] 选择目录异常: {ex.Message}");
            if (dialog != null)
                await dialog.DisplayAlertAsync("导出失败", $"无法打开文件夹选择器：{ex.Message}", "确定");
            return null;
        }

        if (result.Status == FolderPickStatus.Picked && !string.IsNullOrWhiteSpace(result.Path))
            return result.Path;

        if (result.Status == FolderPickStatus.Canceled)
        {
            if (dialog != null)
                await dialog.ShowSnackBarAsync(cancelMessage, NewCosmos.Components.SnackBarType.Info);
            return null;
        }

        Logger?.Error($"[导出] 选择目录失败: {result.Error}");
        if (dialog != null)
            await dialog.DisplayAlertAsync("导出失败", result.Error ?? "无法打开文件夹选择器", "确定");
        return null;
    }

    /// <summary>
    /// 统一的文件选择：无论取消/失败都立即给出可见反馈。
    /// 返回非空路径表示已选；返回 null 表示取消或失败（两种情况均已有提示）。
    /// </summary>
    protected async Task<string?> PickFileWithFeedbackAsync(string title, IReadOnlyList<string> extensions,
        string cancelMessage = "已取消选择")
    {
        var dialog = ServiceProvider?.GetService<IDialogService>();
        var picker = ServiceProvider?.GetService<IFilePickerService>();

        if (picker == null)
        {
            Logger?.Error("[选择文件] 文件选择服务未注册");
            if (dialog != null)
                await dialog.DisplayAlertAsync("选择文件失败", "文件选择服务不可用，请联系管理员", "确定");
            return null;
        }

        FilePickResult result;
        try
        {
            result = await picker.PickFileAsync(title, extensions);
        }
        catch (Exception ex)
        {
            Logger?.Error($"[选择文件] 异常: {ex.Message}");
            if (dialog != null)
                await dialog.DisplayAlertAsync("选择文件失败", $"无法打开文件选择器：{ex.Message}", "确定");
            return null;
        }

        if (result.Status == FilePickStatus.Picked && !string.IsNullOrWhiteSpace(result.Path))
            return result.Path;

        if (result.Status == FilePickStatus.Canceled)
        {
            if (dialog != null)
                await dialog.ShowSnackBarAsync(cancelMessage, NewCosmos.Components.SnackBarType.Info);
            return null;
        }

        Logger?.Error($"[选择文件] 失败: {result.Error}");
        if (dialog != null)
            await dialog.DisplayAlertAsync("选择文件失败", result.Error ?? "无法打开文件选择器", "确定");
        return null;
    }

    /// <summary>
    /// 统一导出完成提示：弹窗展示目录（及文件数量）+ SnackBar，并可选打开目录。
    /// </summary>
    protected async Task ShowExportSuccessAsync(string folder, IReadOnlyList<string?>? fileNames = null,
        string? extra = null, bool openFolder = true)
    {
        var dialog = ServiceProvider?.GetService<IDialogService>();

        var header = fileNames is { Count: > 0 }
            ? $"已导出 {fileNames.Count} 个文件"
            : "导出完成";

        var message = $"{header}\n目录：{folder}";
        if (!string.IsNullOrEmpty(extra))
            message += $"\n{extra}";

        if (dialog != null)
        {
            await dialog.DisplayAlertAsync("导出完成", message, "确定");
            await dialog.ShowSnackBarAsync($"已导出至 {folder}", NewCosmos.Components.SnackBarType.Success);
        }

        if (openFolder)
            TryOpenFolder(folder);
    }

    /// <summary>
    /// 调用资源管理器打开目录（失败静默，不影响导出结果）
    /// </summary>
    protected static void TryOpenFolder(string? folder)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                global::System.Diagnostics.Process.Start(
                    new global::System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{folder}\"")
                    {
                        UseShellExecute = true
                    });
        }
        catch
        {
            // 打开目录失败不影响导出结果
        }
    }

    #endregion

    #region SafeFireAndForget

    /// <summary>
    /// 安全执行异步操作（fire-and-forget），异常被静默捕获并记录日志
    /// </summary>
    protected async void SafeFireAndForget(Func<Task> operation, [System.Runtime.CompilerServices.CallerMemberName] string callerName = "")
    {
        try
        {
            await operation();
        }
        catch (OperationCanceledException)
        {
            // 用户主动取消，静默处理
        }
        catch (Exception ex)
        {
            Logger?.Error($"[{callerName}] Fire-and-forget failed: {ex.Message}");
        }
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// 释放资源。virtual：子类若持有定时器等额外资源可 override 清理后再调 base.Dispose()
    /// （例如 MainViewModel 的顶栏时钟定时器）。
    /// </summary>
    public virtual void Dispose()
    {
        if (!_disposed)
        {
            if (_currentCts != null)
            {
                if (!_currentCts.IsCancellationRequested)
                {
                    _currentCts.Cancel();
                }
                _currentCts.Dispose();
                _currentCts = null;
            }
            _disposed = true;
            GC.SuppressFinalize(this);
        }
    }

    #endregion
}

/// <summary>
/// 分页搜索 ViewModel 基类
/// </summary>
public abstract partial class PagedSearchViewModelBase : ViewModelBase
{
    #region Observable Properties

    /// <summary>
    /// 搜索文本
    /// </summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>
    /// 当前页码
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanGoPrevious))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private int _pageIndex = 1;

    /// <summary>
    /// 每页条数
    /// </summary>
    [ObservableProperty]
    private int _pageSize = 20;

    /// <summary>
    /// 总记录数
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalPages))]
    [NotifyPropertyChangedFor(nameof(HasResults))]
    [NotifyPropertyChangedFor(nameof(PageStatusText))]
    [NotifyPropertyChangedFor(nameof(CanGoNext))]
    private int _totalCount;

    /// <summary>
    /// 是否有搜索结果
    /// </summary>
    public bool HasResults => TotalCount > 0;

    #endregion

    #region Computed Properties

    /// <summary>
    /// 总页数
    /// </summary>
    public int TotalPages => PageSize > 0 ? Math.Max(1, (int)Math.Ceiling((double)TotalCount / PageSize)) : 1;

    /// <summary>
    /// 分页状态文本
    /// </summary>
    public string PageStatusText => TotalCount > 0
        ? $"第{PageIndex}/{TotalPages} 页，共{TotalCount} 条"
        : "暂无数据";

    /// <summary>
    /// 是否可翻到上一页（供 XAML IsEnabled 绑定）
    /// </summary>
    public bool CanGoPrevious => PageIndex > 1;

    /// <summary>
    /// 是否可翻到下一页（供 XAML IsEnabled 绑定）
    /// </summary>
    public bool CanGoNext => PageIndex < TotalPages;

    #endregion

    #region Commands

    /// <summary>
    /// 搜索命令
    /// </summary>
    [RelayCommand]
    private async Task SearchAsync()
    {
        PageIndex = 1;
        await LoadDataAsync();
    }

    /// <summary>
    /// 上一页命令
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoPreviousPage))]
    private async Task PreviousPageAsync()
    {
        if (PageIndex > 1)
        {
            PageIndex--;
            await LoadDataAsync();
        }
    }

    private bool CanGoPreviousPage() => PageIndex > 1 && IsNotBusy;

    /// <summary>
    /// 下一页命令
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoNextPage))]
    private async Task NextPageAsync()
    {
        if (PageIndex < TotalPages)
        {
            PageIndex++;
            await LoadDataAsync();
        }
    }

    private bool CanGoNextPage() => PageIndex < TotalPages && IsNotBusy;

    #endregion

    #region 抽象方法

    /// <summary>
    /// 加载数据（由子类实现）
    /// </summary>
    protected abstract Task LoadDataAsync();

    #endregion

    #region 辅助方法

    /// <summary>
    /// 重置搜索条件
    /// </summary>
    public virtual void ResetSearch()
    {
        SearchText = string.Empty;
        PageIndex = 1;
        TotalCount = 0;
    }

    /// <summary>
    /// 计算偏移量
    /// </summary>
    protected int CalculateOffset() => (PageIndex - 1) * PageSize;

    /// <summary>
    /// 分页填充半段（<see cref="PagedSearchViewModelBase.LoadPageAsync{T}(Func{CancellationToken, Task{Result{PagedResult{T}}}}, IList{T}, Action{PagedResult{T}}?, string)"/> 的分解形态）：清空→填充→同步 TotalCount。
    /// 供带额外并发守卫（如 Tab 版本校验）、无法整体套用模板的子类复用填充段。
    /// </summary>
    protected void FillPagedPage<T>(IList<T> target, Models.Results.PagedResult<T> page)
    {
        target.Clear();
        foreach (var item in page.Items)
            target.Add(item);
        TotalCount = page.TotalCount;
    }

    /// <summary>
    /// LoadDataAsync 推荐实现（模板方法）：执行分页查询，先清空后填充目标集合，并同步 TotalCount。
    /// 成功时回调 onLoaded 供子类做附带联动（如徽章/统计文本）；失败显式提示 + Warn 日志（禁止静默空列表）。
    /// </summary>
    protected async Task LoadPageAsync<T>(
        Func<CancellationToken, Task<Models.Results.Result<Models.Results.PagedResult<T>>>> fetch,
        IList<T> target,
        Action<Models.Results.PagedResult<T>>? onLoaded = null,
        string loadingHint = "正在加载...")
    {
        await ExecuteAsync(async ct =>
        {
            var result = await fetch(ct);
            if (result.IsSuccess && result.Value != null)
            {
                target.Clear();
                foreach (var item in result.Value.Items)
                    target.Add(item);
                TotalCount = result.Value.TotalCount;
                onLoaded?.Invoke(result.Value);
            }
            else
            {
                Logger?.Warn($"分页列表加载失败: {result.Message}");
                ErrorMessage = result.Message ?? "列表加载失败，请稍后重试";
                var dialog = ServiceProvider?.GetService<IDialogService>();
                if (dialog != null)
                    await dialog.DisplayAlertAsync("加载失败", result.Message ?? "列表加载失败，请稍后重试", "确定");
            }
        }, loadingHint);
    }

    /// <summary>
    /// 分页加载模板（async 回调版）：支持在 onLoadedAsync 中执行异步操作（如数据权限标注）。
    /// 失败显式提示 + Warn 日志（禁止静默空列表）。
    /// </summary>
    protected async Task LoadPageAsync<T>(
        Func<CancellationToken, Task<Models.Results.Result<Models.Results.PagedResult<T>>>> fetch,
        IList<T> target,
        Func<Models.Results.PagedResult<T>, Task>? onLoadedAsync = null,
        string loadingHint = "正在加载...")
    {
        await ExecuteAsync(async ct =>
        {
            var result = await fetch(ct);
            if (result.IsSuccess && result.Value != null)
            {
                target.Clear();
                foreach (var item in result.Value.Items)
                    target.Add(item);
                TotalCount = result.Value.TotalCount;
                if (onLoadedAsync != null)
                    await onLoadedAsync(result.Value);
            }
            else
            {
                Logger?.Warn($"分页列表加载失败: {result.Message}");
                ErrorMessage = result.Message ?? "列表加载失败，请稍后重试";
                var dialog = ServiceProvider?.GetService<IDialogService>();
                if (dialog != null)
                    await dialog.DisplayAlertAsync("加载失败", result.Message ?? "列表加载失败，请稍后重试", "确定");
            }
        }, loadingHint);
    }

    #endregion
}

/// <summary>
/// 表单 ViewModel 基类
/// </summary>
public abstract partial class FormViewModelBase : ViewModelBase
{
    #region Observable Properties

    /// <summary>
    /// 操作模式（新建/编辑）
    /// </summary>
    [ObservableProperty]
    private FormOperationMode _operationMode = FormOperationMode.Create;

    /// <summary>
    /// 当前步骤
    /// </summary>
    [ObservableProperty]
    private int _currentStep = 1;

    /// <summary>
    /// 步骤变化时的分部方法实现
    /// </summary>
    partial void OnCurrentStepChanged(int value)
    {
        OnStepChanged();
    }

    /// <summary>
    /// 步骤变化时调用的虚拟方法，子类可重写以通知额外的属性变化
    /// </summary>
    protected virtual void OnStepChanged()
    {
        OnPropertyChanged(nameof(IsFirstStep));
        OnPropertyChanged(nameof(IsNotFirstStep));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(IsNotLastStep));

        // 通知导航命令重新评估 CanExecute（类型级缓存，不再每次反射扫描）
        NotifyAllCommandsCanExecuteChanged();
    }

    /// <summary>
    /// 总步骤数
    /// </summary>
    [ObservableProperty]
    private int _totalSteps = 1;

    #endregion

    #region Computed Properties

    /// <summary>
    /// 是否为编辑模式
    /// </summary>
    public bool IsEditMode => OperationMode == FormOperationMode.Edit;

    /// <summary>
    /// 是否为第一步
    /// </summary>
    public bool IsFirstStep => CurrentStep == 1;

    /// <summary>
    /// 是否为最后一步
    /// </summary>
    public bool IsLastStep => CurrentStep == TotalSteps;

    /// <summary>
    /// 是否不是第一步（控制"上一步"按钮可见性）
    /// </summary>
    public bool IsNotFirstStep => !IsFirstStep;

    /// <summary>
    /// 是否不是最后一步（控制"下一步"按钮可见性）
    /// </summary>
    public bool IsNotLastStep => !IsLastStep;

    #endregion

    #region Commands

    /// <summary>
    /// 下一步命令
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoNextStep))]
    private async Task NextStepAsync()
    {
        if (CurrentStep < TotalSteps)
        {
            var validateResult = await ValidateCurrentStepAsync();
            if (validateResult.IsSuccess)
            {
                CurrentStep++;
            }
            else
            {
                var dialogService = ServiceProvider.GetRequiredService<IDialogService>();
                await dialogService.DisplayAlertAsync("验证失败", validateResult.Message ?? "请填写必填项", "确定");
            }
        }
    }

    // 注意：CanExecute 在每次命令状态刷新时被 XAML 反复求值——此处绝不能写日志/做 I/O
    private bool CanGoNextStep() => !IsLastStep && IsNotBusy;

    /// <summary>
    /// 上一步命令
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGoPreviousStep))]
    private Task PreviousStepAsync()
    {
        if (CurrentStep > 1)
        {
            CurrentStep--;
        }
        return Task.CompletedTask;
    }

    private bool CanGoPreviousStep() => !IsFirstStep && IsNotBusy;

    /// <summary>
    /// 保存命令
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        var validateResult = await ValidateAllStepsAsync();
        if (validateResult.IsSuccess)
        {
            var saveResult = await ExecuteSaveAsync();
            if (!saveResult.IsSuccess)
            {
                Logger.Error("操作失败");
                var dialogService = ServiceProvider.GetService<IDialogService>();
                if (dialogService != null)
                    await dialogService.DisplayAlertAsync("保存失败", saveResult.Message ?? "保存过程中出现错误", "确定");
            }
        }
        else
        {
            Logger.Error("操作失败");
            var dialogService = ServiceProvider.GetService<IDialogService>();
            if (dialogService != null)
                await dialogService.DisplayAlertAsync("验证失败", validateResult.Message ?? "请填写必填项", "确定");
        }
    }

    private bool CanSave() => IsNotBusy;

    /// <summary>
    /// 取消命令
    /// </summary>
    [RelayCommand]
    private Task CancelAsync()
    {
        return OnCancelAsync();
    }

    #endregion

    #region 抽象方法

    /// <summary>
    /// 验证当前步骤
    /// </summary>
    protected abstract Task<Result> ValidateCurrentStepAsync();

    /// <summary>
    /// 验证所有步骤
    /// </summary>
    protected abstract Task<Result> ValidateAllStepsAsync();

    /// <summary>
    /// 执行保存
    /// </summary>
    protected abstract Task<Result> ExecuteSaveAsync();

    /// <summary>
    /// 取消操作
    /// </summary>
    protected abstract Task OnCancelAsync();

    #endregion
}

/// <summary>
/// 表单操作模式
/// </summary>
public enum FormOperationMode
{
    Create,
    Edit,
    View,

    /// <summary>导入库建档后的数据补全模式（复用低收入人口认定申请表单，保存保持原状态）</summary>
    Completion,

    /// <summary>经济状况复核模式（锁定户主Step1与成员Step2，放开经济Step3/调查Step4/认定Step5）</summary>
    Review,

    /// <summary>家庭信息修正模式（可编辑全部步骤，但户主姓名/身份证锁定；仅限本月本周期档案）</summary>
    ReviewWithFamilyCorrection,

    /// <summary>编辑家庭信息模式（可编辑全部步骤，但户主姓名/身份证锁定；无周期限制）</summary>
    EditFamilyInfo,

    /// <summary>家庭成员变更模式（锁定户主Step1，放开成员Step2增删，经济Step3可调、重新认定，停旧建新）</summary>
    MemberChange
}