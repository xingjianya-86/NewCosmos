using System.Text.Json;
using NewCosmos.Helpers;
using NewCosmos.Models.Lottery;
using NewCosmos.Models.Results;

namespace NewCosmos.Services.Lottery;

/// <summary>
/// 用户购彩记录服务接口
/// 预测结果自动入库，开奖后验证命中
/// </summary>
public interface IUserPurchaseService
{
    /// <summary>
    /// 批量保存预测结果为购彩记录
    /// </summary>
    Task<Result<int>> SaveFromPredictionsAsync(LotteryType lotteryType, List<PredictionResult> predictions, string algorithm, CancellationToken ct = default);

    /// <summary>
    /// 验证所有待验证的购彩记录（对照开奖结果）
    /// 返回验证结果摘要
    /// </summary>
    Task<Result<VerificationSummary>> VerifyAndRewardAsync(LotteryType lotteryType, CancellationToken ct = default);

    /// <summary>
    /// 获取购彩记录列表（分页，可按状态筛选）
    /// </summary>
    Task<Result<List<UserPurchaseRecord>>> GetPurchasesAsync(LotteryType lotteryType, LotteryPurchaseFilter filter = LotteryPurchaseFilter.All, int pageNo = 1, int pageSize = 30, CancellationToken ct = default);

    /// <summary>
    /// 获取购彩记录汇总统计（累计/中奖/待验证/中奖金额/最佳奖级）
    /// </summary>
    Task<Result<LotteryPurchaseSummary>> GetPurchaseSummaryAsync(LotteryType lotteryType, CancellationToken ct = default);

    /// <summary>
    /// 获取待验证记录数
    /// </summary>
    Task<Result<int>> GetUnverifiedCountAsync(LotteryType lotteryType, CancellationToken ct = default);

    /// <summary>
    /// 获取购彩记录总数（可按状态筛选）
    /// </summary>
    Task<Result<int>> GetPurchaseCountAsync(LotteryType lotteryType, LotteryPurchaseFilter filter = LotteryPurchaseFilter.All, CancellationToken ct = default);
}

/// <summary>
/// 购彩记录状态筛选
/// </summary>
public enum LotteryPurchaseFilter
{
    /// <summary>全部</summary>
    All = 0,

    /// <summary>待开奖（尚未验证）</summary>
    Pending = 1,

    /// <summary>已中奖</summary>
    Hit = 2,

    /// <summary>未中奖</summary>
    Miss = 3
}

/// <summary>
/// 验证结果摘要
/// </summary>
public class VerificationSummary
{
    public int TotalVerified { get; set; }
    public int HitCount { get; set; }
    public int PendingCount { get; set; }
    public decimal TotalPrizeAmount { get; set; }
    public int BestPrizeLevel { get; set; }
    public string BestPrizeDescription { get; set; } = "";
    public List<HitDetail> HitDetails { get; set; } = new();
    public string EncouragementMessage { get; set; } = "";
}

/// <summary>
/// 命中详情
/// </summary>
public class HitDetail
{
    public string DrawNumber { get; set; } = "";
    public int RedHits { get; set; }
    public int BlueHits { get; set; }
    public int PrizeLevel { get; set; }
    public decimal PrizeAmount { get; set; }
    public string Numbers { get; set; } = "";
}

/// <summary>
/// 用户购彩记录
/// </summary>
public class UserPurchaseRecord
{
    public int Id { get; set; }
    public LotteryType LotteryType { get; set; }
    public string DrawNumber { get; set; } = "";
    public string RedNumbersJson { get; set; } = "[]";
    public string BlueNumbersJson { get; set; } = "[]";

    /// <summary>实际开奖红球（用于中奖号码着色；无对应开奖记录时为空数组）</summary>
    public string ActualRedNumbersJson { get; set; } = "[]";

    /// <summary>实际开奖蓝球（用于中奖号码着色；无对应开奖记录时为空数组）</summary>
    public string ActualBlueNumbersJson { get; set; } = "[]";
    public string Algorithm { get; set; } = "";
    public DateTime PurchaseDate { get; set; }
    public bool IsVerified { get; set; }
    public bool IsHit { get; set; }
    public int HitRedCount { get; set; }
    public int HitBlueCount { get; set; }
    public int PrizeLevel { get; set; }

    /// <summary>
    /// 中奖金额（元），0 表示未中奖或奖级明细缺失
    /// </summary>
    public decimal PrizeAmount { get; set; }

    public DateTime CreatedAt { get; set; }

    // === 显示辅助属性 ===

    /// <summary>是否待开奖（已购彩但尚未验证）</summary>
    public bool IsPending => !IsVerified;

    /// <summary>状态文本</summary>
    public string StatusText => !IsVerified ? "待开奖" : (IsHit ? "已中奖" : "未中奖");

