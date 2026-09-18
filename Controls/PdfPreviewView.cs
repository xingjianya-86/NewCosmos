namespace NewCosmos.Controls;

/// <summary>
/// PDF 预览控件：WebView2 + PDF.js，C# 读取 PDF 后 base64 注入，避免 file:// CORS 问题
/// </summary>
public class PdfPreviewView : ContentView
{
    private readonly WebView _webView;
    private bool _viewerLoaded;
    private string? _pendingBase64;
    private int _loadSequence;

    public static readonly BindableProperty PdfFilePathProperty =
        BindableProperty.Create(nameof(PdfFilePath), typeof(string), typeof(PdfPreviewView),
            default(string), propertyChanged: OnPdfFilePathChanged);

    public string PdfFilePath
    {
        get => (string)GetValue(PdfFilePathProperty);
        set => SetValue(PdfFilePathProperty, value);
    }

    public PdfPreviewView()
    {
        _webView = new WebView
        {
            VerticalOptions = LayoutOptions.Fill,
            HorizontalOptions = LayoutOptions.Fill
        };
        _webView.Navigated += OnWebViewNavigated;
        Content = _webView;
    }

    private static void OnPdfFilePathChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is PdfPreviewView view && newValue is string filePath)
        {
            view.LoadPdf(filePath);
        }
    }

    private void LoadPdf(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return;

        var appDir = AppDomain.CurrentDomain.BaseDirectory;
        var viewerPath = Path.Combine(appDir, "Resources", "PdfJs", "viewer.html");
        if (!File.Exists(viewerPath)) return;

        // 序号防乱序：属性快速变化时只应用最新一次的结果
        var sequence = ++_loadSequence;

        // 文件读取 + base64 转换放到后台线程，避免大 PDF 阻塞 UI 线程
        _ = Task.Run(() =>
        {
            string base64;
            try
            {
                var pdfBytes = File.ReadAllBytes(filePath);
                base64 = Convert.ToBase64String(pdfBytes);
            }
            catch
            {
                // 读取失败（文件被占用/已删除等）时静默放弃本次加载
                return;
            }

            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (sequence != _loadSequence) return;

                _pendingBase64 = base64;

                if (_viewerLoaded)
                {
                    InjectPdfData();
                }
                else
                {
                    _webView.Source = new UrlWebViewSource { Url = $"file:///{viewerPath.Replace("\\", "/")}" };
                }
            });
        });
    }

    private async void OnWebViewNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (e.Result == WebNavigationResult.Success && _pendingBase64 != null)
        {
            await Task.Delay(300);
            _viewerLoaded = true;
            InjectPdfData();
        }
    }

    private async void InjectPdfData()
    {
        if (_pendingBase64 == null) return;
        var b64 = _pendingBase64;
        _pendingBase64 = null;

        await _webView.EvaluateJavaScriptAsync(
            $"window._loadPdfFromBase64('{b64}')");
    }
}
