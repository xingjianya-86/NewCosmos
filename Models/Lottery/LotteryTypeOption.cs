namespace NewCosmos.Models.Lottery;

/// <summary>
/// 彩种选项（用于 Picker 绑定显示中文名称）
/// </summary>
public class LotteryTypeOption
{
    public string Name { get; set; } = string.Empty;
    public LotteryType Value { get; set; }

    public override string ToString() => Name;

    public override bool Equals(object? obj) =>
        obj is LotteryTypeOption o && o.Value == Value;

    public override int GetHashCode() => Value.GetHashCode();

    public static List<LotteryTypeOption> GetAll() => new()
    {
        new LotteryTypeOption { Name = "双色球", Value = LotteryType.SSQ },
        new LotteryTypeOption { Name = "大乐透", Value = LotteryType.DLT }
    };
}
