namespace NewCosmos.Components;

/// <summary>
/// 签名采集弹窗：标题提示 + 签名板 + 重签/取消/确认。
/// 用法：直接 <see cref="ShowAsync"/> 以模态方式弹出并等待签名 PNG；或作为 ContentView 内嵌到页面自行订阅事件。
/// </summary>
public partial class SignaturePopupView : ContentView
{
    /// <summary>确认签名回调，参数为 PNG 字节（用户点击「确认签名」）。</summary>
    public event Action<byte[]?>? Confirmed;

    /// <summary>用户点击「取消」。</summary>
    public event Action? Cancelled;

    public SignaturePopupView()
    {
        InitializeComponent();
        CaptureView.SignatureChanged += OnSignatureChanged;
    }

    /// <summary>设置提示文案（如「请 张三 本人签名」）。</summary>
    public void SetPrompt(string prompt)
    {
        if (PromptLabel != null)
            PromptLabel.Text = string.IsNullOrWhiteSpace(prompt) ? "请本人签名" : prompt;
    }

    /// <summary>设置临摹底图文字（签名者姓名，淡灰大字供描红；仅屏幕显示，不进导出签名）。</summary>
    public void SetTraceText(string traceText)
    {
        CaptureView.TraceText = traceText ?? string.Empty;
    }

    private void OnSignatureChanged()
    {
        if (ConfirmButton != null)
            ConfirmButton.IsEnabled = CaptureView.HasSignature;
    }

    private void OnClearClicked(object? sender, EventArgs e)
    {
        CaptureView.Clear();
        if (ConfirmButton != null)
            ConfirmButton.IsEnabled = false;
    }

    private void OnCancelClicked(object? sender, EventArgs e) => Cancelled?.Invoke();

    private void OnConfirmClicked(object? sender, EventArgs e)
        => Confirmed?.Invoke(CaptureView.GetSignaturePng());

    /// <summary>
    /// 以模态方式弹出签名弹窗，等待用户签名或取消。
    /// </summary>
    /// <param name="prompt">签名框上方提示文案</param>
    /// <param name="traceText">临摹底图文字（签名者姓名），空则不显示底图</param>
    /// <returns>签名 PNG 字节；用户取消或未签名返回 null</returns>
    public static async Task<byte[]?> ShowAsync(string prompt = "请本人签名", string? traceText = null)
    {
        var hostPage = Helpers.WindowNavigator.CurrentPage;
        var navigation = hostPage?.Navigation;
        if (navigation == null)
            return null;

        var tcs = new TaskCompletionSource<byte[]?>();
        var popup = new SignaturePopupView();
        popup.SetPrompt(prompt);
        popup.SetTraceText(traceText ?? string.Empty);

        var page = new ContentPage
        {
            BackgroundColor = Color.FromArgb("#80000000"),
            Content = new Grid
            {
                Padding = new Thickness(24),
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center,
                Children = { popup }
            }
        };

        void Cleanup()
        {
            popup.Confirmed -= OnConfirmed;
            popup.Cancelled -= OnCancelled;
            _ = navigation.PopModalAsync(false);
        }

        void OnConfirmed(byte[]? png)
        {
            tcs.TrySetResult(png);
            Cleanup();
        }

        void OnCancelled()
        {
            tcs.TrySetResult(null);
            Cleanup();
        }

        popup.Confirmed += OnConfirmed;
        popup.Cancelled += OnCancelled;

        await navigation.PushModalAsync(page);
        return await tcs.Task;
    }
}
