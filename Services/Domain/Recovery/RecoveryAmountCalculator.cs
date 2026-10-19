using System.Globalization;
using NewCosmos.Constants;
using NewCosmos.Models.Entities;

namespace NewCosmos.Services.Domain.Recovery;

/// <summary>
/// 追缴应缴总额计算器（依据历史月保障标准分段累加）。
///
/// 口径（**唯一实现**，禁止在 ViewModel / Service 另抄一份）：
///   · 逐月取"生效月 ≤ 该月"的最近一个历史月标准（RecoveryMonthlyStandardPoint），标准变化即切段；
///   · 段金额 = 月标准 × 段月数；总额 = Σ 段金额后**一次性舍入到分**（与高龄补发分段计算同法）；
///   · 历史数据不全（某月之前无标准）、追缴跨度超上限、算得金额为 0 时**不做自动计算**，
///     返回 CanCalculate=false + 原因（RecoveryConstants.REASON_*），由界面转人工填写。
///   · 无可用历史时间轴但调用方提供了月保障额（fallbackMonthlyAmount &gt; 0）时，
///     退化为「月标准 × 月数」的定额口径（AmountSource = Flat）。
/// </summary>
public static class RecoveryAmountCalculator
{
    /// <summary>
    /// 计算应追缴总额。
    /// </summary>
    /// <param name="startMonth">追缴起始月（yyyy-MM）</param>
    /// <param name="endMonth">追缴终止月（yyyy-MM）</param>
    /// <param name="timeline">历史月保障标准时间轴（可空：空表示无历史数据）</param>
    /// <param name="fallbackMonthlyAmount">无历史时间轴时的月保障额人工值（≤0 表示无）</param>
    /// <param name="maxAutoMonths">自动计算月数上限（超过转人工填写）</param>
    public static RecoveryAmountResult Calculate(
        string? startMonth,
        string? endMonth,
        IReadOnlyList<RecoveryMonthlyStandardPoint>? timeline,
        decimal fallbackMonthlyAmount = 0m,
        int maxAutoMonths = RecoveryConstants.MaxAutoMonths)
    {
        var result = new RecoveryAmountResult();

        if (!TryParseMonth(startMonth, out var start) || !TryParseMonth(endMonth, out var end))
        {
            result.Reason = RecoveryConstants.REASON_INVALID_RANGE;
            return result;
        }

        if (end < start)
        {
            result.Reason = RecoveryConstants.REASON_NO_MONTHS;
            return result;
        }

        var totalMonths = ((end.Year - start.Year) * 12) + end.Month - start.Month + 1;

        if (totalMonths > maxAutoMonths)
        {
            result.TotalMonths = totalMonths;
            result.Reason = string.Format(RecoveryConstants.REASON_TOO_LONG, maxAutoMonths);
            return result;
        }

        // 历史时间轴不足：报出"最早缺标准的月份"，便于业务定位数据缺口
        var missingMonth = FindFirstMonthWithoutStandard(start, end, timeline);

        if (missingMonth == null)
        {
            var segments = BuildSegments(start, end, timeline!);
            var total = segments.Sum(s => s.SegmentAmount);

            // 时间轴只含"现值"锚点（无变更记录）时，让界面上的「月保障额」优先——
            // 用户可直接改标准，自动分段在此情形下与「月标准×月数」等价。
            var hasChangeSegments = segments.Exists(s => s.Source == RecoveryConstants.SEGMENT_SOURCE_CHANGE);

            if (!hasChangeSegments && fallbackMonthlyAmount > 0)
            {
                return ApplyFallbackOrFail(
                    result, fallbackMonthlyAmount, start, end, totalMonths, RecoveryConstants.REASON_NO_HISTORY);
            }

            if (total > 0)
            {
                result.CanCalculate = true;
                result.Segments = segments;
                result.TotalMonths = segments.Sum(s => s.Months);
                result.TotalAmount = RoundToFen(total);
                result.AmountSource = hasChangeSegments
                    ? RecoveryConstants.AMOUNT_SOURCE_SEGMENT
                    : RecoveryConstants.AMOUNT_SOURCE_FLAT;
                return result;
            }

            // 逐月标准均为 0：按业务视为无有效保障金月份
            return ApplyFallbackOrFail(
                result, fallbackMonthlyAmount, start, end, totalMonths, RecoveryConstants.REASON_ZERO_AMOUNT);
        }

        var reason = string.Format(RecoveryConstants.REASON_NO_HISTORY_MONTH, missingMonth);
        return ApplyFallbackOrFail(result, fallbackMonthlyAmount, start, end, totalMonths, reason);
    }

