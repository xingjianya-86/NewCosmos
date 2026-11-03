using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Media;
using NewCosmos.Constants;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Platform;
using NewCosmos.ViewModels.Base;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 证件扫描页：原生 Camera2 预览 + 实时取景框（仅身份证类别显示），拍照/选图后按类别识别
/// → 识别询问 → 跳转分配页（用户定向回填）→ 回传实际回填条数。
/// 识别结果经 <see cref="OcrScanParameter.OnCompleted"/> 回传给调用方表单。
/// </summary>
public partial class MobileOcrScanPage : ContentPage, IParameterizedPage<OcrScanParameter>
{
    private const double CardAspectRatio = 85.6 / 54.0;

    private readonly IOcrRecognizeClient _ocrClient;
    private readonly ILoggerService _logger;
    private readonly IDialogService _dialogService;
    private readonly IServiceProvider _serviceProvider;

    /// <summary>可识别类别（PERSON_PHOTO 纯照片跳过；显示序号 ↔ 类别代码映射表）</summary>
    private static readonly (string Code, string Name)[] RecognizableCategories =
        PhotoCategoryConstants.All.Where(c => OcrFieldKeys.GetFields(c.Code) is not null).ToArray();

    private IOcrAssignContext? _assignContext;
    private Action<int>? _onCompleted;
    private bool _capturing;
    private bool _completed;
    private CancellationTokenSource? _scanCts;

    /// <summary>当前类别（默认身份证；取景框仅身份证类别显示）</summary>
    private string _categoryCode = PhotoCategoryConstants.ID_CARD;

    public MobileOcrScanPage(IOcrRecognizeClient ocrClient, ILoggerService logger,
        IDialogService dialogService, IServiceProvider serviceProvider)
    {
        InitializeComponent();
        _ocrClient = ocrClient;
        _logger = logger;
        _dialogService = dialogService;
        _serviceProvider = serviceProvider;

        foreach (var (code, name) in RecognizableCategories)
        {
            CategoryPicker.Items.Add(name);
            if (code == _categoryCode)
                CategoryPicker.SelectedIndex = CategoryPicker.Items.Count - 1;
        }
        if (CategoryPicker.SelectedIndex < 0 && CategoryPicker.Items.Count > 0)
            CategoryPicker.SelectedIndex = 0;
    }

    public Task SetParameterAsync(OcrScanParameter parameter)
    {
        _assignContext = parameter.Context;
        _onCompleted = parameter.OnCompleted;
        return Task.CompletedTask;
    }

    private void OnCategoryChanged(object? sender, EventArgs e)
    {
        if (CategoryPicker.SelectedIndex < 0 || CategoryPicker.SelectedIndex >= RecognizableCategories.Length)
            return;
        _categoryCode = RecognizableCategories[CategoryPicker.SelectedIndex].Code;
        // 取景框仅身份证类别显示（85.6:54 比例框对其他类别无意义）
        GuideFrame.IsVisible = _categoryCode == PhotoCategoryConstants.ID_CARD;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _scanCts = new CancellationTokenSource();

        var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
        if (status != PermissionStatus.Granted)
            status = await Permissions.RequestAsync<Permissions.Camera>();

        if (status != PermissionStatus.Granted)
        {
            await _dialogService.DisplayAlertAsync("提示", "未授予相机权限，无法扫描证件。可改用「从相册选择」", "确定");
            return;
        }

        try
        {
            await Preview.StartAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "启动相机预览失败");
            await _dialogService.DisplayAlertAsync("提示", $"启动相机失败：{ex.Message}。可改用「从相册选择」", "确定");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // 离页取消在途识别（识别继续跑完还可能在已离开的页面上弹错窗）
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = null;
        try
        {
            Preview.Stop();
        }
        catch (Exception ex)
        {
            _logger.Warn($"停止相机预览异常：{ex.Message}");
        }
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        if (width <= 0) return;

        // 竖向取景框：高 = 宽 × (85.6/54)，即身份证长边竖直
        var frameWidth = Math.Min(width * 0.56, 320);
        GuideFrame.WidthRequest = frameWidth;
        GuideFrame.HeightRequest = frameWidth * CardAspectRatio;
    }

