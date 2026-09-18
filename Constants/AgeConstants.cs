namespace NewCosmos.Constants;

/// <summary>
/// 年龄阈值常量
/// </summary>
public static class AgeConstants
{
    /// <summary>
    /// 老年人年龄阈值（60岁）
    /// </summary>
    public const int ELDERLY_THRESHOLD = 60;

    /// <summary>
    /// 未成年人年龄阈值（18岁）
    /// </summary>
    public const int MINOR_THRESHOLD = 18;

    /// <summary>
    /// 是否为老年人
    /// </summary>
    public static bool IsElderly(int age) => age >= ELDERLY_THRESHOLD;

    /// <summary>
    /// 是否为未成年人
    /// </summary>
    public static bool IsMinor(int age) => age < MINOR_THRESHOLD;
}
