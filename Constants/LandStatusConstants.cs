namespace NewCosmos.Constants;

/// <summary>
/// 土地确权状态常量
/// </summary>
public static class LandStatusConstants
{
    /// <summary>
    /// 存活享有（默认状态）
    /// </summary>
    public const string LIVING_ENTITLED = "存活享有";

    /// <summary>
    /// 死亡继承（需选择继承人）
    /// </summary>
    public const string DECEASED_INHERITANCE = "死亡继承";

    /// <summary>
    /// 无土地权（出生日期晚于1998-12-31自动判定）
    /// </summary>
    public const string NO_LAND_RIGHT = "无土地权";

    /// <summary>
    /// 外来土地（不计入家庭份额）
    /// </summary>
    public const string EXTERNAL_LAND = "外来土地";

    public static readonly string[] All = { LIVING_ENTITLED, DECEASED_INHERITANCE, NO_LAND_RIGHT, EXTERNAL_LAND };

    /// <summary>
    /// 应计入家庭份额的状态（排除外来土地和无土地权）
    /// </summary>
    public static bool ShouldCount(string landStatus) =>
        landStatus != EXTERNAL_LAND && landStatus != NO_LAND_RIGHT;
}
