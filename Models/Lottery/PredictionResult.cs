using System.Text.Json;

namespace NewCosmos.Models.Lottery;

/// <summary>
/// 预测记录实体
/// </summary>
public class PredictionResult
{
    /// <summary>
    /// 主键ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 彩种类型
    /// </summary>
    public LotteryType LotteryType { get; set; }

    /// <summary>
    /// 预测目标期号
    /// </summary>
    public string TargetDrawNumber { get; set; } = string.Empty;

    /// <summary>
    /// 预测红球号码（JSON数组）
    /// </summary>
    public string PredictedRedJson { get; set; } = "[]";

    /// <summary>
    /// 预测蓝球号码（JSON数组）
    /// </summary>
    public string PredictedBlueJson { get; set; } = "[]";

    /// <summary>
    /// 算法版本标识
    /// </summary>
    public string AlgorithmVersion { get; set; } = string.Empty;

    /// <summary>
    /// 算法名称
    /// </summary>
    public string AlgorithmName { get; set; } = string.Empty;

    /// <summary>
    /// 置信度评分（0-100）
    /// </summary>
    public decimal ConfidenceScore { get; set; }

    /// <summary>
    /// 红球是否命中
    /// </summary>
    public bool? IsHitRed { get; set; }

    /// <summary>
    /// 蓝球是否命中
    /// </summary>
    public bool? IsHitBlue { get; set; }

    /// <summary>
    /// 红球命中个数
    /// </summary>
    public int? RedHitCount { get; set; }

    /// <summary>
    /// 蓝球命中个数
    /// </summary>
    public int? BlueHitCount { get; set; }

    /// <summary>
    /// 奖级（0=未中奖，1-7=各奖级）
    /// </summary>
    public int? PrizeLevel { get; set; }

    /// <summary>
    /// 奖金金额（单位：元）
    /// </summary>
    public decimal? PrizeAmount { get; set; }

    /// <summary>
    /// 额外分析信息（JSON格式）
    /// </summary>
    public string? AnalysisJson { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // === 便利属性 ===

    /// <summary>
    /// 获取预测红球列表
    /// </summary>
    public int[] GetPredictedReds()
    {
        try
        {
            return JsonSerializer.Deserialize<int[]>(PredictedRedJson) ?? Array.Empty<int>();
        }
        catch
        {
            return Array.Empty<int>();
        }
    }

    /// <summary>
    /// 设置预测红球
    /// </summary>
    public void SetPredictedReds(int[] numbers)
    {
        PredictedRedJson = JsonSerializer.Serialize(numbers);
    }

    /// <summary>
    /// 获取预测蓝球列表
    /// </summary>
    public int[] GetPredictedBlues()
    {
        try
        {
            return JsonSerializer.Deserialize<int[]>(PredictedBlueJson) ?? Array.Empty<int>();
        }
        catch
        {
            return Array.Empty<int>();
        }
    }

    /// <summary>
    /// 设置预测蓝球
    /// </summary>
    public void SetPredictedBlues(int[] numbers)
    {
        PredictedBlueJson = JsonSerializer.Serialize(numbers);
    }

    /// <summary>
    /// 获取预测号码显示字符串
    /// </summary>
    public string GetPredictedNumbersString()
    {
        var reds = GetPredictedReds();
        var blues = GetPredictedBlues();
        var redStr = string.Join(",", reds.Select(n => n.ToString("D2")));
        var blueStr = string.Join(",", blues.Select(n => n.ToString("D2")));
        return $"{redStr} | {blueStr}";
    }
}
