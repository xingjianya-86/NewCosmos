using NewCosmos.Services.Platform;

namespace NewCosmos.Models.Requests;

/// <summary>
/// 证件扫描页导航参数（类别驱动识别版）：调用方传入回填上下文与完成回调。
/// 页面流程：选类别 → 拍照/选图 → 识别询问 → 识别 → 分配页 → 回传实际回填条数。
/// </summary>
/// <param name="Context">回填上下文（表单 VM 实现 IOcrAssignContext）</param>
/// <param name="OnCompleted">完成回调，参数 = 实际回填条数（0 表示未回填/用户取消）</param>
public sealed record OcrScanParameter(IOcrAssignContext Context, Action<int> OnCompleted);
