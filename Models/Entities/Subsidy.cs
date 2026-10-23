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
    /// 计算金额，分两种行型：
    /// ① 面积型（常规）：原始金额 = 面积 × 单价 × 数量，实际金额 = 原始金额 × 比例系数；
    /// ② 金额型（Area=0 且 UnitPrice=0 且行内已有 OriginalAmount——「大豆品种补贴」等无统一单价的分类）：
    ///    以存量原始金额为基数按比例折算。**不可走①**，否则金额会被算成 0，保存时把导入的补贴金额静默清空。
    /// </summary>
    public void CalculateAmount()
    {
        if (Area == 0 && UnitPrice == 0 && OriginalAmount > 0)
        {
            Amount = Math.Round(OriginalAmount * RatioFactor, 2);
        }
        else
        {
            OriginalAmount = Math.Round(Area * UnitPrice * Count, 2);
            Amount = Math.Round(OriginalAmount * RatioFactor, 2);
        }
        OnPropertyChanged(nameof(AmountFormula));
    }

    /// <summary>
    /// 金额显示算式（条件式）：比例=100% 时为「= ¥756.00」；
    /// 折算后为「原 ¥756.00 × 35.20% = ¥266.11」，避免"面积×单价=金额"与实际值不符的误解。
    /// </summary>
    public string AmountFormula
    {
        get
        {
            var percent = Math.Round(RatioFactor * 100, 2);
            return percent >= 99.99m
                ? $"= ¥{Amount:N2}"
                : $"原 ¥{OriginalAmount:N2} × {percent:F2}% = ¥{Amount:N2}";
        }
    }

    /// <summary>
    /// 补贴类型变化时自动设置单价（双轨价单点取 SubsidyPriceConstants.GetPrice）。
    /// 判价日期：本行已录 created_at 的按 created_at（10-15 前建的行改类型仍取旧价，历史不动），
    /// 新建/导入行（赋值顺序上 CreatedAt 尚未写入）按当前时间取新价。
    /// 从库加载时 subsidy_type 先于 unit_price 赋值，行内已存单价会覆盖这里的默认值。
    /// </summary>
    partial void OnSubsidyTypeChanged(string value)
    {
        var asOf = CreatedAt != default ? CreatedAt : DateTime.Now;
        UnitPrice = Constants.SubsidyPriceConstants.GetPrice(value, asOf);
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
