using NewCosmos.Models.NavigationData;
using NewCosmos.ViewModels.ArchiveManagement;

namespace NewCosmos.Pages.ArchiveManagement;

public partial class ArchiveOutputPage : ContentPage
{
    private readonly ArchiveOutputViewModel _viewModel;

    public ArchiveOutputPage(ArchiveOutputViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // 订阅放在 OnAppearing：push→pop 返回本页时 OnDisappearing 已解绑，需重新订阅
        // 先解绑再订阅保证幂等，不会重复订阅
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        // 注入 WebView2 打印回调：核查报告等非 Office/WPS 格式走此通道
        _viewModel.WebViewPrintPdfFunc = PrintPdfViaWebViewAsync;

        await _viewModel.InitializeAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel.WebViewPrintPdfFunc = null;

        // 页面已不在导航栈中（真正离开输出流程，而非 push 子页）时，
        // 清空打印上下文——FieldData/TableData 含公民 PII，不能驻留到进程退出
        var stack = Helpers.WindowNavigator.CurrentNavigation?.NavigationStack;
        if (stack == null || !stack.Contains(this))
        {
            PrintNavigationData.Clear();
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ArchiveOutputViewModel.PdfFilePath)
            && !string.IsNullOrEmpty(_viewModel.PdfFilePath)
            && File.Exists(_viewModel.PdfFilePath))
        {
            // 直接用 WebView 加载 PDF 文件（Windows 自带 PDF 阅读器）
            var filePath = _viewModel.PdfFilePath.Replace("\\", "/");
            var uri = $"file:///{filePath}";
            Dispatcher.Dispatch(() =>
            {
                PdfWebView.Source = null;
                PdfWebView.Source = new UrlWebViewSource { Url = uri };
            });
        }
    }

    // ── WebView2 打印：PdfJs 渲染 PDF → window.print() 弹系统打印对话框 ──

    private TaskCompletionSource? _printTcs;
    private string? _pendingPrintBase64;
    private bool _printStep; // false=等待 viewer.html 导航完成，true=等待 PDF 注入后打印

    /// <summary>
    /// 通过 MAUI WebView（Windows 底层 WebView2）+ PdfJs 渲染 PDF 并打印。
    /// 不依赖系统默认 PDF 关联（解决搜狗 PDF 无 print verb 问题）。
    /// </summary>
    private async Task PrintPdfViaWebViewAsync(string pdfPath)
    {
        var pdfBytes = await File.ReadAllBytesAsync(pdfPath);
        _pendingPrintBase64 = Convert.ToBase64String(pdfBytes);

        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var viewerPath = Path.Combine(appDir, "Resources", "PdfJs", "viewer.html");
        if (!File.Exists(viewerPath))
        {
            _pendingPrintBase64 = null;
            return;
        }

        _printTcs = new TaskCompletionSource();
        _printStep = false;

        // 订阅 Navigated 事件处理打印流程
        PdfWebView.Navigated += OnPrintWebViewNavigated;

        // 先导航到 about:blank 确保后续导航一定触发 Navigated
        await Dispatcher.DispatchAsync(() =>
        {
            PdfWebView.Source = new UrlWebViewSource { Url = "about:blank" };
        });
        await Task.Delay(200);

        // 导航到 PdfJs viewer.html
        var viewerUrl = $"file:///{viewerPath.Replace("\\", "/")}";
        await Dispatcher.DispatchAsync(() =>
        {
            PdfWebView.Source = new UrlWebViewSource { Url = viewerUrl };
        });

        // 等待打印完成（带超时保护）
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        cts.Token.Register(() => _printTcs.TrySetResult());
        await _printTcs.Task;

        // 清理
        PdfWebView.Navigated -= OnPrintWebViewNavigated;
        _pendingPrintBase64 = null;
    }

    private void OnPrintWebViewNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (e.Result != WebNavigationResult.Success || _printTcs == null) return;

        Dispatcher.Dispatch(async () =>
        {
            if (!_printStep && _pendingPrintBase64 != null)
            {
                // 第一步：viewer.html 导航完成 → 注入 PDF base64 数据
                _printStep = true;
                var b64 = _pendingPrintBase64;
                await PdfWebView.EvaluateJavaScriptAsync($"window._loadPdfFromBase64('{b64}')");

                // 等待 PdfJs 渲染完成（canvas 有内容）
                await Task.Delay(2000);

                // 第二步：触发系统打印对话框（window.print() 在 WebView2 中阻塞至对话框关闭）
                await PdfWebView.EvaluateJavaScriptAsync("window.print()");

                _printTcs?.TrySetResult();
            }
        });
    }
}
