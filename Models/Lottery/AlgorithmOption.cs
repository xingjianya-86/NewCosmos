namespace NewCosmos.Models.Lottery;

/// <summary>
/// 预测算法选项（用于 Picker 绑定显示中文描述）
/// </summary>
public class AlgorithmOption
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public PredictionAlgorithm Value { get; set; }

    public override string ToString() => Name;

    public override bool Equals(object? obj) =>
        obj is AlgorithmOption o && o.Value == Value;

    public override int GetHashCode() => Value.GetHashCode();

    public static List<AlgorithmOption> GetAll() => new()
    {
        new AlgorithmOption
        {
            Name = "融合算法",
            Description = "机器学习(LSTM) 0.6 + LotteryML 0.4 评分级融合（默认）",
            Value = PredictionAlgorithm.Fusion
        },
        new AlgorithmOption
        {
            Name = "随机机选",
            Description = "纯随机生成号码，适合娱乐",
            Value = PredictionAlgorithm.Random
        }
    };
}
