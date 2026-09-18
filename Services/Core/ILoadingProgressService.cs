using NewCosmos.Models;

namespace NewCosmos.Services.Core;

/// <summary>
/// 加载进度服务接口
/// </summary>
public interface ILoadingProgressService
{
    /// <summary>
    /// 当前进度
    /// </summary>
    LoadingProgress CurrentProgress { get; }

    /// <summary>
    /// 进度变更事件
    /// </summary>
    event Action<LoadingProgress>? ProgressChanged;

    /// <summary>
    /// 更新进度
    /// </summary>
    void UpdateProgress(int step, string? stepName = null);

    /// <summary>
    /// 设置错误
    /// </summary>
    void SetError(string message);

    /// <summary>
    /// 完成
    /// </summary>
    void Complete();

    /// <summary>
    /// 重置
    /// </summary>
    void Reset();
}
