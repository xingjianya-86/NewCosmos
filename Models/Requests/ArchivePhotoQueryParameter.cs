using NewCosmos.Services.Platform;

namespace NewCosmos.Models.Requests;

/// <summary>
/// 档案照片查询页导航参数：从业务列表/表单（高龄/工作流/月度审核等）点「照片」进入时，
/// 预填检索关键词（通常为身份证号）并在进页后自动检索。
/// ApplicationId：从表单带入的申请 ID（查询页「打开采集页」时优先透传，保持照片与申请的关联）。
/// AssignContext：识别回填上下文（表单 VM 实现）；透传给采集页，保住"拍照 → OCR → 回填表单"能力。
/// </summary>
public sealed record ArchivePhotoQueryParameter(
    string? Keyword = null,
    bool AutoSearch = true,
    long? ApplicationId = null,
    IOcrAssignContext? AssignContext = null);
