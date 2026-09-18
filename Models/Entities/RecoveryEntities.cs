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
    /// 应追缴金额 = 月保障额 × 追缴月数
    /// </summary>
    public decimal RecoveryAmount { get; set; }

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
}