    private async void OnCaptureClicked(object? sender, EventArgs e)
    {
        if (_capturing || _completed) return;

        _capturing = true;
        ShowBusy(true, "正在拍照...");
        try
        {
            var bytes = await Preview.CaptureAsync();
            if (bytes.Length == 0)
            {
                ShowBusy(false);
                await _dialogService.DisplayAlertAsync("提示", "拍照失败，请重试", "确定");
                return;
            }

            await RecognizeAndAssignAsync(bytes);
        }
        catch (OperationCanceledException)
        {
            // 页面已离开触发的取消：静默，不再弹错误框
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "证件扫描失败");
            ShowBusy(false);
            await _dialogService.DisplayAlertAsync("错误", $"扫描失败：{ex.Message}", "确定");
        }
        finally
        {
            _capturing = false;
        }
    }

    private async void OnPickFromGalleryClicked(object? sender, EventArgs e)
    {
        if (_capturing || _completed) return;

        try
        {
            var photos = await MediaPicker.Default.PickPhotosAsync();
            var photo = photos?.FirstOrDefault();
            if (photo is null) return;

            _capturing = true;
            byte[] bytes;
            await using (var stream = await photo.OpenReadAsync())
            using (var ms = new MemoryStream())
            {
                await stream.CopyToAsync(ms);
                bytes = ms.ToArray();
            }

            await RecognizeAndAssignAsync(bytes);
        }
        catch (OperationCanceledException)
        {
            // 页面已离开触发的取消：静默
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从相册识别证件失败");
            ShowBusy(false);
            await _dialogService.DisplayAlertAsync("错误", $"识别失败：{ex.Message}", "确定");
        }
        finally
        {
            _capturing = false;
        }
    }

    /// <summary>识别询问 → 识别（界面等待）→ 跳分配页</summary>
    private async Task RecognizeAndAssignAsync(byte[] bytes)
    {
        var categoryName = PhotoCategoryConstants.GetDisplayName(_categoryCode);

        // 识别询问（与档案照片页一致的完整流程：模型名 + 预计耗时 + 内网声明）
        ShowBusy(false);
        var start = await _dialogService.DisplayAlertAsync(OcrConstants.PromptTitle,
            $"已拍摄 1 张「{categoryName}」照片。\n\n" +
            $"预计耗时 {OcrConstants.FormatEstimate(1)}（{OcrConstants.EstimateRangeText}）。\n" +
            "图片仅发送至内网识别服务，不上传外网。\n" +
            "识别完成后可选择回填位置。",
            "开始识别", "跳过");
        if (!start)
        {
            _logger.LogBusiness("跳过证件 OCR 识别", ("Category", _categoryCode));
            return;
        }

        ShowBusy(true, string.Format(OcrConstants.ProgressTextFormat, 1, 1));
        using var stream = new MemoryStream(bytes);
        var result = await _ocrClient.RecognizeAsync(stream, _categoryCode, _scanCts?.Token ?? CancellationToken.None);
        ShowBusy(false);

        if (result.IsFailure || result.Value is null || !result.Value.HasAny)
        {
            await _dialogService.DisplayAlertAsync("识别失败",
                result.Message ?? "未识别出有效字段，请正对证件、保证光线充足后重拍", "确定");
            return;
        }

        // 跳转分配页（单条记录也显示目标选择，用户确认后填）
        if (_assignContext is null)
        {
            Complete(0);
            return;
        }

        var completion = new TaskCompletionSource<int>();
        await NavigateToAsync(new OcrAssignParameter(
            new List<OcrRecognizeResult> { result.Value },
            _assignContext,
            applied => completion.TrySetResult(applied)));

        var applied = await completion.Task;
        Complete(applied);
    }

    /// <summary>导航到分配页（分配页自行 Pop 后回调）</summary>
    private async Task NavigateToAsync(OcrAssignParameter parameter)
    {
        var page = _serviceProvider.GetService<OcrAssignPage>()
                   ?? throw new InvalidOperationException("OcrAssignPage 未注册 DI");
        await Navigation.PushAsync(page);
        await ((IParameterizedPage<OcrAssignParameter>)page).SetParameterAsync(parameter);
    }

    private void OnCancelClicked(object? sender, EventArgs e) => Complete(0);

    /// <summary>结束扫描：回传结果并关闭页面（幂等）</summary>
    private void Complete(int applied)
    {
        if (_completed) return;
        _completed = true;

        var callback = _onCompleted;
        _onCompleted = null;
        callback?.Invoke(applied);

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                Preview.Stop();
            }
            catch (Exception ex)
            {
                _logger.Warn($"停止相机预览异常：{ex.Message}");
            }

            if (Navigation.NavigationStack.Count > 1)
                await Navigation.PopAsync();
        });
    }

    private void ShowBusy(bool busy, string? message = null)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            BusyOverlay.IsVisible = busy;
            BusyIndicator.IsRunning = busy;
            if (!string.IsNullOrEmpty(message))
                BusyLabel.Text = message;
        });
    }
}