    /// <summary>
    /// 无历史时间轴（或历史算得为 0）时的定额兜底：月保障额 × 月数；月保障额缺失则明确失败。
    /// </summary>
    private static RecoveryAmountResult ApplyFallbackOrFail(
        RecoveryAmountResult result,
        decimal fallbackMonthlyAmount,
        DateTime start,
        DateTime end,
        int totalMonths,
        string reason)
    {
        if (fallbackMonthlyAmount <= 0)
        {
            result.TotalMonths = totalMonths;
            result.Reason = reason;
            return result;
        }

        var amount = RoundToFen(fallbackMonthlyAmount * totalMonths);

        if (amount <= 0)
        {
            result.TotalMonths = totalMonths;
            result.Reason = reason;
            return result;
        }

        result.CanCalculate = true;
        result.AmountSource = RecoveryConstants.AMOUNT_SOURCE_FLAT;
        result.TotalMonths = totalMonths;
        result.TotalAmount = amount;
        result.Segments = new List<RecoverySegment>
        {
            new()
            {
                SegmentNo = 1,
                MonthlyAmount = fallbackMonthlyAmount,
                SegmentStartMonth = ToMonthString(start),
                SegmentEndMonth = ToMonthString(end),
                Months = totalMonths,
                SegmentAmount = amount,
                Source = RecoveryConstants.SEGMENT_SOURCE_LEDGER
            }
        };

        return result;
    }

    /// <summary>
    /// 返回"最早一个取不到月标准的月份"（yyyy-MM）；全部可取到则返回 null。
    /// </summary>
    private static string? FindFirstMonthWithoutStandard(
        DateTime start, DateTime end, IReadOnlyList<RecoveryMonthlyStandardPoint>? timeline)
    {
        if (timeline == null || timeline.Count == 0)
            return ToMonthString(start);

        var points = timeline
            .Where(p => !string.IsNullOrWhiteSpace(p.EffectiveMonth))
            .OrderBy(p => p.EffectiveMonth, StringComparer.Ordinal)
            .ToList();

        if (points.Count == 0)
            return ToMonthString(start);

        var cursor = start;
        while (cursor <= end)
        {
            var month = ToMonthString(cursor);
            if (ResolveStandard(month, points) == null)
                return month;
            cursor = cursor.AddMonths(1);
        }

        return null;
    }

    /// <summary>
    /// 按月切段：标准（金额 + 依据）变化即新起一段。
    /// </summary>
    private static List<RecoverySegment> BuildSegments(
        DateTime start, DateTime end, IReadOnlyList<RecoveryMonthlyStandardPoint> timeline)
    {
        var points = timeline
            .Where(p => !string.IsNullOrWhiteSpace(p.EffectiveMonth))
            .OrderBy(p => p.EffectiveMonth, StringComparer.Ordinal)
            .ToList();

        var segments = new List<RecoverySegment>();
        RecoverySegment? current = null;
        var cursor = start;

        while (cursor <= end)
        {
            var month = ToMonthString(cursor);
            var point = ResolveStandard(month, points);
            if (point == null)
                break;

            if (current == null || current.MonthlyAmount != point.MonthlyAmount || current.Source != point.Source)
            {
                current = new RecoverySegment
                {
                    SegmentNo = segments.Count + 1,
                    MonthlyAmount = point.MonthlyAmount,
                    SegmentStartMonth = month,
                    Source = point.Source
                };
                segments.Add(current);
            }

            current.SegmentEndMonth = month;
            current.Months++;
            current.SegmentAmount = current.MonthlyAmount * current.Months;

            cursor = cursor.AddMonths(1);
        }

        return segments;
    }

    /// <summary>
    /// 取"生效月 ≤ 目标月"的最近一个时间轴点；无则返回 null（该月历史数据缺失）。
    /// </summary>
    private static RecoveryMonthlyStandardPoint? ResolveStandard(
        string month, IReadOnlyList<RecoveryMonthlyStandardPoint> orderedPoints)
    {
        RecoveryMonthlyStandardPoint? hit = null;
        foreach (var point in orderedPoints)
        {
            if (string.CompareOrdinal(point.EffectiveMonth, month) <= 0)
                hit = point;
            else
                break;
        }

        return hit;
    }

    /// <summary>
    /// 一次性舍入到分（过程不做中间舍入）。
    /// </summary>
    private static decimal RoundToFen(decimal value) =>
        Math.Round(value, 2, MidpointRounding.AwayFromZero);

    private static bool TryParseMonth(string? month, out DateTime value)
    {
        if (!string.IsNullOrWhiteSpace(month) &&
            DateTime.TryParseExact(month, "yyyy-MM", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
        {
            value = new DateTime(parsed.Year, parsed.Month, 1);
            return true;
        }

        value = default;
        return false;
    }

    private static string ToMonthString(DateTime d) => d.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}
