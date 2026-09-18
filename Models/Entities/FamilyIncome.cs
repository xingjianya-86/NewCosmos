using System.ComponentModel.DataAnnotations;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 家庭收入项实体（纯 POCO，对应 nc_biz_labor_incomes 表）
/// 收入分类存储为独立子表（工资/经营/财产/转移/其他/赡养/土地/补贴    /// 依据：《中华人民共和国社会救助法》第40条）    /// </summary>
public class FamilyIncome
{
    /// <summary>
    /// 主键ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 家庭ID（关联 nc_biz_applications.id 对应 nc_low_income_families.id ??   /// </summary>
    [Required(ErrorMessage = "家庭ID不能为空")]
    public long FamilyId { get; set; }

    /// <summary>
    /// 收入类别代码
    /// </summary>
    [Required(ErrorMessage = "收入类别不能为空")]
    public string IncomeType { get; set; } = string.Empty;

    /// <summary>
    /// 收入金额（元）
    /// </summary>
    [Required(ErrorMessage = "收入金额不能为空")]
    public decimal Amount { get; set; }

    /// <summary>
    /// 收入来源说明
    /// </summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>
    /// 收入归属家庭成员ID（NULL表示全家    /// </summary>
    public long RecipientMemberId { get; set; }

    /// <summary>
    /// 收入归属家庭成员姓名（显示用    /// </summary>
    public string RecipientMemberName { get; set; } = string.Empty;

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
    /// 收入类别显示名称
    /// </summary>
    public string IncomeTypeDisplay => Constants.IncomeTypeConstants.GetDescription(IncomeType);

    /// <summary>
    /// 金额显示格式
    /// </summary>
    public string AmountDisplay => Amount.ToString("N2");

    /// <summary>
    /// 归属显示（无归属时显示“全家"    /// </summary>
    public string RecipientDisplay => string.IsNullOrEmpty(RecipientMemberName) ? "全家" : RecipientMemberName;
}
