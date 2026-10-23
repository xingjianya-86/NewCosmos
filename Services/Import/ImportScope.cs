namespace NewCosmos.Services.Import;

/// <summary>
/// 导入范围上下文（年份管理型导入使用，如「年度种植补贴导入」）。
/// 三个能力：
/// 1. <see cref="OverrideYear"/> 强制覆盖：页面指定年份 → 忽略文件内「数据年份」列与文件名年份；
/// 2. <see cref="RangeStart"/>/<see cref="RangeEnd"/> 区间校验：最终年份必须落在区间内，越界整份报错、零写库（不静默写错年段）；
/// 3. <see cref="ClearBeforeImport"/> 年段感知清空：只清本次涉及的年段，其余年段原样保留（替代整表 TRUNCATE）。
/// <see cref="ClearedYears"/> 记录本批次已清空的年段——同一清单多文件时只清一次，
/// 否则第二个文件会把第一个文件刚写入的数据连年段一起清掉。
/// </summary>
public sealed class ImportScope
{
    /// <summary>强制年份：非空时以它为准，文件内年份与文件名年份全部忽略</summary>
    public int? OverrideYear { get; init; }

    /// <summary>校验区间起（含）；与 <see cref="RangeEnd"/> 成对使用</summary>
    public int? RangeStart { get; init; }

    /// <summary>校验区间止（含）</summary>
    public int? RangeEnd { get; init; }

    /// <summary>是否在写入前清空本次涉及的年段（整段，不按证件过滤）。可写：调用方在勾选"导入前清空"时可就地打开。</summary>
    public bool ClearBeforeImport { get; set; }

    /// <summary>本批次已清空的年段（服务在清空时写入），同一 scope 实例跨批次必须重建</summary>
    public HashSet<int> ClearedYears { get; } = new();

    public bool HasYearRange => RangeStart.HasValue && RangeEnd.HasValue;

    /// <summary>人读描述（日志/提示用）</summary>
    public string Describe()
    {
        if (OverrideYear.HasValue) return $"强制年份={OverrideYear.Value}";
        if (HasYearRange) return $"年份区间={RangeStart}-{RangeEnd}";
        return "年份按文件解析";
    }
}
