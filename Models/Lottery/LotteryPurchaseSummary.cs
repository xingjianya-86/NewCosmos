namespace NewCosmos.Models.Lottery;

/// <summary>
/// 购彩记录汇总统计（中奖结果页头部统计胶囊）
/// </summary>
public class LotteryPurchaseSummary
{
    /// <summary>
    /// 累计购彩注数
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// 已验证注数
    /// </summary>
    public int VerifiedCount { get; set; }

    /// <summary>
    /// 待验证注数（已购彩但对应开奖结果尚未取得）
    /// </summary>
    public int PendingCount { get; set; }

    /// <summary>
    /// 中奖注数
    /// </summary>
    public int HitCount { get; set; }

    /// <summary>
    /// 中奖金额合计（元）
    /// </summary>
    public decimal TotalPrizeAmount { get; set; }

    /// <summary>
    /// 最佳奖级（0=未中奖，数字越小奖级越高）
    /// </summary>
    public int BestPrizeLevel { get; set; }

    /// <summary>
    /// 最佳奖级描述
    /// </summary>
    public string BestPrizeDescription { get; set; } = string.Empty;
}
