using System.ComponentModel.DataAnnotations;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 土地登记实体（纯 POCO，对对应 nc_biz_land_registrations 表）
/// </summary>
public class LandRegistration
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
    /// 土地所有人姓名
    /// </summary>
    public string OwnerName { get; set; } = string.Empty;

    /// <summary>
    /// 所有人身份证号
    /// </summary>
    public string OwnerIdCard { get; set; } = string.Empty;

    /// <summary>
    /// 土地类型
    /// </summary>
    public string LandType { get; set; } = string.Empty;

    /// <summary>
    /// 土地用途：自行种植/转包他人/承包土地
    /// </summary>
    [Required(ErrorMessage = "土地用途不能为")]
    public string LandUsage { get; set; } = string.Empty;

    /// <summary>
    /// 计量方式
    /// </summary>
    public string MeasurementType { get; set; } = string.Empty;

    /// <summary>
    /// 面积（亩    /// </summary>
    public double Area { get; set; }

    /// <summary>
    /// 单价（元/亩）
    /// </summary>
    public double Price { get; set; }

    /// <summary>
    /// 小计（元 面积 × 单价
    /// </summary>
    public decimal Subtotal { get; set; }

    /// <summary>
    /// 地块位置
    /// </summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// 计算小计
    /// </summary>
    public void CalculateSubtotal()
    {
        Subtotal = Math.Round((decimal)(Area * Price), 2);
    }
}
