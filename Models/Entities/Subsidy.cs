using CommunityToolkit.Mvvm.ComponentModel;

namespace NewCosmos.Models.Entities;

/// <summary>
/// 农业补贴实体（支持属性变更通知，对应 nc_biz_subsidies 表）
/// </summary>
public partial class Subsidy : ObservableObject
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
    /// 补贴类型：地力补贴/大豆补贴/玉米补贴/地表水水稻/地下水水稻/轮作补贴/种植补贴
    /// </summary>
    [ObservableProperty]
    private string _subsidyType = string.Empty;

    /// <summary>
    /// 面积（亩）
    /// </summary>
    [ObservableProperty]
    private decimal _area;

    /// <summary>
    /// 单价（元/亩）
    /// </summary>
    [ObservableProperty]
    private decimal _unitPrice;

    /// <summary>
    /// 数量/倍数
    /// </summary>
    public int Count { get; set; } = 1;

    /// <summary>
    /// 比例系数（用于按份额分配，0~1）
    /// </summary>
    [ObservableProperty]
    private decimal _ratioFactor = 1;

    /// <summary>
    /// 原始金额（元）= 面积 × 单价 × 数量
    /// </summary>
    public decimal OriginalAmount { get; set; }

    /// <summary>
    /// 实际金额（元）= 原始金额 × 比例系数
    /// </summary>
    public decimal Amount { get; set; }

    /// <summary>
    /// 关联成员姓名
    /// </summary>
    public string MemberName { get; set; } = string.Empty;

    /// <summary>
    /// 关联成员身份证号
    /// </summary>
    public string MemberIdCard { get; set; } = string.Empty;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// 更新时间
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// 计算金额
    /// </summary>
    public void CalculateAmount()
    {
        OriginalAmount = Math.Round(Area * UnitPrice * Count, 2);
        Amount = Math.Round(OriginalAmount * RatioFactor, 2);
    }

    /// <summary>
    /// 补贴类型变化时自动设置单价
    /// </summary>
    partial void OnSubsidyTypeChanged(string value)
    {
        if (Constants.SubsidyPriceConstants.TypeToPrice.TryGetValue(value, out var price))
            UnitPrice = price;
        else
            UnitPrice = 0;
    }

    /// <summary>
    /// 单价变化时自动重算金额
    /// </summary>
    partial void OnUnitPriceChanged(decimal value)
    {
        CalculateAmount();
        OnPropertyChanged(nameof(Amount));
        OnPropertyChanged(nameof(OriginalAmount));
    }

    /// <summary>
    /// 面积变化时自动重算金额
    /// </summary>
    partial void OnAreaChanged(decimal value)
    {
        CalculateAmount();
        OnPropertyChanged(nameof(Amount));
        OnPropertyChanged(nameof(OriginalAmount));
    }

    /// <summary>
    /// 比例系数变化时自动重算金额（如「更新计算比例」批量回写）
    /// </summary>
    partial void OnRatioFactorChanged(decimal value)
    {
        CalculateAmount();
        OnPropertyChanged(nameof(Amount));
        OnPropertyChanged(nameof(OriginalAmount));
    }
}
