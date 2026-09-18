using NewCosmos.ViewModels.Shared;

namespace NewCosmos.Pages.Shared;

public partial class LoadingProgressDialog : ContentPage
{
    private readonly LoadingProgressDialogViewModel _viewModel;

    public LoadingProgressDialog(LoadingProgressDialogViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    /// <summary>
    /// 显示进度弹窗并执行操作
    /// </summary>
    public static async Task<bool> ShowAsync(IServiceProvider serviceProvider, Func<CancellationToken, Task> operation)
    {
        var viewModel = serviceProvider.GetRequiredService<LoadingProgressDialogViewModel>();
        var dialog = new LoadingProgressDialog(viewModel);

        // 显示弹窗
        var tcs = new TaskCompletionSource<bool>();
        viewModel.Completed += (result) => tcs.TrySetResult(result);

        // 在导航栈顶部显示（主页面尚未创建时无法弹窗，原实现此处会抛 NRE，这里给出明确异常）
        var navigation = Helpers.WindowNavigator.CurrentNavigation
            ?? throw new InvalidOperationException("应用主页面尚未初始化，无法显示进度弹窗");
        await navigation.PushModalAsync(dialog, false);

        // 执行操作
        await viewModel.StartLoadingAsync(operation);

        // 等待用户关闭弹窗
        var result = await tcs.Task;

        // 关闭弹窗
        if (navigation.ModalStack.Count > 0)
        {
            await navigation.PopModalAsync(false);
        }

        return result;
    }
}
