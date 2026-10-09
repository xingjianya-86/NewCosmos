using NewCosmos.Models.NavigationData;
using NewCosmos.Services.Core;
using NewCosmos.ViewModels.Reprint;

namespace NewCosmos.Pages.Reprint;

public partial class UnifiedReprintPage : ContentPage
{
    private readonly UnifiedReprintViewModel _viewModel;
    private readonly ILoggerService _logger;

    public UnifiedReprintPage(UnifiedReprintViewModel viewModel, ILoggerService logger)
    {
        _logger = logger;
        _logger.LogPageLoad("UnifiedReprintPage", true);

        try
        {
            InitializeComponent();
            _viewModel = viewModel;
            BindingContext = _viewModel;
        }
        catch (Exception ex)
        {
            _logger.LogPageLoad("UnifiedReprintPage", false, ex.Message);
            throw;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // 注入 WebView2 打印回调：核查报告等非 Office/WPS 格式走此通道
        //（组合的 Output 实例未注入时会回退 verb=open 仅打开预览，无法真打印）
        _viewModel.Output.WebViewPrintPdfFunc = PrintPdfViaWebViewAsync;

        await _viewModel.OnAppearingAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();
        _viewModel.Output.WebViewPrintPdfFunc = null;

        // 页面已不在导航栈中（真正离开补打中心，而非 push 子页）时，
        // 清空打印上下文——FieldData/TableData 含公民 PII，不能驻留到进程退出
        var stack = Helpers.WindowNavigator.CurrentNavigation?.NavigationStack;
        if (stack == null || !stack.Contains(this))
        {
            PrintNavigationData.Clear();
        }
    }

    /// <summary>带初始域导航：定位到指定域（入口页传 DomainKey）</summary>
    public void PrepareForDomain(string domainKey)
    {
        _viewModel.PrepareForDomain(domainKey);
    }

    // ── WebView2 打印：PdfJs 渲染 PDF → window.print() 弹系统打印对话框 ──
    // 与 ArchiveOutputPage.PrintPdfViaWebViewAsync 同款（页面各自持有隐藏 WebView 实例）

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
            // async void lambda：异常若不就地捕获会直接终结进程；
            // 失败也要完成 TCS，否则外层要等满 60s 超时
            try
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
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[UnifiedReprint] 打印注入失败: {ex.Message}");
                _printTcs?.TrySetResult();
            }
        });
    }
}
