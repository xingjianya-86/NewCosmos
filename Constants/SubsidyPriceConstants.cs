namespace NewCosmos.Constants;

/// <summary>
/// 农业补贴价格常量（元/亩）——双轨价单点实现。
/// 口径：以 <see cref="CURRENT_PRICE_DATE"/>（2026-10-15）为界，之前取旧价、之后取新价；
/// 判价日期由调用方传入（补贴明细按行 created_at，新建行按当前时间），禁止各处自行抄价格表。
/// 历史行单价已按行存于 nc_biz_subsidies.unit_price，本类只提供"新增/改类型时的默认单价"，
/// 不回写存量数据（2026-10-21 拍板：历史一律不动，不做迁移回算）。
/// </summary>
public static class SubsidyPriceConstants
{
    /// <summary>
    /// 新价生效日：2026-10-15（该日及以后用新价，之前用旧价）
    /// </summary>
    public static readonly DateTime CURRENT_PRICE_DATE = new(2026, 10, 15);

    /// <summary>
    /// 地力补贴单价（2026-10-14 及以前）
    /// </summary>
    public const decimal LAND_FERTILITY_PER_ACRE = 75.60m;

    /// <summary>
    /// 地力补贴单价（2026-10-15 起，耕地地力保护补贴 75.73 元/亩）
    /// </summary>
    public const decimal LAND_FERTILITY_PER_ACRE_20261015 = 75.73m;

    /// <summary>
    /// 轮作补贴单价（两期一致，无新旧之分）
    /// </summary>
    public const decimal ROTATION_PER_ACRE = 150.00m;

    /// <summary>
    /// 大豆补贴单价（2026-10-14 及以前）
    /// </summary>
    public const decimal SOYBEAN_PER_ACRE = 350.73m;

    /// <summary>
    /// 大豆补贴单价（2026-10-15 起）
    /// </summary>
    public const decimal SOYBEAN_PER_ACRE_20261015 = 388.34m;

    /// <summary>
    /// 玉米补贴单价（2026-10-14 及以前）
    /// </summary>
    public const decimal CORN_PER_ACRE = 16.73m;

    /// <summary>
    /// 玉米补贴单价（2026-10-15 起）
    /// </summary>
    public const decimal CORN_PER_ACRE_20261015 = 47.34m;

    /// <summary>
    /// 地表水水稻补贴单价（2026-10-14 及以前）
    /// </summary>
    public const decimal SURFACE_WATER_RICE_PER_ACRE = 156.53m;

    /// <summary>
    /// 地表水水稻补贴单价（2026-10-15 起）
    /// </summary>
    public const decimal SURFACE_WATER_RICE_PER_ACRE_20261015 = 156.75m;

    /// <summary>
    /// 地下水水稻补贴单价（2026-10-14 及以前）
    /// </summary>
    public const decimal GROUND_WATER_RICE_PER_ACRE = 106.53m;

    /// <summary>
    /// 地下水水稻补贴单价（2026-10-15 起）
    /// </summary>
    public const decimal GROUND_WATER_RICE_PER_ACRE_20261015 = 106.75m;

    /// <summary>
    /// 补贴类型 → (旧价, 新价)。单值类型新旧同值。
    /// </summary>
    private static readonly Dictionary<string, (decimal Legacy, decimal Current)> TypePrices = new()
    {
        ["地力补贴"] = (LAND_FERTILITY_PER_ACRE, LAND_FERTILITY_PER_ACRE_20261015),
        ["大豆补贴"] = (SOYBEAN_PER_ACRE, SOYBEAN_PER_ACRE_20261015),
        ["玉米补贴"] = (CORN_PER_ACRE, CORN_PER_ACRE_20261015),
        ["轮作补贴"] = (ROTATION_PER_ACRE, ROTATION_PER_ACRE),
        ["地表水水稻"] = (SURFACE_WATER_RICE_PER_ACRE, SURFACE_WATER_RICE_PER_ACRE_20261015),
        ["地下水水稻"] = (GROUND_WATER_RICE_PER_ACRE, GROUND_WATER_RICE_PER_ACRE_20261015),
        // 大豆品种补贴：无统一标准，只记金额——单价恒为 0，行内金额由 OriginalAmount 承载（金额型行）
        ["大豆品种补贴"] = (0m, 0m)
    };

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
    /// 根据补贴类型与判价日期取单价（单点实现，禁止各处重抄价格）。
    /// 判价日期在 <see cref="CURRENT_PRICE_DATE"/> 及以后取新价，之前取旧价；未登记类型返回 0。
    /// </summary>
    /// <param name="subsidyType">补贴类型（地力补贴/大豆补贴/玉米补贴/轮作补贴/地表水水稻/地下水水稻）</param>
    /// <param name="asOf">判价日期；不传视为当前时间（新建/导入行按当下取价）</param>
    public static decimal GetPrice(string subsidyType, DateTime? asOf = null)
    {
        if (string.IsNullOrEmpty(subsidyType)) return 0;
        if (!TypePrices.TryGetValue(subsidyType, out var price)) return 0;
        return (asOf ?? DateTime.Now) >= CURRENT_PRICE_DATE ? price.Current : price.Legacy;
    }
}
