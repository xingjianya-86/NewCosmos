namespace NewCosmos.Models.Lottery;

/// <summary>
/// 彩种类型枚举
/// </summary>
public enum LotteryType
{
    /// <summary>
    /// 双色球（福利彩票）
    /// 红球：6个（1-33），蓝球：1个（1-16）
    /// </summary>
    SSQ = 0,

    /// <summary>
    /// 大乐透（体育彩票）
    /// 前区：5个（1-35），后区：2个（1-12）
    /// </summary>
    DLT = 1
}

/// <summary>
/// 彩种扩展方法
/// </summary>
public static class LotteryTypeExtensions
{
    /// <summary>
    /// 获取彩种中文名称
    /// </summary>
    public static string GetDisplayName(this LotteryType type)
    {
        return type switch
        {
            LotteryType.SSQ => "双色球",
            LotteryType.DLT => "大乐透",
            _ => "未知"
        };
    }

    /// <summary>
    /// 获取红球/前区数量
    /// </summary>
    public static int GetRedCount(this LotteryType type)
    {
        return type switch
        {
            LotteryType.SSQ => 6,
            LotteryType.DLT => 5,
            _ => 0
        };
    }

    /// <summary>
    /// 获取蓝球/后区数量
    /// </summary>
    public static int GetBlueCount(this LotteryType type)
    {
        return type switch
        {
            LotteryType.SSQ => 1,
            LotteryType.DLT => 2,
            _ => 0
        };
    }

    /// <summary>
    /// 获取红球/前区最大号码
    /// </summary>
    public static int GetRedMax(this LotteryType type)
    {
        return type switch
        {
            LotteryType.SSQ => 33,
            LotteryType.DLT => 35,
            _ => 0
        };
    }

    /// <summary>
    /// 获取蓝球/后区最大号码
    /// </summary>
    public static int GetBlueMax(this LotteryType type)
    {
        return type switch
        {
            LotteryType.SSQ => 16,
            LotteryType.DLT => 12,
            _ => 0
        };
    }
}
