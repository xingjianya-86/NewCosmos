namespace NewCosmos.Models.Requests;

/// <summary>待归属照片页参数：可从照片查询页带入当前身份证号作为归属默认值。</summary>
public sealed record PendingPhotosParameter(string? PrefillIdCard = null);
