using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Media;
using NewCosmos.Models.Requests;
using NewCosmos.Models.Results;
using NewCosmos.Services.Core;
using NewCosmos.Services.Platform;
using NewCosmos.ViewModels.Base;

namespace NewCosmos.Pages.Mobile;

/// <summary>
/// 身份证扫描页：原生 Camera2 预览 + 实时取景框，拍照后走端侧 OCR。
/// 识别结果经 <see cref="IdCardScanParameter.OnCompleted"/> 回传给调用方（快速经济核对页）。
/// </summary>
public partial class MobileIdCardScanPage : ContentPage, IParameterizedPage<IdCardScanParameter>
{
    private const double CardAspectRatio = 85.6 / 54.0;

    private readonly IIdentityReader _identityReader;
    private readonly ILoggerService _logger;
    private readonly IDialogService _dialogService;

    private Action<Result<IdCardInfo>?>? _onCompleted;
    private bool _capturing;
    private bool _completed;
    private CancellationTokenSource? _scanCts;

    public MobileIdCardScanPage(IIdentityReader identityReader, ILoggerService logger, IDialogService dialogService)
    {
        InitializeComponent();
        _identityReader = identityReader;
        _logger = logger;
        _dialogService = dialogService;
    }

    public Task SetParameterAsync(IdCardScanParameter parameter)
    {
        _onCompleted = parameter.OnCompleted;
        return Task.CompletedTask;
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
            await _dialogService.DisplayAlertAsync("提示", "未授予相机权限，无法扫描身份证。可改用「从相册选择」", "确定");
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
        // 离页取消在途 OCR 识别（原硬编码 CancellationToken.None，识别继续跑完还可能在已离开的页面上弹错窗）
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
        if (width <= 0)
            return;

        // 竖向取景框：高 = 宽 × (85.6/54)，即身份证长边竖直
        var frameWidth = Math.Min(width * 0.56, 320);
        GuideFrame.WidthRequest = frameWidth;
        GuideFrame.HeightRequest = frameWidth * CardAspectRatio;
    }

    private async void OnCaptureClicked(object? sender, EventArgs e)
    {
        if (_capturing || _completed)
            return;

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

            ShowBusy(true, "正在识别身份证...");
            using var stream = new MemoryStream(bytes);
            var result = await _identityReader.ReadAsync(stream, _scanCts?.Token ?? CancellationToken.None);
            Complete(result);
        }
        catch (OperationCanceledException)
        {
            // 页面已离开触发的取消：静默，不再弹错误框
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "身份证扫描失败");
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
        if (_capturing || _completed)
            return;

        try
        {
            var photos = await MediaPicker.Default.PickPhotosAsync();
            var photo = photos?.FirstOrDefault();
            if (photo is null)
                return;

            _capturing = true;
            ShowBusy(true, "正在识别身份证...");
            await using var stream = await photo.OpenReadAsync();
            var result = await _identityReader.ReadAsync(stream, _scanCts?.Token ?? CancellationToken.None);
            Complete(result);
        }
        catch (OperationCanceledException)
        {
            // 页面已离开触发的取消：静默，不再弹错误框
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "从相册识别身份证失败");
            ShowBusy(false);
            await _dialogService.DisplayAlertAsync("错误", $"识别失败：{ex.Message}", "确定");
        }
        finally
        {
            _capturing = false;
        }
    }

    private void OnCancelClicked(object? sender, EventArgs e) => Complete(null);

    /// <summary>结束扫描：回传结果并关闭页面（幂等）</summary>
    private void Complete(Result<IdCardInfo>? result)
    {
        if (_completed)
            return;
        _completed = true;

        var callback = _onCompleted;
        _onCompleted = null;
        callback?.Invoke(result);

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
