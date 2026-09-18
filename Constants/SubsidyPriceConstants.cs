namespace NewCosmos.Constants;

/// <summary>
/// 农业补贴价格常量（元/亩）
/// </summary>
public static class SubsidyPriceConstants
{
    /// <summary>
    /// 地力补贴单价
    /// </summary>
    public const decimal LAND_FERTILITY_PER_ACRE = 75.60m;

    /// <summary>
    /// 大豆补贴单价
    /// </summary>
    public const decimal SOYBEAN_PER_ACRE = 350.73m;

    /// <summary>
    /// 玉米补贴单价
    /// </summary>
    public const decimal CORN_PER_ACRE = 16.73m;

    /// <summary>
    /// 轮作补贴单价
    /// </summary>
    public const decimal ROTATION_PER_ACRE = 150.00m;

    /// <summary>
    /// 地表水水稻补贴单价
    /// </summary>
    public const decimal SURFACE_WATER_RICE_PER_ACRE = 156.53m;

    /// <summary>
    /// 地下水水稻补贴单价
    /// </summary>
    public const decimal GROUND_WATER_RICE_PER_ACRE = 106.53m;

    /// <summary>
    /// 计算补贴金额 = 面积 × 单价 × 数量
    /// </summary>
    public static decimal CalculateAmount(double area, decimal pricePerAcre, int count = 1)
    {
        return Math.Round((decimal)area * pricePerAcre * count, 2);
    }

    /// <summary>
    /// 计算面积 = 金额 ÷ 单价 ÷ 数量
    /// </summary>
    public static double CalculateArea(decimal amount, decimal pricePerAcre, int count = 1)
    {
        if (pricePerAcre == 0 || count == 0) return 0;
        return Math.Round((double)(amount / pricePerAcre / count), 2);
    }

    /// <summary>
    /// 补贴类型到单价的映射
    /// </summary>
    public static readonly Dictionary<string, decimal> TypeToPrice = new()
    {
        ["地力补贴"] = LAND_FERTILITY_PER_ACRE,
        ["大豆补贴"] = SOYBEAN_PER_ACRE,
        ["玉米补贴"] = CORN_PER_ACRE,
        ["轮作补贴"] = ROTATION_PER_ACRE,
        ["地表水水稻"] = SURFACE_WATER_RICE_PER_ACRE,
        ["地下水水稻"] = GROUND_WATER_RICE_PER_ACRE
    };

    /// <summary>
    /// 根据补贴类型获取单价，未找到返回 0
    /// </summary>
    public static decimal GetPrice(string subsidyType)
    {
        if (string.IsNullOrEmpty(subsidyType)) return 0;
        return TypeToPrice.TryGetValue(subsidyType, out var price) ? price : 0;
    }
}
