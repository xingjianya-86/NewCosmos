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

/// <summary>
/// 预测算法类型
/// </summary>
public enum PredictionAlgorithm
{
    /// <summary>
    /// 随机机选
    /// </summary>
    Random = 0,

    /// <summary>
    /// 基础统计（频次、冷热号、遗漏）
    /// </summary>
    BasicStatistics = 1,

    /// <summary>
    /// 量化算法（EWMA、Markov、贝叶斯）
    /// </summary>
    Quantitative = 2,

    /// <summary>
    /// 机器学习（TensorFlow LSTM）
    /// </summary>
    MachineLearning = 3,

    /// <summary>
    /// 综合融合（多算法投票）
    /// </summary>
    Ensemble = 4,

    /// <summary>
    /// LotteryML 智能预测（clean-room：逐位分类 + 温度采样 + 共识）
    /// </summary>
    LotteryML = 5,

    /// <summary>
    /// 融合算法（原版机器学习 LSTM 0.6 + LotteryML 0.4，评分级融合）
    /// </summary>
    Fusion = 6
}

/// <summary>
/// 统计类型
/// </summary>
public enum StatisticsType
{
    /// <summary>
    /// 出现频次
    /// </summary>
    Frequency = 0,

    /// <summary>
    /// 热号（近期高频）
    /// </summary>
    Hot = 1,

    /// <summary>
    /// 冷号（近期低频）
    /// </summary>
    Cold = 2,

    /// <summary>
    /// 遗漏值（连续未出现期数）
    /// </summary>
    Missing = 3,

    /// <summary>
    /// 平均遗漏
    /// </summary>
    AverageMissing = 4,

    /// <summary>
    /// 最大遗漏
    /// </summary>
    MaxMissing = 5
}

/// <summary>
/// 号码统计信息
/// </summary>
public class NumberStatistics
{
    /// <summary>
    /// 号码值
    /// </summary>
    public int Number { get; set; }

    /// <summary>
    /// 出现次数
    /// </summary>
    public int Frequency { get; set; }

    /// <summary>
    /// 出现频率（百分比）
    /// </summary>
    public decimal FrequencyRate { get; set; }

    /// <summary>
    /// 当前遗漏次数
    /// </summary>
    public int CurrentMissing { get; set; }

    /// <summary>
    /// 平均遗漏次数
    /// </summary>
    public decimal AverageMissing { get; set; }

    /// <summary>
    /// 最大遗漏次数
    /// </summary>
    public int MaxMissing { get; set; }

    /// <summary>
    /// 最后出现期号
    /// </summary>
    public string LastAppearPeriod { get; set; } = string.Empty;

    /// <summary>
    /// 近N期出现次数
    /// </summary>
    public int RecentFrequency { get; set; }
}
