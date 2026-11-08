namespace NewCosmos.Models.Requests;

/// <summary>
/// 档案照片查询页导航参数：从业务列表（高龄/工作流/月度审核等）点「照片」进入时，
/// 预填检索关键词（通常为身份证号）并在进页后自动检索。
/// </summary>
public sealed record ArchivePhotoQueryParameter(
    string? Keyword = null,
    bool AutoSearch = true);
