using NewCosmos.Models.Results;

namespace NewCosmos.Models.Requests;

/// <summary>
/// 身份证扫描页导航参数：调用方传入完成回调，页面识别结束（成功/取消/失败）时回传结果。
/// </summary>
/// <param name="OnCompleted">完成回调；null 表示用户取消</param>
public sealed record IdCardScanParameter(Action<Result<IdCardInfo>?> OnCompleted);
