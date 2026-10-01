namespace NewCosmos.Navigation;

/// <summary>
/// 加载进度弹窗运行器实现（UI 层基础设施）：包装 Pages.Shared.LoadingProgressDialog。
/// </summary>
public class LoadingProgressRunner : Services.Core.ILoadingProgressRunner
{
    private readonly IServiceProvider _serviceProvider;

    public LoadingProgressRunner(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public Task<bool> RunAsync(Func<CancellationToken, Task> operation)
        => Pages.Shared.LoadingProgressDialog.ShowAsync(_serviceProvider, operation);
}
