using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Models;
using NewCosmos.Services.Core;

namespace NewCosmos.ViewModels.Shared;

/// <summary>
/// 加载进度弹窗 ViewModel
/// </summary>
/// 不继承 ViewModelBase（审计豁免）：弹窗内部 VM，自带 ErrorMessage 语义与 TCS 生命周期，继承需改名冲突成员且无收益。
public partial class LoadingProgressDialogViewModel : ObservableObject
{
    private readonly ILoadingProgressService _progressService;
    private readonly IDialogService _dialogService;
    private CancellationTokenSource? _cts;

    /// <summary>
    /// 进度百分比（0-1）
    /// </summary>
    [ObservableProperty]
    private double _progress;

    /// <summary>
    /// 步骤名称
    /// </summary>
    [ObservableProperty]
    private string _stepName = string.Empty;

    /// <summary>
    /// 步骤信息
    /// </summary>
    [ObservableProperty]
    private string _stepInfo = string.Empty;

    /// <summary>
    /// 是否有错误
    /// </summary>
    [ObservableProperty]
    private bool _hasError;

    /// <summary>
    /// 错误信息
    /// </summary>
    [ObservableProperty]
    private string _errorMessage = string.Empty;

    /// <summary>
    /// 是否显示取消按钮
    /// </summary>
    [ObservableProperty]
    private bool _canCancel = true;

    /// <summary>
    /// 完成事件
    /// </summary>
    public event Action<bool>? Completed;

    public LoadingProgressDialogViewModel(
        ILoadingProgressService progressService,
        IDialogService dialogService)
    {
        _progressService = progressService;
        _dialogService = dialogService;
        _progressService.ProgressChanged += OnProgressChanged;
    }

    /// <summary>
    /// 弹窗生命周期结束时调用：退订单例服务事件并释放 CTS。
    /// 进度服务是 Singleton 而本 VM 是 Transient，不退订会把 VM 永久挂在事件链上。
    /// </summary>
    public void Detach()
    {
        _progressService.ProgressChanged -= OnProgressChanged;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    private void OnProgressChanged(LoadingProgress progress)
    {
        Progress = progress.Progress / 100.0;
        StepName = progress.StepName;
        StepInfo = progress.StepInfo;
        HasError = progress.HasError;
        ErrorMessage = progress.ErrorMessage;
    }

    /// <summary>
    /// 开始加载
    /// </summary>
    public async Task StartLoadingAsync(Func<CancellationToken, Task> operation)
    {
        _cts = new CancellationTokenSource();
        _progressService.Reset();
        CanCancel = true;

        try
        {
            await operation(_cts.Token);
            _progressService.Complete();
            Completed?.Invoke(true);
        }
        catch (OperationCanceledException)
        {
            _progressService.SetError("操作已取消");
            Completed?.Invoke(false);
        }
        catch (Exception ex)
        {
            _progressService.SetError($"加载失败: {ex.Message}");
            Completed?.Invoke(false);
        }
    }

    /// <summary>
    /// 取消操作
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
        CanCancel = false;
    }

    /// <summary>
    /// 关闭弹窗
    /// </summary>
    [RelayCommand]
    private void Close()
    {
        _cts?.Cancel();
        Completed?.Invoke(false);
    }

    /// <summary>
    /// 确认关闭（无错误时）
    /// </summary>
    [RelayCommand]
    private void ConfirmClose()
    {
        Completed?.Invoke(true);
    }
}
