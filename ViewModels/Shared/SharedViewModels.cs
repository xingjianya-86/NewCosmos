using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NewCosmos.Services.Core;

namespace NewCosmos.ViewModels.Shared;



/// <summary>
/// 进度指示器ViewModel
/// </summary>
public partial class ProgressViewModel : ObservableObject
{
    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isIndeterminate;

    [ObservableProperty]
    private bool _isCancellable;

    private CancellationTokenSource _cts = null!;

    public CancellationToken CancellationToken => _cts?.Token ?? CancellationToken.None;

    public void Start(string message, bool isIndeterminate = true, bool isCancellable = true)
    {
        Message = message;
        IsIndeterminate = isIndeterminate;
        IsCancellable = isCancellable;
        Progress = 0;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
    }

    public void UpdateProgress(double progress, string message = null)
    {
        Progress = progress;
        IsIndeterminate = false;

        if (message != null)
            Message = message;
    }

    public void Complete(string message = null)
    {
        Progress = 100;
        IsIndeterminate = false;

        if (message != null)
            Message = message;
    }

    [RelayCommand]
    private void Cancel()
    {
        _cts?.Cancel();
    }

    public void Dispose()
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }
    }
}

