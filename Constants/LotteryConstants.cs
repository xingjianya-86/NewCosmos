namespace NewCosmos.Constants;

/// <summary>
/// 彩票模块常量（置信度门槛、重试策略、UI 预设）
/// </summary>
public static class LotteryConstants
{
    /// <summary>
    /// 默认最低置信度门槛（百分比）。机选不受此门槛约束。
    /// </summary>
    public const decimal DEFAULT_MIN_CONFIDENCE = 80m;

    /// <summary>
    /// 默认最大重试次数（Python 端单注重采样轮数上限）
    /// </summary>
    public const int DEFAULT_MAX_RETRIES = 5;

    /// <summary>
    /// C# 端外层兜底重试轮数上限（防脚本抖动/解析异常，避免与 maxRetries 平方级叠加）
    /// </summary>
    public const int FALLBACK_RETRY_ROUNDS = 2;

    /// <summary>
    /// 置信度门槛下限（UI 预设与参数校验）
    /// </summary>
    public const decimal MIN_CONFIDENCE_FLOOR = 50m;

    /// <summary>
    /// 置信度门槛上限（UI 预设与参数校验）
    /// </summary>
    public const decimal MIN_CONFIDENCE_CEILING = 95m;

    /// <summary>
    /// 最大重试次数上限
    /// </summary>
    public const int MAX_RETRIES_CEILING = 20;

    /// <summary>
    /// 预测页置信度门槛预设选项
    /// </summary>
    public static readonly int[] MinConfidenceOptions = { 70, 75, 80, 85, 90, 95 };

    /// <summary>
    /// 预测页最大重试次数预设选项
    /// </summary>
    public static readonly int[] MaxRetryOptions = { 1, 3, 5, 10, 20 };

    /// <summary>
    /// 判断单注置信度是否达到门槛
    /// </summary>
    public static bool MeetsThreshold(decimal confidenceScore, decimal minConfidence)
    {
        return confidenceScore > minConfidence;
    }

    /// <summary>Python 预测进程超时（秒）</summary>
    public const int PREDICT_TIMEOUT_SECONDS = 300;

    /// <summary>Python 训练进程超时（秒）</summary>
    public const int TRAIN_TIMEOUT_SECONDS = 1200;
}
