using NewCosmos.Helpers;
using NewCosmos.Models.Results;
using NewCosmos.Services.Platform;

namespace NewCosmos.Models.Requests;

/// <summary>
/// 识别结果分配页导航参数：识别结果列表 + 来源表单的回填上下文 + 完成回调。
/// 完成回调参数 = 实际回填条数（0 表示用户全部不使用或全部取消）。
/// </summary>
public sealed record OcrAssignParameter(
    IReadOnlyList<OcrRecognizeResult> Results,
    IOcrAssignContext Context,
    Action<int> OnCompleted);
