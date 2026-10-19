using CommunityToolkit.Mvvm.ComponentModel;
using NewCosmos.Constants;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 后补追缴记录实体
/// </summary>
public partial class RecoveryRecord : ObservableObject
{
    /// <summary>
    /// 主键ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 录入模式：Search=搜索录入，Manual=手工录入
    /// </summary>
    public string InputMode { get; set; } = "Search";

    /// <summary>
    /// 来源类型：RuralSubsistence/UrbanSubsistence/RigidExpenditure/TempRelief/Elderly
    /// </summary>
    public string? SourceType { get; set; }

    /// <summary>
    /// 来源记录ID（搜索录入时有值）
    /// </summary>
    public long? SourceId { get; set; }

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
    /// 追缴月数
    /// </summary>
    public int RecoveryMonths { get; set; }

    /// <summary>
    /// 应追缴金额（唯一事实来源：表单/列表/打印/落库同源）
    /// 取值为金额来源 amount_source 对应口径的结果：自动分段 / 月标准×月数 / 人工核实
    /// </summary>
    public decimal RecoveryAmount { get; set; }

    /// <summary>
    /// 金额来源：Segment=自动分段计算 / Flat=月标准×月数 / Manual=人工核实填写
    /// </summary>
    public string AmountSource { get; set; } = RecoveryConstants.AMOUNT_SOURCE_FLAT;

    /// <summary>
    /// 金额说明：Manual 时的手工填写原因；自动时可为计算依据说明
    /// </summary>
    public string? AmountNote { get; set; }

    /// <summary>
    /// 自动计算的参考值（人工覆盖时保留，便于审计对比）
    /// </summary>
    public decimal? AutoAmount { get; set; }

    /// <summary>
    /// 已追缴金额（手工录入实际到账金额）
    /// </summary>
    public decimal RecoveredAmount { get; set; }

    /// <summary>
    /// 追缴起始月 YYYY-MM
    /// </summary>
    public string? StartMonth { get; set; }

    /// <summary>
    /// 追缴终止月 YYYY-MM
    /// </summary>
    public string? EndMonth { get; set; }

    /// <summary>
    /// 状态：Draft草稿/Confirmed已确认/Printed已打印
    /// </summary>
    public string Status { get; set; } = "Draft";

    /// <summary>
    /// 创建人
    /// </summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// 状态显示文字
    /// </summary>
    public string StatusDisplay => Status switch
    {
        RecoveryConstants.STATUS_CONFIRMED => "已确认",
        RecoveryConstants.STATUS_PRINTED => "已打印",
        _ => "草稿"
    };

    /// <summary>
    /// 来源类型显示名称
    /// </summary>
    public string SourceTypeDisplay => RecoveryConstants.GetSourceTypeDisplayName(SourceType ?? string.Empty);

    /// <summary>
    /// 录入模式显示文字
    /// </summary>
    public string InputModeDisplay => InputMode == RecoveryConstants.INPUT_MODE_MANUAL ? "手工录入" : "搜索录入";

    /// <summary>
    /// 创建时间显示
    /// </summary>
    public string CreatedAtText => CreatedAt.ToString("yyyy-MM-dd HH:mm");

    /// <summary>
    /// 是否可删除（仅草稿状态）
    /// </summary>
    public bool CanDelete => Status == RecoveryConstants.STATUS_DRAFT;

    /// <summary>
    /// 是否可补打（非草稿状态：已确认/已打印）
    /// </summary>
    public bool CanReprint => Status != RecoveryConstants.STATUS_DRAFT;

    /// <summary>
    /// 金额来源显示文字
    /// </summary>
    public string AmountSourceDisplay => RecoveryConstants.GetAmountSourceDisplayName(AmountSource);
}

/// <summary>
/// 追缴金额分段明细（对应 nc_biz_recovery_segments 表）
/// 与 nc_biz_elderly_payback_segments 同构：起止月 + 该段月标准 + 月数 + 段金额
/// </summary>
public class RecoverySegment
{
    /// <summary>主键ID</summary>
    public long Id { get; set; }

    /// <summary>关联追缴记录ID（nc_biz_recovery_records.id）</summary>
    public long RecoveryId { get; set; }

    /// <summary>段序号（从 1 起）</summary>
    public int SegmentNo { get; set; }

    /// <summary>该段月标准金额（元/月）</summary>
    public decimal MonthlyAmount { get; set; }

    /// <summary>该段起始月 yyyy-MM</summary>
    public string SegmentStartMonth { get; set; } = string.Empty;

    /// <summary>该段终止月 yyyy-MM</summary>
    public string SegmentEndMonth { get; set; } = string.Empty;

    /// <summary>该段月数</summary>
    public int Months { get; set; }

    /// <summary>该段金额（元）= 月标准 × 月数</summary>
    public decimal SegmentAmount { get; set; }

    /// <summary>分段依据：ChangeRecord=变更记录 / Ledger=台账现值 / GracePeriod=渐退期原标准</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>起止区间显示文字</summary>
    public string RangeText => $"{SegmentStartMonth} ~ {SegmentEndMonth}";

    /// <summary>分段依据显示文字</summary>
    public string SourceDisplay => RecoveryConstants.GetSegmentSourceDisplayName(Source);
}

/// <summary>
/// 历史月保障标准时间轴点（自动分段计算的输入项）
/// </summary>
public class RecoveryMonthlyStandardPoint
{
    /// <summary>生效月 yyyy-MM（自该月起适用该标准）</summary>
    public string EffectiveMonth { get; set; } = string.Empty;

    /// <summary>该月起生效的月保障标准（元/月）</summary>
    public decimal MonthlyAmount { get; set; }

    /// <summary>依据来源：ChangeRecord / Ledger / GracePeriod</summary>
    public string Source { get; set; } = string.Empty;
}

/// <summary>
/// 应追缴总额自动计算结果（回收 RecoveryAmountCalculator.Calculate）
/// </summary>
public class RecoveryAmountResult
{
    /// <summary>是否可自动计算（false 时 Reason 给出原因，界面转人工填写）</summary>
    public bool CanCalculate { get; set; }

    /// <summary>不可自动计算的原因（可计算时为空）</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>分段明细</summary>
    public List<RecoverySegment> Segments { get; set; } = new();

    /// <summary>合计月数</summary>
    public int TotalMonths { get; set; }

    /// <summary>合计金额（已一次性舍入到分）</summary>
    public decimal TotalAmount { get; set; }

    /// <summary>建议的金额来源（Segment / Flat）</summary>
    public string AmountSource { get; set; } = RecoveryConstants.AMOUNT_SOURCE_FLAT;

    /// <summary>分段是否含"变更记录"依据（含则计为分段口径）</summary>
    public bool HasChangeRecordSegments => Segments.Exists(s => s.Source == RecoveryConstants.SEGMENT_SOURCE_CHANGE);

    /// <summary>合计显示文案</summary>
    public string TotalText => $"合计 {TotalMonths} 个月 / {TotalAmount:N2} 元";
}
