using System.Text.Json;
using NewCosmos.Models.Lottery;

namespace NewCosmos.Helpers;

/// <summary>
/// 彩票奖金解析器：从开奖记录 prize_grades_json 中解析指定奖级的单注奖金。
/// <para>
/// 数据结构示例：
/// [{"type":1,"typenum":"11","typemoney":"6596605"}, {"type":2,...,"typemoney":"128382"}, ...]，
/// <c>type</c> 与 <see cref="UserPurchaseService"/> 判定的奖级一一对应，<c>typemoney</c> 为单注奖金（元）。
/// </para>
/// <para>
/// 解析不到（如大乐透当前未抓取奖级明细）时返回 0，由 UI 标注“浮动奖/以官方为准”。
/// </para>
/// </summary>
public static class LotteryPrizeResolver
{
    /// <summary>
    /// 解析指定奖级的单注奖金，解析失败或未匹配返回 0。
    /// </summary>
    public static decimal ResolvePrizeAmount(string? prizeGradesJson, int prizeLevel)
    {
        if (prizeLevel <= 0 || string.IsNullOrWhiteSpace(prizeGradesJson))
            return 0m;

        try
        {
            using var doc = JsonDocument.Parse(prizeGradesJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return 0m;

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                    continue;

                if (!item.TryGetProperty("type", out var typeProp))
                    continue;

                if (ParseInt(typeProp) != prizeLevel)
                    continue;

                return item.TryGetProperty("typemoney", out var moneyProp)
                    ? ParseDecimal(moneyProp)
                    : 0m;
            }
        }
        catch (JsonException)
        {
            // 奖级明细格式异常不影响主流程，按未知奖金处理
        }

        return 0m;
    }

    /// <summary>
    /// 获取奖级中文描述（纯文本，无表情）
    /// </summary>
    public static string GetPrizeLevelName(LotteryType lotteryType, int level)
    {
        if (level <= 0)
            return string.Empty;

        // 双色球 6 个奖级、大乐透 2026-01-31 起 7 个奖级；8/9 仅用于兼容大乐透改版前历史
        return level switch
        {
            1 => "一等奖",
            2 => "二等奖",
            3 => "三等奖",
            4 => "四等奖",
            5 => "五等奖",
            6 => "六等奖",
            7 => "七等奖",
            8 => "八等奖",
            9 => "九等奖",
            _ => $"第{level}等奖"
        };
    }

    private static int ParseInt(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Number => element.TryGetInt32(out var number) ? number : 0,
        JsonValueKind.String => int.TryParse(element.GetString(), out var parsed) ? parsed : 0,
        _ => 0
    };

    private static decimal ParseDecimal(JsonElement element)
    {
        var raw = element.ValueKind switch
        {
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.String => element.GetString(),
            _ => null
        };

        return decimal.TryParse(raw, out var value) ? value : 0m;
    }
}
