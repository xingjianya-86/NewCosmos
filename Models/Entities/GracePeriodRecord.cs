namespace NewCosmos.Models.Entities;

/// <summary>
/// 渐退期记录实体（对应 nc_biz_grace_periods 表）
/// </summary>
public class GracePeriodRecord
{
    /// <summary>
    /// 主键ID
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// 关联申请ID
    /// </summary>
    public long ApplicationId { get; set; }

    /// <summary>
    /// 是否处于渐退期
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// 渐退期月数
    /// </summary>
    public int? GracePeriodMonths { get; set; }

    /// <summary>
    /// 渐退期开始日期
    /// </summary>
    public DateTime? StartDate { get; set; }

    /// <summary>
    /// 渐退期结束日期
    /// </summary>
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// 渐退前原分类结果
    /// </summary>
    public string? OriginalClassification { get; set; }

    /// <summary>
    /// 渐退前原保障金额
    /// </summary>
    public decimal? OriginalGuaranteeAmount { get; set; }
}