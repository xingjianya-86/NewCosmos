namespace NewCosmos.Constants;

/// <summary>
/// 档案照片压缩阈值（单一事实来源）：
/// 新采集照片统一转 WebP，长边不超过 <see cref="MaxLongEdge"/>，
/// 压缩后单张硬上限 <see cref="HardMaxBytes"/>（500KB），超过则拒绝入库而不是存原图。
/// 证件文字可读性由最低质量/最低长边兜底；仍无法达标时提示重新拍摄。
/// </summary>
public static class PhotoCompressionConstants
{
    /// <summary>压缩后长边上限（像素）。</summary>
    public const int MaxLongEdge = 2000;

    /// <summary>降尺寸时的最低长边（像素）：低于此值不再缩，避免证件文字不可读。</summary>
    public const int MinLongEdge = 1200;

    /// <summary>WebP 起始质量（0-100）。</summary>
    public const int StartQuality = 80;

    /// <summary>WebP 最低质量（0-100）。</summary>
    public const int MinQuality = 50;

    /// <summary>每次降质量的步长。</summary>
    public const int QualityStep = 10;

    /// <summary>目标体积（字节）：通常压到此值以内，但不是硬约束。</summary>
    public const int TargetBytes = 350 * 1024;

    /// <summary>单张硬上限（字节，500KB）：超过则压缩失败，禁止入库。</summary>
    public const int HardMaxBytes = 500 * 1024;

    /// <summary>质量降到底仍超限时，每次缩尺寸的比例（百分比）。</summary>
    public const int DownscalePercent = 85;

    /// <summary>最多缩尺寸次数（2000→1700→1445→1228→1200 左右）。</summary>
    public const int MaxDownscaleAttempts = 4;

    /// <summary>历史照片批量转 WebP 的每批读取条数（控制内存峰值）。</summary>
    public const int MigrationBatchSize = 20;
}
