using NewCosmos.Models;

namespace NewCosmos.Services.Core;

/// <summary>
/// 加载进度服务实现
/// </summary>
public class LoadingProgressService : ILoadingProgressService
{
    private readonly LoadingProgress _progress = new() { TotalSteps = LoadingSteps.Total };

    /// <summary>
    /// 当前进度
    /// </summary>
    public LoadingProgress CurrentProgress => _progress;

    /// <summary>
    /// 进度变更事件
    /// </summary>
    public event Action<LoadingProgress>? ProgressChanged;

    /// <summary>
    /// 更新进度
    /// </summary>
    public void UpdateProgress(int step, string? stepName = null)
    {
        _progress.CurrentStep = step;
        _progress.StepName = stepName ?? LoadingSteps.GetStepName(step);
        _progress.IsCompleted = false;
        _progress.ErrorMessage = string.Empty;
        ProgressChanged?.Invoke(_progress);
    }

    /// <summary>
    /// 设置错误
    /// </summary>
    public void SetError(string message)
    {
        _progress.ErrorMessage = message;
        ProgressChanged?.Invoke(_progress);
    }

    /// <summary>
    /// 完成
    /// </summary>
    public void Complete()
    {
        _progress.CurrentStep = _progress.TotalSteps;
        _progress.StepName = "加载完成";
        _progress.IsCompleted = true;
        _progress.ErrorMessage = string.Empty;
        ProgressChanged?.Invoke(_progress);
    }

    /// <summary>
    /// 重置
    /// </summary>
    public void Reset()
    {
        _progress.CurrentStep = 0;
        _progress.TotalSteps = LoadingSteps.Total;
        _progress.StepName = string.Empty;
        _progress.IsCompleted = false;
        _progress.ErrorMessage = string.Empty;
        ProgressChanged?.Invoke(_progress);
    }
}
