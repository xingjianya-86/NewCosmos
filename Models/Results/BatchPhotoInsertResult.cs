namespace NewCosmos.Models.Results;

/// <summary>
/// 批量插入档案照片的结果汇总（PC 端查询页「插入照片 / 拖拽插入」用）。
/// 部分失败不回滚已成功张（与手机端批量采集口径一致），失败明细由 FirstError 带第一条原因。
/// </summary>
public sealed class BatchPhotoInsertResult
{
    /// <summary>提交张数（含空文件等在服务内被判失败的）。</summary>
    public int Total { get; init; }

    /// <summary>成功入库张数。</summary>
    public int Succeeded { get; init; }

    /// <summary>失败张数（= Total - Succeeded）。</summary>
    public int Failed => Total - Succeeded;

    /// <summary>第一条失败原因（成功全部时为 null；日志/提示用，不含 PII）。</summary>
    public string? FirstError { get; init; }

    /// <summary>成功入库的照片 ID（按提交顺序）。</summary>
    public IReadOnlyList<long> PhotoIds { get; init; } = Array.Empty<long>();

    public bool HasSuccess => Succeeded > 0;
}
