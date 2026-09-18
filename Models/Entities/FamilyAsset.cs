using System.ComponentModel.DataAnnotations;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 家庭资产项实体（纯 POCO，对对应 nc_biz_properties / nc_biz_vehicles / nc_biz_machineries / nc_biz_financial_assets 表）
/// 资产分类存储为独立子表（房产/车辆/农机/金融/土地    /// 依据：《中华人民共和国社会救助法》第41    /// </summary>
public class FamilyAsset
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
    /// 资产类别代码
    /// </summary>
    [Required(ErrorMessage = "资产类别不能为空")]
    public string AssetType { get; set; } = string.Empty;

    /// <summary>
    /// 资产名称/描述
    /// </summary>
    public string AssetName { get; set; } = string.Empty;

    /// <summary>
    /// 估算价值（元）
    /// </summary>
    [Required(ErrorMessage = "估算价值不能为")]
    public decimal EstimatedValue { get; set; }

    /// <summary>
    /// 所有权状态代    /// </summary>
    public string OwnershipStatus { get; set; } = string.Empty;

    /// <summary>
    /// 是否豁免
    /// </summary>
    public bool IsExempted { get; set; }

    /// <summary>
    /// 豁免类型代码
    /// </summary>
    public string ExemptionType { get; set; } = string.Empty;

    /// <summary>
    /// 资产归属家庭成员ID（NULL表示全家    /// </summary>
    public long OwnerMemberId { get; set; }

    /// <summary>
    /// 资产归属家庭成员姓名（显示用    /// </summary>
    public string OwnerMemberName { get; set; } = string.Empty;

    /// <summary>
    /// 备注
    /// </summary>
    public string Remark { get; set; } = string.Empty;

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
    /// 资产类别显示名称
    /// </summary>
    public string AssetTypeDisplay => Constants.AssetTypeConstants.GetDescription(AssetType);

    /// <summary>
    /// 价值显示格    /// </summary>
    public string EstimatedValueDisplay => EstimatedValue.ToString("N2");

    /// <summary>
    /// 归属显示（无归属时显示"全家"）
    /// </summary>
    public string OwnerDisplay => string.IsNullOrEmpty(OwnerMemberName) ? "全家" : OwnerMemberName;

    /// <summary>
    /// 豁免类型显示名称
    /// </summary>
    public string ExemptionTypeDisplay => 
        string.IsNullOrEmpty(ExemptionType) ? "无" : Constants.AssetExemptionConstants.GetDescription(ExemptionType);
}
