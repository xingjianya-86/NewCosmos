namespace NewCosmos.Services.Core;

/// <summary>
/// 加载进度弹窗运行器：Service 层委托 UI 层弹出进度弹窗并执行长操作（签名不含任何 UI 类型）。
/// 实现在 UI 层（LoadingProgressRunner 包装 Pages.Shared.LoadingProgressDialog）。
/// </summary>
public interface ILoadingProgressRunner
{
    /// <summary>
    /// 显示进度弹窗并执行操作；返回 true 表示操作完成，false 表示用户中途关闭。
    /// </summary>
    Task<bool> RunAsync(Func<CancellationToken, Task> operation);
}
