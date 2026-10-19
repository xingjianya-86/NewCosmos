using NewCosmos.Constants;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 停止人员搜索结果DTO
/// </summary>
public class StoppedPersonDto
{
    /// <summary>
    /// 来源类型：RuralSubsistence/UrbanSubsistence/RigidExpenditure/TempRelief/Elderly
    /// </summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>
    /// 来源显示名称
    /// </summary>
    public string SourceDisplay { get; set; } = string.Empty;

    /// <summary>
    /// 来源记录ID
    /// </summary>
    public long SourceId { get; set; }

    /// <summary>
    /// 人员姓名
    /// </summary>
    public string PersonName { get; set; } = string.Empty;

    /// <summary>
    /// 身份证号
    /// </summary>
    public string? IdCard { get; set; }

    /// <summary>
    /// 月保障额
    /// </summary>
    public decimal MonthlyAmount { get; set; }

    /// <summary>
    /// 是否选中
    /// </summary>
    public bool IsSelected { get; set; }

    /// <summary>
    /// 行号（用于显示序号）
    /// </summary>
    public int RowIndex { get; set; }
}

/// <summary>
/// 停止人员统计信息
/// </summary>
public class StoppedPersonStats
{
    /// <summary>
    /// 停止人员总数
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// 农村低保人数
    /// </summary>
    public int RuralSubsistenceCount { get; set; }

    /// <summary>
    /// 城市低保人数
    /// </summary>
    public int UrbanSubsistenceCount { get; set; }

    /// <summary>
    /// 刚性支出人数
    /// </summary>
    public int RigidExpenditureCount { get; set; }

    /// <summary>
    /// 临时救助人数
    /// </summary>
    public int TempReliefCount { get; set; }

    /// <summary>
    /// 高龄人数
    /// </summary>
    public int ElderlyCount { get; set; }
}

/// <summary>
/// 追缴记录DTO（搜索录入）
/// </summary>
public class RecoveryRecordDto
{
    /// <summary>
    /// 来源类型
    /// </summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>
    /// 来源记录ID
    /// </summary>
    public long SourceId { get; set; }

    /// <summary>
    /// 追缴人员姓名
    /// </summary>
    public string PersonName { get; set; } = string.Empty;

    /// <summary>
    /// 身份证号
    /// </summary>
    public string? IdCard { get; set; }

    /// <summary>
    /// 追缴理由
    /// </summary>
    public string? RecoveryReason { get; set; }

    /// <summary>
    /// 月保障额
    /// </summary>
    public decimal MonthlyAmount { get; set; }

    /// <summary>
    /// 追缴起始月
    /// </summary>
    public string? StartMonth { get; set; }

    /// <summary>
    /// 追缴终止月
    /// </summary>
    public string? EndMonth { get; set; }

    /// <summary>
    /// 已追缴金额
    /// </summary>
    public decimal RecoveredAmount { get; set; }

    /// <summary>
    /// 应追缴总额（唯一事实来源；自动计算或人工核实的结果都写在这里）
    /// </summary>
    public decimal RecoveryAmount { get; set; }

    /// <summary>
    /// 金额来源：Segment=自动分段 / Flat=月标准×月数 / Manual=人工核实
    /// </summary>
    public string AmountSource { get; set; } = RecoveryConstants.AMOUNT_SOURCE_FLAT;

    /// <summary>
    /// 金额说明：Manual 时必填（人工填写原因），自动时可为计算依据说明
    /// </summary>
    public string? AmountNote { get; set; }

    /// <summary>
    /// 自动计算的参考值（人工覆盖时保留用于审计对比）
    /// </summary>
    public decimal? AutoAmount { get; set; }

    /// <summary>
    /// 自动计算分段明细（无则空）
    /// </summary>
    public List<RecoverySegment> Segments { get; set; } = new();
}

/// <summary>
/// 手工追缴记录DTO
/// </summary>
public class ManualRecoveryRecordDto
{
    /// <summary>
    /// 追缴人员姓名
    /// </summary>
    public string PersonName { get; set; } = string.Empty;

    /// <summary>
    /// 身份证号
    /// </summary>
    public string? IdCard { get; set; }

    /// <summary>
    /// 追缴分类
    /// </summary>
    public string SourceType { get; set; } = string.Empty;

    /// <summary>
    /// 追缴理由
    /// </summary>
    public string? RecoveryReason { get; set; }

    /// <summary>
    /// 月保障额
    /// </summary>
    public decimal MonthlyAmount { get; set; }

    /// <summary>
    /// 追缴起始月
    /// </summary>
    public string? StartMonth { get; set; }

    /// <summary>
    /// 追缴终止月
    /// </summary>
    public string? EndMonth { get; set; }

    /// <summary>
    /// 已追缴金额
    /// </summary>
    public decimal RecoveredAmount { get; set; }

    /// <summary>
    /// 应追缴总额（唯一事实来源；自动计算或人工核实的结果都写在这里）
    /// </summary>
    public decimal RecoveryAmount { get; set; }

    /// <summary>
    /// 金额来源：Segment=自动分段 / Flat=月标准×月数 / Manual=人工核实
    /// </summary>
    public string AmountSource { get; set; } = RecoveryConstants.AMOUNT_SOURCE_FLAT;

    /// <summary>
    /// 金额说明：Manual 时必填（人工填写原因），自动时可为计算依据说明
    /// </summary>
    public string? AmountNote { get; set; }

    /// <summary>
    /// 自动计算的参考值（人工覆盖时保留用于审计对比）
    /// </summary>
    public decimal? AutoAmount { get; set; }

    /// <summary>
    /// 自动计算分段明细（无则空）
    /// </summary>
    public List<RecoverySegment> Segments { get; set; } = new();
}
