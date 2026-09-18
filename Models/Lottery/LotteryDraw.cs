using System.Text.Json;

namespace NewCosmos.Models.Lottery;

/// <summary>
/// 开奖记录实体
/// </summary>
public class LotteryDraw
{
    /// <summary>
    /// 主键ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 彩种类型 (SSQ/DLT)
    /// </summary>
    public LotteryType LotteryType { get; set; }

    /// <summary>
    /// 期号（如：2026100）
    /// </summary>
    public string DrawNumber { get; set; } = string.Empty;

    /// <summary>
    /// 开奖日期
    /// </summary>
    public DateTime DrawDate { get; set; }

    /// <summary>
    /// 红球/前区号码（JSON数组格式存储）
    /// </summary>
    public string RedNumbersJson { get; set; } = "[]";

    /// <summary>
    /// 蓝球/后区号码（JSON数组格式存储，大乐透有2个）
    /// </summary>
    public string BlueNumbersJson { get; set; } = "[]";

    /// <summary>
    /// 销售额（单位：元）
    /// </summary>
    public decimal SalesAmount { get; set; }

    /// <summary>
    /// 奖池金额（单位：元）
    /// </summary>
    public decimal PoolMoney { get; set; }

    /// <summary>
    /// 奖级明细（JSON格式）
    /// </summary>
    public string PrizeGradesJson { get; set; } = "[]";

    /// <summary>
    /// 中奖详情文本
    /// </summary>
    public string? PrizeDescription { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // === 便利属性 ===

    /// <summary>
    /// 获取红球号码列表
    /// </summary>
    public int[] GetRedNumbers()
    {
        try
        {
            return JsonSerializer.Deserialize<int[]>(RedNumbersJson) ?? Array.Empty<int>();
        }
        catch
        {
            return Array.Empty<int>();
        }
    }

    /// <summary>
    /// 设置红球号码
    /// </summary>
    public void SetRedNumbers(int[] numbers)
    {
        RedNumbersJson = JsonSerializer.Serialize(numbers);
    }

    /// <summary>
    /// 获取蓝球号码列表
    /// </summary>
    public int[] GetBlueNumbers()
    {
        try
        {
            return JsonSerializer.Deserialize<int[]>(BlueNumbersJson) ?? Array.Empty<int>();
        }
        catch
        {
            return Array.Empty<int>();
        }
    }

    /// <summary>
    /// 设置蓝球号码
    /// </summary>
    public void SetBlueNumbers(int[] numbers)
    {
        BlueNumbersJson = JsonSerializer.Serialize(numbers);
    }

    /// <summary>
    /// 获取红球号码字符串（用于显示）
    /// </summary>
    public string GetRedNumbersString()
    {
        var numbers = GetRedNumbers();
        return string.Join(",", numbers.Select(n => n.ToString("D2")));
    }

    /// <summary>
    /// 获取蓝球号码字符串（用于显示）
    /// </summary>
    public string GetBlueNumbersString()
    {
        var numbers = GetBlueNumbers();
        return string.Join(",", numbers.Select(n => n.ToString("D2")));
    }
}