    /// <summary>状态文本颜色</summary>
    public string StatusTextColor => !IsVerified ? "#B45309" : (IsHit ? "#15803D" : "#64748B");

    /// <summary>状态背景颜色</summary>
    public string StatusBackgroundColor => !IsVerified ? "#FEF3C7" : (IsHit ? "#DCFCE7" : "#F1F5F9");

    /// <summary>命中文案（红/蓝命中数）</summary>
    public string HitSummaryText => IsVerified ? $"红 {HitRedCount} · 蓝 {HitBlueCount}" : "—";

    /// <summary>奖级文本</summary>
    public string PrizeLevelText => IsHit && PrizeLevel > 0
        ? LotteryPrizeResolver.GetPrizeLevelName(LotteryType, PrizeLevel)
        : "";

    /// <summary>奖金文本（未中奖为“—”，奖级明细缺失标注以官方为准）</summary>
    public string PrizeAmountText => !IsHit
        ? "—"
        : (PrizeAmount > 0 ? $"¥{PrizeAmount:N2}" : "以官方为准");

    public int[] GetRedNumbers()
    {
        try { return global::System.Text.Json.JsonSerializer.Deserialize<int[]>(RedNumbersJson) ?? Array.Empty<int>(); }
        catch { return Array.Empty<int>(); }
    }

    public int[] GetBlueNumbers()
    {
        try { return global::System.Text.Json.JsonSerializer.Deserialize<int[]>(BlueNumbersJson) ?? Array.Empty<int>(); }
        catch { return Array.Empty<int>(); }
    }

    /// <summary>号码显示（红球 + 蓝球）</summary>
    public string NumbersDisplay => $"{GetRedNumbersString()} + {GetBlueNumbersString()}";

    public string GetRedNumbersString() => string.Join(",", GetRedNumbers().Select(n => n.ToString("D2")));
    public string GetBlueNumbersString() => string.Join(",", GetBlueNumbers().Select(n => n.ToString("D2")));
    public string GetNumbersDisplay() => NumbersDisplay;

    /// <summary>实际开奖号码显示（红 + 蓝；无开奖记录为空）</summary>
    public string ActualNumbersDisplay
    {
        get
        {
            var reds = DeserializeInts(ActualRedNumbersJson);
            var blues = DeserializeInts(ActualBlueNumbersJson);
            if (reds.Length == 0 && blues.Length == 0) return string.Empty;
            return $"{string.Join(",", reds.Select(n => n.ToString("D2")))} + {string.Join(",", blues.Select(n => n.ToString("D2")))}";
        }
    }

    /// <summary>
    /// 号码小球集合（红/蓝球 + 是否命中开奖号码），供中奖结果列表着色显示：
    /// 命中 = 实心红/蓝，未命中 = 浅底描边。
    /// </summary>
    public IReadOnlyList<LotteryNumberToken> NumberTokens
    {
        get
        {
            var actualReds = new HashSet<int>(DeserializeInts(ActualRedNumbersJson));
            var actualBlues = new HashSet<int>(DeserializeInts(ActualBlueNumbersJson));
            var tokens = new List<LotteryNumberToken>();
            foreach (var n in GetRedNumbers())
                tokens.Add(new LotteryNumberToken { Text = n.ToString("D2"), IsBlue = false, IsHit = IsVerified && actualReds.Contains(n) });
            foreach (var n in GetBlueNumbers())
                tokens.Add(new LotteryNumberToken { Text = n.ToString("D2"), IsBlue = true, IsHit = IsVerified && actualBlues.Contains(n) });
            return tokens;
        }
    }

    private static int[] DeserializeInts(string json)
    {
        try { return JsonSerializer.Deserialize<int[]>(json) ?? Array.Empty<int>(); }
        catch { return Array.Empty<int>(); }
    }
}

/// <summary>
/// 号码小球（中奖号码着色）：命中开奖号码 = 实心红/蓝 + 白字，未命中 = 浅底描边 + 红/蓝字。
/// </summary>
public class LotteryNumberToken
{
    public string Text { get; set; } = string.Empty;

    /// <summary>是否蓝球</summary>
    public bool IsBlue { get; set; }

    /// <summary>是否命中开奖号码</summary>
    public bool IsHit { get; set; }

    /// <summary>背景色（命中实心红/蓝；未命中浅灰）</summary>
    public string BackgroundColor => IsHit ? (IsBlue ? "#1E88E5" : "#E53935") : "#EEF2F6";

    /// <summary>文字色（命中白色；未命中红/蓝）</summary>
    public string TextColor => IsHit ? "#FFFFFF" : (IsBlue ? "#1E88E5" : "#E53935");

    /// <summary>描边色</summary>
    public string StrokeColor => IsHit ? "Transparent" : (IsBlue ? "#BBDEFB" : "#FFCDD2");
}
