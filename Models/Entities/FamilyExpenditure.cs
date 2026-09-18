using System.ComponentModel.DataAnnotations;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 家庭支出项实体（刚性支出，纯 POCO，对对应 nc_biz_rigid_expenditures 表）
/// 依据：《刚性支出困难家庭认定办法》（民政部2024年10月）
/// </summary>
public class FamilyExpenditure
{
    /// <summary>
    /// 主键ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 家庭ID（关对应 nc_biz_applications.id 对应 nc_low_income_families.id ??   /// </summary>
    [Required(ErrorMessage = "家庭ID不能为空")]
    public long FamilyId { get; set; }

    /// <summary>
    /// 支出类别代码
    /// </summary>
    [Required(ErrorMessage = "支出类别不能为空")]
    public string ExpenditureType { get; set; } = string.Empty;

    /// <summary>
    /// 支出金额（元）
    /// </summary>
    [Required(ErrorMessage = "支出金额不能为空")]
    public decimal Amount { get; set; }

    /// <summary>
    /// 支出说明
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 支出关联家庭成员ID（如医疗支出对应患病成员    /// </summary>
    public long RelatedMemberId { get; set; }

    /// <summary>
    /// 支出关联家庭成员姓名（显示用    /// </summary>
    public string RelatedMemberName { get; set; } = string.Empty;

    /// <summary>
    /// 支出发生日期（可选）
    /// </summary>
    public DateTime OccurredAt { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 版本号（乐观并发控制    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// 支出类别显示名称
    /// </summary>
    public string ExpenditureTypeDisplay => Constants.RigidExpenditureConstants.GetDescription(ExpenditureType);

    /// <summary>
    /// 金额显示格式
    /// </summary>
    public string AmountDisplay => Amount.ToString("N2");

    /// <summary>
    /// 关联成员显示（无关联时显示"全家"）
    /// </summary>
    public string RelatedMemberDisplay => string.IsNullOrEmpty(RelatedMemberName) ? "全家" : RelatedMemberName;
}
